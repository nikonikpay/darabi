<?php
if (!defined('ABSPATH')) { exit; }

/**
 * Keeps the stock and the site's WooCommerce shop in step. A stock product linked to a site product (products.wc_id) sends it its selling price and its count: the stock is the
 * one that counts (it is computed from purchases and sales), so a count typed on the site is put back at the next sync. A price changed on the site's product page comes back
 * into the stock - so a price change on either side reaches the other and every form that picks the product. A paid site order becomes a final sales invoice of the customer
 * (found by mobile, or made) with its online payment, which also takes the products out of the stock; a cancelled or refunded order voids it (a refund is paid back).
 * Everything here does nothing while WooCommerce is not active or the sync is switched off on the settings page.
 */
final class MZC_Woo
{
    private static $queue = array();
    private static $busy = false;

    public static function boot()
    {
        add_filter('cron_schedules', function ($s) { $s['mzc_15min'] = array('interval' => 900, 'display' => 'Mazesta: every 15 minutes'); return $s; });
        add_action('mzc_woo_sync', array(__CLASS__, 'sync_all'));
        foreach (array('woo_link', 'woo_import', 'woo_push', 'woo_orders') as $a) { add_action('admin_post_mzc_' . $a, array(__CLASS__, 'act_' . $a)); }
        // this plugin loads before WooCommerce, so its hooks are added whether or not it is there yet (without it they never fire)
        add_action('init', function () { if (class_exists('WooCommerce') && !wp_next_scheduled('mzc_woo_sync')) { wp_schedule_event(time() + 300, 'mzc_15min', 'mzc_woo_sync'); } });
        add_action('woocommerce_update_product', array(__CLASS__, 'site_saved'), 20);
        foreach (array('processing', 'completed') as $st) { add_action('woocommerce_order_status_' . $st, array(__CLASS__, 'order_paid')); }
        foreach (array('cancelled', 'refunded') as $st) { add_action('woocommerce_order_status_' . $st, array(__CLASS__, 'order_undone')); }
    }

    /* ---------- settings (meta 'woo') ---------- */

    /** on: prices and counts go both ways; orders: paid orders become invoices; factor: site price = stock price (toman) x factor ('' = from the shop's currency). */
    public static function settings()
    {
        $db = MZC_Crm_Db::db(); $v = $db ? $db->get_var('SELECT v FROM `' . MZC_Crm_Db::t('meta') . "` WHERE k = 'woo'") : null;
        $s = is_string($v) ? json_decode($v, true) : null;
        return (is_array($s) ? $s : array()) + array('on' => false, 'orders' => false, 'factor' => '');
    }

    public static function save_settings($d)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return; }
        $f = trim(MZC_Crm::digits((string) $d['factor']));
        $s = array('on' => !empty($d['on']), 'orders' => !empty($d['orders']), 'factor' => is_numeric($f) && (float) $f > 0 ? (string) (float) $f : '');
        $db->query($db->prepare('REPLACE INTO `' . MZC_Crm_Db::t('meta') . "` (k, v) VALUES ('woo', %s)", wp_json_encode($s)));
    }

    public static function active() { return class_exists('WooCommerce') && function_exists('wc_get_product') && MZC_Crm_Db::ready(); }
    private static function on() { return self::active() && !empty(self::settings()['on']); }

    /** Site money per toman: the setting, else the shop's currency (Iranian rial 10, toman 1, thousand toman 0.001, thousand rial 0.01). */
    public static function factor()
    {
        $f = self::settings()['factor'];
        if ($f !== '' && (float) $f > 0) { return (float) $f; }
        $map = array('IRR' => 10, 'IRT' => 1, 'IRHT' => 0.001, 'IRHR' => 0.01);
        $c = function_exists('get_woocommerce_currency') ? get_woocommerce_currency() : 'IRT';
        return isset($map[$c]) ? (float) $map[$c] : 1.0;
    }

    /* ---------- stock -> site ---------- */

    /** Products whose price or count may have changed: they are sent to the site once, when the request ends. */
    public static function touch($ids)
    {
        if (!self::on()) { return; }
        foreach ((array) $ids as $id) { if ((int) $id > 0) { self::$queue[(int) $id] = 1; } }
        if (self::$queue && !has_action('shutdown', array(__CLASS__, 'flush'))) { add_action('shutdown', array(__CLASS__, 'flush')); }
    }

    public static function flush() { $ids = array_keys(self::$queue); self::$queue = array(); if ($ids) { self::push($ids); } }

    /** Sends the price and the count of these stock products (null: every linked one) to their site products; returns how many site products changed. */
    public static function push($ids = null)
    {
        if (!self::on()) { return 0; }
        $db = MZC_Crm_Db::db(); $where = 'p.wc_id IS NOT NULL';
        if ($ids !== null) { $ids = array_values(array_filter(array_map('intval', (array) $ids))); if (!$ids) { return 0; } $where .= ' AND p.id IN (' . implode(',', $ids) . ')'; }
        $rows = $db->get_results('SELECT p.id, p.wc_id, p.sell_price, ' . MZC_Stock::stock_sql() . ' AS stock FROM `' . MZC_Crm_Db::t('products') . "` p WHERE $where", ARRAY_A) ?: array();
        $f = self::factor(); $n = 0; self::$busy = true;
        try {
            foreach ($rows as $r) {
                $w = wc_get_product((int) $r['wc_id']); if (!$w) { continue; }
                $ch = false;
                if ($r['sell_price'] !== null) { $price = (string) round((int) $r['sell_price'] * $f); if ((string) $w->get_regular_price() !== $price) { $w->set_regular_price($price); $ch = true; } }
                $st = max(0, (int) $r['stock']);   // the site cannot show a negative count; the stock page still does
                if (!$w->get_manage_stock()) { $w->set_manage_stock(true); $ch = true; }
                if ((int) $w->get_stock_quantity() !== $st) { $w->set_stock_quantity($st); $ch = true; }
                if ($ch) { $w->save(); $n++; }
            }
        } finally { self::$busy = false; }
        return $n;
    }

    /** Every 15 minutes: the counts and prices again (a part taken out of a computer moves no hook), and any paid order the site saved while the CRM was out of reach. */
    public static function sync_all()
    {
        if (!self::active()) { return; }
        self::push();
        if (!empty(self::settings()['orders'])) { self::import_recent(30, gmdate('Y-m-d', time() - 3 * 86400)); }
    }

    /* ---------- site -> stock ---------- */

    /** A site product was saved (its page, the REST API, a quick edit): a new price there becomes the stock's selling price; the count is put back to the stock's. */
    public static function site_saved($wcId)
    {
        if (self::$busy || !self::on()) { return; }
        $db = MZC_Crm_Db::db();
        $p = $db->get_row($db->prepare('SELECT id, sell_price FROM `' . MZC_Crm_Db::t('products') . '` WHERE wc_id = %d LIMIT 1', (int) $wcId), ARRAY_A);
        $w = $p ? wc_get_product((int) $wcId) : null; if (!$w) { return; }
        $site = $w->get_regular_price();
        if ($site !== '' && is_numeric($site)) {
            $toman = (int) round((float) $site / self::factor());
            if ($p['sell_price'] === null || (int) $p['sell_price'] !== $toman) { MZC_Stock::save_product((int) $p['id'], array('sell_price' => (string) $toman)); }
        }
        self::touch(array((int) $p['id']));
    }

    /* ---------- orders -> invoices ---------- */

    public static function order_paid($orderId) { if (self::active() && !empty(self::settings()['orders'])) { self::import_order($orderId); } }

    /** Makes a paid order the customer's final sales invoice (once: the order id is kept on it) with the online payment. Returns the invoice id, 0 when it was there already, or an error text. */
    public static function import_order($orderId)
    {
        $db = MZC_Crm_Db::db(); $o = function_exists('wc_get_order') ? wc_get_order($orderId) : null;
        if (!$db || !$o || !is_a($o, 'WC_Order')) { return 'سفارش پیدا نشد.'; }
        if ($db->get_var($db->prepare('SELECT id FROM `' . MZC_Crm_Db::t('invoices') . '` WHERE wc_order_id = %d', (int) $o->get_id()))) { return 0; }
        $cid = self::customer_of($o); if (!is_int($cid)) { return $cid; }
        $f = self::factor(); $t = function ($v) use ($f) { return (int) round((float) $v / $f); }; $items = array();
        foreach ($o->get_items() as $it) {
            if (!is_a($it, 'WC_Order_Item_Product')) { continue; }
            $qty = max(1, (int) $it->get_quantity()); $p = null;
            foreach (array($it->get_variation_id(), $it->get_product_id()) as $wid) { if ($wid && !$p) { $p = $db->get_row($db->prepare('SELECT id FROM `' . MZC_Crm_Db::t('products') . '` WHERE wc_id = %d LIMIT 1', (int) $wid), ARRAY_A); } }
            $items[] = array('product_id' => $p ? (int) $p['id'] : 0, 'name' => $it->get_name(), 'qty' => $qty, 'unit_price' => $t((float) $it->get_total() / $qty));
        }
        foreach ($o->get_fees() as $fee) { if ((float) $fee->get_total() > 0) { $items[] = array('name' => $fee->get_name(), 'qty' => 1, 'unit_price' => $t($fee->get_total())); } }
        if ((float) $o->get_shipping_total() > 0) { $items[] = array('name' => 'هزینهٔ ارسال (' . $o->get_shipping_method() . ')', 'qty' => 1, 'unit_price' => $t($o->get_shipping_total())); }
        if ((float) $o->get_total_tax() > 0) { $items[] = array('name' => 'مالیات سفارش سایت', 'qty' => 1, 'unit_price' => $t($o->get_total_tax())); }
        if (!$items) { return 'سفارش ردیفی ندارد.'; }
        $paid = $o->get_date_paid() ?: $o->get_date_created(); $at = $paid ? $paid->date('Y-m-d') : gmdate('Y-m-d');
        $id = MZC_Acc::save_invoice(0, array('kind' => 'sale', 'customer_id' => $cid, 'at' => $at, 'tax_pct' => 0, 'items' => $items, 'notes' => 'سفارش سایت #' . $o->get_order_number()));
        if (!is_int($id)) { return $id; }
        $db->update(MZC_Crm_Db::t('invoices'), array('wc_order_id' => (int) $o->get_id()), array('id' => $id));
        $r = MZC_Acc::finalize($id, false, $at); if (!is_array($r)) { return $r; }
        if ($o->is_paid()) { MZC_Acc::save_payment(0, array('direction' => 'in', 'amount' => $t($o->get_total()), 'method' => 'online', 'account' => 'درگاه سایت', 'at' => $at, 'customer_id' => $cid, 'invoice_id' => $id, 'tx' => (string) $o->get_transaction_id(), 'notes' => 'سفارش سایت #' . $o->get_order_number())); }
        return $id;
    }

    /** The order's customer in the CRM: the same mobile, else the same site account, else a new customer (who is also linked to that account). */
    private static function customer_of($o)
    {
        $db = MZC_Crm_Db::db(); $m = MZC_Crm::mobile((string) $o->get_billing_phone()); $uid = (int) $o->get_customer_id();
        $c = $m !== '' ? MZC_Crm::customer_by_mobile($m) : null;
        if (!$c && $uid) { $c = $db->get_row($db->prepare('SELECT * FROM `' . MZC_Crm_Db::t('customers') . '` WHERE wp_user_id = %d LIMIT 1', $uid), ARRAY_A); }
        if ($c) { if ($uid && !$c['wp_user_id']) { $db->update(MZC_Crm_Db::t('customers'), array('wp_user_id' => $uid), array('id' => (int) $c['id'])); } return (int) $c['id']; }
        $name = trim($o->get_billing_first_name() . ' ' . $o->get_billing_last_name()) ?: 'مشتری سایت';
        $addr = trim(implode('، ', array_filter(array($o->get_billing_state(), $o->get_billing_city(), $o->get_billing_address_1(), $o->get_billing_address_2()))));
        $id = MZC_Crm::save_customer(0, Mazesta_Connect::clip($name, 190), $m, array('email' => sanitize_email($o->get_billing_email()) ?: null, 'address' => $addr !== '' ? $addr : null, 'notes' => 'از سفارش سایت'));
        if (is_int($id) && $uid) { $db->update(MZC_Crm_Db::t('customers'), array('wp_user_id' => $uid), array('id' => $id)); }
        return $id;
    }

    /** A cancelled or refunded order: its invoice is voided; money that came in online for it is recorded as paid back. */
    public static function order_undone($orderId)
    {
        if (!self::active()) { return; }
        $db = MZC_Crm_Db::db(); $o = wc_get_order($orderId); if (!$o) { return; }
        $id = (int) $db->get_var($db->prepare('SELECT id FROM `' . MZC_Crm_Db::t('invoices') . "` WHERE wc_order_id = %d AND status = 'final'", (int) $o->get_id())); if (!$id) { return; }
        $v = MZC_Acc::invoice($id);
        MZC_Acc::void_invoice($id, 'سفارش سایت ' . ($o->get_status() === 'refunded' ? 'مسترد شد' : 'لغو شد'));
        if ((int) $v['paid'] > 0) { MZC_Acc::save_payment(0, array('direction' => 'out', 'amount' => (int) $v['paid'], 'method' => 'online', 'account' => 'درگاه سایت', 'at' => gmdate('Y-m-d'), 'customer_id' => (int) $v['customer_id'], 'invoice_id' => $id, 'notes' => 'بازپرداخت سفارش سایت #' . $o->get_order_number())); }
    }

    /** Brings in the paid orders not yet in the CRM (newest first, at most $limit, from $after). Returns array(made, errors). */
    public static function import_recent($limit = 100, $after = '')
    {
        $args = array('status' => array('processing', 'completed'), 'limit' => (int) $limit, 'orderby' => 'date', 'order' => 'DESC', 'return' => 'ids');
        if ($after !== '') { $args['date_created'] = '>=' . $after; }
        $made = 0; $err = array();
        foreach (wc_get_orders($args) as $oid) { $r = self::import_order($oid); if (is_int($r) && $r > 0) { $made++; } elseif (is_string($r)) { $err[] = '#' . $oid . ': ' . $r; } }
        return array($made, $err);
    }

    /* ---------- linking ---------- */

    /** Links stock products to site products with the same SKU (part number). Returns how many were linked. */
    public static function link_by_sku()
    {
        $db = MZC_Crm_Db::db(); $n = 0;
        foreach ($db->get_results('SELECT id, sku FROM `' . MZC_Crm_Db::t('products') . "` WHERE wc_id IS NULL AND sku IS NOT NULL AND sku <> ''", ARRAY_A) ?: array() as $p) {
            $wid = (int) wc_get_product_id_by_sku($p['sku']);
            if ($wid && !$db->get_var($db->prepare('SELECT id FROM `' . MZC_Crm_Db::t('products') . '` WHERE wc_id = %d', $wid))) { $db->update(MZC_Crm_Db::t('products'), array('wc_id' => $wid), array('id' => (int) $p['id'])); $n++; }
        }
        return $n;
    }

    /** Makes a stock product for every simple site product not linked yet (by SKU first), at the site's price. Returns how many were made. */
    public static function import_products()
    {
        $db = MZC_Crm_Db::db(); $n = 0; $f = self::factor();
        $have = array_map('intval', $db->get_col('SELECT wc_id FROM `' . MZC_Crm_Db::t('products') . '` WHERE wc_id IS NOT NULL') ?: array());
        foreach (wc_get_products(array('type' => 'simple', 'limit' => 2000, 'status' => array('publish', 'private', 'draft'))) as $w) {
            if (in_array((int) $w->get_id(), $have, true)) { continue; }
            $sku = (string) $w->get_sku(); $price = $w->get_regular_price();
            $p = $sku !== '' ? $db->get_row($db->prepare('SELECT id FROM `' . MZC_Crm_Db::t('products') . '` WHERE sku = %s AND wc_id IS NULL LIMIT 1', $sku), ARRAY_A) : null;
            $id = $p ? (int) $p['id'] : MZC_Stock::save_product(0, array('name' => wp_strip_all_tags($w->get_name()), 'sku' => $sku, 'sell_price' => is_numeric($price) ? (string) round((float) $price / $f) : ''));
            if (is_int($id)) { $db->update(MZC_Crm_Db::t('products'), array('wc_id' => (int) $w->get_id()), array('id' => $id)); $n++; }
        }
        return $n;
    }

    /** A site product's name and edit link for the stock pages, or null. */
    public static function site_product($wcId)
    {
        if (!$wcId || !function_exists('wc_get_product') || !($w = wc_get_product((int) $wcId))) { return null; }
        return array('id' => (int) $w->get_id(), 'name' => wp_strip_all_tags($w->get_name()), 'price' => $w->get_regular_price(), 'stock' => $w->get_stock_quantity(), 'url' => get_edit_post_link($w->get_id(), 'raw'), 'link' => get_permalink($w->get_id()));
    }

    /* ---------- the settings page's buttons ---------- */

    private static function act_guard($a) { MZC_Admin::guard($a, 'manage_options'); if (!self::active()) { MZC_Admin::back('mzc-settings', 'err:ووکامرس فعال نیست یا پایگاه دادهٔ CRM در دسترس نیست.'); } }
    public static function act_woo_link() { self::act_guard('woo_link'); MZC_Admin::back('mzc-settings', 'ok:' . self::link_by_sku() . ' کالا با پارت‌نامبر (SKU) به محصول سایت وصل شد.'); }
    public static function act_woo_import() { self::act_guard('woo_import'); MZC_Admin::back('mzc-settings', 'ok:' . self::import_products() . ' محصول سایت به انبار آمد و وصل شد.'); }
    public static function act_woo_push() { self::act_guard('woo_push'); if (!self::on()) { MZC_Admin::back('mzc-settings', 'err:اول همگام‌سازی با سایت را روشن و ذخیره کنید.'); } MZC_Admin::back('mzc-settings', 'ok:' . self::push() . ' محصول سایت به‌روز شد.'); }
    public static function act_woo_orders()
    {
        self::act_guard('woo_orders');
        list($made, $err) = self::import_recent(200);
        MZC_Admin::back('mzc-settings', ($err ? 'err:' : 'ok:') . $made . ' سفارش پرداخت‌شده فاکتور شد.' . ($err ? ' خطا: ' . implode(' | ', array_slice($err, 0, 3)) : ''));
    }
}
