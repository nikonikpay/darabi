<?php
if (!defined('ABSPATH')) { exit; }

/**
 * Accounts and the Pro plan of the app. Nothing here is switched on by default: the app asks mazesta/v1/access (its random installation id, and the account's token when it has one)
 * and, while "enforce" is off, is told that nothing is locked, so every copy stays free. When the shop turns it on, the copies that ask (every version from the one that has the
 * lock mechanism) are told which menus and tabs ("page:<id>", "menu:<key>") need Pro; a copy whose account has an active Pro plan is told none.
 * An account is a mobile number confirmed by an SMS code (the same gateway as the customer portal, MZC_Sms), and the computers (installation ids) that signed in with it are
 * linked to it, so a computer is known by its owner's number on the site instead of by a random id. Plan, end date and "disabled" are set by the shop on the page "اشتراک‌ها".
 * Tables (CRM database, schema 16): accounts, app_codes (a code is valid five minutes, five tries), app_sessions (the app's token, hashed, 90 days), account_installs.
 */
final class MZC_Access
{
    const CODE_MINUTES = 5;
    const SESSION_DAYS = 90;

    /** What can be locked: key => Persian name. The app never locks the dashboard, messages, settings, app update or help (a customer must always be able to sign in, update and reach support). */
    public static function catalog()
    {
        return array(
            'menu:Nav_Group_Hardware' => 'منوی سخت‌افزار (همهٔ زبانه‌ها)', 'page:monitoring' => 'پایش', 'page:system' => 'مشخصات سیستم', 'page:cpu' => 'پردازنده', 'page:gpu' => 'کارت گرافیک', 'page:ram' => 'رم', 'page:storage' => 'حافظه‌ها', 'page:network' => 'شبکه',
            'menu:Nav_Group_Tests' => 'منوی آزمون‌ها (همهٔ زبانه‌ها)', 'page:tests' => 'آزمون‌ها', 'page:benchmarks' => 'بنچمارک', 'page:checkup' => 'تشخیص', 'page:checks' => 'بررسی‌ها',
            'menu:Nav_Group_Apps' => 'منوی برنامه‌ها', 'page:apps' => 'برنامه‌ها', 'page:ai' => 'مدل‌های هوش مصنوعی',
            'menu:Nav_Group_Gaming' => 'منوی بازی', 'page:play' => 'بازی', 'page:games' => 'بازی‌ها', 'page:overlay' => 'اورلی',
            'menu:Nav_Tuning' => 'تیونینگ کارت گرافیک',
            'menu:Nav_Group_Lights' => 'منوی نور و فن', 'page:lights' => 'نورها', 'page:fans' => 'فن‌ها',
            'menu:Nav_Group_Windows' => 'منوی ابزار ویندوز', 'page:tools' => 'ابزارهای ویندوز', 'page:tweaks' => 'تنظیمات ویندوز', 'page:wake' => 'بیدارکننده‌ها', 'page:shield' => 'امنیت', 'page:updates' => 'به‌روزرسانی ویندوز', 'page:drivers' => 'درایورها',
            'menu:Nav_Reports' => 'گزارش‌ها',
        );
    }

    public static function boot()
    {
        add_action('rest_api_init', array(__CLASS__, 'routes'));
        add_action('admin_post_mzc_access_save', array(__CLASS__, 'act_save'));
        add_action('admin_post_mzc_account_save', array(__CLASS__, 'act_account'));
    }

    public static function routes()
    {
        $open = '__return_true';
        foreach (array('access' => 'rest_access', 'access/code' => 'rest_code', 'access/verify' => 'rest_verify', 'access/logout' => 'rest_logout') as $path => $fn) {
            register_rest_route(Mazesta_Connect::NS, '/' . $path, array('methods' => 'POST', 'callback' => array(__CLASS__, $fn), 'permission_callback' => $open));
        }
    }

    /** The settings: enforce (off: nothing is locked), login (the app may offer the sign-in), the locked keys and a line the app shows on a locked page. */
    public static function settings()
    {
        $c = Mazesta_Connect::config();
        $s = isset($c['access']) && is_array($c['access']) ? $c['access'] : array();
        return $s + array('enforce' => false, 'login' => false, 'locks' => array(), 'message' => '');
    }

    private static function hash($s) { return hash_hmac('sha256', 'app|' . $s, wp_salt('auth')); }
    private static function now() { return gmdate('Y-m-d H:i:s'); }
    private static function ok($d = array(), $st = 200) { return Mazesta_Connect::fresh($d, $st); }
    private static function fail($msg, $st = 400) { return Mazesta_Connect::fresh(array('error' => $msg), $st); }
    private static function install_id($p) { $i = is_array($p) && isset($p['install']) ? strtolower((string) $p['install']) : ''; return preg_match('/^[a-f0-9]{32}$/', $i) ? $i : ''; }
    private static function mask($m) { return substr($m, 0, 4) . '***' . substr($m, -3); }

    /** The account of a token (row) or null. */
    public static function account_of($token)
    {
        $db = MZC_Crm_Db::db(); if (!$db || !is_string($token) || !preg_match('/^[a-f0-9]{64}$/', $token)) { return null; }
        $row = $db->get_row($db->prepare('SELECT a.* FROM `' . MZC_Crm_Db::t('app_sessions') . '` s JOIN `' . MZC_Crm_Db::t('accounts') . '` a ON a.id = s.account_id WHERE s.token_hash = %s AND s.expires > %s', self::hash($token), self::now()), ARRAY_A);
        return $row ?: null;
    }

    public static function is_pro($acc)
    {
        return $acc && empty($acc['disabled']) && $acc['plan'] === 'pro' && (empty($acc['pro_until']) || strtotime($acc['pro_until'] . ' UTC') > time());
    }

    /** What the app is told. While enforce is off (the default) the lists are empty and no sign-in is offered: the app shows nothing new. */
    private static function entitlements($acc)
    {
        $s = self::settings(); $pro = self::is_pro($acc);
        return array('v' => 1, 'enforce' => (bool) $s['enforce'], 'login' => (bool) $s['login'], 'plan' => $pro ? 'pro' : 'free',
            'until' => $pro && !empty($acc['pro_until']) ? Mazesta_Connect::iso($acc['pro_until']) : null, 'account' => $acc ? self::mask($acc['mobile']) : null,
            'locks' => $s['enforce'] && !$pro ? array_values(array_intersect((array) $s['locks'], array_keys(self::catalog()))) : array(),
            'freeLocks' => $s['enforce'] ? array_values(array_intersect((array) $s['locks'], array_keys(self::catalog()))) : array(),   // what applies once a Pro plan runs out while the copy is offline
            'message' => $s['enforce'] && !$pro ? (string) $s['message'] : '');
    }

    private static function link($acc, $install)
    {
        $db = MZC_Crm_Db::db(); if (!$db || !$acc || $install === '') { return; }
        $db->query($db->prepare('INSERT INTO `' . MZC_Crm_Db::t('account_installs') . '` (install, account_id, linked) VALUES (%s, %d, %s) ON DUPLICATE KEY UPDATE account_id = VALUES(account_id)', $install, (int) $acc['id'], self::now()));
        $db->update(MZC_Crm_Db::t('accounts'), array('last_seen' => self::now()), array('id' => (int) $acc['id']));
    }

    public static function rest_access($req)
    {
        if (!Mazesta_Connect::allowed('acc', 240)) { return self::fail('Too many calls from this address; try again in an hour.', 429); }
        $p = $req->get_json_params(); $db = MZC_Crm_Db::db();
        $acc = $db && is_array($p) && isset($p['token']) ? self::account_of((string) $p['token']) : null;
        if ($acc) { self::link($acc, self::install_id($p)); }
        return self::ok(self::entitlements($acc));
    }

    public static function rest_code($req)
    {
        $db = MZC_Crm_Db::db(); $s = self::settings();
        if (!$db || !$s['login'] || !MZC_Sms::configured()) { return self::fail('ورود هنوز در دسترس نیست.', 503); }
        if (!Mazesta_Connect::allowed('acode', 20)) { return self::fail('درخواست‌ها زیاد بود؛ یک ساعت دیگر دوباره امتحان کنید.', 429); }
        $p = $req->get_json_params();
        $mobile = MZC_Crm::mobile(is_array($p) && isset($p['mobile']) ? (string) $p['mobile'] : '');
        if ($mobile === '') { return self::fail('شمارهٔ موبایل درست نیست (مثل 09121234567).'); }
        $c = MZC_Crm_Db::t('app_codes');
        $db->query($db->prepare("DELETE FROM `$c` WHERE expires < %s", gmdate('Y-m-d H:i:s', time() - 3600)));
        if ((int) $db->get_var($db->prepare("SELECT COUNT(*) FROM `$c` WHERE mobile = %s AND created > %s", $mobile, gmdate('Y-m-d H:i:s', time() - 900))) >= 3) { return self::fail('برای این شماره چند کد فرستاده شده است؛ ۱۵ دقیقه دیگر دوباره امتحان کنید.', 429); }
        $code = (string) random_int(100000, 999999);
        $db->insert($c, array('mobile' => $mobile, 'code_hash' => self::hash($mobile . '|' . $code), 'expires' => gmdate('Y-m-d H:i:s', time() + self::CODE_MINUTES * 60), 'attempts' => 0, 'created' => self::now()));
        $sms = MZC_Sms::settings();
        $r = MZC_Sms::send($mobile, str_replace('{code}', $code, (string) $sms['text']), $code);
        if ($r !== true) { error_log('Mazesta Connect: access SMS not sent: ' . $r); return self::fail('پیامک فرستاده نشد؛ کمی بعد دوباره امتحان کنید.', 502); }
        return self::ok(array('sent' => true, 'minutes' => self::CODE_MINUTES));
    }

    public static function rest_verify($req)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return self::fail('ورود هنوز در دسترس نیست.', 503); }
        if (!Mazesta_Connect::allowed('averify', 60)) { return self::fail('درخواست‌ها زیاد بود؛ یک ساعت دیگر دوباره امتحان کنید.', 429); }
        $p = $req->get_json_params();
        $mobile = MZC_Crm::mobile(is_array($p) && isset($p['mobile']) ? (string) $p['mobile'] : '');
        $code = is_array($p) && isset($p['code']) ? preg_replace('/\D+/', '', MZC_Crm::digits((string) $p['code'])) : '';
        if ($mobile === '' || strlen($code) !== 6) { return self::fail('کد شش‌رقمی را وارد کنید.'); }
        $c = MZC_Crm_Db::t('app_codes');
        $row = $db->get_row($db->prepare("SELECT * FROM `$c` WHERE mobile = %s AND expires > %s ORDER BY id DESC LIMIT 1", $mobile, self::now()), ARRAY_A);
        $wrong = self::fail('کد درست نیست یا منقضی شده است.', 401);
        if (!$row || (int) $row['attempts'] >= 5) { return $wrong; }
        $db->query($db->prepare("UPDATE `$c` SET attempts = attempts + 1 WHERE id = %d", (int) $row['id']));
        if (!hash_equals((string) $row['code_hash'], self::hash($mobile . '|' . $code))) { return $wrong; }
        $db->delete($c, array('mobile' => $mobile));
        $a = MZC_Crm_Db::t('accounts');
        $acc = $db->get_row($db->prepare("SELECT * FROM `$a` WHERE mobile = %s", $mobile), ARRAY_A);
        if (!$acc) { $db->insert($a, array('mobile' => $mobile, 'plan' => 'free', 'created' => self::now(), 'last_seen' => self::now())); $acc = $db->get_row($db->prepare("SELECT * FROM `$a` WHERE mobile = %s", $mobile), ARRAY_A); }
        if (!$acc) { return self::fail('ساخت حساب ناموفق بود.', 500); }
        $token = bin2hex(random_bytes(32));
        $db->insert(MZC_Crm_Db::t('app_sessions'), array('token_hash' => self::hash($token), 'account_id' => (int) $acc['id'], 'install' => self::install_id($p), 'expires' => gmdate('Y-m-d H:i:s', time() + self::SESSION_DAYS * 86400), 'created' => self::now()));
        self::link($acc, self::install_id($p));
        return self::ok(array('token' => $token) + self::entitlements($acc));
    }

    public static function rest_logout($req)
    {
        $db = MZC_Crm_Db::db(); $p = $req->get_json_params();
        if ($db && is_array($p) && isset($p['token']) && preg_match('/^[a-f0-9]{64}$/', (string) $p['token'])) { $db->delete(MZC_Crm_Db::t('app_sessions'), array('token_hash' => self::hash((string) $p['token']))); }
        return self::ok(array('ok' => true));
    }

    /** The accounts for the admin page, newest first, each with the installations linked to it. */
    public static function accounts($q = '', $limit = 100)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return array(); }
        $a = MZC_Crm_Db::t('accounts'); $q = preg_replace('/\D+/', '', MZC_Crm::digits((string) $q));
        $rows = (array) $db->get_results($q !== '' ? $db->prepare("SELECT * FROM `$a` WHERE mobile LIKE %s ORDER BY id DESC LIMIT %d", '%' . $db->esc_like($q) . '%', $limit) : $db->prepare("SELECT * FROM `$a` ORDER BY id DESC LIMIT %d", $limit), ARRAY_A);
        foreach ($rows as &$r) { $r['installs'] = (array) $db->get_col($db->prepare('SELECT install FROM `' . MZC_Crm_Db::t('account_installs') . '` WHERE account_id = %d', (int) $r['id'])); }
        return $rows;
    }

    public static function act_save()
    {
        MZC_Admin::guard('access_save', 'manage_options');
        $keys = isset($_POST['locks']) && is_array($_POST['locks']) ? array_values(array_intersect(array_map('sanitize_text_field', wp_unslash($_POST['locks'])), array_keys(self::catalog()))) : array();
        Mazesta_Connect::config();
        Mazesta_Connect::locked(function () use ($keys) {
            $c = Mazesta_Connect::read('config');
            $c['access'] = array('enforce' => !empty($_POST['enforce']), 'login' => !empty($_POST['login']), 'locks' => $keys, 'message' => isset($_POST['message']) ? mb_substr(trim(sanitize_textarea_field(wp_unslash($_POST['message']))), 0, 300) : '');
            Mazesta_Connect::write('config', $c);
        });
        MZC_Admin::back('mzc-access', 'ok:تنظیمات اشتراک ذخیره شد.');
    }

    /** Sets one account's plan: pro for N days from now (or from its end date when still running), free, or disabled/enabled. */
    public static function act_account()
    {
        MZC_Admin::guard('account_save', 'manage_options');
        $db = MZC_Crm_Db::db(); $id = isset($_POST['id']) ? (int) $_POST['id'] : 0;
        if (!$db || $id <= 0) { MZC_Admin::back('mzc-access', 'err:حساب پیدا نشد.'); }
        $a = MZC_Crm_Db::t('accounts'); $acc = $db->get_row($db->prepare("SELECT * FROM `$a` WHERE id = %d", $id), ARRAY_A);
        if (!$acc) { MZC_Admin::back('mzc-access', 'err:حساب پیدا نشد.'); }
        $do = isset($_POST['do']) ? sanitize_key(wp_unslash($_POST['do'])) : '';
        $days = isset($_POST['days']) ? max(0, min(3650, (int) $_POST['days'])) : 0;
        $set = array();
        if ($do === 'pro' && $days > 0) { $from = self::is_pro($acc) && !empty($acc['pro_until']) ? strtotime($acc['pro_until'] . ' UTC') : time(); $set = array('plan' => 'pro', 'pro_until' => gmdate('Y-m-d H:i:s', $from + $days * 86400)); }
        elseif ($do === 'free') { $set = array('plan' => 'free', 'pro_until' => null); }
        elseif ($do === 'disable') { $set = array('disabled' => 1); }
        elseif ($do === 'enable') { $set = array('disabled' => 0); }
        elseif ($do === 'note') { $set = array('note' => Mazesta_Connect::clip(isset($_POST['note']) ? wp_unslash($_POST['note']) : '', 250)); }
        if (!$set) { MZC_Admin::back('mzc-access', 'err:کاری انتخاب نشد.'); }
        $db->update($a, $set, array('id' => $id));
        MZC_Admin::back('mzc-access', 'ok:حساب به‌روز شد.');
    }
}
