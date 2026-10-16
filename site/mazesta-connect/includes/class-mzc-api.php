<?php
if (!defined('ABSPATH')) { exit; }

/**
 * What the staff apps (desktop, Android) read and write: customers, new computers (builds), service jobs, the app's test reports, the SMS receipt and the case label.
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
        'build' => array('service_no' => 'شمارهٔ سرویس', 'customer' => 'نام مشتری', 'mobile' => 'موبایل', 'title' => 'عنوان سیستم', 'invoice' => 'شمارهٔ فاکتور', 'sold' => 'تاریخ فروش', 'status' => 'وضعیت'),
    );
    public static $label_default = array('job' => array('service_no', 'customer', 'mobile', 'device', 'received'), 'build' => array('service_no', 'customer', 'title', 'invoice'));
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
    private static function d($p, $k) { $v = MZC_Crm::digits(self::s($p, $k, 10)); return preg_match('/^\d{4}-\d{2}-\d{2}$/', $v) ? $v : null; }
    private static function flag($p, $k) { return !empty($p[$k]) ? 1 : 0; }
    private static function now() { return gmdate('Y-m-d H:i:s'); }
    private static function t($n) { return MZC_Crm_Db::t($n); }
    private static function cfg() { $c = Mazesta_Connect::read('config'); $x = isset($c['staffCfg']) && is_array($c['staffCfg']) ? $c['staffCfg'] : array(); return $x + array('receipt' => self::RECEIPT_TEXT, 'portalUrl' => '', 'labels' => array()); }
    private static function page($req) { return max(1, (int) $req->get_param('page')); }
    private static function jd($ymd) { return $ymd ? MZC_Crm::jdate($ymd) : ''; }

    public static function log($a, $action, $entity, $id, $note = '')
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return; }
        $u = $a['user'];
        $db->insert(self::t('audit'), array('user_id' => (int) $a['id'], 'who' => mb_substr($u->display_name !== '' ? $u->display_name : $u->user_login, 0, 120), 'action' => $action, 'entity' => $entity, 'entity_id' => (int) $id, 'note' => mb_substr($note, 0, 255), 'at' => self::now()));
    }

    private static function money_seen($a) { return MZC_Staff::can($a['id'], 'payments', 1); }

    /* ---------- how records are sent out ---------- */

    private static function customer_out($c)
    {
        return array('id' => (int) $c['id'], 'name' => $c['name'], 'mobile' => (string) $c['mobile'], 'mobile2' => (string) $c['mobile2'], 'phone' => (string) $c['phone'], 'national_id' => (string) $c['national_id'], 'email' => (string) $c['email'],
            'address' => (string) $c['address'], 'notes' => (string) $c['notes'], 'group' => (string) $c['grp'], 'groupText' => isset(MZC_Crm::$groups[$c['grp']]) ? MZC_Crm::$groups[$c['grp']] : '', 'updated' => $c['updated']);
    }

    private static function build_out($b, $full)
    {
        $o = array('id' => (int) $b['id'], 'service_no' => (string) $b['service_no'], 'invoice_no' => (string) $b['invoice_no'], 'title' => $b['title'], 'status' => $b['status'], 'statusText' => isset(MZC_Crm::$build_statuses[$b['status']]) ? MZC_Crm::$build_statuses[$b['status']] : '',
            'customer_id' => (int) $b['customer_id'], 'sold_at' => (string) $b['sold_at'], 'sold_at_j' => self::jd($b['sold_at']), 'report_id' => (string) $b['report_id'], 'updated' => $b['updated']);
        if (isset($b['customer_name'])) { $o['customer_name'] = $b['customer_name']; $o['customer_mobile'] = (string) $b['customer_mobile']; $o['parts_count'] = (int) $b['parts']; }
        if ($full) {
            $o['notes'] = (string) $b['notes']; $o['parts'] = array();
            foreach ($b['parts'] as $p) { $o['parts'][] = array('category' => $p['category'], 'model' => $p['model'], 'serial' => (string) $p['serial'], 'warranty_months' => $p['warranty_months'] === null ? null : (int) $p['warranty_months'], 'vendor' => (string) $p['vendor'], 'note' => (string) $p['note'],
                'has_warranty' => (bool) $p['has_warranty'], 'has_box' => (bool) $p['has_box'], 'qc' => (bool) $p['qc']); }
            $o['photos'] = count($b['photos']);
        }
        return $o;
    }

    private static function job_out($j, $full, $money)
    {
        $o = array('id' => (int) $j['id'], 'service_no' => (string) $j['service_no'], 'device' => (string) $j['device'], 'status' => $j['status'], 'statusText' => isset(MZC_Crm::$statuses[$j['status']]) ? MZC_Crm::$statuses[$j['status']] : '',
            'customer_id' => (int) $j['customer_id'], 'received_at' => (string) $j['received_at'], 'received_at_j' => self::jd($j['received_at']), 'closed_at' => (string) $j['closed_at'], 'due_at' => (string) $j['due_at'], 'due_at_j' => self::jd($j['due_at']),
            'invoice_no' => (string) $j['invoice_no'], 'report_id' => (string) $j['report_id'], 'updated' => $j['updated']);
        if (isset($j['customer_name'])) { $o['customer_name'] = $j['customer_name']; $o['customer_mobile'] = (string) $j['customer_mobile']; }
        if ($money && isset($j['total'])) { $o['total'] = (int) $j['total']; }
        if ($full) {
            $o += array('complaint' => (string) $j['complaint'], 'work_done' => (string) $j['work_done'], 'notes' => (string) $j['notes'], 'is_mazesta' => (bool) $j['is_mazesta'], 'has_warranty' => (bool) $j['has_warranty'], 'photos' => count($j['photos']));
            $map = function ($rows) use ($money) {
                $out = array();
                foreach ($rows as $p) { $r = array('name' => $p['name'], 'qty' => (int) $p['qty'], 'serial' => (string) $p['serial'], 'warranty_months' => $p['warranty_months'] === null ? null : (int) $p['warranty_months'], 'note' => (string) $p['note'], 'has_box' => (bool) $p['has_box'], 'has_warranty' => (bool) $p['has_warranty']); if ($money) { $r['unit_price'] = (int) $p['unit_price']; } $out[] = $r; }
                return $out;
            };
            $o['parts'] = $map($j['parts']); $o['recv'] = $map($j['recv']);
            if ($money) {
                $o += array('labor_price' => (int) $j['labor_price'], 'discount' => (int) $j['discount'], 'paid' => (int) $j['paid']); $o['pays'] = array();
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
        return self::ok(array('buildStatuses' => MZC_Crm::$build_statuses, 'jobStatuses' => MZC_Crm::$statuses, 'categories' => MZC_Crm::$categories, 'groups' => MZC_Crm::$groups, 'labels' => $labels, 'receiptReady' => MZC_Sms::configured() && MZC_Staff::can($a['id'], 'sms', 2)));
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
        $pg = self::page($req); list($rows, $total) = MZC_Crm::customers(trim((string) $req->get_param('q')), $pg, self::PER, (string) $req->get_param('group'));
        return self::ok(array('total' => $total, 'page' => $pg, 'per' => self::PER, 'items' => array_map(function ($c) { return self::customer_out($c) + array('builds' => (int) $c['builds'], 'jobs' => (int) $c['jobs']); }, $rows)));
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
        $pg = self::page($req); list($rows, $total) = MZC_Crm::builds(trim((string) $req->get_param('q')), $pg, self::PER, 0, (string) $req->get_param('status'));
        return self::ok(array('total' => $total, 'page' => $pg, 'per' => self::PER, 'items' => array_map(function ($b) { return self::build_out($b, false); }, $rows)));
    }

    public static function rest_build($req)
    {
        $a = MZC_Staff::auth($req, 'builds', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $b = MZC_Crm::build((int) $req['id']); if (!$b) { return self::fail('سیستم پیدا نشد.', 404); }
        $c = MZC_Crm::customer($b['customer_id']); $o = self::build_out($b, true);
        if ($c) { $o['customer_name'] = $c['name']; $o['customer_mobile'] = (string) $c['mobile']; }
        return self::ok($o);
    }

    public static function rest_jobs($req)
    {
        $a = MZC_Staff::auth($req, 'jobs', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $pg = self::page($req); list($rows, $total) = MZC_Crm::jobs(trim((string) $req->get_param('q')), (string) $req->get_param('status'), $pg, self::PER);
        $money = self::money_seen($a);
        return self::ok(array('total' => $total, 'page' => $pg, 'per' => self::PER, 'items' => array_map(function ($j) use ($money) { return self::job_out($j, false, $money); }, $rows)));
    }

    public static function rest_job($req)
    {
        $a = MZC_Staff::auth($req, 'jobs', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $j = MZC_Crm::job((int) $req['id']); if (!$j) { return self::fail('سرویس پیدا نشد.', 404); }
        $c = MZC_Crm::customer($j['customer_id']); $o = self::job_out($j, true, self::money_seen($a));
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
        $r = MZC_Crm::save_customer(0, self::s($p, 'c_name', 190), $m);
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
        $r = MZC_Crm::save_customer($id, self::s($p, 'name', 190), self::s($p, 'mobile', 30), array('mobile2' => self::s($p, 'mobile2', 30), 'phone' => self::s($p, 'phone', 80) ?: null, 'national_id' => $nid ?: null,
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
            if (isset($p['updated']) && (string) $p['updated'] !== (string) $old['updated']) { return self::fail('این سیستم در همین فاصله را کس دیگری ذخیره کرده است؛ دوباره بازش کنید.', 409, array('current' => self::build_out($old, true))); }
        }
        $cid = $id && empty($p['customer_id']) && empty($p['c_mobile']) ? (int) $old['customer_id'] : self::customer_for($p, $a);
        if ($cid instanceof WP_REST_Response) { return $cid; }
        $title = self::s($p, 'title', 190) ?: ($old ? $old['title'] : 'سیستم نو');
        $st = isset($p['status']) && isset(MZC_Crm::$build_statuses[$p['status']]) ? (string) $p['status'] : ($old ? $old['status'] : 'queue');
        $inv = self::s($p, 'invoice_no', 60);
        if ($inv !== '' && empty($p['force'])) {
            $dup = $db->get_row($db->prepare('SELECT id, service_no FROM `' . self::t('builds') . '` WHERE invoice_no = %s AND id <> %d LIMIT 1', $inv, $id), ARRAY_A);
            if ($dup) { return self::fail('شمارهٔ فاکتور ' . $inv . ' قبلاً برای سیستم ' . $dup['service_no'] . ' ثبت شده است.', 409, array('duplicate' => array('id' => (int) $dup['id'], 'service_no' => (string) $dup['service_no']))); }
        }
        $rid = self::s($p, 'report_id', 64);
        $row = array('customer_id' => $cid, 'invoice_no' => $inv !== '' ? $inv : ($old ? $old['invoice_no'] : null), 'title' => $title, 'sold_at' => array_key_exists('sold_at', $p) ? self::d($p, 'sold_at') : ($old ? $old['sold_at'] : gmdate('Y-m-d')),
            'notes' => array_key_exists('notes', $p) ? self::s($p, 'notes', 4000) : ($old ? $old['notes'] : ''), 'status' => $st, 'delivered' => $st === 'delivered' ? 1 : 0, 'updated' => self::now());
        if ($rid !== '' && preg_match('/^[A-Za-z0-9-]{8,64}$/', $rid) && MZC_Staff::can($a['id'], 'tests', 2)) { $row['report_id'] = $rid; }
        $parts = null;
        if (isset($p['parts']) && is_array($p['parts'])) {
            $parts = array();
            foreach ($p['parts'] as $x) {
                if (!is_array($x) || self::s($x, 'model', 190) === '') { continue; }
                $cat = isset($x['category'], MZC_Crm::$categories[$x['category']]) ? $x['category'] : 'other'; $m = self::opt_n($x, 'warranty_months');
                $parts[] = array('category' => $cat, 'model' => self::s($x, 'model', 190), 'serial' => self::s($x, 'serial', 120) ?: null, 'warranty_months' => $m, 'vendor' => self::s($x, 'vendor', 120) ?: null, 'note' => self::s($x, 'note', 255) ?: null,
                    'has_warranty' => array_key_exists('has_warranty', $x) ? self::flag($x, 'has_warranty') : ($m ? 1 : 0), 'has_box' => self::flag($x, 'has_box'), 'qc' => self::flag($x, 'qc'));
            }
        }
        $t = self::t('builds'); self::begin($db); $ok = true;
        if ($id) { $ok = $db->update($t, $row, array('id' => $id)) !== false; }
        else {
            $row['created'] = self::now(); $row['service_no'] = self::s($p, 'service_no', 40) ?: null;
            $ins = function ($no) use ($db, $t, &$row) { if ($row['service_no'] === null) { $row['service_no'] = $no; } return $db->insert($t, $row) ? (int) $db->insert_id : 0; };
            $id = self::with_number($db, $ins); $ok = $id > 0;
        }
        if ($ok && $parts !== null) {
            $db->delete(self::t('build_parts'), array('build_id' => $id));
            foreach ($parts as $x) { $x['build_id'] = $id; if (!$db->insert(self::t('build_parts'), $x)) { $ok = false; break; } }
        }
        if (!self::end($db, $ok)) { return self::fail('ذخیره نشد: ' . $db->last_error, 500); }
        $b = MZC_Crm::build($id); $out = self::build_out($b, true);
        self::log($a, $old ? 'update' : 'create', 'build', $id, (string) $b['service_no']);
        // a serial already on another system: said, not refused (a part can legitimately come back and be sold again)
        $warn = array();
        foreach ($b['parts'] as $x) {
            if ($x['serial'] === null || $x['serial'] === '') { continue; }
            $o = $db->get_row($db->prepare('SELECT b.id, b.service_no FROM `' . self::t('build_parts') . '` p JOIN `' . $t . '` b ON b.id = p.build_id WHERE p.serial = %s AND p.build_id <> %d LIMIT 1', $x['serial'], $id), ARRAY_A);
            if ($o) { $warn[] = 'سریال ' . $x['serial'] . ' روی سیستم ' . $o['service_no'] . ' هم ثبت شده است.'; }
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
            if (isset($p['updated']) && (string) $p['updated'] !== (string) $old['updated']) { return self::fail('این سرویس در همین فاصله را کس دیگری ذخیره کرده است؛ دوباره بازش کنید.', 409, array('current' => self::job_out($old, true, self::money_seen($a)))); }
        }
        $cid = $id && empty($p['customer_id']) && empty($p['c_mobile']) ? (int) $old['customer_id'] : self::customer_for($p, $a);
        if ($cid instanceof WP_REST_Response) { return $cid; }
        $st = isset($p['status']) && isset(MZC_Crm::$statuses[$p['status']]) ? (string) $p['status'] : ($old ? $old['status'] : 'received');
        $closed = array_key_exists('closed_at', $p) ? self::d($p, 'closed_at') : ($old ? $old['closed_at'] : null);
        if ($st === 'delivered' && $closed === null) { $closed = gmdate('Y-m-d'); }
        $row = array('customer_id' => $cid, 'device' => array_key_exists('device', $p) ? (self::s($p, 'device', 190) ?: null) : ($old ? $old['device'] : null),
            'received_at' => array_key_exists('received_at', $p) ? (self::d($p, 'received_at') ?: gmdate('Y-m-d')) : ($old ? $old['received_at'] : gmdate('Y-m-d')), 'closed_at' => $closed,
            'due_at' => array_key_exists('due_at', $p) ? self::d($p, 'due_at') : ($old ? $old['due_at'] : null), 'invoice_no' => array_key_exists('invoice_no', $p) ? (self::s($p, 'invoice_no', 60) ?: null) : ($old ? $old['invoice_no'] : null),
            'is_mazesta' => array_key_exists('is_mazesta', $p) ? self::flag($p, 'is_mazesta') : ($old ? (int) $old['is_mazesta'] : 0), 'has_warranty' => array_key_exists('has_warranty', $p) ? self::flag($p, 'has_warranty') : ($old ? (int) $old['has_warranty'] : 0),
            'service_done' => in_array($st, array('ready', 'delivered'), true) ? 1 : 0, 'status' => $st, 'complaint' => array_key_exists('complaint', $p) ? self::s($p, 'complaint', 4000) : ($old ? $old['complaint'] : ''),
            'work_done' => array_key_exists('work_done', $p) ? self::s($p, 'work_done', 4000) : ($old ? $old['work_done'] : ''), 'notes' => array_key_exists('notes', $p) ? self::s($p, 'notes', 4000) : ($old ? $old['notes'] : ''), 'updated' => self::now());
        if ($pay) {
            foreach (array('labor_price', 'discount', 'paid') as $k) { $row[$k] = array_key_exists($k, $p) ? self::n($p, $k) : ($old ? (int) $old[$k] : 0); }
        } elseif (!$old) { $row += array('labor_price' => 0, 'discount' => 0, 'paid' => 0); }
        $rid = self::s($p, 'report_id', 64);
        if ($rid !== '' && preg_match('/^[A-Za-z0-9-]{8,64}$/', $rid) && MZC_Staff::can($a['id'], 'tests', 2)) { $row['report_id'] = $rid; }
        // part lists: a list not sent stays as it was. Without the right on prices a part keeps the price it had (matched by name), a new part is free.
        $price = array(); if ($old) { foreach ($old['parts'] as $x) { if (!isset($price[$x['name']])) { $price[$x['name']] = (int) $x['unit_price']; } } }
        $lists = array();
        foreach (array('add' => 'parts', 'in' => 'recv') as $kind => $field) {
            if (!isset($p[$field]) || !is_array($p[$field])) { continue; }
            $lists[$kind] = array();
            foreach ($p[$field] as $x) {
                if (!is_array($x) || self::s($x, 'name', 190) === '') { continue; }
                $name = self::s($x, 'name', 190); $unit = $kind === 'in' ? 0 : ($pay ? self::n($x, 'unit_price') : (isset($price[$name]) ? $price[$name] : 0));
                $lists[$kind][] = array('name' => $name, 'qty' => max(1, self::n($x, 'qty')), 'unit_price' => $unit, 'serial' => self::s($x, 'serial', 120) ?: null, 'warranty_months' => self::opt_n($x, 'warranty_months'), 'note' => self::s($x, 'note', 255) ?: null,
                    'has_box' => self::flag($x, 'has_box'), 'has_warranty' => self::flag($x, 'has_warranty'), 'kind' => $kind);
            }
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
        return self::ok(self::job_out($j, true, self::money_seen($a)), $old ? 200 : 201);
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
        $text = trim(strtr((string) $cf['receipt'], array('{no}' => (string) $rec['service_no'], '{name}' => $c['name'], '{url}' => (string) $cf['portalUrl'])));
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
        return self::ok(array('kind' => $kind, 'id' => (int) $rec['id'], 'number' => (string) $rec['service_no'], 'qr' => (string) $rec['service_no'], 'lines' => $lines));
    }
}
