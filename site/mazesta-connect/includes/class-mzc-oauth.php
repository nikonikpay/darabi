<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The AI apps' sign-in (ChatGPT, Claude): OAuth 2.1 as the MCP specification asks for it - discovery documents under /.well-known/, dynamic client registration, the
 * authorization-code grant with PKCE (S256 only) and refresh tokens. The person signs in on the site's own page with the same user name and password as in the staff program,
 * so the AI app never sees the password; the access token it gets is an ordinary staff session (see MZC_Staff::new_session), so every MCP call is checked against that person's
 * roles at the moment it runs, and switching the person off or changing the password ends the AI app's access too (MZC_Staff::end_sessions).
 * There are no cookies here and nothing is approved without typing the password, so a forged link can do no more than show the sign-in page.
 */
final class MZC_Oauth
{
    const SCOPE = 'crm';
    const ACCESS_DAYS = 1;
    const REFRESH_DAYS = 60;

    public static function boot()
    {
        add_action('rest_api_init', array(__CLASS__, 'routes'));
        add_action('init', array(__CLASS__, 'well_known'), 0);
    }

    public static function routes()
    {
        $open = '__return_true';
        register_rest_route(Mazesta_Connect::NS, '/oauth/register', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_register'), 'permission_callback' => $open));
        register_rest_route(Mazesta_Connect::NS, '/oauth/authorize', array('methods' => array('GET', 'POST'), 'callback' => array(__CLASS__, 'rest_authorize'), 'permission_callback' => $open));
        register_rest_route(Mazesta_Connect::NS, '/oauth/token', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_token'), 'permission_callback' => $open));
    }

    public static function issuer() { return rtrim(home_url(), '/'); }
    public static function resource() { return rest_url(Mazesta_Connect::NS . '/mcp'); }
    private static function t($n) { return MZC_Crm_Db::t($n); }
    private static function hash($s) { return hash_hmac('sha256', (string) $s, wp_salt('auth') . '|oauth'); }
    private static function now() { return gmdate('Y-m-d H:i:s'); }
    private static function b64url($bin) { return rtrim(strtr(base64_encode($bin), '+/', '-_'), '='); }

    /** The two discovery documents: where the MCP endpoint's sign-in is, and what this sign-in can do. Served on WordPress' first hook, before any theme. */
    public static function well_known()
    {
        $path = (string) parse_url(isset($_SERVER['REQUEST_URI']) ? (string) $_SERVER['REQUEST_URI'] : '', PHP_URL_PATH);
        if (strpos($path, '/.well-known/') === false) { return; }
        $doc = null;
        if (preg_match('#/\.well-known/oauth-protected-resource(/|$)#', $path)) {
            $doc = array('resource' => self::resource(), 'authorization_servers' => array(self::issuer()), 'scopes_supported' => array(self::SCOPE), 'bearer_methods_supported' => array('header'), 'resource_name' => get_bloginfo('name') . ' CRM');
        } elseif (preg_match('#/\.well-known/(oauth-authorization-server|openid-configuration)(/|$)#', $path)) {
            $doc = array('issuer' => self::issuer(), 'authorization_endpoint' => rest_url(Mazesta_Connect::NS . '/oauth/authorize'), 'token_endpoint' => rest_url(Mazesta_Connect::NS . '/oauth/token'),
                'registration_endpoint' => rest_url(Mazesta_Connect::NS . '/oauth/register'), 'response_types_supported' => array('code'), 'grant_types_supported' => array('authorization_code', 'refresh_token'),
                'code_challenge_methods_supported' => array('S256'), 'token_endpoint_auth_methods_supported' => array('none', 'client_secret_post', 'client_secret_basic'), 'scopes_supported' => array(self::SCOPE));
        }
        if ($doc === null) { return; }
        status_header(200); nocache_headers();
        header('Content-Type: application/json; charset=utf-8'); header('Access-Control-Allow-Origin: *'); header('X-Content-Type-Options: nosniff');
        echo wp_json_encode($doc, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE); exit;
    }

    /** An OAuth error answer (RFC 6749 §5.2), never cached. */
    private static function err($code, $desc, $status = 400)
    {
        $r = Mazesta_Connect::fresh(array('error' => $code, 'error_description' => $desc), $status);
        $r->header('Pragma', 'no-cache'); return $r;
    }

    /* ---------- dynamic client registration (RFC 7591) ---------- */

    /** Only https addresses (or the PC itself, for desktop clients), no fragment: where the code is sent back to. */
    private static function redirect_ok($u)
    {
        if (!is_string($u) || strlen($u) > 500 || strpos($u, '#') !== false) { return false; }
        $p = parse_url($u);
        if (!$p || empty($p['scheme']) || empty($p['host'])) { return false; }
        return $p['scheme'] === 'https' || ($p['scheme'] === 'http' && in_array($p['host'], array('localhost', '127.0.0.1', '[::1]'), true));
    }

    public static function rest_register($req)
    {
        $db = MZC_Crm_Db::db(); if (!$db || !MZC_Crm_Db::ready()) { return self::err('temporarily_unavailable', 'The CRM database is not ready.', 503); }
        if (!Mazesta_Connect::allowed('oreg', 30)) { return self::err('slow_down', 'Too many registrations; try again later.', 429); }
        $p = $req->get_json_params(); $p = is_array($p) ? $p : array();
        $uris = isset($p['redirect_uris']) && is_array($p['redirect_uris']) ? array_values(array_unique(array_filter($p['redirect_uris'], 'is_string'))) : array();
        if (!$uris || count($uris) > 10) { return self::err('invalid_redirect_uri', 'redirect_uris is required (at most 10).'); }
        foreach ($uris as $u) { if (!self::redirect_ok($u)) { return self::err('invalid_redirect_uri', 'Only https redirect addresses (or http on localhost) are accepted: ' . $u); } }
        $method = isset($p['token_endpoint_auth_method']) && in_array($p['token_endpoint_auth_method'], array('client_secret_post', 'client_secret_basic'), true) ? $p['token_endpoint_auth_method'] : 'none';
        $name = Mazesta_Connect::clip(isset($p['client_name']) && is_string($p['client_name']) ? $p['client_name'] : '', 120);
        $id = bin2hex(random_bytes(16)); $secret = $method === 'none' ? '' : bin2hex(random_bytes(24));
        $ok = $db->insert(self::t('oauth_clients'), array('client_id' => $id, 'secret_hash' => $secret === '' ? null : self::hash($secret), 'name' => $name !== '' ? $name : 'AI app', 'redirect_uris' => wp_json_encode($uris, JSON_UNESCAPED_SLASHES), 'created' => self::now()));
        if (!$ok) { return self::err('server_error', 'The client could not be saved.', 500); }
        $out = array('client_id' => $id, 'client_id_issued_at' => time(), 'client_name' => $name !== '' ? $name : 'AI app', 'redirect_uris' => $uris, 'token_endpoint_auth_method' => $method,
            'grant_types' => array('authorization_code', 'refresh_token'), 'response_types' => array('code'), 'scope' => self::SCOPE);
        if ($secret !== '') { $out['client_secret'] = $secret; $out['client_secret_expires_at'] = 0; }
        return Mazesta_Connect::fresh($out, 201);
    }

    private static function client($id)
    {
        $db = MZC_Crm_Db::db(); if (!$db || !is_string($id) || !preg_match('/^[a-f0-9]{32}$/', $id)) { return null; }
        $c = $db->get_row($db->prepare('SELECT * FROM `' . self::t('oauth_clients') . '` WHERE client_id = %s', $id), ARRAY_A);
        if ($c) { $c['uris'] = json_decode((string) $c['redirect_uris'], true) ?: array(); }
        return $c;
    }

    /* ---------- the sign-in page ---------- */

    public static function rest_authorize($req)
    {
        $src = $req->get_method() === 'POST' ? $req->get_body_params() : $req->get_query_params();
        $g = function ($k, $max = 500) use ($src) { return isset($src[$k]) && is_string($src[$k]) ? substr(trim($src[$k]), 0, $max) : ''; };
        $q = array('client_id' => $g('client_id', 40), 'redirect_uri' => $g('redirect_uri'), 'state' => $g('state', 500), 'code_challenge' => $g('code_challenge', 128), 'code_challenge_method' => $g('code_challenge_method', 10),
            'response_type' => $g('response_type', 10), 'scope' => $g('scope', 100), 'resource' => $g('resource'));
        $db = MZC_Crm_Db::db();
        if (!$db || !MZC_Crm_Db::ready()) { self::page('سامانه فعلاً در دسترس نیست', '<p>پایگاه دادهٔ CRM آماده نیست؛ بعداً دوباره امتحان کنید.</p>'); }
        $c = self::client($q['client_id']);
        // a wrong client or address is shown here, never sent anywhere (it could be anyone's address)
        if (!$c || !in_array($q['redirect_uri'], $c['uris'], true)) { self::page('درخواست نامعتبر', '<p>برنامه‌ای که شما را به اینجا فرستاده ثبت نشده یا نشانی برگشتش درست نیست. اتصال را در همان برنامه از نو بسازید.</p>'); }
        $back = function ($args) use ($q) { wp_redirect($q['redirect_uri'] . (strpos($q['redirect_uri'], '?') === false ? '?' : '&') . http_build_query($args + ($q['state'] !== '' ? array('state' => $q['state']) : array()) + array('iss' => self::issuer())), 302); exit; };
        if ($q['response_type'] !== 'code') { $back(array('error' => 'unsupported_response_type')); }
        if ($q['code_challenge_method'] !== 'S256' || !preg_match('/^[A-Za-z0-9_-]{43,128}$/', $q['code_challenge'])) { $back(array('error' => 'invalid_request', 'error_description' => 'PKCE with S256 is required.')); }
        $msg = '';
        if ($req->get_method() === 'POST') {
            if (!empty($src['deny'])) { $back(array('error' => 'access_denied')); }
            $login = $g('username', 100); $pass = isset($src['password']) && is_string($src['password']) ? substr($src['password'], 0, 200) : '';
            if (!Mazesta_Connect::allowed('ologin', 60)) { $msg = 'درخواست‌ها زیاد بود؛ کمی بعد دوباره امتحان کنید.'; }
            elseif ($login === '' || $pass === '') { $msg = 'نام کاربری و گذرواژه را بنویسید.'; }
            else {
                $u = MZC_Staff::check_login($login, $pass);
                if ($u instanceof WP_User) {
                    $code = bin2hex(random_bytes(32));
                    $db->query($db->prepare('DELETE FROM `' . self::t('oauth_codes') . '` WHERE expires < %s', self::now()));
                    $db->insert(self::t('oauth_codes'), array('code_hash' => self::hash($code), 'client_id' => $c['client_id'], 'user_id' => (int) $u->ID, 'redirect_uri' => $q['redirect_uri'], 'challenge' => $q['code_challenge'], 'expires' => gmdate('Y-m-d H:i:s', time() + 600)));
                    $back(array('code' => $code));
                }
                $msg = $u[0];
            }
        }
        $hidden = ''; foreach ($q as $k => $v) { $hidden .= '<input type="hidden" name="' . esc_attr($k) . '" value="' . esc_attr($v) . '">'; }
        $host = (string) parse_url($q['redirect_uri'], PHP_URL_HOST);
        self::page('اتصال ' . $c['name'] . ' به CRM مازستا',
            '<p><b dir="auto">' . esc_html($c['name']) . '</b> <span class="lat">(' . esc_html($host) . ')</span> می‌خواهد با دسترسی <b>شما</b> به CRM وصل شود: مشتری‌ها، سیستم‌ها، سرویس‌ها و انبار را ببیند و ثبت کند، به همان اندازه‌ای که نقش شما اجازه می‌دهد.</p>'
            . ($msg !== '' ? '<p class="err" role="alert">' . esc_html($msg) . '</p>' : '')
            . '<form method="post" action="' . esc_url(rest_url(Mazesta_Connect::NS . '/oauth/authorize')) . '">' . $hidden
            . '<label>نام کاربری<input name="username" dir="ltr" autocomplete="username" autocapitalize="off" required autofocus value="' . esc_attr($g('username', 100)) . '"></label>'
            . '<label>گذرواژه<input name="password" type="password" dir="ltr" autocomplete="current-password" required></label>'
            . '<div class="row"><button class="primary" type="submit">ورود و اجازه</button><button type="submit" name="deny" value="1" formnovalidate>اجازه نمی‌دهم</button></div></form>'
            . '<p class="muted">هر وقت بخواهید، مدیر سایت با خاموش کردن حساب یا عوض کردن گذرواژهٔ شما این اتصال را قطع می‌کند.</p>');
    }

    private static function page($title, $body)
    {
        status_header(200); nocache_headers();
        header('Content-Type: text/html; charset=utf-8'); header('X-Frame-Options: DENY'); header("Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; form-action 'self' https: http://localhost:* http://127.0.0.1:*; frame-ancestors 'none'"); header('Referrer-Policy: no-referrer');
        echo '<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>' . esc_html($title) . '</title><style>'
            . 'body{margin:0;background:#f3f4f6;font:15px/1.7 Tahoma,Segoe UI,sans-serif;color:#111}main{max-width:420px;margin:6vh auto;padding:28px;background:#fff;border-radius:14px;box-shadow:0 2px 14px #0001}'
            . 'h1{font-size:18px;margin:0 0 12px}label{display:block;margin:12px 0}input{display:block;width:100%;box-sizing:border-box;margin-top:4px;padding:10px;border:1px solid #ccc;border-radius:8px;font:inherit}'
            . '.row{display:flex;gap:8px;margin-top:16px}button{flex:1;padding:10px;border-radius:8px;border:1px solid #ccc;background:#fff;font:inherit;cursor:pointer}button.primary{background:#111;color:#fff;border-color:#111}'
            . '.err{color:#b91c1c;background:#fee2e2;padding:8px 10px;border-radius:8px}.muted{color:#6b7280;font-size:13px}.lat{direction:ltr;unicode-bidi:isolate}'
            . '</style></head><body><main><h1>' . esc_html($title) . '</h1>' . $body . '</main></body></html>';
        exit;
    }

    /* ---------- the token endpoint ---------- */

    public static function rest_token($req)
    {
        $db = MZC_Crm_Db::db(); if (!$db || !MZC_Crm_Db::ready()) { return self::err('temporarily_unavailable', 'The CRM database is not ready.', 503); }
        $p = $req->get_body_params(); if (!$p) { $j = $req->get_json_params(); $p = is_array($j) ? $j : array(); }
        $g = function ($k) use ($p) { return isset($p[$k]) && is_string($p[$k]) ? trim($p[$k]) : ''; };
        // the client: in the body, or in a Basic header (client_secret_basic)
        $cid = $g('client_id'); $secret = $g('client_secret');
        $basic = (string) $req->get_header('authorization');
        if (stripos($basic, 'basic ') === 0) { $pair = explode(':', (string) base64_decode(substr($basic, 6)), 2); if (count($pair) === 2) { $cid = rawurldecode($pair[0]); $secret = rawurldecode($pair[1]); } }
        $c = self::client($cid);
        if (!$c || ($c['secret_hash'] !== null && !hash_equals((string) $c['secret_hash'], self::hash($secret)))) { return self::err('invalid_client', 'Unknown client or wrong secret.', 401); }
        $type = $g('grant_type');
        if ($type === 'authorization_code') {
            $code = $g('code'); $row = preg_match('/^[a-f0-9]{64}$/', $code) ? $db->get_row($db->prepare('SELECT * FROM `' . self::t('oauth_codes') . '` WHERE code_hash = %s', self::hash($code)), ARRAY_A) : null;
            if ($row) { $db->delete(self::t('oauth_codes'), array('code_hash' => $row['code_hash'])); }   // a code works once, right or wrong
            if (!$row || $row['client_id'] !== $c['client_id'] || $row['expires'] < self::now() || $row['redirect_uri'] !== $g('redirect_uri')) { return self::err('invalid_grant', 'The code is wrong, used or expired.'); }
            if (!hash_equals((string) $row['challenge'], self::b64url(hash('sha256', $g('code_verifier'), true)))) { return self::err('invalid_grant', 'PKCE check failed.'); }
            return self::issue($c, (int) $row['user_id']);
        }
        if ($type === 'refresh_token') {
            $rt = $g('refresh_token'); $row = preg_match('/^[a-f0-9]{64}$/', $rt) ? $db->get_row($db->prepare('SELECT * FROM `' . self::t('oauth_refresh') . '` WHERE token_hash = %s', self::hash($rt)), ARRAY_A) : null;
            if (!$row || $row['client_id'] !== $c['client_id'] || $row['expires'] < self::now()) { return self::err('invalid_grant', 'The refresh token is wrong or expired.'); }
            $db->delete(self::t('oauth_refresh'), array('token_hash' => $row['token_hash']));   // rotated: each refresh token works once
            return self::issue($c, (int) $row['user_id']);
        }
        return self::err('unsupported_grant_type', 'Only authorization_code and refresh_token.');
    }

    /** An access token (a staff session of the person) and a new refresh token, if the person may still use the CRM. */
    private static function issue($c, $uid)
    {
        $db = MZC_Crm_Db::db(); $u = get_userdata($uid);
        if (!$u || !array_filter(MZC_Staff::perms_of($uid))) { return self::err('invalid_grant', 'This person has no access to the CRM any more.'); }
        $access = MZC_Staff::new_session($uid, 'ai: ' . $c['name'], self::ACCESS_DAYS);
        $refresh = bin2hex(random_bytes(32));
        $db->query($db->prepare('DELETE FROM `' . self::t('oauth_refresh') . '` WHERE expires < %s', self::now()));
        $ok = $access !== '' && $db->insert(self::t('oauth_refresh'), array('token_hash' => self::hash($refresh), 'client_id' => $c['client_id'], 'user_id' => $uid, 'expires' => gmdate('Y-m-d H:i:s', time() + self::REFRESH_DAYS * 86400), 'created' => self::now()));
        if (!$ok) { return self::err('server_error', 'The token could not be saved.', 500); }
        $r = Mazesta_Connect::fresh(array('access_token' => $access, 'token_type' => 'Bearer', 'expires_in' => self::ACCESS_DAYS * 86400, 'refresh_token' => $refresh, 'scope' => self::SCOPE));
        $r->header('Pragma', 'no-cache'); return $r;
    }

    /** Who has an AI app connected now (live refresh tokens), for the Users and access page: user id => array(app names). */
    public static function connected()
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return array(); }
        $rows = $db->get_results($db->prepare('SELECT r.user_id, c.name FROM `' . self::t('oauth_refresh') . '` r JOIN `' . self::t('oauth_clients') . '` c ON c.client_id = r.client_id WHERE r.expires > %s', self::now()), ARRAY_A) ?: array();
        $out = array(); foreach ($rows as $r) { $out[(int) $r['user_id']][$r['name']] = true; }
        return array_map('array_keys', $out);
    }
}
