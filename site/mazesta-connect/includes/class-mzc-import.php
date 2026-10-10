<?php
if (!defined('ABSPATH')) { exit; }

/**
 * Brings the old CRM's data in. site/tools/export_old_crm.py turns the old database into one file (gzip, a JSON record per line); this class takes that file on the settings page,
 * unpacks it into the plugin's protected data folder and reads it in small steps (a page-driven loop, so no request runs long). A record that was already imported (same old id) is
 * skipped, so running it again is safe; customers with the same mobile number become one customer.
 */
final class MZC_Import
{
    const MAX_BYTES = 52428800;     // the unpacked file
    const STEP_LINES = 120;
    const STEP_SECONDS = 4;

    public static function boot()
    {
        add_action('admin_post_mzc_import_upload', array(__CLASS__, 'act_upload'));
        add_action('wp_ajax_mzc_import_step', array(__CLASS__, 'ajax_step'));
    }

    private static function file() { return Mazesta_Connect::dir() . '/old-crm.jsonl.dat'; }
    private static function t($n) { return MZC_Crm_Db::t($n); }

    public static function state()
    {
        $db = MZC_Crm::db(); if (!$db) { return null; }
        $v = $db->get_var('SELECT v FROM `' . self::t('meta') . "` WHERE k = 'import'");
        $s = $v ? json_decode($v, true) : null;
        return is_array($s) ? $s : null;
    }

    private static function save_state($s) { $db = MZC_Crm::db(); $db->query($db->prepare('REPLACE INTO `' . self::t('meta') . "` (k, v) VALUES ('import', %s)", wp_json_encode($s))); }

    public static function act_upload()
    {
        MZC_Admin::guard('import_upload', 'manage_options');
        if (!MZC_Crm_Db::ready()) { MZC_Admin::back('mzc-settings', 'err:اول پایگاه دادهٔ CRM را وصل کنید.'); }
        $f = isset($_FILES['old']) ? $_FILES['old'] : null;
        if (!$f || $f['error'] !== UPLOAD_ERR_OK || !is_uploaded_file($f['tmp_name'])) { MZC_Admin::back('mzc-settings', 'err:فایل بارگذاری نشد (حجم مجاز هاست را بررسی کنید).'); }
        $raw = file_get_contents($f['tmp_name']);
        $txt = is_string($raw) && substr($raw, 0, 2) === "\x1f\x8b" ? @gzdecode($raw) : false;
        if (!is_string($txt) || $txt === '' || strlen($txt) > self::MAX_BYTES || strpos($txt, '{"t":"customer"') !== 0) { MZC_Admin::back('mzc-settings', 'err:این فایل خروجی export_old_crm.py نیست.'); }
        if (!Mazesta_Connect::ensure() || file_put_contents(self::file(), $txt) === false) { MZC_Admin::back('mzc-settings', 'err:فایل در پوشهٔ داده‌ها ذخیره نشد.'); }
        self::save_state(array('offset' => 0, 'size' => strlen($txt), 'lines' => 0, 'counts' => new stdClass(), 'skipped' => 0, 'errors' => array(), 'done' => false, 'at' => gmdate('Y-m-d H:i:s')));
        MZC_Admin::back('mzc-settings', 'ok:فایل آماده است؛ «شروع واردسازی» را بزنید.');
    }

    public static function ajax_step()
    {
        if (!Mazesta_Connect::can() || !current_user_can('manage_options')) { wp_send_json(array('error' => 'اجازه ندارید.'), 403); }
        check_ajax_referer('mzc_import_step', 'n');
        $st = self::state(); $db = MZC_Crm::db();
        if (!$st || !$db || !is_readable(self::file())) { wp_send_json(array('error' => 'فایلی برای واردسازی نیست.')); }
        if (!empty($st['done'])) { wp_send_json(self::report($st)); }
        $h = fopen(self::file(), 'rb'); fseek($h, (int) $st['offset']);
        $t0 = microtime(true); $n = 0; $counts = (array) $st['counts'];
        while ($n < self::STEP_LINES && (microtime(true) - $t0) < self::STEP_SECONDS && ($line = fgets($h)) !== false) {
            $n++; $st['offset'] += strlen($line); $st['lines']++;
            $rec = json_decode($line, true);
            if (!is_array($rec) || empty($rec['t'])) { continue; }
            $r = self::one($db, $rec);
            if ($r === true) { $k = $rec['t'] . ($rec['t'] === 'form' ? ':' . $rec['kind'] : ''); $counts[$k] = (isset($counts[$k]) ? $counts[$k] : 0) + 1; }
            elseif ($r === false) { $st['skipped']++; }
            else { if (count($st['errors']) < 20) { $st['errors'][] = $rec['t'] . ' ' . (isset($rec['old']) ? $rec['old'] : '') . ': ' . $r; } }
        }
        $st['done'] = feof($h) || $st['offset'] >= $st['size'];
        fclose($h);
        $st['counts'] = $counts;
        if ($st['done']) { @unlink(self::file()); }
        self::save_state($st);
        wp_send_json(self::report($st));
    }

    private static function report($st) { return array('done' => !empty($st['done']), 'pct' => $st['size'] ? min(100, (int) floor(100 * $st['offset'] / $st['size'])) : 100, 'lines' => $st['lines'], 'counts' => $st['counts'], 'skipped' => $st['skipped'], 'errors' => $st['errors']); }

    /* ---------- one record ---------- */

    private static function mapped($db, $kind, $old)
    {
        return (int) $db->get_var($db->prepare('SELECT new_id FROM `' . self::t('oldmap') . '` WHERE kind = %s AND old_id = %d', $kind, (int) $old));
    }

    private static function remember($db, $kind, $old, $new) { $db->query($db->prepare('REPLACE INTO `' . self::t('oldmap') . '` (kind, old_id, new_id) VALUES (%s, %d, %d)', $kind, (int) $old, (int) $new)); }

    private static function s($v, $max = 255) { return mb_substr(trim(is_scalar($v) ? (string) $v : ''), 0, $max); }
    private static function nz($v) { $v = trim((string) $v); return $v === '' ? null : $v; }
    private static function d($v) { return is_string($v) && preg_match('/^\d{4}-\d{2}-\d{2}/', $v) ? substr($v, 0, 10) : null; }
    private static function ts($v) { return is_string($v) && preg_match('/^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$/', $v) ? $v : gmdate('Y-m-d H:i:s'); }
    private static function json($v) { return empty($v) ? null : wp_json_encode($v, JSON_UNESCAPED_UNICODE); }

    /** The customer a record belongs to; one stand-in customer takes the records whose customer was deleted in the old database. */
    private static function owner($db, $old)
    {
        $id = $old ? self::mapped($db, 'c', $old) : 0;
        if ($id) { return $id; }
        $id = self::mapped($db, 'c', 0);
        if ($id) { return $id; }
        $db->insert(self::t('customers'), array('grp' => 'customer', 'name' => 'مشتری نامشخص (پایگاه قدیمی)', 'created' => gmdate('Y-m-d H:i:s'), 'updated' => gmdate('Y-m-d H:i:s')));
        $id = (int) $db->insert_id; self::remember($db, 'c', 0, $id);
        return $id;
    }

    /** Returns true (imported), false (already there) or an error text. */
    private static function one($db, $r)
    {
        $now = gmdate('Y-m-d H:i:s');
        switch ($r['t']) {
            case 'customer':
                if ($id = self::mapped($db, 'c', $r['old'])) {   // a re-run still brings the group of a customer imported before groups existed
                    if (isset($r['group']) && $r['group'] !== 'customer' && isset(MZC_Crm::$groups[$r['group']])) { $db->update(self::t('customers'), array('grp' => $r['group']), array('id' => $id, 'grp' => 'customer')); }
                    return false;
                }
                $m = MZC_Crm::mobile($r['mobile']); $m2 = MZC_Crm::mobile($r['mobile2']); $name = self::s($r['name'], 190);
                if ($name === '') { $name = $m ?: 'بدون نام'; }
                if ($m && ($ex = $db->get_row($db->prepare('SELECT id, name, notes, mobile2, grp FROM `' . self::t('customers') . '` WHERE mobile = %s', $m), ARRAY_A))) {
                    $upd = array();
                    if ($name !== $ex['name'] && strpos((string) $ex['notes'], $name) === false) { $upd['notes'] = trim($ex['notes'] . "\nنام دیگر در پایگاه قدیمی: " . $name); }
                    if ($m2 && !$ex['mobile2']) { $upd['mobile2'] = $m2; }
                    $g = isset($r['group']) && isset(MZC_Crm::$groups[$r['group']]) ? $r['group'] : 'customer';
                    if ($g !== 'customer' && in_array($ex['grp'], array(null, '', 'customer', 'general'), true)) { $upd['grp'] = $g; }
                    if ($upd) { $db->update(self::t('customers'), $upd, array('id' => (int) $ex['id'])); }
                    self::remember($db, 'c', $r['old'], $ex['id']);
                    return true;
                }
                if ($m2 === $m) { $m2 = ''; }
                $ok = $db->insert(self::t('customers'), array('name' => $name, 'mobile' => $m ?: null, 'mobile2' => $m2 ?: null, 'phone2' => null, 'phone' => self::nz(self::s($r['phone'], 80)),
                    'national_id' => self::nz(self::s($r['national_id'], 20)), 'email' => self::nz(self::s($r['email'], 190)), 'address' => self::nz(self::s($r['address'], 1000)), 'notes' => self::nz(self::s($r['notes'], 4000)),
                    'old_id' => (int) $r['old'], 'grp' => isset($r['group']) && isset(MZC_Crm::$groups[$r['group']]) ? $r['group'] : 'customer', 'created' => self::ts($r['created']), 'updated' => $now));
                if (!$ok) { return $db->last_error; }
                self::remember($db, 'c', $r['old'], $db->insert_id);
                return true;

            case 'build':
                if (self::mapped($db, 'b', $r['old'])) { return false; }
                $ok = $db->insert(self::t('builds'), array('customer_id' => self::owner($db, $r['cust']), 'service_no' => self::nz(self::s($r['no'], 40)), 'invoice_no' => self::nz(self::s($r['invoice'], 60)),
                    'title' => self::s($r['title'], 190) ?: 'سیستم نو', 'sold_at' => self::d($r['at']), 'delivered' => empty($r['delivered']) ? 0 : 1, 'data' => self::json($r['data']), 'old_id' => (int) $r['old'], 'created' => self::ts($r['created']), 'updated' => $now));
                if (!$ok) { return $db->last_error; }
                $id = (int) $db->insert_id; self::remember($db, 'b', $r['old'], $id);
                foreach ((array) $r['parts'] as $p) {
                    $cat = isset(MZC_Crm::$categories[$p['cat']]) ? $p['cat'] : 'other';
                    $db->insert(self::t('build_parts'), array('build_id' => $id, 'category' => $cat, 'model' => self::s($p['model'], 190), 'serial' => self::nz(self::s($p['serial'], 120)), 'has_warranty' => empty($p['warranty']) ? 0 : 1,
                        'has_box' => empty($p['box']) ? 0 : 1, 'qc' => empty($p['qc']) ? 0 : 1, 'note' => self::nz(self::s($p['note']))));
                }
                return true;

            case 'job':
                if (self::mapped($db, 'j', $r['old'])) { return false; }
                $status = !empty($r['handed']) ? 'delivered' : (!empty($r['done']) ? 'ready' : 'received');
                $recv = self::d($r['received']);
                $ok = $db->insert(self::t('jobs'), array('customer_id' => self::owner($db, $r['cust']), 'service_no' => self::nz(self::s($r['no'], 40)), 'received_at' => $recv, 'due_at' => self::d($r['due']),
                    'closed_at' => $status === 'delivered' ? (self::d($r['due']) ?: $recv) : null, 'status' => $status, 'is_mazesta' => empty($r['mazesta']) ? 0 : 1, 'has_warranty' => empty($r['warranty']) ? 0 : 1,
                    'complaint' => self::nz(self::s($r['complaint'], 4000)), 'work_done' => self::nz(self::s($r['work'], 4000)), 'notes' => self::nz(self::s($r['private'], 4000)),
                    'labor_price' => (int) $r['labor'], 'discount' => (int) $r['discount'], 'paid' => (int) $r['paid'], 'invoice_no' => self::nz(self::s($r['invoice'], 60)), 'service_done' => empty($r['done']) ? 0 : 1,
                    'ship' => self::json($r['ship']), 'data' => self::json($r['data']), 'old_id' => (int) $r['old'], 'created' => self::ts($r['created']), 'updated' => $now));
                if (!$ok) { return $db->last_error; }
                $id = (int) $db->insert_id; self::remember($db, 'j', $r['old'], $id);
                foreach (array('in' => 'recv', 'add' => 'add') as $kind => $key) {
                    foreach ((array) $r[$key] as $p) {
                        $db->insert(self::t('job_parts'), array('job_id' => $id, 'kind' => $kind, 'name' => self::s($p['name'], 190) ?: '—', 'qty' => 1, 'unit_price' => $kind === 'add' ? (int) $p['price'] : 0, 'serial' => self::nz(self::s($p['serial'], 120)),
                            'has_box' => empty($p['box']) ? 0 : 1, 'has_warranty' => empty($p['warranty']) ? 0 : 1, 'note' => self::nz(self::s($p['note']))));
                    }
                }
                foreach ((array) $r['pays'] as $p) {
                    $db->insert(self::t('job_pays'), array('job_id' => $id, 'amount' => (int) $p['amount'], 'account' => self::nz(self::s($p['account'], 120)), 'tx' => self::nz(self::s($p['tx'], 80)), 'holder' => self::nz(self::s($p['holder'], 120))));
                }
                return true;

            case 'form':
                if ($db->get_var($db->prepare('SELECT id FROM `' . self::t('forms') . '` WHERE kind = %s AND old_id = %d', self::s($r['kind'], 12), (int) $r['old']))) { return false; }
                $ok = $db->insert(self::t('forms'), array('customer_id' => $r['cust'] ? self::owner($db, $r['cust']) : null, 'kind' => self::s($r['kind'], 12), 'ref' => self::nz(self::s($r['ref'], 60)), 'no' => self::nz(self::s($r['no'], 40)),
                    'at' => self::d($r['at']), 'title' => self::nz(self::s($r['title'])), 'data' => self::json($r['data']), 'old_id' => (int) $r['old'], 'created' => self::ts($r['created'])));
                return $ok ? true : $db->last_error;
        }
        return 'نوع رکورد ناشناخته';
    }
}
