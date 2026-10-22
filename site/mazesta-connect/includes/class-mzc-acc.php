<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The accounting, in the CRM's own database: sales and return invoices, proformas and the ready-made invoice templates (one table, `kind` tells them apart), the money that
 * came in or went out (a customer's deposit, a payment to a supplier, an expense) and the cheques the shop received or wrote, with each customer's and supplier's account.
 * A draft is edited freely; a final invoice is never changed again - it is voided (with a reason) or answered with a return invoice, so what was printed stays what is stored.
 * Numbers are given when an invoice becomes final (a proforma when it is made), one sequence per kind, under a database lock so two desks never get the same one.
 * Stock: the lines of a final sale take products out and a final return puts them back, except on an invoice tied to a new computer - that computer's parts already count.
 * The staff API (MZC_Acc_Api), the admin pages and the WooCommerce orders (MZC_Woo) all go through the functions here, which take plain arrays and return an id or an error text.
 */
final class MZC_Acc
{
    public static $kinds = array('sale' => 'فاکتور فروش', 'return' => 'برگشت از فروش', 'proforma' => 'پیش‌فاکتور', 'template' => 'فاکتور آماده');
    public static $statuses = array('draft' => 'پیش‌نویس', 'final' => 'قطعی', 'void' => 'باطل');
    public static $methods = array('cash' => 'نقد', 'pos' => 'کارتخوان', 'card' => 'کارت به کارت', 'transfer' => 'پایا / ساتنا / حواله', 'cheque' => 'چک', 'online' => 'درگاه سایت');
    public static $cheque_statuses = array('pending' => 'در جریان', 'cleared' => 'پاس شد', 'bounced' => 'برگشت خورد', 'passed' => 'به دیگری داده شد', 'cancelled' => 'باطل');
    /** A payment made with one of these cheques never reached anyone: it is left out of every balance. */
    const DEAD_CHEQUE = "'bounced','cancelled'";
    const EXPENSES = "اجاره\nحقوق و دستمزد\nقبوض (آب، برق، گاز، تلفن، اینترنت)\nپیک و ارسال\nتبلیغات\nملزومات\nتعمیرات\nمالیات و عوارض\nکارمزد بانک\nسایر";

    private static function t($n) { return MZC_Crm_Db::t($n); }
    private static function now() { return gmdate('Y-m-d H:i:s'); }
    /** Toman from a typed value ("12,500,000", Persian digits); null for empty. */
    public static function m($v) { return $v === null || $v === '' || is_array($v) ? null : (int) preg_replace('/[^\d]/', '', MZC_Crm::digits((string) $v)); }
    private static function clip($v, $max) { return is_scalar($v) ? Mazesta_Connect::clip((string) $v, $max) : ''; }
    private static function date_in($v, $def = null) { $d = is_scalar($v) ? MZC_Crm::gdate((string) $v) : null; return $d ?: $def; }

    /* ---------- settings (meta 'acc') ---------- */

    /** start: the first sales invoice number; tax: VAT percent offered on new invoices; remind: days before a cheque's due date it is shown; expenses: the expense headings. */
    public static function settings()
    {
        $db = MZC_Crm_Db::db(); $v = $db ? $db->get_var('SELECT v FROM `' . self::t('meta') . "` WHERE k = 'acc'") : null;
        $s = is_string($v) ? json_decode($v, true) : null;
        return (is_array($s) ? $s : array()) + array('start' => 1, 'tax' => 0, 'remind' => 3, 'expenses' => self::EXPENSES, 'shop' => '', 'phone' => '', 'address' => '', 'foot' => '');
    }

    public static function save_settings($d)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return; }
        $s = array('start' => max(1, (int) self::m(isset($d['start']) ? $d['start'] : 1)), 'tax' => min(100, max(0, round((float) MZC_Crm::digits(isset($d['tax']) ? (string) $d['tax'] : '0'), 2))),
            'remind' => min(60, max(0, (int) (isset($d['remind']) ? $d['remind'] : 3))), 'expenses' => implode("\n", array_slice(array_filter(array_map(function ($x) { return mb_substr(trim($x), 0, 60); }, preg_split('/[\r\n]+/', isset($d['expenses']) ? (string) $d['expenses'] : ''))), 0, 60)));
        foreach (array('shop' => 120, 'phone' => 60, 'address' => 255, 'foot' => 300) as $k => $max) { $s[$k] = self::clip(isset($d[$k]) ? $d[$k] : '', $max); }
        $db->query($db->prepare('REPLACE INTO `' . self::t('meta') . "` (k, v) VALUES ('acc', %s)", wp_json_encode($s, JSON_UNESCAPED_UNICODE)));
    }

    public static function expense_heads() { return array_values(array_filter(array_map('trim', explode("\n", (string) self::settings()['expenses'])))); }

    /* ---------- invoices ---------- */

    /** The SQL of what has been paid on the invoice row aliased `v` (payments by a bounced or void cheque left out). */
    private static function paid_sql()
    {
        return '(SELECT COALESCE(SUM(CASE y.direction WHEN \'in\' THEN y.amount ELSE -y.amount END), 0) FROM `' . self::t('payments') . '` y LEFT JOIN `' . self::t('cheques') . '` q ON q.id = y.cheque_id WHERE y.invoice_id = v.id AND (q.id IS NULL OR q.status NOT IN (' . self::DEAD_CHEQUE . ')))';
    }

    public static function invoice($id)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return null; }
        $v = $db->get_row($db->prepare('SELECT v.*, c.name AS customer_name, c.mobile AS customer_mobile, c.address AS customer_address, c.national_id AS customer_national_id, ' . self::paid_sql() . ' AS paid FROM `' . self::t('invoices')
            . '` v LEFT JOIN `' . self::t('customers') . '` c ON c.id = v.customer_id WHERE v.id = %d', (int) $id), ARRAY_A);
        if (!$v) { return null; }
        $v['items'] = $db->get_results($db->prepare('SELECT * FROM `' . self::t('invoice_items') . '` WHERE invoice_id = %d ORDER BY id', (int) $id), ARRAY_A) ?: array();
        $v['payments'] = $db->get_results($db->prepare('SELECT y.*, q.no AS cheque_no, q.status AS cheque_status, q.due_at AS cheque_due FROM `' . self::t('payments') . '` y LEFT JOIN `' . self::t('cheques') . '` q ON q.id = y.cheque_id WHERE y.invoice_id = %d ORDER BY y.at, y.id', (int) $id), ARRAY_A) ?: array();
        return $v;
    }

    /** A page of invoices. $f: kind, status, q (number, customer, title, a product or serial on it), customer, from, to (Y-m-d). Newest first, with the customer's name and what was paid. */
    public static function invoices($f, $page, $per = 30)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return array(array(), 0); }
        $where = array('1=1'); $args = array();
        if (!empty($f['kind']) && isset(self::$kinds[$f['kind']])) { $where[] = 'v.kind = %s'; $args[] = $f['kind']; } else { $where[] = "v.kind <> 'template'"; }
        if (!empty($f['status']) && isset(self::$statuses[$f['status']])) { $where[] = 'v.status = %s'; $args[] = $f['status']; }
        if (!empty($f['customer'])) { $where[] = 'v.customer_id = %d'; $args[] = (int) $f['customer']; }
        if (!empty($f['from'])) { $where[] = 'v.at >= %s'; $args[] = $f['from']; }
        if (!empty($f['to'])) { $where[] = 'v.at <= %s'; $args[] = $f['to']; }
        if (isset($f['q']) && $f['q'] !== '') {
            $q = MZC_Crm::digits($f['q']); $l = '%' . $db->esc_like($q) . '%';
            $where[] = '(CAST(v.no AS CHAR) = %s OR v.title LIKE %s OR c.name LIKE %s OR c.mobile LIKE %s OR EXISTS (SELECT 1 FROM `' . self::t('invoice_items') . '` i WHERE i.invoice_id = v.id AND (i.name LIKE %s OR i.serials LIKE %s)))';
            array_push($args, $q, $l, $l, $l, $l, $l);
        }
        $from = 'FROM `' . self::t('invoices') . '` v LEFT JOIN `' . self::t('customers') . '` c ON c.id = v.customer_id WHERE ' . implode(' AND ', $where);
        $total = (int) $db->get_var($args ? $db->prepare("SELECT COUNT(*) $from", $args) : "SELECT COUNT(*) $from");
        $rows = $db->get_results($db->prepare('SELECT v.*, c.name AS customer_name, c.mobile AS customer_mobile, (SELECT COUNT(*) FROM `' . self::t('invoice_items') . '` i WHERE i.invoice_id = v.id) AS line_count, ' . self::paid_sql()
            . " AS paid $from ORDER BY COALESCE(v.at, DATE(v.created)) DESC, v.id DESC LIMIT %d OFFSET %d", array_merge($args, array((int) $per, (int) (($page - 1) * $per)))), ARRAY_A);
        return array($rows ?: array(), $total);
    }

    /**
     * The next number of a kind: on from the highest used, a sales invoice never below the start set on the settings page (the old accounting program's next number) and never
     * one a new computer was already typed with by hand (that number is skipped, not followed: one mistyped huge number must not move the whole sequence). Call inside the lock.
     */
    private static function next_no($db, $kind)
    {
        $n = (int) $db->get_var($db->prepare('SELECT MAX(no) FROM `' . self::t('invoices') . '` WHERE kind = %s', $kind)) + 1;
        if ($kind !== 'sale') { return $n; }
        $n = max($n, (int) self::settings()['start']);
        while ($db->get_var($db->prepare('SELECT id FROM `' . self::t('builds') . '` WHERE invoice_no = %s LIMIT 1', (string) $n))) { $n++; }
        return $n;
    }

    private static function locked($db, $work)
    {
        $lock = $db->get_var("SELECT GET_LOCK('" . self::t('inv') . "', 8)");
        if (!$lock) { return 'سامانه مشغول است؛ چند ثانیه بعد دوباره امتحان کنید.'; }
        try { return $work(); } finally { $db->query("DO RELEASE_LOCK('" . self::t('inv') . "')"); }
    }

    /** Cleans the lines of a save: a product named by id (or by its exact name) gives the line its name, category, price, cost and warranty when the line leaves them empty. */
    private static function lines($items)
    {
        $out = array();
        foreach (is_array($items) ? array_slice($items, 0, 300) : array() as $x) {
            if (!is_array($x)) { continue; }
            $name = self::clip(isset($x['name']) ? $x['name'] : '', 190); $pid = isset($x['product_id']) ? (int) $x['product_id'] : 0;
            $p = $pid || $name !== '' ? MZC_Stock::resolve($pid, $name) : null;
            if (!$p && $name === '') { continue; }
            $qty = isset($x['qty']) ? (int) self::m($x['qty']) : 1;
            if ($qty < 1 || $qty > 100000) { return 'تعداد «' . ($name ?: $p['name']) . '» درست نیست.'; }
            $price = self::m(isset($x['unit_price']) ? $x['unit_price'] : null); $cost = self::m(isset($x['buy_price']) ? $x['buy_price'] : null); $w = self::m(isset($x['warranty_months']) ? $x['warranty_months'] : null);
            $cat = isset($x['category'], MZC_Crm::$categories[$x['category']]) ? (string) $x['category'] : ($p ? $p['category'] : 'other');
            $serials = array();
            foreach (is_array(isset($x['serials']) ? $x['serials'] : null) ? $x['serials'] : preg_split('/[\r\n,،]+/u', isset($x['serials']) && is_scalar($x['serials']) ? (string) $x['serials'] : '') as $s) { $s = self::clip($s, 120); if ($s !== '' && !in_array($s, $serials, true)) { $serials[] = $s; } }
            $out[] = array('product_id' => $p ? (int) $p['id'] : null, 'category' => $cat, 'name' => $name !== '' ? $name : $p['name'], 'qty' => $qty,
                'unit_price' => $price !== null ? $price : ($p && $p['sell_price'] !== null ? (int) $p['sell_price'] : 0), 'buy_price' => $cost !== null ? $cost : ($p && $p['buy_price'] !== null ? (int) $p['buy_price'] : null),
                'serials' => $serials ? implode("\n", array_slice($serials, 0, 500)) : null, 'warranty_months' => $w !== null ? min(240, $w) : ($p && $p['warranty_months'] !== null ? (int) $p['warranty_months'] : null));
        }
        return $out;
    }

    /** subtotal, discount (at most the subtotal), tax (VAT on what is left, rounded to the toman) and the total. */
    public static function totals($lines, $discount, $taxPct)
    {
        $sub = 0; foreach ($lines as $l) { $sub += (int) $l['qty'] * (int) $l['unit_price']; }
        $disc = min($sub, max(0, (int) $discount)); $pct = min(100, max(0, round((float) $taxPct, 2)));
        $tax = (int) round(($sub - $disc) * $pct / 100);
        return array('subtotal' => $sub, 'discount' => $disc, 'tax_pct' => $pct, 'tax' => $tax, 'total' => $sub - $disc + $tax);
    }

    /**
     * Creates (no id: $d['kind'] says what) or changes a draft, a proforma that is still open, or a template. $d: customer_id, title, at, valid_until, discount, tax_pct, notes,
     * items (product_id or name, category, qty, unit_price, buy_price, serials, warranty_months). Keys left out keep their value. Returns the id or an error text.
     */
    public static function save_invoice($id, $d, $uid = 0)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return MZC_Crm_Db::error(); }
        $old = $id ? self::invoice($id) : null;
        if ($id && !$old) { return 'فاکتور پیدا نشد.'; }
        if ($old && $old['status'] !== 'draft') { return $old['status'] === 'final' ? 'فاکتور قطعی تغییر نمی‌کند؛ باطلش کنید یا فاکتور برگشت بزنید.' : 'فاکتور باطل‌شده تغییر نمی‌کند.'; }
        $kind = $old ? $old['kind'] : (isset($d['kind'], self::$kinds[$d['kind']]) ? $d['kind'] : 'sale');
        $has = function ($k) use ($d) { return array_key_exists($k, $d); };
        $cid = $has('customer_id') ? (int) $d['customer_id'] : ($old ? (int) $old['customer_id'] : 0);
        if ($cid && !MZC_Crm::customer($cid)) { return 'مشتری پیدا نشد.'; }
        if ($kind === 'template') { $cid = 0; }
        $lines = $has('items') ? self::lines($d['items']) : null;
        if (is_string($lines)) { return $lines; }
        if ($lines === null) { $lines = $old ? array_map(function ($x) { unset($x['id'], $x['invoice_id']); return $x; }, $old['items']) : array(); }
        if ($kind === 'template' && !$lines) { return 'دست‌کم یک ردیف در فاکتور آماده بنویسید.'; }
        $title = $has('title') ? self::clip($d['title'], 190) : ($old ? (string) $old['title'] : '');
        if ($kind === 'template' && $title === '') { return 'برای فاکتور آماده یک نام بنویسید (مثلاً «سیستم گیمینگ اقتصادی»).'; }
        $sum = self::totals($lines, $has('discount') ? self::m($d['discount']) : ($old ? $old['discount'] : 0), $has('tax_pct') ? (float) MZC_Crm::digits((string) $d['tax_pct']) : ($old ? $old['tax_pct'] : self::settings()['tax']));
        $row = array('customer_id' => $cid ?: null, 'title' => $title !== '' ? $title : null, 'at' => $has('at') ? self::date_in($d['at'], gmdate('Y-m-d')) : ($old ? $old['at'] : gmdate('Y-m-d')),
            'valid_until' => $kind === 'proforma' ? ($has('valid_until') ? self::date_in($d['valid_until']) : ($old ? $old['valid_until'] : gmdate('Y-m-d', time() + 3 * 86400))) : null,
            'notes' => $has('notes') ? mb_substr(trim(sanitize_textarea_field((string) $d['notes'])), 0, 2000) : ($old ? $old['notes'] : null), 'updated' => self::now()) + $sum;
        if (!$old) { $row += array('kind' => $kind, 'status' => 'draft', 'created_by' => (int) $uid, 'created' => self::now(), 'ref_id' => !empty($d['ref_id']) ? (int) $d['ref_id'] : null, 'build_id' => !empty($d['build_id']) ? (int) $d['build_id'] : null); }
        $work = function () use ($db, $id, $old, $row, $lines, $kind) {
            $db->query('START TRANSACTION'); $ok = true;
            if ($old) { $ok = $db->update(self::t('invoices'), $row, array('id' => (int) $id)) !== false; }
            else { if ($kind === 'proforma') { $row['no'] = self::next_no($db, 'proforma'); } $ok = (bool) $db->insert(self::t('invoices'), $row); $id = (int) $db->insert_id; }
            if ($ok) { $db->delete(self::t('invoice_items'), array('invoice_id' => (int) $id)); }
            foreach ($lines as $l) { if (!$ok) { break; } $l['invoice_id'] = (int) $id; $ok = (bool) $db->insert(self::t('invoice_items'), $l); }
            $err = $db->last_error; $db->query($ok ? 'COMMIT' : 'ROLLBACK');
            return $ok ? (int) $id : 'ذخیره نشد: ' . $err;
        };
        return !$old && $kind === 'proforma' ? self::locked($db, $work) : $work();
    }

    /**
     * Makes a draft sale or return final: it gets the next number and from now on moves the stock (when not tied to a computer). A sale with $makeBuild becomes a new computer
     * in the assembly queue too, its parts the invoice's lines (one row a unit, the serials shared out), known by the same invoice number. Returns array(id, warnings) or an error text.
     */
    public static function finalize($id, $makeBuild = false, $at = null)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return MZC_Crm_Db::error(); }
        return self::locked($db, function () use ($db, $id, $makeBuild, $at) {
            $v = self::invoice($id);
            if (!$v) { return 'فاکتور پیدا نشد.'; }
            if (!in_array($v['kind'], array('sale', 'return'), true)) { return 'فقط فاکتور فروش و برگشت قطعی می‌شود.'; }
            if ($v['status'] !== 'draft') { return 'این فاکتور پیش‌نویس نیست.'; }
            if (!$v['customer_id']) { return 'مشتری فاکتور را انتخاب کنید.'; }
            if (!$v['items']) { return 'فاکتور ردیفی ندارد.'; }
            $warn = array();
            if ($v['kind'] === 'sale') {
                foreach ($v['items'] as $l) {
                    if (!$l['product_id'] || $v['build_id']) { continue; }
                    $p = MZC_Stock::product($l['product_id']);
                    if ($p && (int) $p['stock'] < (int) $l['qty']) { $warn[] = 'موجودی «' . $p['name'] . '» ' . (int) $p['stock'] . ' است و ' . (int) $l['qty'] . ' عدد فروخته شد.'; }
                }
            }
            $no = self::next_no($db, $v['kind']); $date = $at ? $at : ($v['at'] ?: gmdate('Y-m-d'));
            $db->query('START TRANSACTION');
            $row = array('no' => $no, 'status' => 'final', 'at' => $date, 'updated' => self::now());
            // the cost of each line is fixed now (a later purchase must not change this sale's profit)
            foreach ($v['items'] as $l) { if ($l['buy_price'] === null && $l['product_id'] && ($p = MZC_Stock::product($l['product_id'])) && $p['buy_price'] !== null) { $db->update(self::t('invoice_items'), array('buy_price' => (int) $p['buy_price']), array('id' => (int) $l['id'])); } }
            $ok = $db->update(self::t('invoices'), $row, array('id' => (int) $id)) !== false;
            if ($ok && $v['kind'] === 'sale' && $makeBuild && !$v['build_id']) {
                $b = array('customer_id' => (int) $v['customer_id'], 'invoice_no' => (string) $no, 'sold_at' => $date, 'status' => 'queue', 'delivered' => 0, 'device_type' => 'system', 'notes' => null, 'created' => self::now(), 'updated' => self::now());
                $parts = array();
                foreach ($v['items'] as $l) {
                    if (!$l['product_id']) { continue; }   // a service line (assembly, Windows ...) is not a part of the computer
                    $ser = array_values(array_filter(explode("\n", (string) $l['serials'])));
                    for ($k = 0; $k < (int) $l['qty'] && $k < 50; $k++) {
                        $parts[] = array('product_id' => (int) $l['product_id'], 'category' => $l['category'], 'model' => $l['name'], 'serial' => isset($ser[$k]) ? $ser[$k] : null, 'warranty_months' => $l['warranty_months'] === null ? null : (int) $l['warranty_months'],
                            'has_warranty' => $l['warranty_months'] ? 1 : 0, 'sell_price' => (int) $l['unit_price'], 'buy_price' => $l['buy_price'] === null ? null : (int) $l['buy_price']);
                    }
                }
                $b['title'] = MZC_Crm::build_title($parts);
                $ok = (bool) $db->insert(self::t('builds'), $b); $bid = (int) $db->insert_id;
                foreach ($parts as $x) { if (!$ok) { break; } $x['build_id'] = $bid; $ok = (bool) $db->insert(self::t('build_parts'), $x); }
                if ($ok) { $ok = $db->update(self::t('invoices'), array('build_id' => $bid), array('id' => (int) $id)) !== false; }
            }
            // a sale made from a proforma closes it
            if ($ok && $v['kind'] === 'sale' && $v['ref_id']) { $db->query($db->prepare('UPDATE `' . self::t('invoices') . "` SET status = 'final', updated = %s WHERE id = %d AND kind = 'proforma' AND status = 'draft'", self::now(), (int) $v['ref_id'])); }
            $err = $db->last_error; $db->query($ok ? 'COMMIT' : 'ROLLBACK');
            if (!$ok) { return 'قطعی نشد: ' . $err; }
            MZC_Woo::touch(array_column($v['items'], 'product_id'));
            return array((int) $id, $warn);
        });
    }

    /** Voids a final invoice or an open proforma (a draft is simply changed). Its payments stay as they were: the money is the customer's credit until it is paid back. */
    public static function void_invoice($id, $reason)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return MZC_Crm_Db::error(); }
        $v = self::invoice($id); if (!$v) { return 'فاکتور پیدا نشد.'; }
        if ($v['status'] === 'void') { return 'این فاکتور قبلاً باطل شده است.'; }
        $reason = self::clip($reason, 255); if ($reason === '') { return 'دلیل ابطال را بنویسید.'; }
        if ($v['kind'] === 'template') { return 'فاکتور آماده باطل نمی‌شود؛ پاکش کنید.'; }
        // a return of this sale would then put back parts that never left
        if ($v['kind'] === 'sale' && $db->get_var($db->prepare('SELECT no FROM `' . self::t('invoices') . "` WHERE kind = 'return' AND status = 'final' AND ref_id = %d LIMIT 1", (int) $id))) { return 'این فاکتور برگشت قطعی دارد؛ اول برگشت را باطل کنید.'; }
        $db->update(self::t('invoices'), array('status' => 'void', 'void_reason' => $reason, 'updated' => self::now()), array('id' => (int) $id));
        MZC_Woo::touch(array_column($v['items'], 'product_id'));
        return (int) $id;
    }

    /** Deletes a template or a draft that never became anything (a final invoice is voided instead, so its number is never reused). */
    public static function delete_draft($id)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return MZC_Crm_Db::error(); }
        $v = self::invoice($id); if (!$v) { return 'فاکتور پیدا نشد.'; }
        if ($v['kind'] !== 'template' && ($v['status'] !== 'draft' || $v['no'] || $v['payments'])) { return 'فقط پیش‌نویس بدون شماره و بدون پرداخت پاک می‌شود؛ بقیه را باطل کنید.'; }
        $db->delete(self::t('invoice_items'), array('invoice_id' => (int) $id)); $db->delete(self::t('invoices'), array('id' => (int) $id));
        return (int) $id;
    }

    /**
     * A new draft made from another document: a template (its products at today's price), a proforma (to sell it), a sale (to return it, or to keep it as a template).
     * $kind is what to make. Returns the new id or an error text.
     */
    public static function copy($id, $kind, $uid = 0, $customer = 0)
    {
        $v = self::invoice($id); if (!$v) { return 'فاکتور پیدا نشد.'; }
        if (!isset(self::$kinds[$kind])) { return 'نوع فاکتور درست نیست.'; }
        if ($kind === 'return' && ($v['kind'] !== 'sale' || $v['status'] !== 'final')) { return 'برگشت فقط از فاکتور فروش قطعی زده می‌شود.'; }
        $fresh = $v['kind'] === 'template';   // a template keeps no price of its own for a stock product: today's price is the one to sell at
        $items = array();
        foreach ($v['items'] as $l) {
            $p = $fresh && $l['product_id'] ? MZC_Stock::product($l['product_id']) : null;
            $items[] = array('product_id' => $l['product_id'], 'category' => $l['category'], 'name' => $l['name'], 'qty' => $l['qty'], 'unit_price' => $p && $p['sell_price'] !== null ? $p['sell_price'] : $l['unit_price'],
                'buy_price' => $kind === 'return' ? $l['buy_price'] : null, 'serials' => $kind === 'return' ? $l['serials'] : null, 'warranty_months' => $l['warranty_months']);
        }
        $d = array('kind' => $kind, 'items' => $items, 'discount' => $v['discount'], 'tax_pct' => $v['tax_pct'], 'customer_id' => $customer ?: (int) $v['customer_id'], 'at' => gmdate('Y-m-d'),
            'title' => $kind === 'template' ? ($v['title'] ?: 'فاکتور آماده') : null, 'notes' => $kind === 'return' ? 'برگشت از فاکتور ' . $v['no'] : null,
            'ref_id' => in_array($v['kind'], array('proforma', 'sale'), true) && $kind !== 'template' ? (int) $v['id'] : null, 'build_id' => $kind === 'return' && $v['build_id'] ? (int) $v['build_id'] : null);
        return self::save_invoice(0, $d, $uid);
    }

    /** The draft sale of a new computer entered before its invoice (its parts at their prices), or the invoice it already has. */
    public static function from_build($bid, $uid = 0)
    {
        $db = MZC_Crm_Db::db(); $b = MZC_Crm::build($bid); if (!$db || !$b) { return 'سیستم پیدا نشد.'; }
        $have = (int) $db->get_var($db->prepare('SELECT id FROM `' . self::t('invoices') . "` WHERE build_id = %d AND kind = 'sale' AND status <> 'void' ORDER BY id LIMIT 1", (int) $bid));
        if ($have) { return $have; }
        $items = array();
        foreach ($b['parts'] as $x) { $items[] = array('product_id' => $x['product_id'], 'category' => $x['category'], 'name' => $x['model'], 'qty' => 1, 'unit_price' => $x['sell_price'], 'buy_price' => $x['buy_price'], 'serials' => $x['serial'], 'warranty_months' => $x['warranty_months']); }
        return self::save_invoice(0, array('kind' => 'sale', 'customer_id' => (int) $b['customer_id'], 'build_id' => (int) $bid, 'at' => $b['sold_at'] ?: gmdate('Y-m-d'), 'items' => $items), $uid);
    }

    /* ---------- payments ---------- */

    public static function payment($id)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return null; }
        return $db->get_row($db->prepare('SELECT y.*, q.no AS cheque_no, q.bank AS cheque_bank, q.due_at AS cheque_due, q.status AS cheque_status FROM `' . self::t('payments') . '` y LEFT JOIN `' . self::t('cheques') . '` q ON q.id = y.cheque_id WHERE y.id = %d', (int) $id), ARRAY_A);
    }

    /** A page of payments. $f: direction, customer, vendor, invoice, method, account, category, expense (only expenses), from, to, q. Newest first, with the party's name. */
    public static function payments($f, $page, $per = 30)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return array(array(), 0); }
        $where = array('1=1'); $args = array();
        foreach (array('direction' => 'y.direction = %s', 'method' => 'y.method = %s', 'account' => 'y.account = %s', 'category' => 'y.category = %s', 'from' => 'y.at >= %s', 'to' => 'y.at <= %s') as $k => $sql) { if (!empty($f[$k])) { $where[] = $sql; $args[] = (string) $f[$k]; } }
        foreach (array('customer' => 'y.customer_id', 'vendor' => 'y.vendor_id', 'invoice' => 'y.invoice_id') as $k => $col) { if (!empty($f[$k])) { $where[] = "$col = %d"; $args[] = (int) $f[$k]; } }
        if (!empty($f['expense'])) { $where[] = "y.direction = 'out' AND y.customer_id IS NULL AND y.vendor_id IS NULL"; }
        if (isset($f['q']) && $f['q'] !== '') { $l = '%' . $db->esc_like(MZC_Crm::digits($f['q'])) . '%'; $where[] = '(c.name LIKE %s OR w.name LIKE %s OR y.tx LIKE %s OR y.notes LIKE %s OR y.category LIKE %s)'; array_push($args, $l, $l, $l, $l, $l); }
        $from = 'FROM `' . self::t('payments') . '` y LEFT JOIN `' . self::t('customers') . '` c ON c.id = y.customer_id LEFT JOIN `' . self::t('vendors') . '` w ON w.id = y.vendor_id LEFT JOIN `' . self::t('cheques') . '` q ON q.id = y.cheque_id LEFT JOIN `'
            . self::t('invoices') . '` v ON v.id = y.invoice_id WHERE ' . implode(' AND ', $where);
        $total = (int) $db->get_var($args ? $db->prepare("SELECT COUNT(*) $from", $args) : "SELECT COUNT(*) $from");
        $rows = $db->get_results($db->prepare("SELECT y.*, c.name AS customer_name, w.name AS vendor_name, q.no AS cheque_no, q.status AS cheque_status, q.due_at AS cheque_due, v.no AS invoice_no, v.kind AS invoice_kind $from ORDER BY y.at DESC, y.id DESC LIMIT %d OFFSET %d",
            array_merge($args, array((int) $per, (int) (($page - 1) * $per)))), ARRAY_A);
        return array($rows ?: array(), $total);
    }

    /**
     * Creates or changes a payment. $d: direction (in: money received; out: paid), amount, method, account (where the money went in or came out), at, customer_id or vendor_id
     * (neither: an expense, `category` says of what), invoice_id (a customer's invoice), purchase_id (a supplier's invoice), tx, notes; a cheque payment carries
     * cheque (no, bank, due_at, account) and makes or updates that cheque. Returns the id or an error text.
     */
    public static function save_payment($id, $d, $uid = 0)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return MZC_Crm_Db::error(); }
        $old = $id ? self::payment($id) : null; if ($id && !$old) { return 'پرداخت پیدا نشد.'; }
        $g = function ($k, $def = null) use ($d, $old) { return array_key_exists($k, $d) ? $d[$k] : ($old && array_key_exists($k, $old) ? $old[$k] : $def); };
        $dir = $g('direction', 'in') === 'out' ? 'out' : 'in';
        $amount = (int) self::m($g('amount'));
        if ($amount < 1 || $amount > 999999999999) { return 'مبلغ را درست بنویسید.'; }
        $method = isset(self::$methods[(string) $g('method', 'cash')]) ? (string) $g('method', 'cash') : 'cash';
        $cid = (int) $g('customer_id', 0); $vid = (int) $g('vendor_id', 0); $iid = (int) $g('invoice_id', 0); $pid = (int) $g('purchase_id', 0);
        if ($cid && $vid) { return 'پرداخت یا برای مشتری است یا برای فروشنده، نه هر دو.'; }
        if ($cid && !MZC_Crm::customer($cid)) { return 'مشتری پیدا نشد.'; }
        if ($vid && !$db->get_var($db->prepare('SELECT id FROM `' . self::t('vendors') . '` WHERE id = %d', $vid))) { return 'فروشنده پیدا نشد.'; }
        if ($iid) {
            $inv = $db->get_row($db->prepare('SELECT id, customer_id, status FROM `' . self::t('invoices') . "` WHERE id = %d AND kind IN ('sale','return')", $iid), ARRAY_A);
            if (!$inv) { return 'فاکتور این پرداخت پیدا نشد.'; }
            if (!$cid) { $cid = (int) $inv['customer_id']; }
            if ((int) $inv['customer_id'] !== $cid) { return 'این فاکتور مال مشتری دیگری است.'; }
        }
        if ($pid) {
            $vp = $db->get_var($db->prepare('SELECT vendor_id FROM `' . self::t('purchases') . '` WHERE id = %d', $pid));
            if ($vp === null) { return 'فاکتور خرید پیدا نشد.'; }
            if (!$vid) { $vid = (int) $vp; } if ((int) $vp !== $vid) { return 'این فاکتور خرید مال فروشندهٔ دیگری است.'; }
        }
        $cat = $cid || $vid ? null : (self::clip($g('category', ''), 60) ?: 'سایر');
        if (!$cid && !$vid && $dir === 'in') { $cat = self::clip($g('category', ''), 60) ?: 'دریافت متفرقه'; }
        $row = array('direction' => $dir, 'amount' => $amount, 'method' => $method, 'account' => self::clip($g('account', ''), 120) ?: null, 'at' => self::date_in($g('at'), gmdate('Y-m-d')),
            'customer_id' => $cid ?: null, 'vendor_id' => $vid ?: null, 'invoice_id' => $iid ?: null, 'purchase_id' => $pid ?: null, 'category' => $cat,
            'tx' => self::clip($g('tx', ''), 80) ?: null, 'notes' => self::clip($g('notes', ''), 255) ?: null, 'updated' => self::now());
        $db->query('START TRANSACTION'); $ok = true; $chq = $old ? (int) $old['cheque_id'] : 0;
        if ($method === 'cheque') {
            $c = isset($d['cheque']) && is_array($d['cheque']) ? $d['cheque'] : array();
            $cr = self::save_cheque($chq, $c + array('direction' => $dir, 'amount' => $amount, 'customer_id' => $cid, 'vendor_id' => $vid, 'issued_at' => $row['at'], 'account' => $row['account']), $uid, false);
            if (!is_int($cr)) { $db->query('ROLLBACK'); return $cr; }
            $chq = $cr;
        } elseif ($chq) { $db->update(self::t('cheques'), array('status' => 'cancelled', 'updated' => self::now()), array('id' => $chq)); $chq = 0; }   // no longer paid by that cheque
        $row['cheque_id'] = $chq ?: null;
        if ($old) { $ok = $db->update(self::t('payments'), $row, array('id' => (int) $id)) !== false; }
        else { $row += array('created_by' => (int) $uid, 'created' => self::now()); $ok = (bool) $db->insert(self::t('payments'), $row); $id = (int) $db->insert_id; }
        $err = $db->last_error; $db->query($ok ? 'COMMIT' : 'ROLLBACK');
        return $ok ? (int) $id : 'ذخیره نشد: ' . $err;
    }

    /** Removes a payment entered by mistake (its cheque, if it had one, is voided - the cheque's number stays on record). */
    public static function delete_payment($id)
    {
        $db = MZC_Crm_Db::db(); $p = $db ? self::payment($id) : null; if (!$p) { return 'پرداخت پیدا نشد.'; }
        if ($p['cheque_id']) { $db->update(self::t('cheques'), array('status' => 'cancelled', 'updated' => self::now()), array('id' => (int) $p['cheque_id'])); }
        $db->delete(self::t('payments'), array('id' => (int) $id));
        return (int) $id;
    }

    /* ---------- cheques ---------- */

    public static function cheque($id)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return null; }
        $q = $db->get_row($db->prepare('SELECT q.*, c.name AS customer_name, w.name AS vendor_name FROM `' . self::t('cheques') . '` q LEFT JOIN `' . self::t('customers') . '` c ON c.id = q.customer_id LEFT JOIN `' . self::t('vendors') . '` w ON w.id = q.vendor_id WHERE q.id = %d', (int) $id), ARRAY_A);
        if ($q) { $q['payment_id'] = (int) $db->get_var($db->prepare('SELECT id FROM `' . self::t('payments') . '` WHERE cheque_id = %d LIMIT 1', (int) $id)); }
        return $q;
    }

    /** A page of cheques. $f: direction, status, due ('soon': pending and due within the reminder days, 'late': pending and past due), customer, vendor, q, from, to (due dates). Soonest first. */
    public static function cheques($f, $page, $per = 30)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return array(array(), 0); }
        $where = array('1=1'); $args = array(); $today = MZC_Crm::ymd();
        if (!empty($f['direction'])) { $where[] = 'q.direction = %s'; $args[] = $f['direction'] === 'out' ? 'out' : 'in'; }
        if (!empty($f['status']) && isset(self::$cheque_statuses[$f['status']])) { $where[] = 'q.status = %s'; $args[] = $f['status']; }
        if (isset($f['due']) && $f['due'] === 'soon') { $where[] = "q.status = 'pending' AND q.due_at <= %s"; $args[] = gmdate('Y-m-d', strtotime($today) + (int) self::settings()['remind'] * 86400); }
        if (isset($f['due']) && $f['due'] === 'late') { $where[] = "q.status = 'pending' AND q.due_at < %s"; $args[] = $today; }
        if (!empty($f['customer'])) { $where[] = 'q.customer_id = %d'; $args[] = (int) $f['customer']; }
        if (!empty($f['vendor'])) { $where[] = 'q.vendor_id = %d'; $args[] = (int) $f['vendor']; }
        if (!empty($f['from'])) { $where[] = 'q.due_at >= %s'; $args[] = $f['from']; }
        if (!empty($f['to'])) { $where[] = 'q.due_at <= %s'; $args[] = $f['to']; }
        if (isset($f['q']) && $f['q'] !== '') { $l = '%' . $db->esc_like(MZC_Crm::digits($f['q'])) . '%'; $where[] = '(q.no LIKE %s OR q.bank LIKE %s OR q.party LIKE %s OR c.name LIKE %s OR w.name LIKE %s)'; array_push($args, $l, $l, $l, $l, $l); }
        $from = 'FROM `' . self::t('cheques') . '` q LEFT JOIN `' . self::t('customers') . '` c ON c.id = q.customer_id LEFT JOIN `' . self::t('vendors') . '` w ON w.id = q.vendor_id WHERE ' . implode(' AND ', $where);
        $total = (int) $db->get_var($args ? $db->prepare("SELECT COUNT(*) $from", $args) : "SELECT COUNT(*) $from");
        $rows = $db->get_results($db->prepare("SELECT q.*, c.name AS customer_name, w.name AS vendor_name $from ORDER BY (q.status = 'pending') DESC, q.due_at ASC, q.id DESC LIMIT %d OFFSET %d", array_merge($args, array((int) $per, (int) (($page - 1) * $per)))), ARRAY_A);
        return array($rows ?: array(), $total);
    }

    /** Pending cheques due within the reminder days (or already late): what the desks and the daily e-mail remind of. */
    public static function due_soon() { list($rows) = self::cheques(array('due' => 'soon'), 1, 100); return $rows; }

    /**
     * Creates or changes a cheque. $d: direction (in: received; out: the shop wrote it), no, bank, account, amount, issued_at, due_at, customer_id / vendor_id / party (a name),
     * status, notes, pay (a new cheque that is a payment: also enter that payment). A cheque that is a payment's keeps its amount and party in step with that payment. Returns the id or an error text.
     */
    public static function save_cheque($id, $d, $uid = 0, $own = true)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return MZC_Crm_Db::error(); }
        $old = $id ? self::cheque($id) : null; if ($id && !$old) { return 'چک پیدا نشد.'; }
        $g = function ($k, $def = null) use ($d, $old) { return array_key_exists($k, $d) ? $d[$k] : ($old && array_key_exists($k, $old) ? $old[$k] : $def); };
        $no = self::clip(MZC_Crm::digits((string) $g('no', '')), 40); if ($no === '') { return 'شمارهٔ چک (صیادی یا سری) را بنویسید.'; }
        $amount = (int) self::m($g('amount')); if ($amount < 1) { return 'مبلغ چک را بنویسید.'; }
        $due = self::date_in($g('due_at')); if (!$due) { return 'تاریخ سررسید چک را بنویسید.'; }
        $st = isset(self::$cheque_statuses[(string) $g('status', 'pending')]) ? (string) $g('status', 'pending') : 'pending';
        $cid = (int) $g('customer_id', 0); $vid = (int) $g('vendor_id', 0);
        $row = array('direction' => $g('direction', 'in') === 'out' ? 'out' : 'in', 'no' => $no, 'bank' => self::clip($g('bank', ''), 80) ?: null, 'account' => self::clip($g('account', ''), 120) ?: null, 'amount' => $amount,
            'issued_at' => self::date_in($g('issued_at')), 'due_at' => $due, 'customer_id' => $cid ?: null, 'vendor_id' => $vid ?: null, 'party' => self::clip($g('party', ''), 190) ?: null, 'status' => $st,
            'notes' => self::clip($g('notes', ''), 255) ?: null, 'updated' => self::now());
        if ($old) { if ($db->update(self::t('cheques'), $row, array('id' => (int) $id)) === false) { return 'ذخیره نشد: ' . $db->last_error; } }
        else { $row += array('created_by' => (int) $uid, 'created' => self::now()); if (!$db->insert(self::t('cheques'), $row)) { return 'ذخیره نشد: ' . $db->last_error; } $id = (int) $db->insert_id; }
        if ($own) {
            $pay = $old ? (int) $old['payment_id'] : 0;
            if ($pay) { $db->update(self::t('payments'), array('amount' => $amount, 'customer_id' => $cid ?: null, 'vendor_id' => $vid ?: null, 'direction' => $row['direction'], 'updated' => self::now()), array('id' => $pay)); }
            elseif (!empty($d['pay']) && ($cid || $vid)) {
                $r = self::save_payment(0, array('direction' => $row['direction'], 'amount' => $amount, 'method' => 'cash', 'at' => $row['issued_at'] ?: gmdate('Y-m-d'), 'customer_id' => $cid, 'vendor_id' => $vid, 'notes' => 'چک ' . $no), $uid);
                if (is_int($r)) { $db->update(self::t('payments'), array('method' => 'cheque', 'cheque_id' => (int) $id), array('id' => $r)); }
            }
        }
        return (int) $id;
    }

    /* ---------- accounts ---------- */

    /**
     * A customer's account, oldest first with the running balance (positive: the customer owes): final sales (debit) and returns (credit), finished services (their total,
     * debit) and what was paid on them, payments received (credit) and paid back (debit). Payments by a bounced or void cheque are listed but not counted.
     */
    public static function ledger($cid)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return array('rows' => array(), 'balance' => 0); }
        $rows = array();
        foreach ($db->get_results($db->prepare('SELECT id, kind, no, at, total FROM `' . self::t('invoices') . "` WHERE customer_id = %d AND status = 'final' AND kind IN ('sale','return')", (int) $cid), ARRAY_A) ?: array() as $v) {
            $rows[] = array('at' => $v['at'], 'kind' => $v['kind'], 'ref' => 'invoice', 'id' => (int) $v['id'], 'text' => self::$kinds[$v['kind']] . ' ' . $v['no'], 'debit' => $v['kind'] === 'sale' ? (int) $v['total'] : 0, 'credit' => $v['kind'] === 'return' ? (int) $v['total'] : 0);
        }
        foreach ($db->get_results($db->prepare('SELECT id FROM `' . self::t('jobs') . '` WHERE customer_id = %d AND service_done = 1', (int) $cid)) ?: array() as $j) {
            $j = MZC_Crm::job($j->id); if (!$j) { continue; }
            $rows[] = array('at' => $j['closed_at'] ?: $j['received_at'], 'kind' => 'job', 'ref' => 'job', 'id' => (int) $j['id'], 'text' => 'سرویس ' . $j['service_no'], 'debit' => max(0, (int) $j['total']), 'credit' => 0);
            foreach ($j['pays'] as $p) { $rows[] = array('at' => $p['paid_at'] ?: $j['received_at'], 'kind' => 'job_pay', 'ref' => 'job', 'id' => (int) $j['id'], 'text' => 'پرداخت سرویس ' . $j['service_no'] . ($p['account'] ? ' — ' . $p['account'] : ''), 'debit' => 0, 'credit' => (int) $p['amount']); }
        }
        foreach ($db->get_results($db->prepare('SELECT y.*, q.status AS cheque_status, q.no AS cheque_no, v.no AS invoice_no FROM `' . self::t('payments') . '` y LEFT JOIN `' . self::t('cheques') . '` q ON q.id = y.cheque_id LEFT JOIN `' . self::t('invoices') . '` v ON v.id = y.invoice_id WHERE y.customer_id = %d', (int) $cid), ARRAY_A) ?: array() as $p) {
            $dead = $p['cheque_status'] !== null && in_array($p['cheque_status'], array('bounced', 'cancelled'), true);
            $txt = ($p['direction'] === 'in' ? 'دریافت ' : 'پرداخت به مشتری ') . self::$methods[$p['method']] . ($p['cheque_no'] ? ' ' . $p['cheque_no'] : '') . ($p['invoice_no'] ? ' (فاکتور ' . $p['invoice_no'] . ')' : '') . ($dead ? ' — ' . self::$cheque_statuses[$p['cheque_status']] . '، حساب نمی‌شود' : '');
            $rows[] = array('at' => $p['at'], 'kind' => 'pay', 'ref' => 'payment', 'id' => (int) $p['id'], 'text' => $txt, 'debit' => !$dead && $p['direction'] === 'out' ? (int) $p['amount'] : 0, 'credit' => !$dead && $p['direction'] === 'in' ? (int) $p['amount'] : 0, 'dead' => $dead);
        }
        return self::run($rows);
    }

    /** A supplier's account (positive: the shop owes them): purchase invoices (credit to them), payments to them, money they paid back. */
    public static function vendor_ledger($vid)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return array('rows' => array(), 'balance' => 0); }
        $rows = array();
        foreach ($db->get_results($db->prepare('SELECT u.id, u.invoice_no, u.bought_at, (SELECT COALESCE(SUM(i.qty * i.unit_cost), 0) FROM `' . self::t('purchase_items') . '` i WHERE i.purchase_id = u.id) AS total FROM `' . self::t('purchases') . '` u WHERE u.vendor_id = %d', (int) $vid), ARRAY_A) ?: array() as $u) {
            $rows[] = array('at' => $u['bought_at'], 'kind' => 'purchase', 'ref' => 'purchase', 'id' => (int) $u['id'], 'text' => 'فاکتور خرید ' . ($u['invoice_no'] ?: '#' . $u['id']), 'debit' => (int) $u['total'], 'credit' => 0);
        }
        foreach ($db->get_results($db->prepare('SELECT y.*, q.status AS cheque_status, q.no AS cheque_no FROM `' . self::t('payments') . '` y LEFT JOIN `' . self::t('cheques') . '` q ON q.id = y.cheque_id WHERE y.vendor_id = %d', (int) $vid), ARRAY_A) ?: array() as $p) {
            $dead = $p['cheque_status'] !== null && in_array($p['cheque_status'], array('bounced', 'cancelled'), true);
            $rows[] = array('at' => $p['at'], 'kind' => 'pay', 'ref' => 'payment', 'id' => (int) $p['id'], 'text' => ($p['direction'] === 'out' ? 'پرداخت ' : 'دریافت از فروشنده ') . self::$methods[$p['method']] . ($p['cheque_no'] ? ' ' . $p['cheque_no'] : '') . ($dead ? ' — ' . self::$cheque_statuses[$p['cheque_status']] . '، حساب نمی‌شود' : ''),
                'debit' => !$dead && $p['direction'] === 'in' ? (int) $p['amount'] : 0, 'credit' => !$dead && $p['direction'] === 'out' ? (int) $p['amount'] : 0, 'dead' => $dead);
        }
        return self::run($rows);
    }

    private static function run($rows)
    {
        usort($rows, function ($a, $b) { return strcmp((string) $a['at'], (string) $b['at']); });
        $bal = 0; foreach ($rows as &$r) { $bal += $r['debit'] - $r['credit']; $r['balance'] = $bal; } unset($r);
        return array('rows' => $rows, 'balance' => $bal);
    }

    /**
     * The money of a period (Y-m-d): sales and returns (count, totals, the profit of their lines where the cost is known), what came in and went out by method and by account,
     * the expenses by heading, and - not tied to the period - every account's balance, the cheques due soon and the customers who owe most.
     */
    public static function summary($from, $to)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return null; }
        $iv = self::t('invoices'); $ii = self::t('invoice_items'); $py = self::t('payments'); $ch = self::t('cheques');
        $s = $db->get_results($db->prepare("SELECT kind, COUNT(*) AS n, COALESCE(SUM(total), 0) AS total, COALESCE(SUM(discount), 0) AS discount, COALESCE(SUM(tax), 0) AS tax FROM `$iv` WHERE status = 'final' AND kind IN ('sale','return') AND at BETWEEN %s AND %s GROUP BY kind", $from, $to), OBJECT_K);
        $p = $db->get_row($db->prepare("SELECT COALESCE(SUM(CASE v.kind WHEN 'sale' THEN 1 ELSE -1 END * i.qty * (i.unit_price - i.buy_price)), 0) AS profit, SUM(i.buy_price IS NULL) AS missing FROM `$ii` i JOIN `$iv` v ON v.id = i.invoice_id WHERE v.status = 'final' AND v.kind IN ('sale','return') AND v.at BETWEEN %s AND %s", $from, $to), ARRAY_A);
        $live = "(y.cheque_id IS NULL OR NOT EXISTS (SELECT 1 FROM `$ch` q WHERE q.id = y.cheque_id AND q.status IN (" . self::DEAD_CHEQUE . ')))';
        $flow = $db->get_results($db->prepare("SELECT y.direction, y.method, COALESCE(SUM(y.amount), 0) AS amount FROM `$py` y WHERE $live AND y.at BETWEEN %s AND %s GROUP BY y.direction, y.method", $from, $to), ARRAY_A) ?: array();
        $exp = $db->get_results($db->prepare("SELECT y.category, COALESCE(SUM(y.amount), 0) AS amount FROM `$py` y WHERE $live AND y.direction = 'out' AND y.customer_id IS NULL AND y.vendor_id IS NULL AND y.at BETWEEN %s AND %s GROUP BY y.category ORDER BY amount DESC", $from, $to), ARRAY_A) ?: array();
        // an account holds a cheque's money only once the cheque has cleared
        $acc = $db->get_results("SELECT COALESCE(y.account, '') AS account, SUM(CASE y.direction WHEN 'in' THEN y.amount ELSE -y.amount END) AS balance FROM `$py` y WHERE (y.cheque_id IS NULL OR EXISTS (SELECT 1 FROM `$ch` q WHERE q.id = y.cheque_id AND q.status = 'cleared')) GROUP BY y.account ORDER BY balance DESC", ARRAY_A) ?: array();
        $jp = $db->get_results('SELECT COALESCE(account, \'\') AS account, SUM(amount) AS amount FROM `' . self::t('job_pays') . '` GROUP BY account', OBJECT_K) ?: array();
        foreach ($acc as &$a) { if (isset($jp[$a['account']])) { $a['balance'] += (int) $jp[$a['account']]->amount; unset($jp[$a['account']]); } } unset($a);
        foreach ($jp as $k => $x) { $acc[] = array('account' => (string) $k, 'balance' => (int) $x->amount); }
        $num = function ($k, $f) use ($s) { return isset($s[$k]) ? (int) $s[$k]->$f : 0; };
        return array('from' => $from, 'to' => $to, 'sales' => array('count' => $num('sale', 'n'), 'total' => $num('sale', 'total'), 'discount' => $num('sale', 'discount'), 'tax' => $num('sale', 'tax')),
            'returns' => array('count' => $num('return', 'n'), 'total' => $num('return', 'total')), 'net' => $num('sale', 'total') - $num('return', 'total'),
            'profit' => (int) $p['profit'], 'missing_cost' => (int) $p['missing'], 'flow' => array_map(function ($r) { return array('direction' => $r['direction'], 'method' => $r['method'], 'methodText' => isset(self::$methods[$r['method']]) ? self::$methods[$r['method']] : $r['method'], 'amount' => (int) $r['amount']); }, $flow),
            'expenses' => array_map(function ($r) { return array('category' => (string) $r['category'], 'amount' => (int) $r['amount']); }, $exp),
            'accounts' => array_map(function ($r) { return array('account' => $r['account'] !== '' ? $r['account'] : 'بدون حساب', 'balance' => (int) $r['balance']); }, $acc), 'cheques' => self::due_soon(), 'debtors' => self::debtors(10));
    }

    /** The customers who owe the most, from invoices and finished services less what they paid. */
    public static function debtors($n = 10)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return array(); }
        $ids = $db->get_col('SELECT DISTINCT customer_id FROM `' . self::t('invoices') . "` WHERE status = 'final' AND customer_id IS NOT NULL UNION SELECT DISTINCT customer_id FROM `" . self::t('jobs') . '` WHERE service_done = 1 AND customer_id IS NOT NULL LIMIT 3000') ?: array();
        $out = array();
        // ponytail: a balance per customer is a ledger each (fine for a shop's few thousand customers); a summary table would be the next step if it ever gets slow
        foreach ($ids as $cid) { $b = self::ledger((int) $cid)['balance']; if ($b > 0) { $out[] = array('customer_id' => (int) $cid, 'balance' => $b); } }
        usort($out, function ($a, $b) { return $b['balance'] <=> $a['balance']; });
        $out = array_slice($out, 0, $n);
        foreach ($out as &$o) { $c = MZC_Crm::customer($o['customer_id']); $o['name'] = $c ? $c['name'] : ''; $o['mobile'] = $c ? (string) $c['mobile'] : ''; } unset($o);
        return $out;
    }

    /* ---------- the daily reminder of cheques ---------- */

    public static function boot()
    {
        add_action('mzc_cheque_remind', array(__CLASS__, 'remind'));
        if (!wp_next_scheduled('mzc_cheque_remind')) { wp_schedule_event(time() + 600, 'daily', 'mzc_cheque_remind'); }
        add_action('admin_notices', array(__CLASS__, 'notice'));
        foreach (array('acc_save', 'invoice_void', 'invoice_save', 'invoice_final', 'invoice_copy', 'invoice_delete', 'payment_delete', 'cheque_status') as $a) { add_action('admin_post_mzc_' . $a, array(__CLASS__, 'act_' . $a)); }
    }

    /** Once a day: the site's administrator gets an e-mail of the cheques due within the reminder days (nothing is sent when there are none). */
    public static function remind()
    {
        if (!MZC_Crm_Db::ready()) { return; }
        $rows = self::due_soon(); if (!$rows) { return; }
        $lines = array_map(function ($q) { return ($q['direction'] === 'out' ? 'پرداختنی' : 'دریافتنی') . ' — چک ' . $q['no'] . ' — ' . number_format((int) $q['amount']) . ' تومان — سررسید ' . MZC_Crm::jdate($q['due_at']) . ' — ' . ($q['customer_name'] ?: ($q['vendor_name'] ?: (string) $q['party'])); }, $rows);
        wp_mail(get_option('admin_email'), 'مازستا: ' . count($rows) . ' چک نزدیک سررسید', implode("\n", $lines) . "\n\n" . admin_url('admin.php?page=mzc-acc&tab=cheques'));
    }

    /** The same reminder at the top of the plugin's pages. */
    public static function notice()
    {
        $s = function_exists('get_current_screen') ? get_current_screen() : null;
        if (!$s || strpos((string) $s->id, 'mzc') === false || !Mazesta_Connect::can() || !MZC_Crm_Db::ready()) { return; }
        $n = count(self::due_soon()); if (!$n) { return; }
        echo '<div class="notice notice-warning"><p>' . esc_html($n . ' چک در جریان تا ' . (int) self::settings()['remind'] . ' روز آینده (یا گذشته) سررسید دارد.') . ' <a href="' . esc_url(MZC_Admin::url('mzc-acc', array('tab' => 'cheques', 'due' => 'soon'))) . '">چک‌ها</a></p></div>';
    }

    /* ---------- admin actions: settings, invoices (the same steps as the staff program), voiding and corrections ---------- */

    public static function act_acc_save()
    {
        MZC_Admin::guard('acc_save', 'manage_options');
        $d = array(); foreach (array('start', 'tax', 'remind', 'expenses', 'shop', 'phone', 'address', 'foot') as $k) { $d[$k] = isset($_POST[$k]) ? sanitize_textarea_field(wp_unslash($_POST[$k])) : ''; }
        self::save_settings($d);
        MZC_Woo::save_settings(array('on' => !empty($_POST['woo_on']), 'orders' => !empty($_POST['woo_orders']), 'factor' => isset($_POST['woo_factor']) ? sanitize_text_field(wp_unslash($_POST['woo_factor'])) : ''));
        MZC_Admin::back('mzc-settings', 'ok:تنظیمات حسابداری ذخیره شد.');
    }

    public static function act_invoice_void()
    {
        MZC_Admin::guard('invoice_void');
        $id = isset($_POST['id']) ? (int) $_POST['id'] : 0;
        if ($h = MZC_Rev::blocker('invoice', $id, get_current_user_id())) { MZC_Admin::back('mzc-acc', 'err:' . MZC_Rev::busy_text($h), array('view' => $id)); }
        $r = self::void_invoice($id, MZC_Crm::post('reason', 255)); if (is_int($r)) { MZC_Rev::record('invoice', $id, get_current_user_id(), 'void'); }
        MZC_Admin::back('mzc-acc', is_int($r) ? 'ok:فاکتور باطل شد.' : 'err:' . $r, array('view' => $id));
    }

    /** The site's invoice form: saves the draft (a sale, proforma or template); "final" makes a sale final right after. */
    public static function act_invoice_save()
    {
        MZC_Admin::guard('invoice_save');
        $id = isset($_POST['id']) ? (int) $_POST['id'] : 0; $kind = isset($_POST['kind'], self::$kinds[$_POST['kind']]) ? sanitize_key($_POST['kind']) : 'sale';
        $back = $id ? array('edit' => $id) : array('edit' => 'new', 'kind' => $kind);
        if ($id && ($h = MZC_Rev::blocker('invoice', $id, get_current_user_id()))) { MZC_Admin::back('mzc-acc', 'err:' . MZC_Rev::busy_text($h), array('view' => $id)); }
        $d = array('kind' => $kind, 'title' => MZC_Crm::post('title', 190), 'at' => MZC_Crm::post('at', 20), 'valid_until' => MZC_Crm::post('valid_until', 20), 'discount' => MZC_Crm::post('discount', 30),
            'tax_pct' => MZC_Crm::post('tax_pct', 10), 'notes' => MZC_Crm::post_long('notes', 2000), 'items' => isset($_POST['items']) && is_array($_POST['items']) ? array_values(wp_unslash($_POST['items'])) : array());
        if ($kind !== 'template' && (!empty($_POST['customer_id']) || MZC_Crm::post('c_mobile', 30) !== '')) {
            $cid = MZC_Crm::customer_of_form(); if (!is_int($cid)) { MZC_Admin::back('mzc-acc', 'err:' . $cid, $back); }
            $d['customer_id'] = $cid;
        }
        $r = self::save_invoice($id, $d, get_current_user_id());
        if (!is_int($r)) { MZC_Admin::back('mzc-acc', 'err:' . $r, $back); }
        MZC_Rev::record('invoice', $r, get_current_user_id(), $id ? 'update' : 'create'); MZC_Rev::unlock('invoice', $r, get_current_user_id());
        if (isset($_POST['go']) && $_POST['go'] === 'final') {
            $f = self::finalize($r, !empty($_POST['make_build']));
            if (!is_array($f)) { MZC_Admin::back('mzc-acc', 'err:ذخیره شد ولی قطعی نشد: ' . $f, array('edit' => $r)); }
            MZC_Rev::record('invoice', $r, get_current_user_id(), 'final');
            MZC_Admin::back('mzc-acc', 'ok:قطعی شد.' . ($f[1] ? ' ' . implode(' ', $f[1]) : ''), array('view' => $r));
        }
        MZC_Admin::back('mzc-acc', 'ok:ذخیره شد.', array('view' => $r));
    }

    public static function act_invoice_final()
    {
        MZC_Admin::guard('invoice_final');
        $id = isset($_GET['id']) ? (int) $_GET['id'] : 0;
        if ($h = MZC_Rev::blocker('invoice', $id, get_current_user_id())) { MZC_Admin::back('mzc-acc', 'err:' . MZC_Rev::busy_text($h), array('view' => $id)); }
        $f = self::finalize($id); if (is_array($f)) { MZC_Rev::record('invoice', $id, get_current_user_id(), 'final'); }
        MZC_Admin::back('mzc-acc', is_array($f) ? 'ok:قطعی شد.' . ($f[1] ? ' ' . implode(' ', $f[1]) : '') : 'err:' . $f, array('view' => $id));
    }

    /** A new draft from another invoice: a sale from a proforma or a template, a return of a final sale, a template from any invoice. Opens the draft to change. */
    public static function act_invoice_copy()
    {
        MZC_Admin::guard('invoice_copy');
        $id = isset($_REQUEST['id']) ? (int) $_REQUEST['id'] : 0; $kind = isset($_REQUEST['kind']) ? sanitize_key($_REQUEST['kind']) : '';
        $r = self::copy($id, $kind, get_current_user_id());
        MZC_Admin::back('mzc-acc', is_int($r) ? 'ok:' . self::$kinds[$kind] . ' ساخته شد؛ مشتری و ردیف‌ها را بررسی کنید.' : 'err:' . $r, is_int($r) ? array('edit' => $r) : array('view' => $id));
    }

    public static function act_invoice_delete()
    {
        MZC_Admin::guard('invoice_delete');
        $id = isset($_GET['id']) ? (int) $_GET['id'] : 0; $r = self::delete_draft($id);
        MZC_Admin::back('mzc-acc', is_int($r) ? 'ok:پاک شد.' : 'err:' . $r, is_int($r) ? array() : array('view' => $id));
    }

    public static function act_payment_delete()
    {
        MZC_Admin::guard('payment_delete', 'manage_options');
        $r = self::delete_payment(isset($_GET['id']) ? (int) $_GET['id'] : 0);
        MZC_Admin::back('mzc-acc', is_int($r) ? 'ok:پرداخت پاک شد.' : 'err:' . $r, array('tab' => 'payments'));
    }

    public static function act_cheque_status()
    {
        MZC_Admin::guard('cheque_status');
        $id = isset($_GET['id']) ? (int) $_GET['id'] : 0; $st = isset($_GET['st']) ? sanitize_key($_GET['st']) : '';
        $r = isset(self::$cheque_statuses[$st]) ? self::save_cheque($id, array('status' => $st)) : 'وضعیت درست نیست.';
        MZC_Admin::back('mzc-acc', is_int($r) ? 'ok:وضعیت چک عوض شد.' : 'err:' . $r, array('tab' => 'cheques'));
    }
}
