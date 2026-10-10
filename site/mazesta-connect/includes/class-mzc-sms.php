<?php
if (!defined('ABSPATH')) { exit; }

/**
 * Sends the portal's login codes by SMS. Iranian gateways differ, so there are two drivers: Kavenegar (its REST API: a verify-lookup template, or a plain text message) and a
 * generic "web address" driver that calls any gateway which can be driven by one HTTP request (the mobile number, the text and the code are put into the address and the body).
 * The API key is kept sealed like the CRM's database password. Nothing is sent until a driver is chosen on the settings page.
 */
final class MZC_Sms
{
    public static function settings()
    {
        $c = Mazesta_Connect::config();
        $s = isset($c['sms']) && is_array($c['sms']) ? $c['sms'] : array();
        return $s + array('driver' => '', 'key' => '', 'sender' => '', 'template' => '', 'url' => '', 'method' => 'GET', 'body' => '', 'text' => 'کد ورود شما به سامانهٔ مازستا: {code}');
    }

    public static function configured() { $s = self::settings(); return $s['driver'] === 'kavenegar' ? $s['key'] !== '' : ($s['driver'] === 'url' && $s['url'] !== ''); }

    private static function api_key($s) { $raw = base64_decode((string) $s['key'], true); return $raw === false || strlen($raw) < 17 ? '' : (string) openssl_decrypt(substr($raw, 16), 'aes-256-cbc', hash('sha256', 'mzc|' . wp_salt('auth'), true), OPENSSL_RAW_DATA, substr($raw, 0, 16)); }

    /** Sends $text (for a login code also $code, the template's token) to $mobile; true, or an error text. */
    public static function send($mobile, $text, $code = '')
    {
        $s = self::settings();
        $args = array('timeout' => 15, 'redirection' => 0);
        if ($s['driver'] === 'kavenegar') {
            $key = self::api_key($s); if ($key === '') { return 'کلید API کاوه‌نگار ذخیره نشده است.'; }
            $base = 'https://api.kavenegar.com/v1/' . rawurlencode($key);
            if ($s['template'] !== '' && $code !== '') { $res = wp_remote_post($base . '/verify/lookup.json', $args + array('body' => array('receptor' => $mobile, 'token' => $code, 'template' => $s['template']))); }
            else { $body = array('receptor' => $mobile, 'message' => $text); if ($s['sender'] !== '') { $body['sender'] = $s['sender']; } $res = wp_remote_post($base . '/sms/send.json', $args + array('body' => $body)); }
            if (is_wp_error($res)) { return 'ارتباط با کاوه‌نگار برقرار نشد: ' . $res->get_error_message(); }
            $j = json_decode((string) wp_remote_retrieve_body($res), true);
            if (!is_array($j) || !isset($j['return']['status']) || (int) $j['return']['status'] !== 200) { return 'کاوه‌نگار نپذیرفت' . (isset($j['return']['message']) ? ': ' . $j['return']['message'] : '.'); }
            return true;
        }
        if ($s['driver'] === 'url' && $s['url'] !== '') {
            $fill = function ($t, $enc) use ($mobile, $text, $code) { return strtr($t, array('{mobile}' => $enc ? rawurlencode($mobile) : $mobile, '{message}' => $enc ? rawurlencode($text) : $text, '{code}' => $enc ? rawurlencode($code) : $code)); };
            $url = $fill($s['url'], true);
            if (!preg_match('#^https://#i', $url)) { return 'نشانی درگاه باید با https:// شروع شود.'; }
            if ($s['method'] === 'POST') { $res = wp_remote_post($url, $args + array('body' => $fill($s['body'], false), 'headers' => array('Content-Type' => strpos(ltrim($s['body']), '{') === 0 ? 'application/json' : 'application/x-www-form-urlencoded'))); }
            else { $res = wp_remote_get($url, $args); }
            if (is_wp_error($res)) { return 'ارتباط با درگاه برقرار نشد: ' . $res->get_error_message(); }
            $st = (int) wp_remote_retrieve_response_code($res);
            return $st >= 200 && $st < 300 ? true : 'درگاه با کد ' . $st . ' پاسخ داد.';
        }
        return 'پیامک در تنظیمات فعال نشده است.';
    }

    public static function act_save()
    {
        MZC_Admin::guard('sms_save', 'manage_options');
        Mazesta_Connect::config();
        $p = function ($k, $max = 500) { return Mazesta_Connect::clip(isset($_POST[$k]) ? wp_unslash($_POST[$k]) : '', $max); };
        Mazesta_Connect::locked(function () use ($p) {
            $c = Mazesta_Connect::read('config'); $old = isset($c['sms']) && is_array($c['sms']) ? $c['sms'] : array();
            $key = isset($_POST['key']) ? trim((string) wp_unslash($_POST['key'])) : '';
            $driver = in_array($p('driver', 12), array('kavenegar', 'url'), true) ? $p('driver', 12) : '';
            $text = isset($_POST['text']) ? mb_substr(trim(sanitize_textarea_field(wp_unslash($_POST['text']))), 0, 300) : '';
            $c['sms'] = array('driver' => $driver, 'key' => $key !== '' ? MZC_Crm_Db::seal($key) : (isset($old['key']) ? $old['key'] : ''), 'sender' => $p('sender', 30), 'template' => $p('template', 60),
                'url' => esc_url_raw($p('url', 500)), 'method' => $p('method', 4) === 'POST' ? 'POST' : 'GET', 'body' => isset($_POST['body']) ? mb_substr(trim((string) wp_unslash($_POST['body'])), 0, 1000) : '', 'text' => $text !== '' ? $text : 'کد ورود شما: {code}');
            Mazesta_Connect::write('config', $c);
        });
        MZC_Admin::back('mzc-settings', 'ok:تنظیمات پیامک ذخیره شد.');
    }

    public static function act_test()
    {
        MZC_Admin::guard('sms_test', 'manage_options');
        $m = MZC_Crm::mobile(isset($_POST['test_mobile']) ? wp_unslash($_POST['test_mobile']) : '');
        if ($m === '') { MZC_Admin::back('mzc-settings', 'err:شمارهٔ موبایل آزمایشی درست نیست.'); }
        $r = self::send($m, 'پیامک آزمایشی مازستا', '12345');
        MZC_Admin::back('mzc-settings', $r === true ? 'ok:پیامک آزمایشی فرستاده شد؛ به گوشی نگاه کنید.' : 'err:' . $r);
    }
}
