<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The stock: the products the shop buys and sells, the purchase invoices they came in on (from a supplier - a vendor of kind 'supplier', see MZC_Ships) and where each one went.
 * Nothing is counted by hand: a product's stock is what its purchase lines brought in less what the new computers (one per part row) and the service jobs (their qty) took out,
 * so deleting a wrong invoice or a wrong part puts the count right by itself. A part row on a form is linked to a product by product_id; old rows (and free-typed ones) have none.
 * The admin pages, the staff API and the AI apps' MCP tools all save through save_product() and save_purchase() here, which take plain arrays and return an id or an error text.
 */
final class MZC_Stock
{
    public static function boot()
    {
        foreach (array('product_save', 'product_delete', 'purchase_save', 'purchase_delete') as $a) { add_action('admin_post_mzc_' . $a, array(__CLASS__, 'act_' . $a)); }
    }

    private static function t($n) { return MZC_Crm_Db::t($n); }
    private static function now() { return gmdate('Y-m-d H:i:s'); }
    private static function money_in($v) { return $v === null || $v === '' ? null : (int) preg_replace('/[^\d]/', '', MZC_Crm::digits((string) $v)); }

    /** The stock of the product row aliased `p`, as an SQL expression. */
    public static function stock_sql()
    {
        return '(COALESCE((SELECT SUM(i.qty) FROM `' . self::t('purchase_items') . '` i WHERE i.product_id = p.id), 0) - (SELECT COUNT(*) FROM `' . self::t('build_parts') . '` x WHERE x.product_id = p.id)'
            . ' - COALESCE((SELECT SUM(y.qty) FROM `' . self::t('job_parts') . "` y WHERE y.product_id = p.id AND y.kind <> 'in'), 0))";
    }

    /* ---------- products ---------- */

    public static function product($id)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return null; }
        return $db->get_row($db->prepare('SELECT p.*, ' . self::stock_sql() . ' AS stock FROM `' . self::t('products') . '` p WHERE p.id = %d', (int) $id), ARRAY_A);
    }

    /** A page of products (name, brand, part number), with the stock of each; $instock keeps only those with at least one. */
    public static function products($q, $page, $per = 30, $cat = '', $instock = false)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return array(array(), 0); }
        $where = array('1=1'); $args = array();
        if ($q !== '') { $l = '%' . $db->esc_like($q) . '%'; $where[] = '(p.name LIKE %s OR p.brand LIKE %s OR p.sku LIKE %s)'; array_push($args, $l, $l, $l); }
        if ($cat !== '' && isset(MZC_Crm::$categories[$cat])) { $where[] = 'p.category = %s'; $args[] = $cat; }
        if ($instock) { $where[] = self::stock_sql() . ' > 0'; }
        $from = 'FROM `' . self::t('products') . '` p WHERE ' . implode(' AND ', $where);
        $total = (int) $db->get_var($args ? $db->prepare("SELECT COUNT(*) $from", $args) : "SELECT COUNT(*) $from");
        $rows = $db->get_results($db->prepare('SELECT p.*, ' . self::stock_sql() . " AS stock $from ORDER BY p.name LIMIT %d OFFSET %d", array_merge($args, array((int) $per, (int) (($page - 1) * $per)))), ARRAY_A);
        return array($rows ?: array(), $total);
    }

    public static function product_by_name($name)
    {
        $db = MZC_Crm_Db::db(); if (!$db || trim((string) $name) === '') { return null; }
        return $db->get_row($db->prepare('SELECT * FROM `' . self::t('products') . '` WHERE name = %s ORDER BY id LIMIT 1', trim((string) $name)), ARRAY_A);
    }

    /** Creates or updates a product. $d: name, category, brand, sku, buy_price, sell_price, warranty_months, notes (keys left out keep their value). Returns the id or an error text. */
    public static function save_product($id, $d)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return MZC_Crm_Db::error(); }
        $old = $id ? self::product($id) : null;
        if ($id && !$old) { return 'کالا پیدا نشد.'; }
        $name = array_key_exists('name', $d) ? Mazesta_Connect::clip((string) $d['name'], 190) : ($old ? $old['name'] : '');
        if ($name === '') { return 'نام کالا را بنویسید.'; }
        $dup = $db->get_var($db->prepare('SELECT id FROM `' . self::t('products') . '` WHERE name = %s AND id <> %d', $name, (int) $id));
        if ($dup) { return 'کالای دیگری با همین نام هست (شمارهٔ ' . $dup . ').'; }
        $row = array('name' => $name, 'updated' => self::now());
        if (array_key_exists('category', $d) || !$old) { $row['category'] = isset($d['category'], MZC_Crm::$categories[$d['category']]) ? $d['category'] : ($old ? $old['category'] : 'other'); }
        foreach (array('brand' => 80, 'sku' => 120, 'notes' => 255) as $k => $max) { if (array_key_exists($k, $d)) { $row[$k] = Mazesta_Connect::clip((string) $d[$k], $max) ?: null; } }
        foreach (array('buy_price', 'sell_price', 'warranty_months') as $k) { if (array_key_exists($k, $d)) { $row[$k] = self::money_in($d[$k]); } }
        if ($id) { return $db->update(self::t('products'), $row, array('id' => (int) $id)) === false ? 'ذخیره نشد: ' . $db->last_error : (int) $id; }
        $row['created'] = self::now();
        return $db->insert(self::t('products'), $row) ? (int) $db->insert_id : 'ذخیره نشد: ' . $db->last_error;
    }

    /** A product a form names: by id, else by exact name, else (with $create) a new one. Returns the row or null. */
    public static function resolve($pid, $name, $category = 'other', $create = false)
    {
        if ($pid && ($p = self::product($pid))) { return $p; }
        if ($p = self::product_by_name($name)) { return $p; }
        if (!$create || trim((string) $name) === '') { return null; }
        $id = self::save_product(0, array('name' => $name, 'category' => $category));
        return is_int($id) ? self::product($id) : null;
    }

    /** The products for a form's picker (the page fills price and category from it): id, name, category, prices, stock. */
    public static function options()
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return array(); }
        return $db->get_results('SELECT p.id, p.name, p.category, p.buy_price, p.sell_price, p.warranty_months, ' . self::stock_sql() . ' AS stock FROM `' . self::t('products') . '` p ORDER BY p.name LIMIT 3000', ARRAY_A) ?: array();
    }

    /**
     * Everything about one product: the purchase lines it came in on (when, from whom, how many, at what cost) and where it went (the new computer or service, the customer,
     * when, at what price), newest first, with the totals.
     */
    public static function report($id)
    {
        $db = MZC_Crm_Db::db(); $p = self::product($id); if (!$db || !$p) { return null; }
        $bought = $db->get_results($db->prepare('SELECT i.qty, i.unit_cost, i.serials, u.id AS purchase_id, u.invoice_no, u.bought_at, v.id AS vendor_id, v.name AS vendor_name FROM `' . self::t('purchase_items') . '` i JOIN `' . self::t('purchases')
            . '` u ON u.id = i.purchase_id LEFT JOIN `' . self::t('vendors') . '` v ON v.id = u.vendor_id WHERE i.product_id = %d ORDER BY u.bought_at DESC, u.id DESC', (int) $id), ARRAY_A) ?: array();
        $b = $db->get_results($db->prepare("SELECT 'build' AS kind, b.id, COALESCE(NULLIF(b.invoice_no, ''), b.service_no) AS number, c.id AS customer_id, c.name AS customer_name, COALESCE(b.sold_at, DATE(b.created)) AS at, 1 AS qty, x.sell_price AS price, x.buy_price AS cost, x.serial FROM `"
            . self::t('build_parts') . '` x JOIN `' . self::t('builds') . '` b ON b.id = x.build_id JOIN `' . self::t('customers') . '` c ON c.id = b.customer_id WHERE x.product_id = %d', (int) $id), ARRAY_A) ?: array();
        $j = $db->get_results($db->prepare("SELECT 'job' AS kind, j.id, j.service_no AS number, c.id AS customer_id, c.name AS customer_name, COALESCE(j.received_at, DATE(j.created)) AS at, y.qty, y.unit_price AS price, y.buy_price AS cost, y.serial FROM `"
            . self::t('job_parts') . '` y JOIN `' . self::t('jobs') . '` j ON j.id = y.job_id JOIN `' . self::t('customers') . "` c ON c.id = j.customer_id WHERE y.product_id = %d AND y.kind <> 'in'", (int) $id), ARRAY_A) ?: array();
        $used = array_merge($b, $j);
        usort($used, function ($x, $y) { return strcmp((string) $y['at'], (string) $x['at']); });
        $sum = function ($rows, $f) { $s = 0; foreach ($rows as $r) { $s += $f($r); } return $s; };
        return array('product' => $p, 'bought' => $bought, 'used' => $used, 'totals' => array(
            'bought' => $sum($bought, function ($r) { return (int) $r['qty']; }), 'used' => $sum($used, function ($r) { return (int) $r['qty']; }), 'stock' => (int) $p['stock'],
            'spent' => $sum($bought, function ($r) { return (int) $r['qty'] * (int) $r['unit_cost']; }), 'sold' => $sum($used, function ($r) { return (int) $r['qty'] * (int) $r['price']; })));
    }

    /* ---------- suppliers (vendors of kind 'supplier') ---------- */

    /** Every vendor a purchase may name - suppliers first, then the warranty companies and repair shops (a part can be bought from one of those too) - with how many invoices each has. */
    public static function suppliers()
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return array(); }
        return $db->get_results('SELECT v.*, (SELECT COUNT(*) FROM `' . self::t('purchases') . '` u WHERE u.vendor_id = v.id) AS purchases, (SELECT MAX(u.bought_at) FROM `' . self::t('purchases') . "` u WHERE u.vendor_id = v.id) AS last_at
            FROM `" . self::t('vendors') . "` v ORDER BY (v.kind = 'supplier') DESC, v.name", ARRAY_A) ?: array();
    }

    public static function save_supplier($id, $d)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return MZC_Crm_Db::error(); }
        $name = Mazesta_Connect::clip(isset($d['name']) ? (string) $d['name'] : '', 120);
        if ($name === '') { return 'نام فروشنده را بنویسید.'; }
        $row = array('name' => $name, 'kind' => isset($d['kind'], MZC_Ships::$kinds[$d['kind']]) ? $d['kind'] : 'supplier', 'phone' => Mazesta_Connect::clip(isset($d['phone']) ? (string) $d['phone'] : '', 60) ?: null, 'notes' => Mazesta_Connect::clip(isset($d['notes']) ? (string) $d['notes'] : '', 255) ?: null);
        $dup = $db->get_var($db->prepare('SELECT id FROM `' . self::t('vendors') . '` WHERE name = %s AND id <> %d', $name, (int) $id));
        if ($dup) { return $id ? 'نام «' . $name . '» برای فروشندهٔ دیگری ثبت شده است.' : (int) $dup; }   // a new one by a known name is that vendor, left as it was
        if ($id) { return $db->update(self::t('vendors'), $row, array('id' => (int) $id)) === false ? 'ذخیره نشد: ' . $db->last_error : (int) $id; }
        return $db->insert(self::t('vendors'), $row) ? (int) $db->insert_id : 'ذخیره نشد: ' . $db->last_error;
    }

    /* ---------- purchase invoices ---------- */

    private static function serial_list($v)
    {
        $list = is_array($v) ? $v : preg_split('/[\r\n,،]+/u', (string) $v);
        $out = array();
        foreach ($list as $s) { $s = Mazesta_Connect::clip(is_string($s) ? $s : '', 120); if ($s !== '' && !in_array($s, $out, true)) { $out[] = $s; } }
        return array_slice($out, 0, 500);
    }

    public static function purchase($id)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return null; }
        $u = $db->get_row($db->prepare('SELECT u.*, v.name AS vendor_name FROM `' . self::t('purchases') . '` u LEFT JOIN `' . self::t('vendors') . '` v ON v.id = u.vendor_id WHERE u.id = %d', (int) $id), ARRAY_A);
        if (!$u) { return null; }
        $u['items'] = $db->get_results($db->prepare('SELECT i.*, p.name AS product_name, p.category FROM `' . self::t('purchase_items') . '` i LEFT JOIN `' . self::t('products') . '` p ON p.id = i.product_id WHERE i.purchase_id = %d ORDER BY i.id', (int) $id), ARRAY_A) ?: array();
        $u['total'] = 0;
        foreach ($u['items'] as &$i) { $i['serial_list'] = self::serial_list((string) $i['serials']); $u['total'] += (int) $i['qty'] * (int) $i['unit_cost']; }
        return $u;
    }

    /** A page of purchase invoices (invoice number, supplier, a product or serial on them), newest first, with the count of lines and the total. */
    public static function purchases($q, $vendor, $page, $per = 30)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return array(array(), 0); }
        $where = array('1=1'); $args = array(); $it = self::t('purchase_items');
        if ($vendor) { $where[] = 'u.vendor_id = %d'; $args[] = (int) $vendor; }
        if ($q !== '') {
            $l = '%' . $db->esc_like($q) . '%';
            $where[] = '(u.invoice_no LIKE %s OR v.name LIKE %s OR u.notes LIKE %s OR EXISTS (SELECT 1 FROM `' . $it . '` i JOIN `' . self::t('products') . '` p ON p.id = i.product_id WHERE i.purchase_id = u.id AND (p.name LIKE %s OR i.serials LIKE %s)))';
            array_push($args, $l, $l, $l, $l, $l);
        }
        $from = 'FROM `' . self::t('purchases') . '` u LEFT JOIN `' . self::t('vendors') . '` v ON v.id = u.vendor_id WHERE ' . implode(' AND ', $where);
        $total = (int) $db->get_var($args ? $db->prepare("SELECT COUNT(*) $from", $args) : "SELECT COUNT(*) $from");
        $rows = $db->get_results($db->prepare("SELECT u.*, v.name AS vendor_name, (SELECT COUNT(*) FROM `$it` i WHERE i.purchase_id = u.id) AS line_count, (SELECT COALESCE(SUM(i.qty * i.unit_cost), 0) FROM `$it` i WHERE i.purchase_id = u.id) AS total
            $from ORDER BY u.bought_at DESC, u.id DESC LIMIT %d OFFSET %d", array_merge($args, array((int) $per, (int) (($page - 1) * $per)))), ARRAY_A);
        return array($rows ?: array(), $total);
    }

    /**
     * Creates or updates a purchase invoice. $d: vendor_id or vendor_name (a new name becomes a supplier), invoice_no, bought_at (Y-m-d), notes, force (save although the supplier already
     * has an invoice with this number), items: [product_id or name (+ category; an unknown name becomes a new product), qty, unit_cost, sell_price (optional: the product's new price),
     * serials (list or one per line; more serials than qty raises qty)]. A saved line makes its price the product's latest buy price. Returns the id or an error text (or
     * array('duplicate' => id) when the invoice number is taken).
     */
    public static function save_purchase($id, $d)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return MZC_Crm_Db::error(); }
        if ($id && !self::purchase($id)) { return 'فاکتور خرید پیدا نشد.'; }
        $vid = isset($d['vendor_id']) ? (int) $d['vendor_id'] : 0;
        if ($vid && !$db->get_var($db->prepare('SELECT id FROM `' . self::t('vendors') . '` WHERE id = %d', $vid))) { $vid = 0; }
        if (!$vid && !empty($d['vendor_name'])) { $vid = self::save_supplier(0, array('name' => (string) $d['vendor_name'])); if (!is_int($vid)) { return $vid; } }
        if (!$vid) { return 'فروشنده را از فهرست انتخاب کنید (یا نام فروشندهٔ تازه را بنویسید).'; }
        $inv = Mazesta_Connect::clip(MZC_Crm::digits(isset($d['invoice_no']) ? (string) $d['invoice_no'] : ''), 60);
        if ($inv !== '' && empty($d['force'])) {
            $dup = (int) $db->get_var($db->prepare('SELECT id FROM `' . self::t('purchases') . '` WHERE vendor_id = %d AND invoice_no = %s AND id <> %d LIMIT 1', $vid, $inv, (int) $id));
            if ($dup) { return array('duplicate' => $dup); }
        }
        $items = array();
        foreach (isset($d['items']) && is_array($d['items']) ? $d['items'] : array() as $x) {
            if (!is_array($x)) { continue; }
            $name = Mazesta_Connect::clip(isset($x['name']) ? (string) $x['name'] : '', 190); $pid = isset($x['product_id']) ? (int) $x['product_id'] : 0;
            if (!$pid && $name === '') { continue; }
            $serials = self::serial_list(isset($x['serials']) ? $x['serials'] : '');
            $qty = max(1, (int) self::money_in(isset($x['qty']) ? $x['qty'] : 1), count($serials));
            $items[] = array('pid' => $pid, 'name' => $name, 'category' => isset($x['category']) ? (string) $x['category'] : 'other', 'qty' => $qty, 'unit_cost' => (int) self::money_in(isset($x['unit_cost']) ? $x['unit_cost'] : 0),
                'sell' => isset($x['sell_price']) && $x['sell_price'] !== '' && $x['sell_price'] !== null ? self::money_in($x['sell_price']) : null, 'serials' => $serials);
        }
        if (!$items) { return 'دست‌کم یک کالا در فاکتور بنویسید.'; }
        $date = isset($d['bought_at']) && preg_match('/^\d{4}-\d{2}-\d{2}$/', (string) $d['bought_at']) ? (string) $d['bought_at'] : gmdate('Y-m-d');
        $row = array('vendor_id' => $vid, 'invoice_no' => $inv !== '' ? $inv : null, 'bought_at' => $date, 'notes' => isset($d['notes']) ? mb_substr(trim(wp_strip_all_tags((string) $d['notes'])), 0, 2000) : null, 'updated' => self::now());
        $db->query('START TRANSACTION'); $ok = true; $err = '';
        if ($id) { $ok = $db->update(self::t('purchases'), $row, array('id' => (int) $id)) !== false; }
        else { $row['created'] = self::now(); $ok = (bool) $db->insert(self::t('purchases'), $row); $id = (int) $db->insert_id; }
        if ($ok) { $db->delete(self::t('purchase_items'), array('purchase_id' => (int) $id)); }
        foreach ($items as $x) {
            if (!$ok) { break; }
            $p = self::resolve($x['pid'], $x['name'], $x['category'], true);
            if (!$p) { $ok = false; $err = 'کالای «' . $x['name'] . '» ساخته نشد.'; break; }
            $ok = (bool) $db->insert(self::t('purchase_items'), array('purchase_id' => (int) $id, 'product_id' => (int) $p['id'], 'qty' => $x['qty'], 'unit_cost' => $x['unit_cost'], 'serials' => $x['serials'] ? implode("\n", $x['serials']) : null));
            $upd = array('buy_price' => $x['unit_cost'], 'updated' => self::now()); if ($x['sell'] !== null) { $upd['sell_price'] = $x['sell']; }
            if ($ok) { $db->update(self::t('products'), $upd, array('id' => (int) $p['id'])); }
        }
        $db->query($ok ? 'COMMIT' : 'ROLLBACK');
        return $ok ? (int) $id : ($err !== '' ? $err : 'ذخیره نشد: ' . $db->last_error);
    }

    /** Where a serial number has been: bought on which invoice, put in which computer or service, sent to which warranty company. */
    public static function trace($serial)
    {
        $db = MZC_Crm_Db::db(); $serial = trim((string) $serial); $out = array('bought' => array(), 'builds' => array(), 'jobs' => array(), 'ships' => array());
        if (!$db || mb_strlen($serial) < 3) { return $out; }
        $l = '%' . $db->esc_like($serial) . '%';
        foreach ($db->get_results($db->prepare('SELECT i.serials, i.unit_cost, p.id AS product_id, p.name AS product_name, u.id AS purchase_id, u.invoice_no, u.bought_at, v.name AS vendor_name FROM `' . self::t('purchase_items') . '` i JOIN `' . self::t('purchases') . '` u ON u.id = i.purchase_id
            LEFT JOIN `' . self::t('products') . '` p ON p.id = i.product_id LEFT JOIN `' . self::t('vendors') . '` v ON v.id = u.vendor_id WHERE i.serials LIKE %s LIMIT 20', $l), ARRAY_A) ?: array() as $r) {
            foreach (self::serial_list((string) $r['serials']) as $s) { if (stripos($s, $serial) !== false) { unset($r['serials']); $out['bought'][] = $r + array('serial' => $s); } }
        }
        $out['builds'] = $db->get_results($db->prepare('SELECT b.id, b.invoice_no, b.service_no, b.title, b.sold_at, c.name AS customer_name, x.model, x.serial FROM `' . self::t('build_parts') . '` x JOIN `' . self::t('builds') . '` b ON b.id = x.build_id JOIN `' . self::t('customers') . '` c ON c.id = b.customer_id WHERE x.serial LIKE %s LIMIT 20', $l), ARRAY_A) ?: array();
        $out['jobs'] = $db->get_results($db->prepare('SELECT j.id, j.service_no, j.device, j.received_at, c.name AS customer_name, y.name AS part, y.serial, y.kind FROM `' . self::t('job_parts') . '` y JOIN `' . self::t('jobs') . '` j ON j.id = y.job_id JOIN `' . self::t('customers') . '` c ON c.id = j.customer_id WHERE y.serial LIKE %s LIMIT 20', $l), ARRAY_A) ?: array();
        $out['ships'] = $db->get_results($db->prepare('SELECT s.id, s.part_name, s.serial, s.back_serial, s.sent_at, s.received_at, v.name AS vendor_name FROM `' . self::t('shipments') . '` s LEFT JOIN `' . self::t('vendors') . '` v ON v.id = s.vendor_id WHERE s.serial LIKE %s OR s.back_serial LIKE %s LIMIT 20', $l, $l), ARRAY_A) ?: array();
        return $out;
    }

    /**
     * Links the part rows of a form to products: a row with a product_id (or whose name is exactly a product's) gets the product_id, and any price the row left empty is taken from
     * the product. $name is the row's name field ('model' for a computer, 'name' for a service); $prices maps a row field to the product's ('sell_price' => 'sell_price').
     */
    public static function link_parts($rows, $name, $prices)
    {
        foreach ($rows as &$r) {
            $p = self::resolve(isset($r['product_id']) ? (int) $r['product_id'] : 0, isset($r[$name]) ? $r[$name] : '');
            $r['product_id'] = $p ? (int) $p['id'] : null;
            if (!$p) { continue; }
            if (empty($r[$name])) { $r[$name] = $p['name']; }
            foreach ($prices as $f => $pf) { if (!isset($r[$f]) || $r[$f] === null || $r[$f] === '') { $r[$f] = $p[$pf] === null ? null : (int) $p[$pf]; } }
            if (array_key_exists('warranty_months', $r) && ($r['warranty_months'] === null || $r['warranty_months'] === '') && $p['warranty_months'] !== null) { $r['warranty_months'] = (int) $p['warranty_months']; }
        }
        return $rows;
    }

    /* ---------- admin actions ---------- */

    public static function act_product_save()
    {
        MZC_Admin::guard('product_save');
        $id = isset($_POST['id']) ? (int) $_POST['id'] : 0; $d = array();
        foreach (array('name', 'category', 'brand', 'sku', 'buy_price', 'sell_price', 'warranty_months', 'notes') as $k) { if (isset($_POST[$k])) { $d[$k] = wp_unslash($_POST[$k]); } }
        $r = self::save_product($id, $d);
        if (!is_int($r)) { MZC_Admin::back('mzc-stock', 'err:' . $r, $id ? array('edit' => $id) : array('add' => 1)); }
        MZC_Admin::back('mzc-stock', 'ok:کالا ذخیره شد.', array('view' => $r));
    }

    public static function act_product_delete()
    {
        MZC_Admin::guard('product_delete');
        $db = MZC_Crm_Db::db(); $id = isset($_GET['id']) ? (int) $_GET['id'] : 0; $rep = $db && $id ? self::report($id) : null;
        if (!$rep) { MZC_Admin::back('mzc-stock', 'err:کالا پیدا نشد.'); }
        if ($rep['bought'] || $rep['used']) { MZC_Admin::back('mzc-stock', 'err:این کالا در فاکتور خرید یا فرم‌ها آمده است؛ اول آن‌ها را پاک کنید.', array('view' => $id)); }
        $db->delete(self::t('products'), array('id' => $id));
        MZC_Admin::back('mzc-stock', 'ok:کالا پاک شد.');
    }

    public static function act_purchase_save()
    {
        MZC_Admin::guard('purchase_save');
        $id = isset($_POST['id']) ? (int) $_POST['id'] : 0;
        $items = isset($_POST['items']) && is_array($_POST['items']) ? wp_unslash($_POST['items']) : array();
        $r = self::save_purchase($id, array('vendor_id' => isset($_POST['vendor_id']) ? (int) $_POST['vendor_id'] : 0, 'vendor_name' => MZC_Crm::post('vendor_new', 120), 'invoice_no' => MZC_Crm::post('invoice_no', 60),
            'bought_at' => MZC_Crm::date('bought_at'), 'notes' => MZC_Crm::post_long('notes', 2000), 'force' => !empty($_POST['force']), 'items' => array_values($items)));
        if (is_array($r)) { MZC_Admin::back('mzc-purchases', 'err:این فروشنده فاکتوری با همین شماره دارد (فاکتور ' . $r['duplicate'] . '). اگر درست است، «با وجود شمارهٔ تکراری ثبت شود» را بزنید.', $id ? array('edit' => $id) : array('add' => 1)); }
        if (!is_int($r)) { MZC_Admin::back('mzc-purchases', 'err:' . $r, $id ? array('edit' => $id) : array('add' => 1)); }
        MZC_Admin::back('mzc-purchases', 'ok:فاکتور خرید ثبت شد.', array('view' => $r));
    }

    public static function act_purchase_delete()
    {
        MZC_Admin::guard('purchase_delete');
        $db = MZC_Crm_Db::db(); $id = isset($_GET['id']) ? (int) $_GET['id'] : 0;
        if ($db && $id) { $db->delete(self::t('purchase_items'), array('purchase_id' => $id)); $db->delete(self::t('purchases'), array('id' => $id)); }
        MZC_Admin::back('mzc-purchases', 'ok:فاکتور خرید پاک شد.');
    }
}
