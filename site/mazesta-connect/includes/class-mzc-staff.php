<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The shop's own people (secretary, assembly, test, warehouse, service ...) and what each may do in the CRM. They are ordinary WordPress users - so passwords, resets and lock-outs are
 * WordPress' own - given the empty role "mzc_staff" (no right in WordPress itself, wp-admin sends them away) plus one or more of the roles defined here. A role is a name and a level
 * for each area (none / view / view and edit); a person with several roles gets the highest level of each area, so a secretary who also assembles just has both roles ticked.
 * Roles and who has them are set on the plugin's "کاربران و دسترسی" page. The apps (desktop, Android) log in with the user name and password at /staff/login, get a token, and every
 * request is checked against the user's current roles - taking a role away or switching a person off works at once, not at the next login.
 * Site administrators have every right everywhere.
 */
final class MZC_Staff
{
    const SESSION_DAYS = 14;
    const WP_ROLE = 'mzc_staff';

    /** The areas a level is given for. */
    public static $areas = array(
        'customers' => 'مشتریان', 'builds' => 'سیستم‌های نو (فروش و اسمبل)', 'jobs' => 'سرویس‌ها', 'tests' => 'گزارش و نتیجهٔ تست',
        'ships' => 'ارسال به گارانتی', 'stock' => 'انبار، کالاها و فاکتور خرید', 'payments' => 'قیمت‌ها و پرداخت‌ها', 'reports' => 'آمار و گزارش مالی', 'sms' => 'فرستادن پیامک',
        'accounting' => 'حسابداری (فاکتور فروش، پیش‌فاکتور، دریافت و پرداخت، چک)',
    );
    public static $levels = array(0 => 'ندارد', 1 => 'دیدن', 2 => 'دیدن و ویرایش');

    /** The roles a new site starts with (all can be changed or deleted on the page). */
    public static function default_roles()
    {
        return array(
            'reception' => array('label' => 'منشی', 'p' => array('customers' => 2, 'builds' => 2, 'jobs' => 2, 'tests' => 1, 'ships' => 1, 'stock' => 1, 'payments' => 2, 'sms' => 2)),
            'assembly' => array('label' => 'اسمبل', 'p' => array('customers' => 1, 'builds' => 2, 'jobs' => 1, 'tests' => 2, 'stock' => 1)),
            'test' => array('label' => 'تست', 'p' => array('customers' => 1, 'builds' => 1, 'jobs' => 1, 'tests' => 2)),
            'warehouse' => array('label' => 'انبار', 'p' => array('builds' => 1, 'jobs' => 1, 'ships' => 2, 'stock' => 2)),
            'service' => array('label' => 'سرویس', 'p' => array('customers' => 1, 'jobs' => 2, 'tests' => 2, 'ships' => 2, 'stock' => 1)),
            'accountant' => array('label' => 'حسابدار', 'p' => array('customers' => 2, 'builds' => 1, 'jobs' => 1, 'stock' => 2, 'payments' => 2, 'reports' => 1, 'accounting' => 2)),
        );
    }

    public static function boot()
    {
        add_action('rest_api_init', array(__CLASS__, 'routes'));
        add_action('admin_init', array(__CLASS__, 'keep_out'));
        foreach (array('staff_save', 'staff_roles_save', 'staff_cfg_save') as $a) { add_action('admin_post_mzc_' . $a, array(__CLASS__, 'act_' . $a)); }
    }

    public static function routes()
    {
        $open = '__return_true';
        register_rest_route(Mazesta_Connect::NS, '/staff/login', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_login'), 'permission_callback' => $open));
        register_rest_route(Mazesta_Connect::NS, '/staff/me', array('methods' => 'GET', 'callback' => array(__CLASS__, 'rest_me'), 'permission_callback' => $open));
        register_rest_route(Mazesta_Connect::NS, '/staff/logout', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_logout'), 'permission_callback' => $open));
    }

    /** Staff have no business in wp-admin; their tools are the apps. */
    public static function keep_out()
    {
        $u = wp_get_current_user();
        if ($u && $u->ID && in_array(self::WP_ROLE, (array) $u->roles, true) && !current_user_can('manage_options') && !(defined('DOING_AJAX') && DOING_AJAX)) { wp_safe_redirect(home_url('/')); exit; }
    }

    /* ---------- roles and levels ---------- */

    private static function clean_roles($roles)
    {
        $out = array();
        if (!is_array($roles)) { return $out; }
        foreach ($roles as $k => $r) {
            $k = sanitize_key((string) $k);
            if ($k === '' || strlen($k) > 24 || !is_array($r) || !isset($r['label'])) { continue; }
            $label = Mazesta_Connect::clip((string) $r['label'], 40);
            if ($label === '') { continue; }
            $p = array();
            foreach (self::$areas as $a => $_) { $v = isset($r['p'][$a]) ? (int) $r['p'][$a] : 0; if ($v > 0) { $p[$a] = min(2, $v); } }
            $out[$k] = array('label' => $label, 'p' => $p);
        }
        return $out;
    }

    /** The roles in use: the saved ones (read from the file, so a save is seen at once), else the defaults. */
    public static function roles()
    {
        $c = Mazesta_Connect::read('config');
        if (!isset($c['staffRoles']) || !is_array($c['staffRoles'])) { return self::default_roles(); }
        $roles = self::clean_roles($c['staffRoles']);
        // roles saved before the stock area existed (2.5.0) get its default level for the built-in roles, until the page saves them again
        if (empty($c['staffRolesStock'])) { foreach (self::default_roles() as $k => $r) { if (isset($roles[$k], $r['p']['stock']) && !isset($roles[$k]['p']['stock'])) { $roles[$k]['p']['stock'] = $r['p']['stock']; } } }
        // roles saved before the accounting (2.9.0) get the accountant role, until the page saves them again (nobody else gets the new area by itself)
        if (empty($c['staffRolesAcc']) && !isset($roles['accountant'])) { $d = self::default_roles(); $roles['accountant'] = $d['accountant']; }
        return $roles;
    }

    public static function save_roles($roles)
    {
        $roles = self::clean_roles($roles);
        Mazesta_Connect::config();
        Mazesta_Connect::locked(function () use ($roles) { $c = Mazesta_Connect::read('config'); $c['staffRoles'] = $roles; $c['staffRolesStock'] = 1; $c['staffRolesAcc'] = 1; Mazesta_Connect::write('config', $c); });
    }

    /** What the user's record says: array(roles => keys, on => bool). */
    public static function record($uid)
    {
        $m = get_user_meta($uid, 'mzc_staff', true);
        $m = is_array($m) ? $m : array();
        $roles = isset($m['roles']) && is_array($m['roles']) ? array_values(array_filter(array_map('sanitize_key', $m['roles']))) : array();
        return array('roles' => $roles, 'on' => !empty($m['on']));
    }

    public static function is_admin($uid) { return user_can($uid, 'manage_options'); }

    /** The user's level in each area (admins: edit everywhere; a switched-off person: nothing). */
    public static function perms_of($uid)
    {
        $out = array_fill_keys(array_keys(self::$areas), 0);
        if (self::is_admin($uid)) { return array_fill_keys(array_keys(self::$areas), 2); }
        $rec = self::record($uid); if (!$rec['on']) { return $out; }
        $roles = self::roles();
        foreach ($rec['roles'] as $k) { if (isset($roles[$k])) { foreach ($roles[$k]['p'] as $a => $v) { if (isset($out[$a]) && $v > $out[$a]) { $out[$a] = (int) $v; } } } }
        return $out;
    }

    public static function can($uid, $area, $need = 1) { $p = self::perms_of($uid); return isset($p[$area]) && $p[$area] >= $need; }

    /** Every user who has been given a staff record, newest first. */
    public static function users()
    {
        $us = get_users(array('meta_key' => 'mzc_staff', 'meta_compare' => 'EXISTS', 'orderby' => 'ID', 'order' => 'DESC'));
        return is_array($us) ? $us : array();
    }

    /* ---------- the apps' session ---------- */

    private static function ok($d = array(), $st = 200) { return Mazesta_Connect::fresh($d, $st); }
    private static function fail($msg, $st = 400) { return Mazesta_Connect::fresh(array('error' => $msg), $st); }
    private static function hash($s) { return hash_hmac('sha256', (string) $s, wp_salt('auth')); }
    private static function now() { return gmdate('Y-m-d H:i:s'); }

    private static function summary($u)
    {
        $rec = self::record($u->ID); $roles = self::roles(); $names = array();
        foreach ($rec['roles'] as $k) { if (isset($roles[$k])) { $names[] = array('key' => $k, 'label' => $roles[$k]['label']); } }
        return array('id' => (int) $u->ID, 'username' => $u->user_login, 'name' => $u->display_name !== '' ? $u->display_name : $u->user_login, 'admin' => self::is_admin($u->ID), 'roles' => $names,
            'perms' => self::perms_of($u->ID), 'areas' => self::$areas);
    }

    public static function rest_login($req)
    {
        $db = MZC_Crm_Db::db();
        if (!$db || !MZC_Crm_Db::ready()) { return self::fail('سامانه فعلاً در دسترس نیست.', 503); }
        if (!Mazesta_Connect::allowed('slogin', 60)) { return self::fail('درخواست‌ها زیاد بود؛ کمی بعد دوباره امتحان کنید.', 429); }
        $p = $req->get_json_params();
        $login = is_array($p) && isset($p['username']) ? trim((string) $p['username']) : '';
        $pass = is_array($p) && isset($p['password']) ? (string) $p['password'] : '';
        if ($login === '' || $pass === '' || strlen($login) > 100 || strlen($pass) > 200) { return self::fail('نام کاربری و گذرواژه را بنویسید.'); }
        $u = self::check_login($login, $pass);
        if (!($u instanceof WP_User)) { return self::fail($u[0], $u[1]); }
        $token = self::new_session($u->ID, is_array($p) && isset($p['device']) ? (string) $p['device'] : '', self::SESSION_DAYS);
        if ($token === '') { return self::fail('سامانه فعلاً در دسترس نیست.', 503); }
        return self::ok(array('token' => $token, 'days' => self::SESSION_DAYS) + self::summary($u));
    }

    /**
     * The staff member a user name and password belong to, or array(error text, HTTP status). Six wrong tries on one name lock that name for fifteen minutes, whoever tries
     * (a per-address limit does not stop one name being guessed from many addresses); a right password of a person with no right in the CRM is refused too.
     */
    public static function check_login($login, $pass)
    {
        $lock = 'login:' . md5(strtolower($login)); $n = Mazesta_Connect::hit($lock, 900, 0);
        if ($n >= 6) { return array('چند بار گذرواژه غلط بود؛ ۱۵ دقیقه دیگر دوباره امتحان کنید.', 429); }
        $u = wp_authenticate($login, $pass);
        if (is_wp_error($u) || !($u instanceof WP_User)) { Mazesta_Connect::hit($lock, 900); return array('نام کاربری یا گذرواژه درست نیست.', 401); }
        if (!array_filter(self::perms_of($u->ID))) { return array('این حساب دسترسی به برنامه ندارد؛ از مدیر بخواهید دسترسی بدهد.', 403); }
        Mazesta_Connect::unhit($lock);
        return $u;
    }

    /** A new app session (the token goes back to the app once; only its hash is kept). */
    public static function new_session($uid, $device, $days)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return ''; }
        $db->query($db->prepare('DELETE FROM `' . MZC_Crm_Db::t('sessions') . '` WHERE expires < %s', self::now()));
        $token = bin2hex(random_bytes(32));
        $ok = $db->insert(MZC_Crm_Db::t('sessions'), array('token_hash' => self::hash($token), 'customer_id' => 0, 'user_id' => (int) $uid, 'device' => Mazesta_Connect::clip((string) $device, 80),
            'expires' => gmdate('Y-m-d H:i:s', time() + (int) round($days * 86400)), 'created' => self::now()));
        return $ok ? $token : '';
    }

    /**
     * The signed-in staff member of a request, or a ready error response (check with `instanceof WP_REST_Response`). With $area the person must have at least level $need there.
     * The roles are read now, so a change on the page applies to the very next request.
     */
    public static function auth($req, $area = '', $need = 1)
    {
        $db = MZC_Crm_Db::db(); $t = (string) $req->get_header('x_mzc_token');
        if (!$db || !preg_match('/^[a-f0-9]{64}$/', $t)) { return self::fail('نشست شما تمام شده؛ دوباره وارد شوید.', 401); }
        $uid = (int) $db->get_var($db->prepare('SELECT user_id FROM `' . MZC_Crm_Db::t('sessions') . '` WHERE token_hash = %s AND expires > %s AND user_id IS NOT NULL', self::hash($t), self::now()));
        $u = $uid ? get_userdata($uid) : false;
        if (!$u || !array_filter(self::perms_of($uid))) { return self::fail('نشست شما تمام شده؛ دوباره وارد شوید.', 401); }
        if ($area !== '' && !self::can($uid, $area, $need)) { return self::fail('دسترسی این کار را ندارید.', 403); }
        return array('id' => $uid, 'user' => $u);
    }

    public static function rest_me($req)
    {
        $a = self::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        return self::ok(self::summary($a['user']));
    }

    public static function rest_logout($req)
    {
        $db = MZC_Crm_Db::db(); $t = (string) $req->get_header('x_mzc_token');
        if ($db && preg_match('/^[a-f0-9]{64}$/', $t)) { $db->delete(MZC_Crm_Db::t('sessions'), array('token_hash' => self::hash($t))); }
        return self::ok(array('ok' => true));
    }

    /** Ends every app session of a user (switched off, or the password changed), the AI apps' sign-ins with them. */
    public static function end_sessions($uid)
    {
        $db = MZC_Crm_Db::db(); if ($db) { $db->delete(MZC_Crm_Db::t('sessions'), array('user_id' => (int) $uid)); $db->delete(MZC_Crm_Db::t('oauth_refresh'), array('user_id' => (int) $uid)); }
    }

    /* ---------- the page's actions ---------- */

    public static function act_staff_roles_save()
    {
        MZC_Admin::guard('staff_roles_save', 'manage_options');
        $in = isset($_POST['roles']) && is_array($_POST['roles']) ? wp_unslash($_POST['roles']) : array();
        $keep = array();
        foreach ($in as $k => $r) { if (!empty($r['del'])) { continue; } $keep[$k] = $r; }
        // a role made on the page: the "new" row has a name but no key yet
        if (isset($_POST['new_label']) && Mazesta_Connect::clip((string) wp_unslash($_POST['new_label']), 40) !== '') {
            $np = isset($_POST['new_p']) && is_array($_POST['new_p']) ? wp_unslash($_POST['new_p']) : array();
            $keep['r' . dechex(time())] = array('label' => (string) wp_unslash($_POST['new_label']), 'p' => $np);
        }
        self::save_roles($keep);
        MZC_Admin::back('mzc-staff', 'ok:نقش‌ها ذخیره شد. تغییر دسترسی از درخواست بعدی هر کاربر اعمال می‌شود.');
    }

    /** The receipt SMS text, the page the customer follows their case on, and which fields each label prints. */
    public static function act_staff_cfg_save()
    {
        MZC_Admin::guard('staff_cfg_save', 'manage_options');
        $labels = array();
        foreach (MZC_Api::$label_fields as $kind => $f) {
            $want = isset($_POST['label'][$kind]) && is_array($_POST['label'][$kind]) ? array_keys($_POST['label'][$kind]) : array();
            $ord = isset($_POST['lord'][$kind]) && is_array($_POST['lord'][$kind]) ? $_POST['lord'][$kind] : array(); $keys = array();
            foreach ($want as $k) { $k = sanitize_key((string) $k); if (isset($f[$k])) { $keys[$k] = isset($ord[$k]) ? (int) $ord[$k] : 50; } }
            asort($keys); $labels[$kind] = $keys ? array_keys($keys) : MZC_Api::$label_default[$kind];
        }
        $text = isset($_POST['receipt']) ? mb_substr(trim(sanitize_textarea_field(wp_unslash($_POST['receipt']))), 0, 300) : '';
        $url = isset($_POST['portalUrl']) ? esc_url_raw(trim((string) wp_unslash($_POST['portalUrl']))) : '';
        Mazesta_Connect::config();
        Mazesta_Connect::locked(function () use ($labels, $text, $url) { $c = Mazesta_Connect::read('config'); $c['staffCfg'] = array('receipt' => $text !== '' ? $text : MZC_Api::RECEIPT_TEXT, 'portalUrl' => $url, 'labels' => $labels); Mazesta_Connect::write('config', $c); });
        MZC_Admin::back('mzc-staff', 'ok:تنظیم رسید و برچسب ذخیره شد.');
    }

    public static function act_staff_save()
    {
        MZC_Admin::guard('staff_save', 'manage_options');
        $login = isset($_POST['username']) ? trim(sanitize_text_field(wp_unslash($_POST['username']))) : '';
        $uid = isset($_POST['uid']) ? (int) $_POST['uid'] : 0;
        $pass = isset($_POST['password']) ? (string) wp_unslash($_POST['password']) : '';
        $name = Mazesta_Connect::clip(isset($_POST['display_name']) ? (string) wp_unslash($_POST['display_name']) : '', 80);
        $roles = isset($_POST['roles']) && is_array($_POST['roles']) ? array_values(array_intersect(array_map('sanitize_key', array_keys($_POST['roles'])), array_keys(self::roles()))) : array();
        if ($pass !== '' && strlen($pass) < 8) { MZC_Admin::back('mzc-staff', 'err:گذرواژه دست‌کم ۸ نویسه باشد.'); }
        if (!get_role(self::WP_ROLE)) { add_role(self::WP_ROLE, 'کارمند مازستا', array()); }
        if (!$uid && $login !== '') {
            $ex = get_user_by('login', $login);
            if ($ex) { $uid = (int) $ex->ID; }
            else {
                if (!validate_username($login)) { MZC_Admin::back('mzc-staff', 'err:نام کاربری فقط حرف لاتین، عدد و نقطه و خط زیر داشته باشد.'); }
                if (strlen($pass) < 8) { MZC_Admin::back('mzc-staff', 'err:برای کاربر تازه گذرواژه (دست‌کم ۸ نویسه) بنویسید.'); }
                $uid = wp_insert_user(array('user_login' => $login, 'user_pass' => $pass, 'display_name' => $name !== '' ? $name : $login, 'role' => self::WP_ROLE));
                if (is_wp_error($uid)) { MZC_Admin::back('mzc-staff', 'err:' . $uid->get_error_message()); }
                $pass = '';
            }
        }
        $u = $uid ? get_userdata($uid) : false;
        if (!$u) { MZC_Admin::back('mzc-staff', 'err:کاربر پیدا نشد.'); }
        if (!empty($_POST['remove'])) {
            delete_user_meta($uid, 'mzc_staff'); self::end_sessions($uid);
            MZC_Admin::back('mzc-staff', 'ok:دسترسی «' . $u->user_login . '» به CRM برداشته شد (حساب وردپرسی‌اش پاک نشد).');
        }
        $on = !empty($_POST['on']) || (!isset($_POST['uid']) || !$_POST['uid']);
        update_user_meta($uid, 'mzc_staff', array('roles' => $roles, 'on' => $on));
        $upd = array('ID' => $uid);
        if ($name !== '') { $upd['display_name'] = $name; }
        if ($pass !== '') { $upd['user_pass'] = $pass; }
        if (count($upd) > 1) { wp_update_user($upd); }
        if ($pass !== '' || !$on) { self::end_sessions($uid); }
        MZC_Admin::back('mzc-staff', 'ok:ذخیره شد.');
    }
}
