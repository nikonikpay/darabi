<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The plugin's own log, for finding faults on the live site: one file a day in the data folder (logs/plugin-YYYY-MM-DD.log.php, behind the same guard line as the other
 * data files, so it is never served), kept LOG_DAYS days. A line is: time (UTC), level, area, message and a JSON context. Written by itself:
 *   - every answer of the plugin's REST routes that failed (4xx and 5xx, without the request's body or keys) or was slow (over SLOW_MS);
 *   - a PHP fatal error raised in the plugin's own files;
 *   - what the code reports with info / warn / error (database writes that failed, restores, releases ...).
 * Read on the Settings page (the last lines, filtered by level and text) and downloadable as text.
 */
final class MZC_Log
{
    const LOG_DAYS = 14;
    const SLOW_MS = 3000;
    const MAX_LINE = 4000;
    private static $t0 = 0.0;

    public static function boot()
    {
        self::$t0 = microtime(true);
        add_filter('rest_post_dispatch', array(__CLASS__, 'rest_done'), 99, 3);
        register_shutdown_function(array(__CLASS__, 'shutdown'));
        add_action('admin_post_mzc_log_download', array(__CLASS__, 'act_download'));
    }

    public static function info($area, $msg, $ctx = array()) { self::write('INFO', $area, $msg, $ctx); }
    public static function warn($area, $msg, $ctx = array()) { self::write('WARN', $area, $msg, $ctx); }
    public static function error($area, $msg, $ctx = array()) { self::write('ERROR', $area, $msg, $ctx); }

    private static function dir() { return Mazesta_Connect::dir() . '/logs'; }
    private static function file_of($day) { return self::dir() . '/plugin-' . $day . '.log.php'; }

    public static function write($level, $area, $msg, $ctx = array())
    {
        $d = self::dir();
        if (!is_dir($d) && (!Mazesta_Connect::ensure() || !wp_mkdir_p($d))) { return; }
        $f = self::file_of(gmdate('Y-m-d'));
        $line = gmdate('H:i:s') . ' ' . $level . ' [' . $area . '] ' . str_replace(array("\r", "\n"), ' ', (string) $msg) . ($ctx ? ' ' . wp_json_encode($ctx, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES) : '');
        if (strlen($line) > self::MAX_LINE) { $line = substr($line, 0, self::MAX_LINE) . '…'; }
        $new = !is_file($f);
        @file_put_contents($f, ($new ? Mazesta_Connect::GUARD : '') . $line . "\n", FILE_APPEND | LOCK_EX);
        if ($new) { self::prune(); }
    }

    private static function prune()
    {
        $keep = gmdate('Y-m-d', time() - self::LOG_DAYS * 86400);
        foreach ((array) glob(self::dir() . '/plugin-*.log.php') as $f) { if (substr(basename($f), 7, 10) < $keep) { @unlink($f); } }
    }

    /** The plugin's REST answers that failed or took long, with the route, who asked (a staff user id when signed in) and the time it took. */
    public static function rest_done($res, $server, $req)
    {
        $route = (string) $req->get_route();
        if (strpos($route, '/' . Mazesta_Connect::NS . '/') !== 0 || !($res instanceof WP_HTTP_Response)) { return $res; }
        $st = (int) $res->get_status(); $ms = (int) round((microtime(true) - self::$t0) * 1000);
        if ($st < 400 && $ms < self::SLOW_MS) { return $res; }
        $data = $res->get_data(); $err = is_array($data) ? (isset($data['error']) ? $data['error'] : (isset($data['message']) ? $data['message'] : '')) : '';
        $ctx = array('status' => $st, 'ms' => $ms, 'method' => $req->get_method(), 'ip' => self::ip());
        if (get_current_user_id()) { $ctx['user'] = get_current_user_id(); }
        // 401/403/404 are mostly people and apps asking without a key; kept, but as warnings
        self::write($st >= 500 ? 'ERROR' : ($st >= 400 ? 'WARN' : 'INFO'), 'rest', $route . ($err !== '' ? ' — ' . $err : ($st < 400 ? ' slow' : '')), $ctx);
        return $res;
    }

    private static function ip() { $ip = isset($_SERVER['REMOTE_ADDR']) ? (string) $_SERVER['REMOTE_ADDR'] : ''; return preg_replace('/\.\d+$/', '.x', $ip); }   // the last part dropped: enough to tell callers apart

    public static function shutdown()
    {
        $e = error_get_last();
        if (!$e || !in_array($e['type'], array(E_ERROR, E_PARSE, E_CORE_ERROR, E_COMPILE_ERROR, E_RECOVERABLE_ERROR), true)) { return; }
        if (strpos(wp_normalize_path((string) $e['file']), wp_normalize_path(MZC_DIR)) !== 0) { return; }
        self::write('FATAL', 'php', $e['message'], array('file' => str_replace(wp_normalize_path(MZC_DIR), '', wp_normalize_path($e['file'])), 'line' => $e['line'], 'uri' => isset($_SERVER['REQUEST_URI']) ? substr((string) $_SERVER['REQUEST_URI'], 0, 200) : ''));
    }

    /** The newest lines, newest first, of the last $days days, optionally of a level or containing a text. */
    public static function tail($n = 300, $level = '', $q = '', $days = 3)
    {
        $out = array();
        for ($i = 0; $i < $days && count($out) < $n; $i++) {
            $day = gmdate('Y-m-d', time() - $i * 86400); $f = self::file_of($day);
            if (!is_readable($f)) { continue; }
            $lines = array_reverse(explode("\n", trim(substr((string) file_get_contents($f), strlen(Mazesta_Connect::GUARD)))));
            foreach ($lines as $l) {
                if ($l === '' || ($level !== '' && strpos($l, ' ' . $level . ' ') === false) || ($q !== '' && stripos($l, $q) === false)) { continue; }
                $out[] = $day . ' ' . $l; if (count($out) >= $n) { break; }
            }
        }
        return $out;
    }

    public static function act_download()
    {
        if (!current_user_can('manage_options')) { wp_die('اجازه این کار را ندارید.', '', array('response' => 403)); }
        check_admin_referer('mzc_log_download');
        nocache_headers(); header('Content-Type: text/plain; charset=utf-8'); header('Content-Disposition: attachment; filename="mazesta-connect-log.txt"');
        echo implode("\n", array_reverse(self::tail(20000, '', '', self::LOG_DAYS)));
        exit;
    }

    /** The Settings page's log panel. */
    public static function panel()
    {
        $lv = isset($_GET['lv']) ? sanitize_key($_GET['lv']) : ''; $q = isset($_GET['lq']) ? sanitize_text_field(wp_unslash($_GET['lq'])) : '';
        $lv = in_array($lv, array('info', 'warn', 'error', 'fatal'), true) ? strtoupper($lv) : '';
        $o = '<div class="mzc-panel"><header><h3>لاگ افزونه</h3><a class="mzc-btn sm" href="' . esc_url(wp_nonce_url(admin_url('admin-post.php?action=mzc_log_download'), 'mzc_log_download')) . '">دریافت ' . self::LOG_DAYS . ' روز اخیر</a></header><div class="body">'
            . '<p class="muted mzc-prose">خطاها و درخواست‌های ناموفق یا کند افزونه (برنامه، Desk، پورتال و هوش مصنوعی)، خطاهای PHP خود افزونه و کارهای مهم مثل بازگشت به نسخهٔ قبلی. زمان‌ها به وقت جهانی (UTC) است.</p>'
            . '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-settings"><select name="lv"><option value="">همهٔ سطح‌ها</option>';
        foreach (array('error' => 'خطا', 'fatal' => 'خطای PHP', 'warn' => 'هشدار', 'info' => 'اطلاع') as $k => $t) { $o .= '<option value="' . $k . '"' . selected(strtolower($lv), $k, false) . '>' . $t . '</option>'; }
        $o .= '</select><input type="search" name="lq" value="' . esc_attr($q) . '" placeholder="متن، مسیر یا کد" class="mzc-search"><button class="mzc-btn primary">نمایش</button></form>';
        $lines = self::tail(300, $lv, $q);
        $o .= $lines ? '<pre class="mzc-log" dir="ltr">' . esc_html(implode("\n", $lines)) . '</pre>' : '<p class="muted">در سه روز اخیر چیزی ثبت نشده است.</p>';
        return $o . '</div></div>';
    }
}
