<?php
if (!defined('ABSPATH')) { exit; }

/**
 * What the staff apps (desktop, Android) and the AI apps (through MZC_Mcp) read and write: customers, new computers (builds), service jobs, the stock and the purchase invoices,
 * the app's test reports, the SMS receipt and the case label.
 * Every route checks the signed-in person's level for the area it touches (see MZC_Staff::auth) - the apps only hide what the server would refuse anyway. Nothing here deletes
 * a record (that stays a site-administrator job on the plugin's pages), and every change is written to the audit table with who made it.
 * A record carries its `updated` stamp; a save that names a stamp which is no longer the current one is refused with 409 (somebody else saved first), so two desks never overwrite each other unseen.
 */
final class MZC_Api
{
    const PER = 30;

    /** The label fields each kind of record can print (key => caption); which of them a label shows is set on the Users and access page. */
    public static $label_fields = array(
        'job' => array('service_no' => 'شمارهٔ سرویس', 'customer' => 'نام مشتری', 'mobile' => 'موبایل', 'device' => 'دستگاه', 'received' => 'تاریخ پذیرش', 'due' => 'تاریخ قول', 'invoice' => 'شمارهٔ فاکتور', 'complaint' => 'مشکل', 'status' => 'وضعیت'),
        'build' => array('invoice' => 'شمارهٔ فاکتور', 'customer' => 'نام مشتری', 'mobile' => 'موبایل', 'title' => 'عنوان سیستم', 'service_no' => 'شمارهٔ سرویس (سیستم‌های قدیمی)', 'sold' => 'تاریخ فروش', 'status' => 'وضعیت'),
    );
    public static $label_default = array('job' => array('service_no', 'customer', 'mobile', 'device', 'received'), 'build' => array('invoice', 'customer', 'title'));
    const RECEIPT_TEXT = 'مشتری گرامی، دستگاه شما با شمارهٔ پذیرش {no} در مازستا ثبت شد. {url}';

    public static function boot() { add_action('rest_api_init', array(__CLASS__, 'routes')); }

    public static function routes()
    {
        $r = array(
            array('/staff/config', 'GET', 'config'), array('/staff/find', 'GET', 'find'),
            array('/staff/customers', 'GET', 'customers'), array('/staff/customers/(?P<id>\d+)', 'GET', 'customer'), array('/staff/customers', 'POST', 'customer_save'),
            array('/staff/builds', 'GET', 'builds'), array('/staff/builds/(?P<id>\d+)', 'GET', 'build'), array('/staff/builds', 'POST', 'build_save'), array('/staff/builds/status', 'POST', 'build_status'),
            array('/staff/jobs', 'GET', 'jobs'), array('/staff/jobs/(?P<id>\d+)', 'GET', 'job'), array('/staff/jobs', 'POST', 'job_save'), array('/staff/jobs/status', 'POST', 'job_status'),
            array('/staff/reports', 'GET', 'reports'), array('/staff/reports/(?P<id>[A-Za-z0-9-]{8,64})', 'GET', 'report'), array('/staff/attach', 'POST', 'attach'),
            array('/staff/sms/receipt', 'POST', 'receipt'), array('/staff/label/(?P<kind>job|build)/(?P<id>\d+)', 'GET', 'label'),
            array('/staff/products', 'GET', 'products'), array('/staff/products/(?P<id>\d+)', 'GET', 'product'), array('/staff/products', 'POST', 'product_save'),
            array('/staff/suppliers', 'GET', 'suppliers'), array('/staff/suppliers', 'POST', 'supplier_save'),
            array('/staff/purchases', 'GET', 'purchases'), array('/staff/purchases/(?P<id>\d+)', 'GET', 'purchase'), array('/staff/purchases', 'POST', 'purchase_save'), array('/staff/serial', 'GET', 'serial'),
            array('/staff/stats', 'GET', 'stats'),
        );
        foreach ($r as $x) { register_rest_route(Mazesta_Connect::NS, $x[0], array('methods' => $x[1], 'callback' => array(__CLASS__, 'rest_' . $x[2]), 'permission_callback' => '__return_true')); }
    }

    /* ---------- small helpers ---------- */

    private static function ok($d = array(), $st = 200) { return Mazesta_Connect::fresh($d, $st); }
    private static function fail($msg, $st = 400, $more = array()) { return Mazesta_Connect::fresh(array('error' => $msg) + $more, $st); }
    private static function body($req) { $p = $req->get_json_params(); return is_array($p) ? $p : array(); }
    private static function s($p, $k, $max = 255) { return isset($p[$k]) && is_string($p[$k]) ? mb_substr(trim(wp_strip_all_tags($p[$k])), 0, $max) : ''; }
    private static function n($p, $k) { $v = isset($p[$k]) && (is_string($p[$k]) || is_numeric($p[$k])) ? MZC_Crm::digits((string) $p[$k]) : ''; return (int) preg_replace('/[^\d]/', '', $v); }
    private static function opt_n($p, $k) { return isset($p[$k]) && $p[$k] !== '' && $p[$k] !== null ? self::n($p, $k) : null; }
    private static function d($p, $k) { return MZC_Crm::gdate(self::s($p, $k, 10)); }   // Persian dates (1405/07/25) are taken too: the AI apps pass what people say
    private static function flag($p, $k) { return !empty($p[$k]) ? 1 : 0; }
    private static function now() { return gmdate('Y-m-d H:i:s'); }
    private static function t($n) { return MZC_Crm_Db::t($n); }
    private static function cfg() { $c = Mazesta_Connect::read('config'); $x = isset($c['staffCfg']) && is_array($c['staffCfg']) ? $c['staffCfg'] : array(); return $x + array('receipt' => self::RECEIPT_TEXT, 'portalUrl' => '', 'labels' => array()); }
    private static function per($req) { $n = (int) $req->get_param('per'); return $n > 0 ? min(50, $n) : self::PER; }
    private static function page($req) { return max(1, (int) $req->get_param('page')); }
    private static function jd($ymd) { return $ymd ? MZC_Crm::jdate($ymd) : ''; }

    public static function log($a, $action, $entity, $id, $note = '')
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return; }
        $u = $a['user'];
        $db->insert(self::t('audit'), array('user_id' => (int) $a['id'], 'who' => mb_substr($u->display_name !== '' ? $u->display_name : $u->user_login, 0, 120), 'action' => $action, 'entity' => $entity, 'entity_id' => (int) $id, 'note' => mb_substr($note, 0, 255), 'at' => self::now()));
    }

    private static function money_seen($a) { return MZC_Staff::can($a['id'], 'payments', 1); }
    /** Buy prices (what the shop paid): the people who see the money, and the warehouse who enters the purchase invoices. */
    private static function cost_seen($a) { return self::money_seen($a) || MZC_Staff::can($a['id'], 'stock', 2); }
    private static function cost_edit($a) { return MZC_Staff::can($a['id'], 'payments', 2) || MZC_Staff::can($a['id'], 'stock', 2); }
    private static function media_out($v) { return MZC_Crm::media_list((string) $v); }
    private static function dtype($k) { return isset(MZC_Crm::$device_types[$k]) ? MZC_Crm::$device_types[$k] : ''; }
    /** A record's requested programs, for the apps: software = array of (name, done), software_note, software_by_customer (when the customer last saved it on their page). */
    private static function soft_fields($json) { $s = MZC_Crm::soft_out($json); return array('software' => $s['items'], 'software_note' => $s['note'], 'software_by_customer' => $s['by_customer']); }
    /** The software of a save: sent rows/note replace what is stored; a list not sent stays. */
    private static function soft_save($p, $old)
    {
        if (!array_key_exists('software', $p) && !array_key_exists('software_note', $p)) { return $old ? $old['soft'] : null; }
        $was = MZC_Crm::soft_out($old ? $old['soft'] : null);
        return MZC_Crm::soft_in(array_key_exists('software', $p) ? $p['software'] : $was['items'], array_key_exists('software_note', $p) ? self::s($p, 'software_note', 2000) : $was['note'], $old ? $old['soft'] : null);
    }

    /* ---------- how records are sent out ---------- */

    private static function customer_out($c)
    {
        return array('id' => (int) $c['id'], 'name' => $c['name'], 'mobile' => (string) $c['mobile'], 'mobile2' => (string) $c['mobile2'], 'mobiles' => MZC_Crm::mobiles_of($c), 'phone' => (string) $c['phone'], 'national_id' => (string) $c['national_id'], 'email' => (string) $c['email'],
            'address' => (string) $c['address'], 'notes' => (string) $c['notes'], 'group' => (string) $c['grp'], 'groupText' => isset(MZC_Crm::$groups[$c['grp']]) ? MZC_Crm::$groups[$c['grp']] : '', 'updated' => $c['updated']);
    }

    private static function build_out($b, $full, $a = null)
    {
        $o = array('id' => (int) $b['id'], 'number' => (string) ($b['invoice_no'] ?: $b['service_no']), 'service_no' => (string) $b['service_no'], 'invoice_no' => (string) $b['invoice_no'], 'title' => $b['title'],
            'device_type' => (string) $b['device_type'], 'deviceText' => self::dtype($b['device_type']), 'status' => $b['status'], 'statusText' => isset(MZC_Crm::$build_statuses[$b['status']]) ? MZC_Crm::$build_statuses[$b['status']] : '',
            'customer_id' => (int) $b['customer_id'], 'sold_at' => (string) $b['sold_at'], 'sold_at_j' => self::jd($b['sold_at']), 'report_id' => (string) $b['report_id'], 'updated' => $b['updated']);
        if (isset($b['customer_name'])) { $o['customer_name'] = $b['customer_name']; $o['customer_mobile'] = (string) $b['customer_mobile']; $o['parts_count'] = (int) $b['parts']; }
        if ($full) {
            $o['notes'] = (string) $b['notes']; $o['media'] = self::media_out($b['media']); $o += self::soft_fields($b['soft']); $o['parts'] = array();
            $money = $a && self::money_seen($a); $cost = $a && self::cost_seen($a);
            foreach ($b['parts'] as $p) {
                $r = array('product_id' => $p['product_id'] === null ? null : (int) $p['product_id'], 'category' => $p['category'], 'model' => $p['model'], 'serial' => (string) $p['serial'], 'warranty_months' => $p['warranty_months'] === null ? null : (int) $p['warranty_months'], 'vendor' => (string) $p['vendor'], 'note' => (string) $p['note'],
                    'has_warranty' => (bool) $p['has_warranty'], 'has_box' => (bool) $p['has_box'], 'qc' => (bool) $p['qc']);
                if ($money) { $r['sell_price'] = $p['sell_price'] === null ? null : (int) $p['sell_price']; }
                if ($cost) { $r['buy_price'] = $p['buy_price'] === null ? null : (int) $p['buy_price']; }
                $o['parts'][] = $r;
            }
            $o['photos'] = count($b['photos']);
        }
        return $o;
    }

    private static function job_out($j, $full, $money, $cost = false)
    {
        $o = array('id' => (int) $j['id'], 'number' => (string) $j['service_no'], 'service_no' => (string) $j['service_no'], 'device' => (string) $j['device'], 'device_serial' => (string) $j['device_serial'], 'device_type' => (string) $j['device_type'], 'deviceText' => self::dtype($j['device_type']), 'status' => $j['status'], 'statusText' => isset(MZC_Crm::$statuses[$j['status']]) ? MZC_Crm::$statuses[$j['status']] : '',
            'customer_id' => (int) $j['customer_id'], 'received_at' => (string) $j['received_at'], 'received_at_j' => self::jd($j['received_at']), 'closed_at' => (string) $j['closed_at'], 'due_at' => (string) $j['due_at'], 'due_at_j' => self::jd($j['due_at']),
            'invoice_no' => (string) $j['invoice_no'], 'report_id' => (string) $j['report_id'], 'updated' => $j['updated']);
        if (isset($j['customer_name'])) { $o['customer_name'] = $j['customer_name']; $o['customer_mobile'] = (string) $j['customer_mobile']; }
        if ($money && isset($j['total'])) { $o['total'] = (int) $j['total']; }
        if ($full) {
            $o += array('complaint' => (string) $j['complaint'], 'work_done' => (string) $j['work_done'], 'notes' => (string) $j['notes'], 'is_mazesta' => (bool) $j['is_mazesta'], 'has_warranty' => (bool) $j['has_warranty'], 'photos' => count($j['photos']), 'media' => self::media_out($j['media'])) + self::soft_fields($j['soft']);
            $map = function ($rows) use ($money, $cost) {
                $out = array();
                foreach ($rows as $p) { $r = array('product_id' => $p['product_id'] === null ? null : (int) $p['product_id'], 'name' => $p['name'], 'qty' => (int) $p['qty'], 'serial' => (string) $p['serial'], 'warranty_months' => $p['warranty_months'] === null ? null : (int) $p['warranty_months'], 'note' => (string) $p['note'], 'has_box' => (bool) $p['has_box'], 'has_warranty' => (bool) $p['has_warranty']); if ($money) { $r['unit_price'] = (int) $p['unit_price']; } if ($cost && $p['kind'] !== 'in') { $r['buy_price'] = $p['buy_price'] === null ? null : (int) $p['buy_price']; } $out[] = $r; }
                return $out;
            };
            $o['parts'] = $map($j['parts']); $o['recv'] = $map($j['recv']);
            if ($money) {
                $o += array('labor_price' => (int) $j['labor_price'], 'discount' => (int) $j['discount'], 'paid' => (int) $j['paid'], 'paid_note' => (string) $j['paid_note']); $o['pays'] = array();
                foreach ($j['pays'] as $p) { $o['pays'][] = array('amount' => (int) $p['amount'], 'account' => (string) $p['account'], 'tx' => (string) $p['tx'], 'holder' => (string) $p['holder']); }
            }
        }
        return $o;
    }

    /* ---------- reading ---------- */

    public static function rest_config($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $c = self::cfg(); $labels = array();
        foreach (self::$label_fields as $kind => $f) { $labels[$kind] = array('fields' => $f, 'chosen' => self::label_keys($kind, $c)); }
        return self::ok(array('buildStatuses' => MZC_Crm::$build_statuses, 'jobStatuses' => MZC_Crm::$statuses, 'categories' => MZC_Crm::$categories, 'groups' => MZC_Crm::$groups, 'deviceTypes' => MZC_Crm::$device_types, 'software' => MZC_Crm::software_catalog(), 'accounts' => MZC_Crm::software_catalog('accounts'), 'costSeen' => self::cost_seen($a), 'costEdit' => self::cost_edit($a), 'labels' => $labels, 'receiptReady' => MZC_Sms::configured() && MZC_Staff::can($a['id'], 'sms', 2)));
    }

    private static function label_keys($kind, $c)
    {
        $want = isset($c['labels'][$kind]) && is_array($c['labels'][$kind]) ? $c['labels'][$kind] : self::$label_default[$kind];
        $ok = array_values(array_filter($want, function ($k) use ($kind) { return isset(self::$label_fields[$kind][$k]); }));
        return $ok ? $ok : self::$label_default[$kind];
    }

    public static function rest_find($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $q = trim((string) $req->get_param('q')); if (mb_strlen($q) < 2) { return self::fail('دست‌کم دو نویسه بنویسید.'); }
        $out = array('customers' => array(), 'builds' => array(), 'jobs' => array()); $money = self::money_seen($a);
        if (MZC_Staff::can($a['id'], 'customers', 1)) { list($r) = MZC_Crm::customers($q, 1, 8); foreach ($r as $c) { $out['customers'][] = self::customer_out($c); } }
        if (MZC_Staff::can($a['id'], 'builds', 1)) { list($r) = MZC_Crm::builds($q, 1, 8); foreach ($r as $b) { $out['builds'][] = self::build_out($b, false); } }
        if (MZC_Staff::can($a['id'], 'jobs', 1)) { list($r) = MZC_Crm::jobs($q, '', 1, 8); foreach ($r as $j) { $out['jobs'][] = self::job_out($j, false, $money); } }
        return self::ok($out);
    }

    public static function rest_customers($req)
    {
        $a = MZC_Staff::auth($req, 'customers', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $pg = self::page($req); list($rows, $total) = MZC_Crm::customers(trim((string) $req->get_param('q')), $pg, self::per($req), (string) $req->get_param('group'));
        return self::ok(array('total' => $total, 'page' => $pg, 'per' => self::per($req), 'items' => array_map(function ($c) { return self::customer_out($c) + array('builds' => (int) $c['builds'], 'jobs' => (int) $c['jobs']); }, $rows)));
    }

    public static function rest_customer($req)
    {
        $a = MZC_Staff::auth($req, 'customers', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $id = (int) $req['id']; $c = MZC_Crm::customer($id); if (!$c) { return self::fail('مشتری پیدا نشد.', 404); }
        $out = array('customer' => self::customer_out($c), 'builds' => array(), 'jobs' => array()); $money = self::money_seen($a);
        if (MZC_Staff::can($a['id'], 'builds', 1)) { list($r) = MZC_Crm::builds('', 1, 50, $id); foreach ($r as $b) { $out['builds'][] = self::build_out($b, false); } }
        if (MZC_Staff::can($a['id'], 'jobs', 1)) { list($r) = MZC_Crm::jobs('', '', 1, 50, $id); foreach ($r as $j) { $out['jobs'][] = self::job_out($j, false, $money); } }
        return self::ok($out);
    }

    public static function rest_builds($req)
    {
        $a = MZC_Staff::auth($req, 'builds', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $pg = self::page($req); list($rows, $total) = MZC_Crm::builds(trim((string) $req->get_param('q')), $pg, self::per($req), 0, (string) $req->get_param('status'));
        return self::ok(array('total' => $total, 'page' => $pg, 'per' => self::per($req), 'items' => array_map(function ($b) { return self::build_out($b, false); }, $rows)));
    }

    public static function rest_build($req)
    {
        $a = MZC_Staff::auth($req, 'builds', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $b = MZC_Crm::build((int) $req['id']); if (!$b) { return self::fail('سیستم پیدا نشد.', 404); }
        $c = MZC_Crm::customer($b['customer_id']); $o = self::build_out($b, true, $a);
        if ($c) { $o['customer_name'] = $c['name']; $o['customer_mobile'] = (string) $c['mobile']; }
        return self::ok($o);
    }

    public static function rest_jobs($req)
    {
        $a = MZC_Staff::auth($req, 'jobs', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $pg = self::page($req); list($rows, $total) = MZC_Crm::jobs(trim((string) $req->get_param('q')), (string) $req->get_param('status'), $pg, self::per($req));
        $money = self::money_seen($a);
        return self::ok(array('total' => $total, 'page' => $pg, 'per' => self::per($req), 'items' => array_map(function ($j) use ($money) { return self::job_out($j, false, $money); }, $rows)));
    }

    public static function rest_job($req)
    {
        $a = MZC_Staff::auth($req, 'jobs', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $j = MZC_Crm::job((int) $req['id']); if (!$j) { return self::fail('سرویس پیدا نشد.', 404); }
        $c = MZC_Crm::customer($j['customer_id']); $o = self::job_out($j, true, self::money_seen($a), self::cost_seen($a));
        if ($c) { $o['customer_name'] = $c['name']; $o['customer_mobile'] = (string) $c['mobile']; }
        return self::ok($o);
    }

    /* ---------- writing ---------- */

    /** The customer a form names: an existing id, or a mobile (and name) that finds the customer or - with edit right on customers - makes one. An int, or an error response. */
    private static function customer_for($p, $a)
    {
        $id = self::n($p, 'customer_id');
        if ($id) { return MZC_Crm::customer($id) ? $id : self::fail('مشتری پیدا نشد.', 404); }
        $m = MZC_Crm::mobile(self::s($p, 'c_mobile', 30));
        if ($m === '') { return self::fail('یک مشتری انتخاب کنید یا شمارهٔ موبایل او را بنویسید.'); }
        if ($c = MZC_Crm::customer_by_mobile($m)) { return (int) $c['id']; }
        if (!MZC_Staff::can($a['id'], 'customers', 2)) { return self::fail('این مشتری ثبت نشده و اجازهٔ ثبت مشتری تازه را ندارید.', 403); }
        $r = MZC_Crm::save_customer(0, self::s($p, 'c_name', 190), $m, array('mobile2' => self::s($p, 'c_mobile2', 30)));
        if (!is_int($r)) { return self::fail($r); }
        self::log($a, 'create', 'customer', $r, 'ثبت همراه فرم');
        return $r;
    }

    public static function rest_customer_save($req)
    {
        $a = MZC_Staff::auth($req, 'customers', 2); if ($a instanceof WP_REST_Response) { return $a; }
        $p = self::body($req); $id = self::n($p, 'id');
        if ($id) {
            $old = MZC_Crm::customer($id); if (!$old) { return self::fail('مشتری پیدا نشد.', 404); }
            if (isset($p['updated']) && (string) $p['updated'] !== (string) $old['updated']) { return self::fail('این مشتری در همین فاصله را کس دیگری ذخیره کرده است؛ دوباره بازش کنید.', 409, array('current' => self::customer_out($old))); }
        }
        $nid = self::s($p, 'national_id', 20); $email = self::s($p, 'email', 190);
        // `mobiles` (every number, the main one first) wins over mobile/mobile2 when sent
        $more = isset($p['mobiles']) && is_array($p['mobiles']) ? array('mobiles' => array_map(function ($x) { return is_scalar($x) ? mb_substr((string) $x, 0, 30) : ''; }, $p['mobiles'])) : array();
        $r = MZC_Crm::save_customer($id, self::s($p, 'name', 190), self::s($p, 'mobile', 30), $more + array('mobile2' => self::s($p, 'mobile2', 30), 'phone' => self::s($p, 'phone', 80) ?: null, 'national_id' => $nid ?: null,
            'email' => $email !== '' && is_email($email) ? $email : null, 'address' => self::s($p, 'address', 1000) ?: null, 'notes' => self::s($p, 'notes', 4000) ?: null, 'grp' => self::s($p, 'group', 12)));
        if (!is_int($r)) { return self::fail($r, 422); }
        self::log($a, $id ? 'update' : 'create', 'customer', $r);
        return self::ok(self::customer_out(MZC_Crm::customer($r)));
    }

    private static function begin($db) { $db->query('START TRANSACTION'); }
    private static function end($db, $ok) { $db->query($ok ? 'COMMIT' : 'ROLLBACK'); return $ok; }

    /** A new service number under the lock, run through $work($number); the lock covers the insert so two desks never get one number. */
    private static function with_number($db, $work)
    {
        $lock = $db->get_var("SELECT GET_LOCK('" . self::t('svc') . "', 5)");
        try { return $work(MZC_Crm::next_service_no($db)); }
        finally { if ($lock) { $db->query("DO RELEASE_LOCK('" . self::t('svc') . "')"); } }
    }

    public static function rest_build_save($req)
    {
        $a = MZC_Staff::auth($req, 'builds', 2); if ($a instanceof WP_REST_Response) { return $a; }
        $db = MZC_Crm::db(); $p = self::body($req); $id = self::n($p, 'id'); $old = null;
        if ($id) {
            $old = MZC_Crm::build($id); if (!$old) { return self::fail('سیستم پیدا نشد.', 404); }
            if (isset($p['updated']) && (string) $p['updated'] !== (string) $old['updated']) { return self::fail('این سیستم در همین فاصله را کس دیگری ذخیره کرده است؛ دوباره بازش کنید.', 409, array('current' => self::build_out($old, true, $a))); }
        }
        $cid = $id && empty($p['customer_id']) && empty($p['c_mobile']) ? (int) $old['customer_id'] : self::customer_for($p, $a);
        if ($cid instanceof WP_REST_Response) { return $cid; }
        $st = isset($p['status']) && isset(MZC_Crm::$build_statuses[$p['status']]) ? (string) $p['status'] : ($old ? $old['status'] : 'queue');
        $inv = MZC_Crm::digits(self::s($p, 'invoice_no', 60));
        // a new computer is known by its invoice number; the old ones keep their service numbers
        if ($inv === '' && !$old) { return self::fail('شمارهٔ فاکتور را بنویسید؛ سیستم نو با شمارهٔ فاکتور شناخته می‌شود.', 422); }
        if ($inv !== '' && empty($p['force'])) {
            $dup = $db->get_row($db->prepare('SELECT id, service_no FROM `' . self::t('builds') . '` WHERE invoice_no = %s AND id <> %d LIMIT 1', $inv, $id), ARRAY_A);
            if ($dup) { return self::fail('شمارهٔ فاکتور ' . $inv . ' قبلاً برای سیستم دیگری (' . ($dup['service_no'] ?: '#' . $dup['id']) . ') ثبت شده است.', 409, array('duplicate' => array('id' => (int) $dup['id'], 'service_no' => (string) $dup['service_no']))); }
        }
        $rid = self::s($p, 'report_id', 64);
        $row = array('customer_id' => $cid, 'invoice_no' => $inv !== '' ? $inv : ($old ? $old['invoice_no'] : null), 'soft' => self::soft_save($p, $old), 'sold_at' => array_key_exists('sold_at', $p) ? self::d($p, 'sold_at') : ($old ? $old['sold_at'] : gmdate('Y-m-d')),
            'notes' => array_key_exists('notes', $p) ? self::s($p, 'notes', 4000) : ($old ? $old['notes'] : ''), 'status' => $st, 'delivered' => $st === 'delivered' ? 1 : 0, 'updated' => self::now(),
            'device_type' => isset($p['device_type'], MZC_Crm::$device_types[$p['device_type']]) ? (string) $p['device_type'] : ($old ? $old['device_type'] : 'system'),
            'media' => array_key_exists('media', $p) ? (implode("\n", MZC_Crm::media_list($p['media'])) ?: null) : ($old ? $old['media'] : null));
        if ($rid !== '' && preg_match('/^[A-Za-z0-9-]{8,64}$/', $rid) && MZC_Staff::can($a['id'], 'tests', 2)) { $row['report_id'] = $rid; }
        // prices: a person without the right keeps what the row had (matched by model), else the product's price
        $pay = MZC_Staff::can($a['id'], 'payments', 2); $ce = self::cost_edit($a); $was = array();
        if ($old) { foreach ($old['parts'] as $x) { if (!isset($was[$x['model']])) { $was[$x['model']] = $x; } } }
        $parts = null;
        if (isset($p['parts']) && is_array($p['parts'])) {
            $parts = array();
            foreach ($p['parts'] as $x) {
                if (!is_array($x) || (self::s($x, 'model', 190) === '' && !self::n($x, 'product_id'))) { continue; }   // a row picked from the stock may leave the name to the product
                $cat = isset($x['category'], MZC_Crm::$categories[$x['category']]) ? $x['category'] : 'other'; $m = self::opt_n($x, 'warranty_months');
                $model = self::s($x, 'model', 190); $w = isset($was[$model]) ? $was[$model] : null;
                $parts[] = array('product_id' => self::n($x, 'product_id') ?: null, 'category' => $cat, 'model' => $model, 'serial' => self::s($x, 'serial', 120) ?: null, 'warranty_months' => $m, 'vendor' => self::s($x, 'vendor', 120) ?: null, 'note' => self::s($x, 'note', 255) ?: null,
                    'has_warranty' => array_key_exists('has_warranty', $x) ? self::flag($x, 'has_warranty') : ($m ? 1 : 0), 'has_box' => self::flag($x, 'has_box'), 'qc' => self::flag($x, 'qc'),
                    'sell_price' => $pay ? self::opt_n($x, 'sell_price') : ($w ? $w['sell_price'] : null), 'buy_price' => $ce ? self::opt_n($x, 'buy_price') : ($w ? $w['buy_price'] : null));
            }
            $parts = MZC_Stock::link_parts($parts, 'model', array('sell_price' => 'sell_price', 'buy_price' => 'buy_price'));
        }
        // the name in the lists comes from the parts (graphics card, else processor); a title sent by an older app or the AI still wins
        $row['title'] = self::s($p, 'title', 190) ?: ($parts !== null ? MZC_Crm::build_title($parts) : ($old ? $old['title'] : 'سیستم نو'));
        $t = self::t('builds'); self::begin($db); $ok = true;
        if ($id) { $ok = $db->update($t, $row, array('id' => $id)) !== false; }
        else { $row['created'] = self::now(); $row['service_no'] = self::s($p, 'service_no', 40) ?: null; $ok = (bool) $db->insert($t, $row); $id = (int) $db->insert_id; }
        if ($ok && $parts !== null) {
            $db->delete(self::t('build_parts'), array('build_id' => $id));
            foreach ($parts as $x) { $x['build_id'] = $id; if (!$db->insert(self::t('build_parts'), $x)) { $ok = false; break; } }
        }
        if (!self::end($db, $ok)) { return self::fail('ذخیره نشد: ' . $db->last_error, 500); }
        $b = MZC_Crm::build($id); $out = self::build_out($b, true, $a);
        self::log($a, $old ? 'update' : 'create', 'build', $id, (string) ($b['invoice_no'] ?: $b['service_no']));
        // a serial already on another system: said, not refused (a part can legitimately come back and be sold again)
        $warn = array();
        foreach ($b['parts'] as $x) {
            if ($x['serial'] === null || $x['serial'] === '') { continue; }
            $o = $db->get_row($db->prepare('SELECT b.id, b.service_no FROM `' . self::t('build_parts') . '` p JOIN `' . $t . '` b ON b.id = p.build_id WHERE p.serial = %s AND p.build_id <> %d LIMIT 1', $x['serial'], $id), ARRAY_A);
            if ($o) { $warn[] = 'سریال ' . $x['serial'] . ' روی سیستم ' . ($o['service_no'] ?: '#' . $o['id']) . ' هم ثبت شده است.'; }
        }
        $out['warnings'] = $warn;
        return self::ok($out, $old ? 200 : 201);
    }

    public static function rest_build_status($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $p = self::body($req); $id = self::n($p, 'id'); $st = isset($p['status']) ? (string) $p['status'] : '';
        if (!isset(MZC_Crm::$build_statuses[$st])) { return self::fail('وضعیت درست نیست.'); }
        // the test desk may move a computer through testing without being allowed to edit the sale
        if (!MZC_Staff::can($a['id'], 'builds', 2) && !(MZC_Staff::can($a['id'], 'tests', 2) && MZC_Staff::can($a['id'], 'builds', 1) && in_array($st, array('testing', 'tested'), true))) { return self::fail('دسترسی این کار را ندارید.', 403); }
        $db = MZC_Crm::db(); $b = $id ? MZC_Crm::build($id) : null; if (!$b) { return self::fail('سیستم پیدا نشد.', 404); }
        if (isset($p['updated']) && (string) $p['updated'] !== (string) $b['updated']) { return self::fail('این سیستم در همین فاصله را کس دیگری عوض کرده است؛ دوباره بازش کنید.', 409, array('current' => self::build_out($b, false))); }
        $db->update(self::t('builds'), array('status' => $st, 'delivered' => $st === 'delivered' ? 1 : 0, 'updated' => self::now()), array('id' => $id));
        self::log($a, 'status', 'build', $id, $b['status'] . ' → ' . $st);
        return self::ok(self::build_out(MZC_Crm::build($id), false));
    }

    public static function rest_job_save($req)
    {
        $a = MZC_Staff::auth($req, 'jobs', 2); if ($a instanceof WP_REST_Response) { return $a; }
        $db = MZC_Crm::db(); $p = self::body($req); $id = self::n($p, 'id'); $old = null; $pay = MZC_Staff::can($a['id'], 'payments', 2);
        if ($id) {
            $old = MZC_Crm::job($id); if (!$old) { return self::fail('سرویس پیدا نشد.', 404); }
            if (isset($p['updated']) && (string) $p['updated'] !== (string) $old['updated']) { return self::fail('این سرویس در همین فاصله را کس دیگری ذخیره کرده است؛ دوباره بازش کنید.', 409, array('current' => self::job_out($old, true, self::money_seen($a), self::cost_seen($a)))); }
        }
        $cid = $id && empty($p['customer_id']) && empty($p['c_mobile']) ? (int) $old['customer_id'] : self::customer_for($p, $a);
        if ($cid instanceof WP_REST_Response) { return $cid; }
        $st = isset($p['status']) && isset(MZC_Crm::$statuses[$p['status']]) ? (string) $p['status'] : ($old ? $old['status'] : 'received');
        $closed = array_key_exists('closed_at', $p) ? self::d($p, 'closed_at') : ($old ? $old['closed_at'] : null);
        if ($st === 'delivered' && $closed === null) { $closed = gmdate('Y-m-d'); }
        $row = array('customer_id' => $cid, 'device' => array_key_exists('device', $p) ? (self::s($p, 'device', 190) ?: null) : ($old ? $old['device'] : null),
            'device_serial' => array_key_exists('device_serial', $p) ? (self::s($p, 'device_serial', 120) ?: null) : ($old ? $old['device_serial'] : null), 'soft' => self::soft_save($p, $old),
            'device_type' => isset($p['device_type'], MZC_Crm::$device_types[$p['device_type']]) ? (string) $p['device_type'] : ($old ? $old['device_type'] : null),
            'media' => array_key_exists('media', $p) ? (implode("\n", MZC_Crm::media_list($p['media'])) ?: null) : ($old ? $old['media'] : null),
            'received_at' => array_key_exists('received_at', $p) ? (self::d($p, 'received_at') ?: gmdate('Y-m-d')) : ($old ? $old['received_at'] : gmdate('Y-m-d')), 'closed_at' => $closed,
            'due_at' => array_key_exists('due_at', $p) ? self::d($p, 'due_at') : ($old ? $old['due_at'] : null), 'invoice_no' => array_key_exists('invoice_no', $p) ? (self::s($p, 'invoice_no', 60) ?: null) : ($old ? $old['invoice_no'] : null),
            'is_mazesta' => array_key_exists('is_mazesta', $p) ? self::flag($p, 'is_mazesta') : ($old ? (int) $old['is_mazesta'] : 0), 'has_warranty' => array_key_exists('has_warranty', $p) ? self::flag($p, 'has_warranty') : ($old ? (int) $old['has_warranty'] : 0),
            'service_done' => in_array($st, array('ready', 'delivered'), true) ? 1 : 0, 'status' => $st, 'complaint' => array_key_exists('complaint', $p) ? self::s($p, 'complaint', 4000) : ($old ? $old['complaint'] : ''),
            'work_done' => array_key_exists('work_done', $p) ? self::s($p, 'work_done', 4000) : ($old ? $old['work_done'] : ''), 'notes' => array_key_exists('notes', $p) ? self::s($p, 'notes', 4000) : ($old ? $old['notes'] : ''), 'updated' => self::now());
        if ($pay) {
            foreach (array('labor_price', 'discount', 'paid') as $k) { $row[$k] = array_key_exists($k, $p) ? self::n($p, $k) : ($old ? (int) $old[$k] : 0); }
            $row['paid_note'] = array_key_exists('paid_note', $p) ? (self::s($p, 'paid_note', 255) ?: null) : ($old ? $old['paid_note'] : null);
        } elseif (!$old) { $row += array('labor_price' => 0, 'discount' => 0, 'paid' => 0); }
        $rid = self::s($p, 'report_id', 64);
        if ($rid !== '' && preg_match('/^[A-Za-z0-9-]{8,64}$/', $rid) && MZC_Staff::can($a['id'], 'tests', 2)) { $row['report_id'] = $rid; }
        // part lists: a list not sent stays as it was. Without the right on prices a part keeps the price it had (matched by name), a new one gets its product's price (or none).
        $price = array(); $cost = array(); $ce = self::cost_edit($a);
        if ($old) { foreach ($old['parts'] as $x) { if (!isset($price[$x['name']])) { $price[$x['name']] = (int) $x['unit_price']; $cost[$x['name']] = $x['buy_price']; } } }
        $lists = array();
        foreach (array('add' => 'parts', 'in' => 'recv') as $kind => $field) {
            if (!isset($p[$field]) || !is_array($p[$field])) { continue; }
            $lists[$kind] = array();
            foreach ($p[$field] as $x) {
                if (!is_array($x) || (self::s($x, 'name', 190) === '' && ($kind === 'in' || !self::n($x, 'product_id')))) { continue; }
                $name = self::s($x, 'name', 190); $unit = $kind === 'in' ? 0 : ($pay ? self::opt_n($x, 'unit_price') : (isset($price[$name]) ? $price[$name] : null));
                $lists[$kind][] = array('product_id' => $kind === 'in' ? null : (self::n($x, 'product_id') ?: null), 'name' => $name, 'qty' => max(1, self::n($x, 'qty')), 'unit_price' => $unit, 'serial' => self::s($x, 'serial', 120) ?: null, 'warranty_months' => self::opt_n($x, 'warranty_months'), 'note' => self::s($x, 'note', 255) ?: null,
                    'has_box' => self::flag($x, 'has_box'), 'has_warranty' => self::flag($x, 'has_warranty'), 'kind' => $kind, 'buy_price' => $kind === 'in' ? null : ($ce ? self::opt_n($x, 'buy_price') : (isset($cost[$name]) ? $cost[$name] : null)));
            }
            if ($kind === 'add') { $lists[$kind] = MZC_Stock::link_parts($lists[$kind], 'name', array('unit_price' => 'sell_price', 'buy_price' => 'buy_price')); }
            foreach ($lists[$kind] as &$x) { $x['unit_price'] = (int) $x['unit_price']; } unset($x);
        }
        $pays = null;
        if ($pay && isset($p['pays']) && is_array($p['pays'])) { $pays = array(); foreach ($p['pays'] as $x) { if (is_array($x) && self::n($x, 'amount')) { $pays[] = array('amount' => self::n($x, 'amount'), 'account' => self::s($x, 'account', 120) ?: null, 'tx' => self::s($x, 'tx', 80) ?: null, 'holder' => self::s($x, 'holder', 120) ?: null); } } }
        $t = self::t('jobs'); self::begin($db); $ok = true;
        if ($id) { $ok = $db->update($t, $row, array('id' => $id)) !== false; }
        else { $row['created'] = self::now(); $id = self::with_number($db, function ($no) use ($db, $t, &$row) { $row['service_no'] = $no; return $db->insert($t, $row) ? (int) $db->insert_id : 0; }); $ok = $id > 0; }
        if ($ok) {
            foreach ($lists as $kind => $rows) {
                $db->delete(self::t('job_parts'), array('job_id' => $id, 'kind' => $kind));
                foreach ($rows as $x) { $x['job_id'] = $id; if (!$db->insert(self::t('job_parts'), $x)) { $ok = false; break 2; } }
            }
        }
        if ($ok && $pays !== null) { $db->delete(self::t('job_pays'), array('job_id' => $id)); foreach ($pays as $x) { $x['job_id'] = $id; if (!$db->insert(self::t('job_pays'), $x)) { $ok = false; break; } } }
        if (!self::end($db, $ok)) { return self::fail('ذخیره نشد: ' . $db->last_error, 500); }
        $j = MZC_Crm::job($id);
        self::log($a, $old ? 'update' : 'create', 'job', $id, (string) $j['service_no']);
        return self::ok(self::job_out($j, true, self::money_seen($a), self::cost_seen($a)), $old ? 200 : 201);
    }

    public static function rest_job_status($req)
    {
        $a = MZC_Staff::auth($req, 'jobs', 2); if ($a instanceof WP_REST_Response) { return $a; }
        $p = self::body($req); $id = self::n($p, 'id'); $st = isset($p['status']) ? (string) $p['status'] : '';
        if (!isset(MZC_Crm::$statuses[$st])) { return self::fail('وضعیت درست نیست.'); }
        $db = MZC_Crm::db(); $j = $id ? MZC_Crm::job($id) : null; if (!$j) { return self::fail('سرویس پیدا نشد.', 404); }
        if (isset($p['updated']) && (string) $p['updated'] !== (string) $j['updated']) { return self::fail('این سرویس در همین فاصله را کس دیگری عوض کرده است؛ دوباره بازش کنید.', 409, array('current' => self::job_out($j, false, self::money_seen($a)))); }
        $row = array('status' => $st, 'service_done' => in_array($st, array('ready', 'delivered'), true) ? 1 : 0, 'updated' => self::now());
        if ($st === 'delivered' && !$j['closed_at']) { $row['closed_at'] = gmdate('Y-m-d'); }
        $db->update(self::t('jobs'), $row, array('id' => $id));
        self::log($a, 'status', 'job', $id, $j['status'] . ' → ' . $st);
        return self::ok(self::job_out(MZC_Crm::job($id), false, self::money_seen($a)));
    }

    /* ---------- stock: products, suppliers, purchase invoices, serial numbers ---------- */

    /** Products are seen by the stock people and by whoever fills a computer's or a service's parts (the forms pick from them). */
    private static function stock_auth($req, $need = 1)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        if ($need === 1 && (MZC_Staff::can($a['id'], 'stock', 1) || MZC_Staff::can($a['id'], 'builds', 2) || MZC_Staff::can($a['id'], 'jobs', 2))) { return $a; }
        return MZC_Staff::can($a['id'], 'stock', $need) ? $a : self::fail('دسترسی این کار را ندارید.', 403);
    }

    private static function product_out($p, $a)
    {
        $o = array('id' => (int) $p['id'], 'name' => $p['name'], 'category' => $p['category'], 'categoryText' => isset(MZC_Crm::$categories[$p['category']]) ? MZC_Crm::$categories[$p['category']] : '', 'brand' => (string) $p['brand'], 'sku' => (string) $p['sku'],
            'sell_price' => $p['sell_price'] === null ? null : (int) $p['sell_price'], 'warranty_months' => $p['warranty_months'] === null ? null : (int) $p['warranty_months'], 'notes' => (string) $p['notes'], 'stock' => isset($p['stock']) ? (int) $p['stock'] : null, 'updated' => $p['updated']);
        if (self::cost_seen($a)) { $o['buy_price'] = $p['buy_price'] === null ? null : (int) $p['buy_price']; }
        return $o;
    }

    public static function rest_products($req)
    {
        $a = self::stock_auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $pg = self::page($req); list($rows, $total) = MZC_Stock::products(trim((string) $req->get_param('q')), $pg, self::per($req), (string) $req->get_param('category'), (bool) $req->get_param('instock'));
        return self::ok(array('total' => $total, 'page' => $pg, 'per' => self::per($req), 'items' => array_map(function ($p) use ($a) { return self::product_out($p, $a); }, $rows)));
    }

    /** One product with where it came from and where it went (the purchase side needs the stock area; prices follow the money rights). */
    public static function rest_product($req)
    {
        $a = self::stock_auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $r = MZC_Stock::report((int) $req['id']); if (!$r) { return self::fail('کالا پیدا نشد.', 404); }
        $cost = self::cost_seen($a); $money = self::money_seen($a); $out = array('product' => self::product_out($r['product'], $a), 'bought' => array(), 'used' => array(), 'totals' => array('bought' => $r['totals']['bought'], 'used' => $r['totals']['used'], 'stock' => $r['totals']['stock']));
        if (MZC_Staff::can($a['id'], 'stock', 1)) {
            foreach ($r['bought'] as $x) { $o = array('purchase_id' => (int) $x['purchase_id'], 'invoice_no' => (string) $x['invoice_no'], 'bought_at' => (string) $x['bought_at'], 'bought_at_j' => self::jd($x['bought_at']), 'vendor_id' => (int) $x['vendor_id'], 'vendor_name' => (string) $x['vendor_name'], 'qty' => (int) $x['qty'], 'serials' => array_values(array_filter(explode("\n", (string) $x['serials'])))); if ($cost) { $o['unit_cost'] = (int) $x['unit_cost']; } $out['bought'][] = $o; }
            if ($cost) { $out['totals']['spent'] = $r['totals']['spent']; }
        }
        foreach ($r['used'] as $x) {
            if (!MZC_Staff::can($a['id'], $x['kind'] === 'build' ? 'builds' : 'jobs', 1)) { continue; }
            $o = array('kind' => $x['kind'], 'id' => (int) $x['id'], 'number' => (string) $x['number'], 'customer_id' => (int) $x['customer_id'], 'customer_name' => $x['customer_name'], 'at' => (string) $x['at'], 'at_j' => self::jd($x['at']), 'qty' => (int) $x['qty'], 'serial' => (string) $x['serial']);
            if ($money) { $o['price'] = $x['price'] === null ? null : (int) $x['price']; } if ($cost) { $o['cost'] = $x['cost'] === null ? null : (int) $x['cost']; }
            $out['used'][] = $o;
        }
        if ($money) { $out['totals']['sold'] = $r['totals']['sold']; }
        return self::ok($out);
    }

    public static function rest_product_save($req)
    {
        $a = self::stock_auth($req, 2); if ($a instanceof WP_REST_Response) { return $a; }
        $p = self::body($req); $id = self::n($p, 'id'); $d = array();
        if ($id) {
            $old = MZC_Stock::product($id); if (!$old) { return self::fail('کالا پیدا نشد.', 404); }
            if (isset($p['updated']) && (string) $p['updated'] !== (string) $old['updated']) { return self::fail('این کالا در همین فاصله را کس دیگری ذخیره کرده است؛ دوباره بازش کنید.', 409, array('current' => self::product_out($old, $a))); }
        }
        foreach (array('name', 'category', 'brand', 'sku', 'notes', 'warranty_months', 'sell_price') as $k) { if (array_key_exists($k, $p)) { $d[$k] = is_scalar($p[$k]) || $p[$k] === null ? (string) $p[$k] : ''; } }
        if (array_key_exists('buy_price', $p) && self::cost_edit($a)) { $d['buy_price'] = is_scalar($p['buy_price']) ? (string) $p['buy_price'] : ''; }
        $r = MZC_Stock::save_product($id, $d);
        if (!is_int($r)) { return self::fail($r, 422); }
        self::log($a, $id ? 'update' : 'create', 'product', $r, Mazesta_Connect::clip(isset($d['name']) ? $d['name'] : '', 120));
        return self::ok(self::product_out(MZC_Stock::product($r), $a), $id ? 200 : 201);
    }

    public static function rest_suppliers($req)
    {
        $a = MZC_Staff::auth($req, 'stock', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $kinds = MZC_Ships::$kinds;
        return self::ok(array('items' => array_map(function ($v) use ($kinds) { return array('id' => (int) $v['id'], 'name' => $v['name'], 'kind' => $v['kind'], 'kindText' => isset($kinds[$v['kind']]) ? $kinds[$v['kind']] : '', 'phone' => (string) $v['phone'], 'notes' => (string) $v['notes'],
            'purchases' => (int) $v['purchases'], 'last_at' => (string) $v['last_at'], 'last_at_j' => self::jd($v['last_at'])); }, MZC_Stock::suppliers())));
    }

    public static function rest_supplier_save($req)
    {
        $a = MZC_Staff::auth($req, 'stock', 2); if ($a instanceof WP_REST_Response) { return $a; }
        $p = self::body($req); $id = self::n($p, 'id');
        $r = MZC_Stock::save_supplier($id, array('name' => self::s($p, 'name', 120), 'kind' => self::s($p, 'kind', 10) ?: 'supplier', 'phone' => self::s($p, 'phone', 60), 'notes' => self::s($p, 'notes', 255)));
        if (!is_int($r)) { return self::fail($r, 422); }
        self::log($a, $id ? 'update' : 'create', 'vendor', $r, self::s($p, 'name', 120));
        return self::ok(array('id' => $r, 'name' => self::s($p, 'name', 120)), $id ? 200 : 201);
    }

    private static function purchase_out($u, $a, $full)
    {
        $cost = self::cost_seen($a);
        $o = array('id' => (int) $u['id'], 'vendor_id' => (int) $u['vendor_id'], 'vendor_name' => (string) $u['vendor_name'], 'invoice_no' => (string) $u['invoice_no'], 'bought_at' => (string) $u['bought_at'], 'bought_at_j' => self::jd($u['bought_at']), 'notes' => (string) $u['notes'], 'updated' => $u['updated']);
        if (isset($u['line_count'])) { $o['lines'] = (int) $u['line_count']; }
        if ($cost && isset($u['total'])) { $o['total'] = (int) $u['total']; }
        if ($full) {
            $o['items'] = array();
            foreach ($u['items'] as $i) { $x = array('product_id' => (int) $i['product_id'], 'name' => (string) $i['product_name'], 'category' => (string) $i['category'], 'qty' => (int) $i['qty'], 'serials' => $i['serial_list']); if ($cost) { $x['unit_cost'] = (int) $i['unit_cost']; } $o['items'][] = $x; }
        }
        return $o;
    }

    public static function rest_purchases($req)
    {
        $a = MZC_Staff::auth($req, 'stock', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $pg = self::page($req); list($rows, $total) = MZC_Stock::purchases(trim((string) $req->get_param('q')), (int) $req->get_param('vendor'), $pg, self::per($req));
        return self::ok(array('total' => $total, 'page' => $pg, 'per' => self::per($req), 'items' => array_map(function ($u) use ($a) { return self::purchase_out($u, $a, false); }, $rows)));
    }

    public static function rest_purchase($req)
    {
        $a = MZC_Staff::auth($req, 'stock', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $u = MZC_Stock::purchase((int) $req['id']); if (!$u) { return self::fail('فاکتور خرید پیدا نشد.', 404); }
        return self::ok(self::purchase_out($u, $a, true));
    }

    /** A purchase invoice; the costs on it are what entering it is for, so it needs the stock area's edit right (which also lets a person see buy prices). */
    public static function rest_purchase_save($req)
    {
        $a = MZC_Staff::auth($req, 'stock', 2); if ($a instanceof WP_REST_Response) { return $a; }
        $p = self::body($req); $id = self::n($p, 'id');
        if ($id) {
            $old = MZC_Stock::purchase($id); if (!$old) { return self::fail('فاکتور خرید پیدا نشد.', 404); }
            if (isset($p['updated']) && (string) $p['updated'] !== (string) $old['updated']) { return self::fail('این فاکتور در همین فاصله را کس دیگری ذخیره کرده است؛ دوباره بازش کنید.', 409, array('current' => self::purchase_out($old, $a, true))); }
        }
        $items = array();
        foreach (isset($p['items']) && is_array($p['items']) ? $p['items'] : array() as $x) {
            if (!is_array($x)) { continue; }
            $items[] = array('product_id' => self::n($x, 'product_id'), 'name' => self::s($x, 'name', 190), 'category' => self::s($x, 'category', 40), 'qty' => self::n($x, 'qty'), 'unit_cost' => self::n($x, 'unit_cost'),
                'sell_price' => self::opt_n($x, 'sell_price'), 'serials' => isset($x['serials']) && is_array($x['serials']) ? array_map('strval', array_filter($x['serials'], 'is_scalar')) : self::s($x, 'serials', 20000));
        }
        $r = MZC_Stock::save_purchase($id, array('vendor_id' => self::n($p, 'vendor_id'), 'vendor_name' => self::s($p, 'vendor_name', 120), 'invoice_no' => self::s($p, 'invoice_no', 60), 'bought_at' => self::d($p, 'bought_at'),
            'notes' => self::s($p, 'notes', 2000), 'force' => !empty($p['force']), 'items' => $items));
        if (is_array($r)) { return self::fail('این فروشنده فاکتوری با همین شماره دارد.', 409, array('duplicate' => array('id' => $r['duplicate']))); }
        if (!is_int($r)) { return self::fail($r, 422); }
        $u = MZC_Stock::purchase($r);
        self::log($a, $id ? 'update' : 'create', 'purchase', $r, trim($u['vendor_name'] . ' ' . $u['invoice_no']));
        return self::ok(self::purchase_out($u, $a, true), $id ? 200 : 201);
    }

    /** Where a serial number has been (bought, in a computer, in a service, sent to warranty), each part only to those who may see that area. */
    public static function rest_serial($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $q = trim((string) $req->get_param('q')); if (mb_strlen($q) < 3) { return self::fail('دست‌کم سه نویسه از سریال را بنویسید.'); }
        $t = MZC_Stock::trace($q); $cost = self::cost_seen($a);
        foreach ($t as &$rows) { foreach ($rows as &$x) { foreach (array('id', 'product_id', 'purchase_id', 'unit_cost') as $k) { if (isset($x[$k])) { $x[$k] = (int) $x[$k]; } } } unset($x); } unset($rows);
        $out = array('bought' => array(), 'builds' => array(), 'jobs' => array(), 'ships' => array());
        if (MZC_Staff::can($a['id'], 'stock', 1)) { foreach ($t['bought'] as $x) { if (!$cost) { unset($x['unit_cost']); } $out['bought'][] = $x; } }
        if (MZC_Staff::can($a['id'], 'builds', 1)) { $out['builds'] = $t['builds']; }
        if (MZC_Staff::can($a['id'], 'jobs', 1)) { $out['jobs'] = $t['jobs']; }
        if (MZC_Staff::can($a['id'], 'ships', 1)) { $out['ships'] = $t['ships']; }
        return self::ok($out);
    }

    /* ---------- the period report ---------- */

    /** ?from=&to= (Gregorian or Persian dates; default this Persian month) or ?period=month|last|d30|year. Each part only for those who may see it. */
    public static function rest_stats($req)
    {
        $a = MZC_Staff::auth($req, 'reports', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $jobs = MZC_Staff::can($a['id'], 'jobs', 1); $builds = MZC_Staff::can($a['id'], 'builds', 1);
        if (!$jobs && !$builds) { return self::fail('دسترسی این کار را ندارید.', 403); }
        $per = MZC_Report::periods(); $k = (string) $req->get_param('period'); $def = isset($per[$k]) ? $per[$k] : $per['month'];
        $from = MZC_Crm::gdate((string) $req->get_param('from')) ?: $def[1]; $to = MZC_Crm::gdate((string) $req->get_param('to')) ?: $def[2];
        if ($from > $to) { list($from, $to) = array($to, $from); }
        $r = MZC_Report::build($from, $to); if (!$r) { return self::fail(MZC_Crm_Db::error(), 503); }
        $r = MZC_Report::visible($r, $jobs, $builds, self::money_seen($a), self::cost_seen($a) && self::money_seen($a));
        $r['periods'] = array_map(function ($x) { return array('label' => $x[0], 'from' => $x[1], 'to' => $x[2]); }, $per);
        return self::ok($r);
    }

    /* ---------- test reports ---------- */

    public static function rest_reports($req)
    {
        $a = MZC_Staff::auth($req, 'tests', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $svc = trim((string) $req->get_param('service')); $q = mb_strtolower(trim((string) $req->get_param('q'))); $rows = Mazesta_Connect::read('reports'); $out = array();
        uasort($rows, function ($x, $y) { return strcmp((string) $y['created'], (string) $x['created']); });
        foreach ($rows as $id => $r) {
            $hay = mb_strtolower($r['service'] . ' ' . $r['device'] . ' ' . $r['machine'] . ' ' . $r['title']);
            if ($q !== '' && mb_strpos($hay, $q) === false) { continue; }
            $out[] = array('id' => (string) $id, 'service' => (string) $r['service'], 'device' => (string) $r['device'], 'machine' => (string) $r['machine'], 'title' => (string) $r['title'], 'verdict' => (string) $r['verdict'],
                'summary' => (string) $r['summary'], 'created' => (string) $r['created'], 'match' => $svc !== '' && $r['service'] === $svc);
            if (count($out) >= 100) { break; }
        }
        usort($out, function ($x, $y) { return (int) $y['match'] <=> (int) $x['match']; });
        return self::ok(array('items' => $out));
    }

    public static function rest_report($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        if (!MZC_Staff::can($a['id'], 'tests', 1) && !MZC_Staff::can($a['id'], 'builds', 1) && !MZC_Staff::can($a['id'], 'jobs', 1)) { return self::fail('دسترسی این کار را ندارید.', 403); }
        $id = (string) $req['id']; $rows = Mazesta_Connect::read('reports'); $html = Mazesta_Connect::raw('report-' . $id);
        if (!isset($rows[$id]) || $html === null) { return self::fail('گزارش پیدا نشد.', 404); }
        $r = $rows[$id];
        return self::ok(array('id' => $id, 'service' => (string) $r['service'], 'device' => (string) $r['device'], 'machine' => (string) $r['machine'], 'title' => (string) $r['title'], 'verdict' => (string) $r['verdict'], 'summary' => (string) $r['summary'],
            'notes' => (string) $r['notes'], 'created' => (string) $r['created'], 'html' => $html));
    }

    public static function rest_attach($req)
    {
        $a = MZC_Staff::auth($req, 'tests', 2); if ($a instanceof WP_REST_Response) { return $a; }
        $p = self::body($req); $kind = isset($p['kind']) && $p['kind'] === 'build' ? 'build' : 'job'; $id = self::n($p, 'id'); $rid = self::s($p, 'report_id', 64);
        if (!MZC_Staff::can($a['id'], $kind === 'build' ? 'builds' : 'jobs', 1)) { return self::fail('دسترسی این کار را ندارید.', 403); }
        $reports = Mazesta_Connect::read('reports');
        if ($rid !== '' && !isset($reports[$rid])) { return self::fail('این گزارش روی سایت نیست.', 404); }
        $db = MZC_Crm::db(); $tb = self::t($kind === 'build' ? 'builds' : 'jobs');
        if (!$id || !$db->get_var($db->prepare("SELECT id FROM `$tb` WHERE id = %d", $id))) { return self::fail('پرونده پیدا نشد.', 404); }
        $db->update($tb, array('report_id' => $rid !== '' ? $rid : null, 'updated' => self::now()), array('id' => $id));
        self::log($a, 'report', $kind, $id, $rid);
        return self::ok(array('ok' => true, 'report_id' => $rid));
    }

    /* ---------- receipt SMS and label ---------- */

    public static function rest_receipt($req)
    {
        $a = MZC_Staff::auth($req, 'sms', 2); if ($a instanceof WP_REST_Response) { return $a; }
        if (!MZC_Sms::configured()) { return self::fail('پیامک در تنظیمات سایت فعال نشده است.', 503); }
        $p = self::body($req); $kind = isset($p['kind']) && $p['kind'] === 'build' ? 'build' : 'job'; $id = self::n($p, 'id');
        if (!MZC_Staff::can($a['id'], $kind === 'build' ? 'builds' : 'jobs', 1)) { return self::fail('دسترسی این کار را ندارید.', 403); }
        $rec = $kind === 'build' ? MZC_Crm::build($id) : MZC_Crm::job($id); if (!$rec) { return self::fail('پرونده پیدا نشد.', 404); }
        $c = MZC_Crm::customer($rec['customer_id']); $m = $c ? (string) $c['mobile'] : '';
        if ($m === '') { return self::fail('این مشتری شمارهٔ موبایل ندارد.', 422); }
        $db = MZC_Crm::db();
        $n = (int) $db->get_var($db->prepare('SELECT COUNT(*) FROM `' . self::t('audit') . "` WHERE action = 'sms' AND entity = %s AND entity_id = %d AND at > %s", $kind, $id, gmdate('Y-m-d H:i:s', time() - 86400)));
        if ($n >= 3) { return self::fail('امروز سه پیامک برای این پرونده فرستاده شده است.', 429); }
        $cf = self::cfg();
        $no = $kind === 'build' && $rec['invoice_no'] ? (string) $rec['invoice_no'] : (string) $rec['service_no'];
        $text = trim(strtr((string) $cf['receipt'], array('{no}' => $no, '{name}' => $c['name'], '{url}' => (string) $cf['portalUrl'])));
        $r = MZC_Sms::send($m, $text, '');
        if ($r !== true) { return self::fail('پیامک فرستاده نشد: ' . $r, 502); }
        self::log($a, 'sms', $kind, $id, $m);
        return self::ok(array('sent' => true, 'to' => $m, 'text' => $text));
    }

    public static function rest_label($req)
    {
        $kind = (string) $req['kind']; $a = MZC_Staff::auth($req, $kind === 'build' ? 'builds' : 'jobs', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $rec = $kind === 'build' ? MZC_Crm::build((int) $req['id']) : MZC_Crm::job((int) $req['id']); if (!$rec) { return self::fail('پرونده پیدا نشد.', 404); }
        $c = MZC_Crm::customer($rec['customer_id']); $keys = self::label_keys($kind, self::cfg()); $lines = array();
        $val = array('service_no' => (string) $rec['service_no'], 'customer' => $c ? $c['name'] : '', 'mobile' => $c ? (string) $c['mobile'] : '', 'invoice' => (string) $rec['invoice_no'], 'status' => $kind === 'build' ? (isset(MZC_Crm::$build_statuses[$rec['status']]) ? MZC_Crm::$build_statuses[$rec['status']] : '') : (isset(MZC_Crm::$statuses[$rec['status']]) ? MZC_Crm::$statuses[$rec['status']] : ''));
        if ($kind === 'build') { $val += array('title' => $rec['title'], 'sold' => self::jd($rec['sold_at'])); }
        else { $val += array('device' => (string) $rec['device'], 'received' => self::jd($rec['received_at']), 'due' => self::jd($rec['due_at']), 'complaint' => mb_substr((string) $rec['complaint'], 0, 140)); }
        foreach ($keys as $k) { $lines[] = array('key' => $k, 'label' => self::$label_fields[$kind][$k], 'value' => isset($val[$k]) ? $val[$k] : ''); }
        $no = $kind === 'build' && $rec['invoice_no'] ? (string) $rec['invoice_no'] : (string) $rec['service_no'];
        return self::ok(array('kind' => $kind, 'id' => (int) $rec['id'], 'number' => $no, 'qr' => $no, 'lines' => $lines));
    }
}
