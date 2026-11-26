<?php
if (!defined('ABSPATH')) { exit; }

/**
 * Three more things the staff program needs: the shortage list (parts the shop does not have, asked for by whoever needs them and answered by whoever buys), the warranty shipments
 * (what was sent to a warranty company, how it went, when it was handed back) and the daily backup of the CRM database to a folder the manager names.
 */
final class MZC_Desk_Api
{
    public static $short_statuses = array('open' => 'درخواست تازه', 'looking' => 'در حال پیدا کردن', 'bought' => 'خریداری شد', 'arrived' => 'رسید', 'cancelled' => 'لغو شد');
    public static $short_kinds = array('invoice' => 'فاکتور', 'job' => 'سرویس', 'build' => 'سیستم نو', 'other' => 'سایر');

    public static function boot() { add_action('rest_api_init', array(__CLASS__, 'routes')); MZC_Backup::boot(); }

    public static function routes()
    {
        $r = array(array('/staff/shortages', 'GET', 'shortages'), array('/staff/shortages', 'POST', 'shortage_save'), array('/staff/shortages/count', 'GET', 'shortage_count'),
            array('/staff/ships', 'GET', 'ships'), array('/staff/ships/(?P<id>\d+)', 'GET', 'ship'), array('/staff/ships', 'POST', 'ship_save'),
            array('/staff/backup', 'GET', 'backup'), array('/staff/backup', 'POST', 'backup_save'), array('/staff/backup/run', 'POST', 'backup_run'), array('/staff/backup/pull', 'POST', 'backup_pull'));
        foreach ($r as $x) { register_rest_route(Mazesta_Connect::NS, $x[0], array('methods' => $x[1], 'callback' => array(__CLASS__, 'rest_' . $x[2]), 'permission_callback' => '__return_true')); }
    }

    private static function fail($m, $st = 400) { return MZC_Api::fail($m, $st); }
    private static function db() { return MZC_Crm_Db::db(); }
    private static function t($n) { return MZC_Crm_Db::t($n); }
    /** Whoever buys: the stock keepers and the accountants answer a shortage. */
    private static function buyer($uid) { return MZC_Staff::can($uid, 'stock', 2) || MZC_Staff::can($uid, 'accounting', 2); }

    /* ---------- shortages ---------- */

    /** One shortage line with the names its list shows, or null. */
    public static function shortage($id)
    {
        $db = self::db(); if (!$db) { return null; }
        return $db->get_row($db->prepare('SELECT s.*, c.name AS customer_name, v.name AS vendor_name FROM `' . self::t('shortages') . '` s LEFT JOIN `' . self::t('customers') . '` c ON c.id = s.customer_id LEFT JOIN `' . self::t('vendors') . '` v ON v.id = s.vendor_id WHERE s.id = %d', (int) $id), ARRAY_A);
    }

    public static function short_out($x)
    {
        return array('id' => (int) $x['id'], 'name' => $x['name'], 'qty' => (int) $x['qty'], 'kind' => $x['kind'], 'kindText' => isset(self::$short_kinds[$x['kind']]) ? self::$short_kinds[$x['kind']] : '', 'ref' => (string) $x['ref'],
            'customer_id' => (int) $x['customer_id'], 'customer_name' => isset($x['customer_name']) ? (string) $x['customer_name'] : '', 'status' => $x['status'], 'statusText' => self::$short_statuses[$x['status']], 'note' => (string) $x['note'],
            'vendor_id' => (int) $x['vendor_id'], 'vendor_name' => isset($x['vendor_name']) ? (string) $x['vendor_name'] : '', 'who' => $x['who'], 'created_j' => MZC_Api::jd(substr($x['created'], 0, 10)), 'updated' => $x['updated']);
    }

    public static function rest_shortages($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $db = self::db(); if (!$db) { return self::fail(MZC_Crm_Db::error(), 503); }
        $st = (string) $req->get_param('status'); $q = (string) $req->get_param('q'); $where = array('1=1'); $args = array();
        if ($st === 'active') { $where[] = "s.status IN ('open','looking','bought')"; } elseif (isset(self::$short_statuses[$st])) { $where[] = 's.status = %s'; $args[] = $st; }
        if ($q !== '') { $l = '%' . $db->esc_like($q) . '%'; $where[] = '(s.name LIKE %s OR s.ref LIKE %s OR s.note LIKE %s OR c.name LIKE %s OR s.who LIKE %s)'; array_push($args, $l, $l, $l, $l, $l); }
        $from = 'FROM `' . self::t('shortages') . '` s LEFT JOIN `' . self::t('customers') . '` c ON c.id = s.customer_id LEFT JOIN `' . self::t('vendors') . '` v ON v.id = s.vendor_id WHERE ' . implode(' AND ', $where);
        $pg = MZC_Api::page($req); $per = MZC_Api::per($req);
        $total = (int) ($args ? $db->get_var($db->prepare("SELECT COUNT(*) $from", $args)) : $db->get_var("SELECT COUNT(*) $from"));
        $rows = $db->get_results($db->prepare("SELECT s.*, c.name AS customer_name, v.name AS vendor_name $from ORDER BY FIELD(s.status, 'open', 'looking', 'bought', 'arrived', 'cancelled'), s.id DESC LIMIT %d OFFSET %d", array_merge($args, array($per, ($pg - 1) * $per))), ARRAY_A) ?: array();
        return MZC_Api::ok(array('total' => $total, 'page' => $pg, 'per' => $per, 'statuses' => self::$short_statuses, 'kinds' => self::$short_kinds, 'canAnswer' => self::buyer($a['id']), 'items' => array_map(array(__CLASS__, 'short_out'), $rows)));
    }

    /** How many are waiting for an answer: the buyers' badge (asked once a sign-in and now and then). */
    public static function rest_shortage_count($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $db = self::db(); if (!$db) { return MZC_Api::ok(array('open' => 0, 'buyer' => false)); }
        $n = self::buyer($a['id']) ? (int) $db->get_var('SELECT COUNT(*) FROM `' . self::t('shortages') . "` WHERE status = 'open'") : 0;
        return MZC_Api::ok(array('open' => $n, 'buyer' => self::buyer($a['id'])));
    }

    /** Anyone signed in may ask for a part; only the buyers change an existing line (its status, who it was bought from, the note). */
    public static function rest_shortage_save($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $db = self::db(); if (!$db) { return self::fail(MZC_Crm_Db::error(), 503); }
        $p = MZC_Api::body($req); $id = MZC_Api::n($p, 'id'); $old = $id ? $db->get_row($db->prepare('SELECT * FROM `' . self::t('shortages') . '` WHERE id = %d', $id), ARRAY_A) : null;
        if ($id && !$old) { return self::fail('درخواست پیدا نشد.', 404); }
        $now = gmdate('Y-m-d H:i:s'); $t = self::t('shortages');
        if ($old) {
            if (!self::buyer($a['id'])) { return self::fail('جواب دادن به درخواست قطعه با انبار یا حسابداری است.', 403); }
            if (MZC_Rev::stale('shortage', $id, $p, $old['updated'])) { return self::fail('این درخواست در همین فاصله را کس دیگری عوض کرده است؛ دوباره بازش کنید.', 409); }
            $row = array('updated' => $now);
            if (isset($p['status'])) { if (!isset(self::$short_statuses[(string) $p['status']])) { return self::fail('وضعیت درست نیست.', 422); } $row['status'] = (string) $p['status']; }
            if (array_key_exists('note', $p)) { $row['note'] = MZC_Api::s($p, 'note', 255) ?: null; }
            if (array_key_exists('vendor_id', $p)) { $row['vendor_id'] = MZC_Api::n($p, 'vendor_id') ?: null; }
            if (array_key_exists('qty', $p)) { $row['qty'] = max(1, min(100000, MZC_Api::n($p, 'qty'))); }
            if ($db->update($t, $row, array('id' => $id)) === false) { return self::fail('ذخیره نشد: ' . $db->last_error, 500); }
            MZC_Api::log($a, 'update', 'shortage', $id, $old['name'] . ' ' . (isset($row['status']) ? $row['status'] : ''));
        } else {
            $name = MZC_Api::s($p, 'name', 190); if ($name === '') { return self::fail('نام قطعه را بنویسید.', 422); }
            $kind = isset($p['kind'], self::$short_kinds[(string) $p['kind']]) ? (string) $p['kind'] : 'other'; $cid = MZC_Api::n($p, 'customer_id');
            if ($cid && !MZC_Crm::customer($cid)) { $cid = 0; }
            $row = array('name' => $name, 'qty' => max(1, min(100000, MZC_Api::n($p, 'qty') ?: 1)), 'kind' => $kind, 'ref' => MZC_Api::s($p, 'ref', 60) ?: null, 'customer_id' => $cid ?: null, 'status' => 'open',
                'note' => MZC_Api::s($p, 'note', 255) ?: null, 'asked_by' => (int) $a['id'], 'who' => mb_substr($a['user']->display_name, 0, 120), 'created' => $now, 'updated' => $now);
            if (!$db->insert($t, $row)) { return self::fail('ذخیره نشد: ' . $db->last_error, 500); }
            $id = (int) $db->insert_id; MZC_Api::log($a, 'create', 'shortage', $id, $name);
        }
        $x = $db->get_row($db->prepare("SELECT s.*, c.name AS customer_name, v.name AS vendor_name FROM `$t` s LEFT JOIN `" . self::t('customers') . '` c ON c.id = s.customer_id LEFT JOIN `' . self::t('vendors') . '` v ON v.id = s.vendor_id WHERE s.id = %d', $id), ARRAY_A);
        return MZC_Api::ok(self::short_out($x), $old ? 200 : 201);
    }

    /* ---------- warranty shipments ---------- */

    public static function ship_out($s)
    {
        $state = !empty($s['picked_up']) ? 'done' : ($s['received_at'] ? 'back' : 'sent');
        return array('id' => (int) $s['id'], 'part_name' => $s['part_name'], 'serial' => (string) $s['serial'], 'problem' => (string) $s['problem'], 'sent_at' => (string) $s['sent_at'], 'sent_at_j' => MZC_Api::jd($s['sent_at']), 'tracking' => (string) $s['tracking'],
            'vendor_id' => (int) $s['vendor_id'], 'vendor_name' => isset($s['vendor_name']) ? (string) $s['vendor_name'] : '', 'vendor_reply' => (string) $s['vendor_reply'], 'received_at' => (string) $s['received_at'], 'received_at_j' => MZC_Api::jd($s['received_at']),
            'fix_notes' => (string) $s['fix_notes'], 'back_part' => (string) $s['back_part'], 'back_serial' => (string) $s['back_serial'], 'shelf' => (string) $s['shelf'], 'picked_up' => (int) $s['picked_up'], 'owner' => $s['owner'],
            'customer_id' => (int) $s['customer_id'], 'customer_name' => isset($s['customer_name']) ? (string) $s['customer_name'] : '', 'ref' => (string) $s['ref'], 'customer_reply' => (string) $s['customer_reply'], 'state' => $state,
            'stateText' => array('sent' => 'نزد گارانتی', 'back' => 'برگشته، تحویل نشده', 'done' => 'تحویل شد')[$state], 'updated' => $s['updated']);
    }

    public static function rest_ships($req)
    {
        $a = MZC_Staff::auth($req, 'ships', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $st = (string) $req->get_param('status'); $pg = MZC_Api::page($req); $per = MZC_Api::per($req);
        $status = in_array($st, array('open', 'sent', 'back'), true) ? $st : ''; $q = (string) $req->get_param('q');
        list($rows, $total) = MZC_Ships::ships($q, $status, (int) $req->get_param('vendor'), $pg, $per);
        $vendors = array_map(function ($v) { return array('id' => (int) $v['id'], 'name' => $v['name'], 'kind' => $v['kind']); }, MZC_Ships::vendors());
        return MZC_Api::ok(array('total' => $total, 'page' => $pg, 'per' => $per, 'vendors' => $vendors, 'items' => array_map(array(__CLASS__, 'ship_out'), $rows)));
    }

    public static function rest_ship($req)
    {
        $a = MZC_Staff::auth($req, 'ships', 1); if ($a instanceof WP_REST_Response) { return $a; }
        $s = MZC_Ships::ship((int) $req['id']); if (!$s) { return self::fail('ارسال پیدا نشد.', 404); }
        return MZC_Api::ok(self::ship_out($s));
    }

    public static function rest_ship_save($req)
    {
        $a = MZC_Staff::auth($req, 'ships', 2); if ($a instanceof WP_REST_Response) { return $a; }
        $p = MZC_Api::body($req); $id = MZC_Api::n($p, 'id'); $old = $id ? MZC_Ships::ship($id) : null;
        if ($old && MZC_Rev::stale('ship', $id, $p, $old['updated'])) { return self::fail('این مورد در همین فاصله را کس دیگری عوض کرده است؛ دوباره بازش کنید.', 409); }
        $r = MZC_Ships::api_save($id, $p); if (!is_int($r)) { return self::fail($r, 422); }
        MZC_Api::log($a, $id ? 'update' : 'create', 'ship', $r, (string) (isset($p['part_name']) ? $p['part_name'] : ''));
        return MZC_Api::ok(self::ship_out(MZC_Ships::ship($r)), $id ? 200 : 201);
    }

    /* ---------- backup ---------- */

    private static function backup_auth($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        if (!MZC_Staff::is_admin($a['id']) && !MZC_Staff::can($a['id'], 'accounting', 2)) { return self::fail('پشتیبان‌گیری با مدیر یا حسابدار است.', 403); }
        return $a;
    }

    public static function rest_backup($req)
    {
        $a = self::backup_auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        return MZC_Api::ok(MZC_Backup::state());
    }

    public static function rest_backup_save($req)
    {
        $a = self::backup_auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $p = MZC_Api::body($req); $e = MZC_Backup::save(MZC_Api::s($p, 'dir', 300), max(1, min(365, MZC_Api::n($p, 'keep') ?: 14)));
        if ($e !== '') { return self::fail($e, 422); }
        MZC_Api::log($a, 'update', 'backup', 0, 'folder');
        return MZC_Api::ok(MZC_Backup::state());
    }

    /** The backup for a PC: the program keeps it in the folder the manager chose there. */
    public static function rest_backup_pull($req)
    {
        $a = self::backup_auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $r = MZC_Backup::pull(); if (is_string($r)) { return self::fail($r, 422); }
        MZC_Api::log($a, 'create', 'backup', 0, $r['name'] . ' (PC)');
        return MZC_Api::ok($r);
    }

    public static function rest_backup_run($req)
    {
        $a = self::backup_auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $r = MZC_Backup::run(); if (is_string($r)) { return self::fail($r, 422); }
        MZC_Api::log($a, 'create', 'backup', 0, $r['name']);
        return MZC_Api::ok(MZC_Backup::state());
    }
}

/**
 * A dump of the CRM database (its own tables only, none of the sign-in or counter tables) written once a day, and on demand, as a .sql.gz into a folder named on the program's
 * backup page. The folder must be outside the web site, or anyone could download the customers; the newest `keep` files stay.
 */
final class MZC_Backup
{
    const SKIP = 'sessions otps app_codes app_sessions oauth_codes oauth_refresh oauth_clients limits edit_locks';
    const HOOK = 'mzc_backup_daily';

    public static function boot()
    {
        add_action(self::HOOK, array(__CLASS__, 'cron'));
        if (!wp_next_scheduled(self::HOOK)) { wp_schedule_event(time() + 900, 'daily', self::HOOK); }
    }

    private static function meta() { $db = MZC_Crm_Db::db(); $v = $db ? $db->get_var('SELECT v FROM `' . MZC_Crm_Db::t('meta') . "` WHERE k = 'backup'") : null; $s = is_string($v) ? json_decode($v, true) : null; return (is_array($s) ? $s : array()) + array('dir' => '', 'keep' => 14, 'last' => '', 'error' => ''); }
    private static function put($s) { $db = MZC_Crm_Db::db(); if ($db) { $db->query($db->prepare('REPLACE INTO `' . MZC_Crm_Db::t('meta') . "` (k, v) VALUES ('backup', %s)", wp_json_encode($s, JSON_UNESCAPED_UNICODE))); } }

    /** '' when the folder can be used (made if missing), else why not. */
    public static function check($dir)
    {
        if ($dir === '') { return 'پوشه را بنویسید.'; }
        if (!preg_match('~^([A-Za-z]:[\\\\/]|/)~', $dir)) { return 'مسیر کامل پوشه را بنویسید (مثلاً D:\\Backups).'; }
        if (!is_dir($dir) && !@mkdir($dir, 0775, true)) { return 'پوشه ساخته نشد؛ مسیر یا دسترسی را بررسی کنید.'; }
        $real = realpath($dir); if ($real === false || !is_writable($real)) { return 'در این پوشه نمی‌شود نوشت.'; }
        $norm = function ($x) { return strtolower(rtrim(str_replace('\\', '/', $x), '/')) . '/'; };
        foreach (array(ABSPATH, WP_CONTENT_DIR) as $web) { $w = realpath($web); if ($w !== false && strpos($norm($real), $norm($w)) === 0) { return 'پوشه نباید داخل پوشهٔ سایت باشد؛ فایل پشتیبان از اینترنت قابل گرفتن می‌شود. جایی مثل D:\\Backups بدهید.'; } }
        return '';
    }

    public static function save($dir, $keep)
    {
        $dir = rtrim($dir, "\\/ "); $e = self::check($dir); if ($e !== '') { return $e; }
        $s = self::meta(); $s['dir'] = $dir; $s['keep'] = $keep; self::put($s);
        return '';
    }

    public static function files()
    {
        $s = self::meta(); if ($s['dir'] === '' || !is_dir($s['dir'])) { return array(); }
        $out = array();
        foreach (glob(rtrim($s['dir'], "\\/") . DIRECTORY_SEPARATOR . 'mazesta-crm-*.sql.gz') ?: array() as $f) { $out[] = array('name' => basename($f), 'size' => (int) filesize($f), 'at' => gmdate('c', (int) filemtime($f))); }
        usort($out, function ($a, $b) { return strcmp($b['name'], $a['name']); });
        return array_slice($out, 0, 30);
    }

    public static function state() { $s = self::meta(); return array('dir' => $s['dir'], 'keep' => (int) $s['keep'], 'last' => $s['last'], 'error' => $s['error'], 'files' => self::files(), 'next' => wp_next_scheduled(self::HOOK) ? gmdate('c', wp_next_scheduled(self::HOOK)) : ''); }

    public static function cron() { $s = self::meta(); if ($s['dir'] !== '') { self::run(); } }

    private static function lit($db, $v) { return $v === null ? 'NULL' : "'" . mysqli_real_escape_string($db->dbh, (string) $v) . "'"; }

    /** The dump as the program on a PC takes it (the backup is kept on that PC): array(name, size, data = base64 of the .sql.gz) or an error text. */
    public static function pull()
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return MZC_Crm_Db::error(); }
        $tmp = tempnam(get_temp_dir(), 'mzcbk'); $e = self::dump($db, $tmp);
        if ($e !== '') { @unlink($tmp); return $e; }
        $raw = file_get_contents($tmp); @unlink($tmp);
        return array('name' => 'mazesta-crm-' . wp_date('Ymd-His') . '.sql.gz', 'size' => strlen($raw), 'data' => base64_encode($raw));
    }

    /** Writes one dump file; returns array(name, size) or an error text (also kept for the page). */
    public static function run()
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return MZC_Crm_Db::error(); }
        $s = self::meta(); $e = self::check($s['dir']); if ($e !== '') { return self::fail($s, $e); }
        $dir = rtrim(realpath($s['dir']), "\\/") . DIRECTORY_SEPARATOR; $name = 'mazesta-crm-' . wp_date('Ymd-His') . '.sql.gz'; $tmp = $dir . $name . '.part';
        $e = self::dump($db, $tmp); if ($e !== '') { @unlink($tmp); return self::fail($s, $e); }
        if (!@rename($tmp, $dir . $name)) { @unlink($tmp); return self::fail($s, 'فایل پشتیبان در پوشه گذاشته نشد.'); }
        $old = glob($dir . 'mazesta-crm-*.sql.gz') ?: array(); rsort($old);
        foreach (array_slice($old, max(1, (int) $s['keep'])) as $f) { @unlink($f); }
        $s['last'] = gmdate('c'); $s['error'] = ''; self::put($s);
        return array('name' => $name, 'size' => (int) filesize($dir . $name));
    }

    /** Writes the gzipped SQL of every table of the CRM to $tmp; '' or an error text. */
    private static function dump($db, $tmp)
    {
        @set_time_limit(600);
        $gz = @gzopen($tmp, 'wb6'); if (!$gz) { return 'فایل پشتیبان ساخته نشد.'; }
        $prefix = MZC_Crm_Db::prefix(); $skip = array_map(function ($x) use ($prefix) { return $prefix . $x; }, explode(' ', self::SKIP));
        $tables = array_values(array_diff($db->get_col($db->prepare('SHOW TABLES LIKE %s', $db->esc_like($prefix) . '%')) ?: array(), $skip));
        gzwrite($gz, "-- Mazesta CRM backup " . gmdate('c') . "\nSET NAMES utf8mb4; SET FOREIGN_KEY_CHECKS=0;\n");
        foreach ($tables as $tb) {
            $c = $db->get_row("SHOW CREATE TABLE `$tb`", ARRAY_N); if (!$c) { gzclose($gz); return 'ساختار جدول ' . $tb . ' خوانده نشد.'; }
            gzwrite($gz, "\nDROP TABLE IF EXISTS `$tb`;\n" . $c[1] . ";\n");
            for ($off = 0; ; $off += 500) {
                $rows = $db->get_results("SELECT * FROM `$tb` ORDER BY 1 LIMIT 500 OFFSET $off", ARRAY_N); if (!$rows) { break; }
                gzwrite($gz, "INSERT INTO `$tb` VALUES " . implode(",\n", array_map(function ($r) use ($db) { return '(' . implode(',', array_map(function ($v) use ($db) { return self::lit($db, $v); }, $r)) . ')'; }, $rows)) . ";\n");
            }
        }
        gzwrite($gz, "\nSET FOREIGN_KEY_CHECKS=1;\n"); gzclose($gz);
        return '';
    }

    private static function fail($s, $msg) { $s['error'] = $msg; self::put($s); return $msg; }
}
