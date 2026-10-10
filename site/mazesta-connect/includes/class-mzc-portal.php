<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The customer's own page: the shortcode [mazesta_portal] on any page of the site. The customer types their mobile number, gets a six-digit code by SMS, and sees the history
 * kept for that number - the computers bought from the shop (parts, serial numbers, warranty left, test report) and the service jobs (what was asked, what was done, the parts
 * added and their prices), with the photos. Nothing of another customer, and none of the shop's internal notes, ever leaves here.
 * The page itself is static (a cached copy is harmless); everything personal comes through the REST routes below, which are never cached and need the code first.
 * A code is valid five minutes, five tries; three codes per number in fifteen minutes; the number's existence is not revealed (the same answer for a known and an unknown number).
 */
final class MZC_Portal
{
    const CODE_MINUTES = 5;
    const SESSION_DAYS = 7;

    public static function boot()
    {
        add_shortcode('mazesta_portal', array(__CLASS__, 'shortcode'));
        add_action('rest_api_init', array(__CLASS__, 'routes'));
        add_action('init', array(__CLASS__, 'photo'));
    }

    public static function routes()
    {
        $open = '__return_true';
        register_rest_route(Mazesta_Connect::NS, '/portal/code', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_code'), 'permission_callback' => $open));
        register_rest_route(Mazesta_Connect::NS, '/portal/verify', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_verify'), 'permission_callback' => $open));
        register_rest_route(Mazesta_Connect::NS, '/portal/me', array('methods' => 'GET', 'callback' => array(__CLASS__, 'rest_me'), 'permission_callback' => $open));
        register_rest_route(Mazesta_Connect::NS, '/portal/logout', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_logout'), 'permission_callback' => $open));
    }

    public static function shortcode()
    {
        wp_enqueue_style('mzc-portal', MZC_URL . 'assets/portal.css', array(), MZC_VERSION);
        wp_enqueue_script('mzc-portal', MZC_URL . 'assets/portal.js', array(), MZC_VERSION, true);
        wp_localize_script('mzc-portal', 'MZC_PORTAL', array('api' => esc_url_raw(rest_url(Mazesta_Connect::NS . '/portal/')), 'photo' => esc_url_raw(home_url('/?mzc_portal_photo=')), 'codeMinutes' => self::CODE_MINUTES));
        return '<div id="mzc-portal" class="mzc-portal" dir="rtl"><noscript>برای دیدن سوابق، جاوااسکریپت مرورگر باید روشن باشد.</noscript></div>';
    }

    private static function hash($s) { return hash_hmac('sha256', (string) $s, wp_salt('auth')); }
    private static function now() { return gmdate('Y-m-d H:i:s'); }
    private static function ok($d = array(), $st = 200) { return Mazesta_Connect::fresh($d, $st); }
    private static function fail($msg, $st = 400) { return Mazesta_Connect::fresh(array('error' => $msg), $st); }

    public static function rest_code($req)
    {
        $db = MZC_Crm_Db::db();
        if (!$db || !MZC_Sms::configured()) { return self::fail('سامانه فعلاً در دسترس نیست. لطفاً با ما تماس بگیرید.', 503); }
        if (!Mazesta_Connect::allowed('otp', 20)) { return self::fail('درخواست‌ها زیاد بود؛ یک ساعت دیگر دوباره امتحان کنید.', 429); }
        $p = $req->get_json_params();
        $mobile = MZC_Crm::mobile(is_array($p) && isset($p['mobile']) ? (string) $p['mobile'] : '');
        if ($mobile === '') { return self::fail('شمارهٔ موبایل درست نیست (مثل 09121234567).'); }
        $o = MZC_Crm_Db::t('otps');
        $db->query($db->prepare("DELETE FROM `$o` WHERE expires < %s", gmdate('Y-m-d H:i:s', time() - 3600)));
        $db->query($db->prepare('DELETE FROM `' . MZC_Crm_Db::t('sessions') . '` WHERE expires < %s', self::now()));
        $recent = (int) $db->get_var($db->prepare("SELECT COUNT(*) FROM `$o` WHERE mobile = %s AND created > %s", $mobile, gmdate('Y-m-d H:i:s', time() - 900)));
        if ($recent >= 3) { return self::fail('برای این شماره چند کد فرستاده شده است؛ ' . '۱۵ دقیقه دیگر دوباره امتحان کنید.', 429); }
        // The same answer whether the number is in the records or not; only a known number is sent a code (no SMS cost for strangers, nothing to learn from the answer).
        if (MZC_Crm::customer_by_mobile($mobile)) {
            $code = (string) random_int(100000, 999999);
            $db->insert($o, array('mobile' => $mobile, 'code_hash' => self::hash($mobile . '|' . $code), 'expires' => gmdate('Y-m-d H:i:s', time() + self::CODE_MINUTES * 60), 'attempts' => 0, 'created' => self::now()));
            $s = MZC_Sms::settings();
            $r = MZC_Sms::send($mobile, str_replace('{code}', $code, (string) $s['text']), $code);
            if ($r !== true) { error_log('Mazesta Connect: SMS not sent: ' . $r); }
        }
        return self::ok(array('sent' => true, 'minutes' => self::CODE_MINUTES));
    }

    public static function rest_verify($req)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return self::fail('سامانه فعلاً در دسترس نیست.', 503); }
        if (!Mazesta_Connect::allowed('otpv', 60)) { return self::fail('درخواست‌ها زیاد بود؛ یک ساعت دیگر دوباره امتحان کنید.', 429); }
        $p = $req->get_json_params();
        $mobile = MZC_Crm::mobile(is_array($p) && isset($p['mobile']) ? (string) $p['mobile'] : '');
        $code = is_array($p) && isset($p['code']) ? preg_replace('/\D+/', '', MZC_Crm::digits((string) $p['code'])) : '';
        if ($mobile === '' || strlen($code) !== 6) { return self::fail('کد شش‌رقمی را وارد کنید.'); }
        $o = MZC_Crm_Db::t('otps');
        $row = $db->get_row($db->prepare("SELECT * FROM `$o` WHERE mobile = %s AND expires > %s ORDER BY id DESC LIMIT 1", $mobile, self::now()), ARRAY_A);
        $wrong = self::fail('کد درست نیست یا منقضی شده است.', 401);
        if (!$row || (int) $row['attempts'] >= 5) { return $wrong; }
        $db->query($db->prepare("UPDATE `$o` SET attempts = attempts + 1 WHERE id = %d", (int) $row['id']));
        if (!hash_equals((string) $row['code_hash'], self::hash($mobile . '|' . $code))) { return $wrong; }
        $c = MZC_Crm::customer_by_mobile($mobile);
        if (!$c) { return $wrong; }
        $db->delete($o, array('mobile' => $mobile));
        $token = bin2hex(random_bytes(32));
        $db->insert(MZC_Crm_Db::t('sessions'), array('token_hash' => self::hash($token), 'customer_id' => (int) $c['id'], 'expires' => gmdate('Y-m-d H:i:s', time() + self::SESSION_DAYS * 86400), 'created' => self::now()));
        return self::ok(array('token' => $token, 'name' => $c['name']));
    }

    /** The customer id of the request's token, or 0. */
    private static function customer_of($token)
    {
        $db = MZC_Crm_Db::db(); if (!$db || !is_string($token) || !preg_match('/^[a-f0-9]{64}$/', $token)) { return 0; }
        return (int) $db->get_var($db->prepare('SELECT customer_id FROM `' . MZC_Crm_Db::t('sessions') . '` WHERE token_hash = %s AND expires > %s', self::hash($token), self::now()));
    }

    public static function rest_logout($req)
    {
        $db = MZC_Crm_Db::db(); $t = (string) $req->get_header('x_mzc_token');
        if ($db && preg_match('/^[a-f0-9]{64}$/', $t)) { $db->delete(MZC_Crm_Db::t('sessions'), array('token_hash' => self::hash($t))); }
        return self::ok(array('ok' => true));
    }

    public static function rest_me($req)
    {
        $cid = self::customer_of((string) $req->get_header('x_mzc_token'));
        if (!$cid) { return self::fail('نشست شما تمام شده؛ دوباره وارد شوید.', 401); }
        $c = MZC_Crm::customer($cid);
        list($builds, $jobs) = MZC_Crm::history($cid);
        $reports = Mazesta_Connect::read('reports'); $public = !empty(Mazesta_Connect::config()['publicLinks']);
        $link = function ($rid) use ($reports, $public) { return $public && $rid && isset($reports[$rid]['token']) ? home_url('/?mazesta_report=' . $reports[$rid]['token']) : null; };
        $outB = array();
        foreach ($builds as $b) {
            $parts = array();
            foreach ($b['parts'] as $p) {
                list($until, $left) = MZC_Crm::warranty($b['sold_at'], $p['warranty_months']);
                $parts[] = array('category' => isset(MZC_Crm::$categories[$p['category']]) ? MZC_Crm::$categories[$p['category']] : $p['category'], 'model' => $p['model'], 'serial' => $p['serial'], 'vendor' => $p['vendor'],
                    'months' => $p['warranty_months'] !== null ? (int) $p['warranty_months'] : null, 'until' => $until ? MZC_Crm::jdate($until) : null, 'left' => $left);
            }
            $outB[] = array('title' => $b['title'], 'date' => $b['sold_at'] ? MZC_Crm::jdate($b['sold_at']) : '', 'invoice' => $b['invoice_no'], 'service' => $b['service_no'], 'parts' => $parts,
                'report' => $link($b['report_id']), 'photos' => array_map(function ($x) { return (int) $x['id']; }, $b['photos']));
        }
        $outJ = array();
        foreach ($jobs as $j) {
            $parts = array();
            foreach ($j['parts'] as $p) { $parts[] = array('name' => $p['name'], 'qty' => (int) $p['qty'], 'price' => (int) $p['unit_price'], 'serial' => $p['serial'], 'months' => $p['warranty_months'] !== null ? (int) $p['warranty_months'] : null); }
            $outJ[] = array('service' => $j['service_no'], 'device' => $j['device'], 'received' => $j['received_at'] ? MZC_Crm::jdate($j['received_at']) : '', 'closed' => $j['closed_at'] ? MZC_Crm::jdate($j['closed_at']) : '',
                'status' => $j['status'], 'statusText' => isset(MZC_Crm::$statuses[$j['status']]) ? MZC_Crm::$statuses[$j['status']] : $j['status'], 'complaint' => $j['complaint'], 'work' => $j['work_done'],
                'labor' => (int) $j['labor_price'], 'total' => (int) $j['total'], 'parts' => $parts, 'report' => $link($j['report_id']), 'photos' => array_map(function ($x) { return (int) $x['id']; }, $j['photos']));
        }
        return self::ok(array('name' => $c['name'], 'builds' => $outB, 'jobs' => $outJ));
    }

    /** ?mzc_portal_photo=ID with the token in a header (the page fetches it and shows the bytes), only for a photo of the token's own customer. */
    public static function photo()
    {
        if (empty($_GET['mzc_portal_photo'])) { return; }
        $cid = self::customer_of(isset($_SERVER['HTTP_X_MZC_TOKEN']) ? (string) $_SERVER['HTTP_X_MZC_TOKEN'] : '');
        $p = $cid ? MZC_Crm::photo_row((int) $_GET['mzc_portal_photo']) : null;
        if ($p) {
            $db = MZC_Crm_Db::db();
            $owner = (int) $db->get_var($db->prepare('SELECT customer_id FROM `' . MZC_Crm_Db::t($p['entity'] === 'job' ? 'jobs' : 'builds') . '` WHERE id = %d', (int) $p['entity_id']));
            if ($owner === $cid) { MZC_Crm::send_photo($p); }
        }
        status_header(404); exit;
    }
}
