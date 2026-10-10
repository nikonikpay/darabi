<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The CRM on the second database (see MZC_Crm_Db): customers, the new computers the shop built and sold (their parts with serial numbers and warranty, the invoice number of the
 * accounting program, the test report the app made), the service jobs (what the customer asked for, what was done, the parts added and their prices) and the photos of each.
 * The admin pages in admin/ read and write through this class; the customer's portal reads the same history (only its own).
 */
final class MZC_Crm
{
    const PHOTO_MAX = 8;
    const PHOTO_BYTES = 8388608;

    public static $categories = array('cpu' => 'پردازنده', 'motherboard' => 'مادربرد', 'ram' => 'رم', 'gpu' => 'کارت گرافیک', 'ssd' => 'SSD / NVMe', 'hdd' => 'هارد', 'psu' => 'پاور',
        'case' => 'کیس', 'cooler' => 'خنک‌کننده', 'fan' => 'فن', 'monitor' => 'مانیتور', 'other' => 'سایر');
    /** The old CRM's customer groups (shop customers, agents, staff, ...). */
    public static $groups = array('customer' => 'مشتری', 'lead' => 'مشتری بالقوه', 'agent' => 'نماینده', 'general' => 'عمومی', 'warranty' => 'گارانتی', 'staff' => 'پرسنل', 'store' => 'انبار', 'courier' => 'پیک و باربری');

    /** Where a new computer is on its way: the shop's own steps from the queue to the customer's hands. */
    public static $build_statuses = array('queue' => 'در صف اسمبل', 'assembling' => 'در حال اسمبل', 'assembled' => 'اسمبل شده', 'testing' => 'در حال تست', 'tested' => 'اتمام تست و اعلام به مشتری', 'delivered' => 'ارسال و تحویل');

    public static $statuses = array('received' => 'پذیرش شد', 'diagnosing' => 'در حال بررسی', 'waiting_part' => 'منتظر قطعه', 'ready' => 'آماده تحویل', 'delivered' => 'تحویل شد', 'cancelled' => 'لغو شد');

    public static function boot()
    {
        foreach (array('customer_save', 'customer_delete', 'build_save', 'build_delete', 'job_save', 'job_delete', 'photo_delete', 'db_wipe', 'db_save', 'db_install', 'sms_save', 'sms_test') as $a) {
            add_action('admin_post_mzc_' . $a, array(__CLASS__, 'act_' . $a));
        }
        add_action('admin_post_mzc_photo', array(__CLASS__, 'act_photo'));
    }

    /* ---------- small helpers ---------- */

    public static function mobile($s)
    {
        $s = strtr((string) $s, array('۰' => '0', '۱' => '1', '۲' => '2', '۳' => '3', '۴' => '4', '۵' => '5', '۶' => '6', '۷' => '7', '۸' => '8', '۹' => '9',
            '٠' => '0', '١' => '1', '٢' => '2', '٣' => '3', '٤' => '4', '٥' => '5', '٦' => '6', '٧' => '7', '٨' => '8', '٩' => '9'));
        $s = preg_replace('/\D+/', '', $s);
        if (strpos($s, '0098') === 0) { $s = substr($s, 4); } elseif (strpos($s, '98') === 0 && strlen($s) === 12) { $s = substr($s, 2); }
        if (strlen($s) === 10 && $s[0] === '9') { $s = '0' . $s; }
        return preg_match('/^09\d{9}$/', $s) ? $s : '';
    }

    public static function digits($s) { return strtr((string) $s, array('۰' => '0', '۱' => '1', '۲' => '2', '۳' => '3', '۴' => '4', '۵' => '5', '۶' => '6', '۷' => '7', '۸' => '8', '۹' => '9')); }

    public static function post($k, $max = 255) { return Mazesta_Connect::clip(isset($_POST[$k]) ? wp_unslash($_POST[$k]) : '', $max); }
    public static function post_long($k, $max = 4000) { return isset($_POST[$k]) ? mb_substr(trim(sanitize_textarea_field(wp_unslash($_POST[$k]))), 0, $max) : ''; }
    public static function date($k) { $v = self::digits(self::post($k, 10)); return preg_match('/^\d{4}-\d{2}-\d{2}$/', $v) ? $v : null; }
    private static function now() { return gmdate('Y-m-d H:i:s'); }

    /** A Gregorian date (Y-m-d) as the Persian calendar's Y/m/d. */
    public static function jdate($ymd)
    {
        if (!is_string($ymd) || !preg_match('/^(\d{4})-(\d{2})-(\d{2})/', $ymd, $m)) { return ''; }
        $gy = (int) $m[1]; $gm = (int) $m[2]; $gd = (int) $m[3];
        $g = array(0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334);
        $gy2 = $gm > 2 ? $gy + 1 : $gy;
        $days = 355666 + 365 * $gy + (int) (($gy2 + 3) / 4) - (int) (($gy2 + 99) / 100) + (int) (($gy2 + 399) / 400) + $gd + $g[$gm - 1];
        $jy = -1595 + 33 * (int) ($days / 12053); $days %= 12053;
        $jy += 4 * (int) ($days / 1461); $days %= 1461;
        if ($days > 365) { $jy += (int) (($days - 1) / 365); $days = ($days - 1) % 365; }
        $jm = $days < 186 ? 1 + (int) ($days / 31) : 7 + (int) (($days - 186) / 30);
        $jd = 1 + ($days < 186 ? $days % 31 : ($days - 186) % 30);
        return sprintf('%04d/%02d/%02d', $jy, $jm, $jd);
    }

    public static function money($n) { return number_format((int) $n) . ' تومان'; }

    /** The warranty's end for a part sold on $sold with $months months: array(date or null, days left or null). */
    public static function warranty($sold, $months)
    {
        if (!$sold || !$months) { return array(null, null); }
        $end = strtotime($sold . ' +' . (int) $months . ' months');
        return array(gmdate('Y-m-d', $end), (int) floor(($end - time()) / 86400));
    }

    public static function db() { return MZC_Crm_Db::db(); }
    private static function t($n) { return MZC_Crm_Db::t($n); }

    /* ---------- customers ---------- */

    public static function customer($id)
    {
        $db = self::db(); if (!$db) { return null; }
        return $db->get_row($db->prepare('SELECT * FROM `' . self::t('customers') . '` WHERE id = %d', (int) $id), ARRAY_A);
    }

    public static function customer_by_mobile($mobile)
    {
        $db = self::db(); if (!$db) { return null; }
        return $db->get_row($db->prepare('SELECT * FROM `' . self::t('customers') . '` WHERE mobile = %s OR mobile2 = %s ORDER BY (mobile = %s) DESC LIMIT 1', $mobile, $mobile, $mobile), ARRAY_A);
    }

    /** A page of customers (search in name, mobile, national id), newest first, with how many systems and jobs each has. */
    public static function customers($q, $page, $per = 30, $grp = '')
    {
        $db = self::db(); if (!$db) { return array(array(), 0); }
        $c = self::t('customers'); $where = '1=1'; $args = array();
        if ($q !== '') { $like = '%' . $db->esc_like(self::digits($q)) . '%'; $likeq = '%' . $db->esc_like($q) . '%'; $where = '(c.name LIKE %s OR c.mobile LIKE %s OR c.mobile2 LIKE %s OR c.national_id LIKE %s OR c.phone LIKE %s)'; $args = array($likeq, $like, $like, $like, $like); }
        if ($grp !== '' && isset(self::$groups[$grp])) { $where = '(' . $where . ') AND c.grp = %s'; $args[] = $grp; }
        $total = (int) $db->get_var($args ? $db->prepare("SELECT COUNT(*) FROM `$c` c WHERE $where", $args) : "SELECT COUNT(*) FROM `$c`");
        $sql = "SELECT c.*, (SELECT COUNT(*) FROM `" . self::t('builds') . "` b WHERE b.customer_id = c.id) AS builds, (SELECT COUNT(*) FROM `" . self::t('jobs') . "` j WHERE j.customer_id = c.id) AS jobs
            FROM `$c` c WHERE $where ORDER BY c.id DESC LIMIT %d OFFSET %d";
        $rows = $db->get_results($db->prepare($sql, array_merge($args, array((int) $per, (int) (($page - 1) * $per)))), ARRAY_A);
        return array($rows ? $rows : array(), $total);
    }

    /** Creates or updates a customer from the posted fields. Returns the id, or a string with the error. */
    public static function save_customer($id, $name, $mobile, $extra = array())
    {
        $db = self::db(); if (!$db) { return MZC_Crm_Db::error(); }
        $m = self::mobile($mobile);
        if ($name === '') { return 'نام مشتری را بنویسید.'; }
        if ($m === '' && trim((string) $mobile) !== '') { return 'شمارهٔ موبایل درست نیست (مثل 09121234567).'; }
        $t = self::t('customers');
        $dup = $m === '' ? null : $db->get_var($db->prepare("SELECT id FROM `$t` WHERE mobile = %s AND id <> %d", $m, (int) $id));
        if ($dup) { return 'مشتری دیگری با این شمارهٔ موبایل ثبت شده است.'; }
        $m2 = self::mobile(isset($extra['mobile2']) ? $extra['mobile2'] : '');
        $row = array('name' => $name, 'mobile' => $m === '' ? null : $m, 'mobile2' => $m2 === '' || $m2 === $m ? null : $m2, 'phone' => isset($extra['phone']) ? $extra['phone'] : null, 'phone2' => isset($extra['phone2']) ? $extra['phone2'] : null, 'national_id' => isset($extra['national_id']) ? $extra['national_id'] : null,
            'email' => isset($extra['email']) ? $extra['email'] : null, 'address' => isset($extra['address']) ? $extra['address'] : null, 'notes' => isset($extra['notes']) ? $extra['notes'] : null, 'updated' => self::now());
        if (isset($extra['grp']) && isset(self::$groups[$extra['grp']])) { $row['grp'] = $extra['grp']; }
        if ($id) { $ok = $db->update($t, $row, array('id' => (int) $id)); return $ok === false ? 'ذخیره نشد: ' . $db->last_error : (int) $id; }
        $row['created'] = self::now(); if (!isset($row['grp'])) { $row['grp'] = 'customer'; }
        return $db->insert($t, $row) ? (int) $db->insert_id : 'ذخیره نشد: ' . $db->last_error;
    }

    /** The customer a form names: an existing one by mobile, or a new one made from the form's name. */
    private static function customer_of_form()
    {
        $id = isset($_POST['customer_id']) ? (int) $_POST['customer_id'] : 0;
        if ($id && self::customer($id)) { return $id; }
        $mobile = self::mobile(self::post('c_mobile', 30));
        if ($mobile === '') { return 'یک مشتری انتخاب کنید یا شمارهٔ موبایل او را بنویسید.'; }
        if ($c = self::customer_by_mobile($mobile)) { return (int) $c['id']; }
        return self::save_customer(0, self::post('c_name', 190), $mobile);
    }

    /* ---------- builds (new computers) ---------- */

    public static function build($id)
    {
        $db = self::db(); if (!$db) { return null; }
        $b = $db->get_row($db->prepare('SELECT * FROM `' . self::t('builds') . '` WHERE id = %d', (int) $id), ARRAY_A);
        if (!$b) { return null; }
        $b['parts'] = $db->get_results($db->prepare('SELECT * FROM `' . self::t('build_parts') . '` WHERE build_id = %d ORDER BY id', (int) $id), ARRAY_A) ?: array();
        $b['photos'] = self::photos('build', $id);
        $b['more'] = !empty($b['data']) ? json_decode($b['data'], true) : array();
        return $b;
    }

    public static function builds($q, $page, $per = 30, $customer = 0, $status = '')
    {
        $db = self::db(); if (!$db) { return array(array(), 0); }
        $b = self::t('builds'); $c = self::t('customers'); $where = array('1=1'); $args = array();
        if ($customer) { $where[] = 'b.customer_id = %d'; $args[] = (int) $customer; }
        if ($status !== '' && isset(self::$build_statuses[$status])) { $where[] = 'b.status = %s'; $args[] = $status; }
        if ($q !== '') {
            $l = '%' . $db->esc_like($q) . '%';
            $where[] = "(b.service_no LIKE %s OR b.invoice_no LIKE %s OR b.title LIKE %s OR c.name LIKE %s OR c.mobile LIKE %s OR EXISTS (SELECT 1 FROM `" . self::t('build_parts') . "` p WHERE p.build_id = b.id AND (p.serial LIKE %s OR p.model LIKE %s)))";
            array_push($args, $l, $l, $l, $l, '%' . $db->esc_like(self::digits($q)) . '%', $l, $l);
        }
        $w = implode(' AND ', $where);
        $from = "FROM `$b` b JOIN `$c` c ON c.id = b.customer_id WHERE $w";
        $total = (int) $db->get_var($args ? $db->prepare("SELECT COUNT(*) $from", $args) : "SELECT COUNT(*) $from");
        $rows = $db->get_results($db->prepare("SELECT b.*, c.name AS customer_name, c.mobile AS customer_mobile, (SELECT COUNT(*) FROM `" . self::t('build_parts') . "` p WHERE p.build_id = b.id) AS parts $from ORDER BY b.id DESC LIMIT %d OFFSET %d",
            array_merge($args, array((int) $per, (int) (($page - 1) * $per)))), ARRAY_A);
        return array($rows ? $rows : array(), $total);
    }

    private static function posted_parts($prefix, $fields)
    {
        $out = array(); $rows = isset($_POST[$prefix]) && is_array($_POST[$prefix]) ? wp_unslash($_POST[$prefix]) : array();
        foreach ($rows as $r) {
            if (!is_array($r)) { continue; }
            $row = array();
            foreach ($fields as $f => $kind) {
                $v = isset($r[$f]) ? (is_string($r[$f]) ? trim($r[$f]) : '') : '';
                if ($kind === 'flag') { $row[$f] = !empty($r[$f]) ? 1 : 0; continue; }
                if ($kind === 'int') { $v = self::digits($v); $row[$f] = $v === '' ? null : (int) preg_replace('/[^\d]/', '', $v); }
                elseif ($kind === 'cat') { $row[$f] = isset(self::$categories[$v]) ? $v : 'other'; }
                else { $row[$f] = $v === '' ? null : mb_substr(sanitize_text_field($v), 0, $kind); }
            }
            $out[] = $row;
        }
        return $out;
    }

    public static function act_build_save()
    {
        MZC_Admin::guard('build_save');
        $db = self::db(); if (!$db) { MZC_Admin::back('mzc-builds', 'err:' . MZC_Crm_Db::error()); }
        $id = isset($_POST['id']) ? (int) $_POST['id'] : 0;
        $cid = self::customer_of_form();
        if (!is_int($cid)) { MZC_Admin::back('mzc-builds', 'err:' . $cid, $id ? array('edit' => $id) : array('add' => 1)); }
        $title = self::post('title', 190);
        if ($title === '') { $title = 'سیستم نو'; }
        $bst = isset($_POST['status']) && isset(self::$build_statuses[$_POST['status']]) ? (string) $_POST['status'] : 'queue';
        $row = array('customer_id' => $cid, 'service_no' => self::post('service_no', 40) ?: null, 'invoice_no' => self::post('invoice_no', 60) ?: null, 'title' => $title, 'sold_at' => self::date('sold_at'),
            'report_id' => preg_match('/^[A-Za-z0-9-]{8,64}$/', self::post('report_id', 64)) ? self::post('report_id', 64) : null, 'notes' => self::post_long('notes'), 'status' => $bst, 'delivered' => $bst === 'delivered' ? 1 : 0, 'updated' => self::now());
        $t = self::t('builds');
        if ($id) { if ($db->update($t, $row, array('id' => $id)) === false) { MZC_Admin::back('mzc-builds', 'err:ذخیره نشد: ' . $db->last_error); } }
        else { $row['created'] = self::now(); if (!$db->insert($t, $row)) { MZC_Admin::back('mzc-builds', 'err:ذخیره نشد: ' . $db->last_error); } $id = (int) $db->insert_id; }
        $parts = self::posted_parts('parts', array('category' => 'cat', 'model' => 190, 'serial' => 120, 'warranty_months' => 'int', 'vendor' => 120, 'note' => 255, 'has_warranty' => 'flag', 'has_box' => 'flag', 'qc' => 'flag'));
        $db->delete(self::t('build_parts'), array('build_id' => $id));
        foreach ($parts as $p) { if ($p['model'] === null) { continue; } $p['build_id'] = $id; $db->insert(self::t('build_parts'), $p); }
        $bad = self::add_photos('build', $id);
        MZC_Admin::back('mzc-builds', 'ok:ثبت شد.' . ($bad ? ' (' . $bad . ')' : ''), array('view' => $id));
    }

    public static function act_build_delete()
    {
        MZC_Admin::guard('build_delete');
        $db = self::db(); $id = isset($_GET['id']) ? (int) $_GET['id'] : 0;
        if ($db && $id) { self::delete_photos('build', $id); $db->delete(self::t('build_parts'), array('build_id' => $id)); $db->delete(self::t('builds'), array('id' => $id)); }
        MZC_Admin::back('mzc-builds', 'ok:سیستم پاک شد.');
    }

    /* ---------- service jobs ---------- */

    public static function job($id)
    {
        $db = self::db(); if (!$db) { return null; }
        $j = $db->get_row($db->prepare('SELECT * FROM `' . self::t('jobs') . '` WHERE id = %d', (int) $id), ARRAY_A);
        if (!$j) { return null; }
        $all = $db->get_results($db->prepare('SELECT * FROM `' . self::t('job_parts') . '` WHERE job_id = %d ORDER BY id', (int) $id), ARRAY_A) ?: array();
        $j['parts'] = array_values(array_filter($all, function ($p) { return $p['kind'] !== 'in'; }));      // added parts (they are charged)
        $j['recv'] = array_values(array_filter($all, function ($p) { return $p['kind'] === 'in'; }));       // parts the customer brought
        $j['pays'] = $db->get_results($db->prepare('SELECT * FROM `' . self::t('job_pays') . '` WHERE job_id = %d ORDER BY id', (int) $id), ARRAY_A) ?: array();
        $j['shipping'] = !empty($j['ship']) ? json_decode($j['ship'], true) : array();
        $j['more'] = !empty($j['data']) ? json_decode($j['data'], true) : array();
        $j['photos'] = self::photos('job', $id);
        $j['total'] = self::job_total($j);
        return $j;
    }

    public static function job_total($j)
    {
        $sum = (int) $j['labor_price'] - (int) $j['discount'];
        foreach ($j['parts'] as $p) { $sum += (int) $p['qty'] * (int) $p['unit_price']; }
        return $sum;
    }

    public static function jobs($q, $status, $page, $per = 30, $customer = 0)
    {
        $db = self::db(); if (!$db) { return array(array(), 0); }
        $j = self::t('jobs'); $c = self::t('customers'); $where = array('1=1'); $args = array();
        if ($customer) { $where[] = 'j.customer_id = %d'; $args[] = (int) $customer; }
        if ($status !== '' && isset(self::$statuses[$status])) { $where[] = 'j.status = %s'; $args[] = $status; }
        if ($q !== '') {
            $l = '%' . $db->esc_like($q) . '%';
            $where[] = '(j.service_no LIKE %s OR j.device LIKE %s OR j.complaint LIKE %s OR c.name LIKE %s OR c.mobile LIKE %s)';
            array_push($args, $l, $l, $l, $l, '%' . $db->esc_like(self::digits($q)) . '%');
        }
        $from = "FROM `$j` j JOIN `$c` c ON c.id = j.customer_id WHERE " . implode(' AND ', $where);
        $total = (int) $db->get_var($args ? $db->prepare("SELECT COUNT(*) $from", $args) : "SELECT COUNT(*) $from");
        $rows = $db->get_results($db->prepare("SELECT j.*, c.name AS customer_name, c.mobile AS customer_mobile, j.labor_price - j.discount + COALESCE((SELECT SUM(p.qty * p.unit_price) FROM `" . self::t('job_parts') . "` p WHERE p.job_id = j.id AND p.kind <> 'in'), 0) AS total $from ORDER BY j.id DESC LIMIT %d OFFSET %d",
            array_merge($args, array((int) $per, (int) (($page - 1) * $per)))), ARRAY_A);
        return array($rows ? $rows : array(), $total);
    }

    /** The next service number: the old CRM's numbers (R-3579) go on from the highest one used by a service or a new computer. Call inside the lock. */
    public static function next_service_no($db)
    {
        $max = 0;
        foreach (array('jobs', 'builds') as $tb) { $max = max($max, (int) $db->get_var("SELECT MAX(CAST(SUBSTRING(service_no, 3) AS UNSIGNED)) FROM `" . self::t($tb) . "` WHERE service_no REGEXP '^R-[0-9]+$'")); }
        return 'R-' . ($max + 1);
    }

    public static function act_job_save()
    {
        MZC_Admin::guard('job_save');
        $db = self::db(); if (!$db) { MZC_Admin::back('mzc-jobs', 'err:' . MZC_Crm_Db::error()); }
        $id = isset($_POST['id']) ? (int) $_POST['id'] : 0;
        $cid = self::customer_of_form();
        if (!is_int($cid)) { MZC_Admin::back('mzc-jobs', 'err:' . $cid, $id ? array('edit' => $id) : array('add' => 1)); }
        $status = isset($_POST['status'], self::$statuses[$_POST['status']]) ? (string) $_POST['status'] : 'received';
        $closed = self::date('closed_at'); if ($status === 'delivered' && $closed === null) { $closed = gmdate('Y-m-d'); }
        $labor = (int) preg_replace('/[^\d]/', '', self::digits(self::post('labor_price', 15)));
        $row = array('customer_id' => $cid, 'service_no' => $id ? (self::post('service_no', 40) ?: null) : null, 'device' => self::post('device', 190) ?: null, 'received_at' => self::date('received_at') ?: gmdate('Y-m-d'),
            'closed_at' => $closed, 'due_at' => self::date('due_at'), 'invoice_no' => self::post('invoice_no', 60) ?: null, 'is_mazesta' => empty($_POST['is_mazesta']) ? 0 : 1, 'has_warranty' => empty($_POST['has_warranty']) ? 0 : 1,
            'discount' => (int) preg_replace('/[^\d]/', '', self::digits(self::post('discount', 15))), 'paid' => (int) preg_replace('/[^\d]/', '', self::digits(self::post('paid', 15))), 'service_done' => in_array($status, array('ready', 'delivered'), true) ? 1 : 0, 'status' => $status, 'complaint' => self::post_long('complaint'), 'work_done' => self::post_long('work_done'), 'labor_price' => $labor,
            'report_id' => preg_match('/^[A-Za-z0-9-]{8,64}$/', self::post('report_id', 64)) ? self::post('report_id', 64) : null, 'notes' => self::post_long('notes'), 'updated' => self::now());
        $t = self::t('jobs');
        if ($id && $row['service_no'] === null) { unset($row['service_no']); }   // an edit that leaves the number empty keeps it
        if ($id) { if ($db->update($t, $row, array('id' => $id)) === false) { MZC_Admin::back('mzc-jobs', 'err:ذخیره نشد: ' . $db->last_error); } }
        else {
            $row['created'] = self::now(); $lock = $db->get_var("SELECT GET_LOCK('" . self::t('svc') . "', 5)");   // two desks saving at once must not get the same number
            $row['service_no'] = self::next_service_no($db);
            $ok = $db->insert($t, $row); $new = (int) $db->insert_id; if ($lock) { $db->query("DO RELEASE_LOCK('" . self::t('svc') . "')"); }
            if (!$ok) { MZC_Admin::back('mzc-jobs', 'err:ذخیره نشد: ' . $db->last_error); } $id = $new;
        }
        $db->delete(self::t('job_parts'), array('job_id' => $id));
        foreach (array('add' => 'parts', 'in' => 'recv') as $kind => $field) {
            foreach (self::posted_parts($field, array('name' => 190, 'qty' => 'int', 'unit_price' => 'int', 'serial' => 120, 'warranty_months' => 'int', 'note' => 255, 'has_box' => 'flag', 'has_warranty' => 'flag')) as $p) {
                if ($p['name'] === null) { continue; }
                $p['qty'] = max(1, (int) $p['qty']); $p['unit_price'] = $kind === 'in' ? 0 : (int) $p['unit_price']; $p['job_id'] = $id; $p['kind'] = $kind; $db->insert(self::t('job_parts'), $p);
            }
        }
        $db->delete(self::t('job_pays'), array('job_id' => $id));
        foreach (self::posted_parts('pays', array('amount' => 'int', 'account' => 120, 'tx' => 80, 'holder' => 120)) as $p) { if (!$p['amount']) { continue; } $p['job_id'] = $id; $db->insert(self::t('job_pays'), $p); }
        $bad = self::add_photos('job', $id);
        MZC_Admin::back('mzc-jobs', 'ok:ثبت شد.' . ($bad ? ' (' . $bad . ')' : ''), array('view' => $id));
    }

    public static function act_job_delete()
    {
        MZC_Admin::guard('job_delete');
        $db = self::db(); $id = isset($_GET['id']) ? (int) $_GET['id'] : 0;
        if ($db && $id) { self::delete_photos('job', $id); $db->delete(self::t('job_parts'), array('job_id' => $id)); $db->delete(self::t('job_pays'), array('job_id' => $id)); $db->delete(self::t('jobs'), array('id' => $id)); }
        MZC_Admin::back('mzc-jobs', 'ok:سرویس پاک شد.');
    }

    /* ---------- customers: admin actions ---------- */

    public static function act_customer_save()
    {
        MZC_Admin::guard('customer_save');
        $id = isset($_POST['id']) ? (int) $_POST['id'] : 0;
        $r = self::save_customer($id, self::post('name', 190), self::post('mobile', 30), array('mobile2' => self::post('mobile2', 30), 'phone' => self::post('phone', 80) ?: null, 'phone2' => null, 'national_id' => self::post('national_id', 20) ?: null,
            'email' => sanitize_email(self::post('email', 190)) ?: null, 'address' => self::post_long('address', 1000) ?: null, 'notes' => self::post_long('notes') ?: null, 'grp' => self::post('grp', 12)));
        if (!is_int($r)) { MZC_Admin::back('mzc-customers', 'err:' . $r, $id ? array('view' => $id) : array('add' => 1)); }
        MZC_Admin::back('mzc-customers', 'ok:مشتری ثبت شد.', array('view' => $r));
    }

    public static function act_customer_delete()
    {
        MZC_Admin::guard('customer_delete');
        $db = self::db(); $id = isset($_GET['id']) ? (int) $_GET['id'] : 0;
        if ($db && $id) {
            $n = (int) $db->get_var($db->prepare('SELECT (SELECT COUNT(*) FROM `' . self::t('builds') . '` WHERE customer_id = %d) + (SELECT COUNT(*) FROM `' . self::t('jobs') . '` WHERE customer_id = %d)', $id, $id));
            if ($n > 0) { MZC_Admin::back('mzc-customers', 'err:این مشتری سیستم یا سرویس ثبت‌شده دارد؛ اول آن‌ها را پاک کنید.', array('view' => $id)); }
            $db->delete(self::t('sessions'), array('customer_id' => $id)); $db->delete(self::t('customers'), array('id' => $id));
        }
        MZC_Admin::back('mzc-customers', 'ok:مشتری پاک شد.');
    }

    /** Everything a customer has: their systems and service jobs, each with parts and photos, newest first (the admin page and the portal both show this). */
    public static function history($customerId)
    {
        $db = self::db(); if (!$db) { return array(array(), array()); }
        $builds = array(); $jobs = array();
        foreach ((array) $db->get_col($db->prepare('SELECT id FROM `' . self::t('builds') . '` WHERE customer_id = %d ORDER BY COALESCE(sold_at, DATE(created)) DESC, id DESC', (int) $customerId)) as $id) { $builds[] = self::build($id); }
        foreach ((array) $db->get_col($db->prepare('SELECT id FROM `' . self::t('jobs') . '` WHERE customer_id = %d ORDER BY COALESCE(received_at, DATE(created)) DESC, id DESC', (int) $customerId)) as $id) { $jobs[] = self::job($id); }
        return array($builds, $jobs);
    }

    /** The customer's other forms (tests, warranty parts, hand-overs ...), newest first. */
    public static function forms_of($customerId)
    {
        $db = self::db(); if (!$db) { return array(); }
        $rows = $db->get_results($db->prepare('SELECT * FROM `' . self::t('forms') . '` WHERE customer_id = %d ORDER BY COALESCE(at, DATE(created)) DESC, id DESC', (int) $customerId), ARRAY_A) ?: array();
        foreach ($rows as &$r) { $r['more'] = !empty($r['data']) ? json_decode($r['data'], true) : array(); }
        return $rows;
    }

    public static $form_kinds = array('test' => 'تست سیستم', 'warranty' => 'قطعهٔ گارانتی / تعمیرگاه', 'handover' => 'تحویل و دریافت کالا', 'online' => 'خدمات آنلاین', 'solution' => 'راه‌حل');

    /* ---------- photos: files in the plugin's data folder (not in the media library), the names in the CRM database ---------- */

    private static function photo_dir() { return Mazesta_Connect::dir() . '/photos'; }

    public static function photos($entity, $id)
    {
        $db = self::db(); if (!$db) { return array(); }
        return $db->get_results($db->prepare('SELECT * FROM `' . self::t('photos') . '` WHERE entity = %s AND entity_id = %d ORDER BY id', $entity, (int) $id), ARRAY_A) ?: array();
    }

    /** Stores the posted images (resized to 1600 px, recompressed); returns a note about any that were refused, or ''. */
    private static function add_photos($entity, $id)
    {
        $db = self::db();
        if (!$db || empty($_FILES['photos']['name']) || !is_array($_FILES['photos']['name'])) { return ''; }
        if (!wp_mkdir_p(self::photo_dir())) { return 'پوشهٔ عکس‌ها ساخته نشد'; }
        $have = count(self::photos($entity, $id)); $refused = 0; $caption = self::post('photo_caption', 190);
        foreach ($_FILES['photos']['name'] as $i => $name) {
            if ($_FILES['photos']['error'][$i] === UPLOAD_ERR_NO_FILE) { continue; }
            $tmp = $_FILES['photos']['tmp_name'][$i];
            if ($have >= self::PHOTO_MAX || $_FILES['photos']['error'][$i] !== UPLOAD_ERR_OK || $_FILES['photos']['size'][$i] > self::PHOTO_BYTES || !is_uploaded_file($tmp)) { $refused++; continue; }
            $info = @getimagesize($tmp);
            $ext = $info && isset($info[2]) ? array(IMAGETYPE_JPEG => 'jpg', IMAGETYPE_PNG => 'png', IMAGETYPE_WEBP => 'webp') : array();
            $ext = $info && isset($ext[$info[2]]) ? $ext[$info[2]] : '';
            if ($ext === '') { $refused++; continue; }
            $file = bin2hex(random_bytes(16)) . '.' . $ext; $dest = self::photo_dir() . '/' . $file . '.dat';
            $ed = wp_get_image_editor($tmp);
            $ok = false;
            if (!is_wp_error($ed)) { $ed->resize(1600, 1600, false); $ed->set_quality(82); $saved = $ed->save($dest, $info['mime']); $ok = !is_wp_error($saved) && is_file($dest); }
            if (!$ok) { $refused++; continue; }
            $db->insert(self::t('photos'), array('entity' => $entity, 'entity_id' => (int) $id, 'file' => $file, 'caption' => $caption ?: null, 'created' => self::now()));
            $have++;
        }
        return $refused ? $refused . ' عکس پذیرفته نشد (فقط jpg/png/webp تا ۸ مگابایت و حداکثر ' . self::PHOTO_MAX . ' عکس)' : '';
    }

    private static function delete_photos($entity, $id)
    {
        $db = self::db(); if (!$db) { return; }
        foreach (self::photos($entity, $id) as $p) { @unlink(self::photo_dir() . '/' . $p['file'] . '.dat'); }
        $db->delete(self::t('photos'), array('entity' => $entity, 'entity_id' => (int) $id));
    }

    public static function act_photo_delete()
    {
        MZC_Admin::guard('photo_delete');
        $db = self::db(); $id = isset($_GET['id']) ? (int) $_GET['id'] : 0;
        if ($db && ($p = $db->get_row($db->prepare('SELECT * FROM `' . self::t('photos') . '` WHERE id = %d', $id), ARRAY_A))) {
            @unlink(self::photo_dir() . '/' . $p['file'] . '.dat'); $db->delete(self::t('photos'), array('id' => $id));
            MZC_Admin::back($p['entity'] === 'job' ? 'mzc-jobs' : 'mzc-builds', 'ok:عکس پاک شد.', array('edit' => (int) $p['entity_id']));
        }
        MZC_Admin::back('mzc-builds', 'err:عکس پیدا نشد.');
    }

    /** Sends a stored photo's bytes (the caller has checked who may see it). */
    public static function send_photo($p)
    {
        $f = self::photo_dir() . '/' . basename((string) $p['file']) . '.dat';
        if (!is_readable($f)) { status_header(404); exit; }
        $ext = pathinfo((string) $p['file'], PATHINFO_EXTENSION);
        nocache_headers();
        header('Content-Type: ' . ($ext === 'png' ? 'image/png' : ($ext === 'webp' ? 'image/webp' : 'image/jpeg')));
        header('Content-Length: ' . filesize($f)); header('X-Content-Type-Options: nosniff'); header("Content-Security-Policy: default-src 'none'; sandbox"); header('Cache-Control: private, max-age=3600');
        readfile($f); exit;
    }

    public static function photo_row($id)
    {
        $db = self::db(); if (!$db) { return null; }
        return $db->get_row($db->prepare('SELECT * FROM `' . self::t('photos') . '` WHERE id = %d', (int) $id), ARRAY_A);
    }

    public static function act_photo()
    {
        if (!Mazesta_Connect::can()) { wp_die('اجازه ندارید.', '', array('response' => 403)); }
        $p = self::photo_row(isset($_GET['id']) ? (int) $_GET['id'] : 0);
        if (!$p) { status_header(404); exit; }
        self::send_photo($p);
    }

    /* ---------- reports from the app, for the forms' "attach the test report" ---------- */

    /** The reports the app sent, newest first, for a dropdown; those whose service number is $service come first. */
    public static function report_options($service = '')
    {
        $rows = Mazesta_Connect::read('reports');
        uasort($rows, function ($a, $b) { return strcmp((string) $b['created'], (string) $a['created']); });
        $out = array();
        foreach (array_slice($rows, 0, 300, true) as $id => $r) {
            $label = trim(($r['service'] !== '' ? $r['service'] . ' — ' : '') . ($r['device'] !== '' ? $r['device'] : $r['machine']) . ' — ' . gmdate('Y/m/d', (int) strtotime($r['created'])));
            $out[(string) $id] = array('label' => $label, 'match' => $service !== '' && $r['service'] === $service);
        }
        uasort($out, function ($a, $b) { return (int) $b['match'] <=> (int) $a['match']; });
        return $out;
    }

    /* ---------- settings: the database and the SMS ---------- */

    public static function act_db_save()
    {
        MZC_Admin::guard('db_save', 'manage_options');
        $e = MZC_Crm_Db::save(self::post('host', 190) ?: 'localhost', self::post('name', 64), self::post('user', 64), isset($_POST['pass']) ? (string) wp_unslash($_POST['pass']) : '',
            preg_match('/^[A-Za-z0-9_]{1,20}$/', self::post('prefix', 20)) ? self::post('prefix', 20) : 'mz_');
        if ($e !== '') { MZC_Admin::back('mzc-settings', 'err:' . $e); }
        $e = MZC_Crm_Db::install();
        MZC_Admin::back('mzc-settings', $e === '' ? 'ok:اتصال برقرار شد و جدول‌ها آماده‌اند.' : 'err:' . $e);
    }

    /** Empties every CRM table (not the settings or the schema mark) so the old data can be imported again from nothing. Needs the site administrator and the typed word. */
    public static function act_db_wipe()
    {
        MZC_Admin::guard('db_wipe', 'manage_options');
        $db = self::db(); if (!$db) { MZC_Admin::back('mzc-settings', 'err:' . MZC_Crm_Db::error()); }
        if (self::post('confirm', 20) !== 'پاک‌کن') { MZC_Admin::back('mzc-settings', 'err:برای پاک کردن، کلمهٔ «پاک‌کن» را دقیق بنویسید.'); }
        foreach ((array) $db->get_col('SELECT file FROM `' . self::t('photos') . '`') as $f) { if (preg_match('/^[a-f0-9]{32}\.(jpg|png|webp)$/', (string) $f)) { @unlink(self::photo_dir() . '/' . $f . '.dat'); } }
        foreach (array('customers', 'builds', 'build_parts', 'jobs', 'job_parts', 'job_pays', 'forms', 'shipments', 'vendors', 'oldmap', 'photos', 'otps', 'sessions') as $tb) { $db->query('TRUNCATE TABLE `' . self::t($tb) . '`'); }
        $db->query('DELETE FROM `' . self::t('meta') . "` WHERE k = 'import'");
        MZC_Admin::back('mzc-settings', 'ok:همهٔ اطلاعات CRM پاک شد؛ فایل واردسازی را دوباره بارگذاری کنید.');
    }

    public static function act_db_install()
    {
        MZC_Admin::guard('db_install', 'manage_options');
        $e = MZC_Crm_Db::install();
        MZC_Admin::back('mzc-settings', $e === '' ? 'ok:جدول‌ها بررسی و آماده شد.' : 'err:' . $e);
    }

    public static function act_sms_save() { MZC_Sms::act_save(); }
    public static function act_sms_test() { MZC_Sms::act_test(); }
}
