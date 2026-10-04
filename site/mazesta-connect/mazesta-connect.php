<?php
/**
 * Plugin Name: Mazesta Connect
 * Description: پل ارتباط برنامه Mazesta Test با سایت: خلاصه گزارش‌های آزمون برای چاپ روی کیس‌های سرویسی، نتایج بنچمارک خود برنامه و فهرست‌های مقایسه، اشتراک‌گذاری نتیجه بنچمارک کاربران، و انتشار نسخه تازه برنامه. داده‌ها در فایل نگه داشته می‌شوند، نه در پایگاه داده وردپرس.
 * Version: 1.1.0
 * Requires at least: 6.0
 * Requires PHP: 7.4
 * Author: Mazesta
 * Text Domain: mazesta-connect
 */

if (!defined('ABSPATH')) { exit; }

/**
 * Everything the desktop app says to the site goes through this plugin's REST routes (namespace mazesta/v1):
 *   GET  status                 is the plugin there, and is this key the shop's
 *   POST reports                a report's one-page summary (key)          -> kept, listed and printed in the dashboard
 *   POST bench/runs             benchmark runs (key; or without one into the review queue when the shop allows it)
 *   POST share                  a user's latest benchmark results (no key) -> a page of their own, with a link to pass on
 *   GET  bench/index, benchdb/* the comparison lists built from the approved runs, one file per benchmark, version and settings
 *   POST release/chunk, commit  the signed update folder (release key)     -> written to /mazesta/ in the site's root, where the app reads it
 * The lists are built the way the app builds them (BenchmarkPeers.Aggregate): one row per part model, the median of each system's best run.
 *
 * Nothing is kept in WordPress' database: no table, no option, no role change. The data is files in wp-content/mazesta-connect-data. Every
 * file there that is not public begins with a line of PHP that ends the request, so asking the web server for it returns nothing, whatever
 * the server makes of the folder's .htaccess.
 */
final class Mazesta_Connect
{
    const VERSION = '1.1.0';
    const NS = 'mazesta/v1';
    const MAX_HTML = 800000;
    const MAX_RUNS = 500;
    const MAX_SHARES = 5000;
    const GUARD = "<?php exit; ?>\n";

    public static function boot()
    {
        add_action('rest_api_init', array(__CLASS__, 'routes'));
        add_action('admin_menu', array(__CLASS__, 'menu'));
        add_action('init', array(__CLASS__, 'public_pages'));
        foreach (array('report', 'report_delete', 'run_approve', 'run_delete', 'run_feature', 'share_delete', 'settings', 'rebuild') as $a) {
            add_action('admin_post_mzc_' . $a, array(__CLASS__, 'act_' . $a));
        }
    }

    /* ---------- storage: files, never the database ---------- */

    private static function dir() { return WP_CONTENT_DIR . '/mazesta-connect-data'; }

    private static function ensure()
    {
        $d = self::dir();
        if (is_dir($d . '/benchdb')) { return true; }
        if (!wp_mkdir_p($d . '/benchdb')) { return false; }
        @file_put_contents($d . '/.htaccess', "<IfModule mod_authz_core.c>\nRequire all denied\n</IfModule>\n<IfModule !mod_authz_core.c>\nDeny from all\n</IfModule>\n");
        @file_put_contents($d . '/index.php', "<?php // nothing here\n");
        return true;
    }

    private static function file($name) { return self::dir() . '/' . $name . '.php'; }

    /** A data file's content after its guard line, or null when there is none. */
    private static function raw($name)
    {
        $f = self::file($name);
        if (!is_readable($f)) { return null; }
        $s = (string) file_get_contents($f);
        return strncmp($s, self::GUARD, strlen(self::GUARD)) === 0 ? substr($s, strlen(self::GUARD)) : null;
    }

    private static function read($name, $default = array())
    {
        $s = self::raw($name);
        $v = $s === null ? null : json_decode($s, true);
        return is_array($v) ? $v : $default;
    }

    private static function put($name, $text)
    {
        if (!self::ensure()) { return false; }
        $f = self::file($name); $tmp = $f . '.' . bin2hex(random_bytes(4)) . '.tmp';
        if (file_put_contents($tmp, self::GUARD . $text) === false) { return false; }
        if (!@rename($tmp, $f)) { @unlink($tmp); return false; }
        return true;
    }

    private static function write($name, $data)
    {
        $json = wp_json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
        return $json !== false && self::put($name, $json);
    }

    private static function remove($name) { $f = self::file($name); if (is_file($f)) { @unlink($f); } }

    /** Runs $work while holding the data folder's lock, so two uploads at once do not write over each other. */
    private static function locked($work)
    {
        if (!self::ensure()) { return $work(); }
        $h = @fopen(self::dir() . '/.lock', 'c');
        if ($h) { flock($h, LOCK_EX); }
        try { return $work(); }
        finally { if ($h) { flock($h, LOCK_UN); fclose($h); } }
    }

    private static function new_key() { return 'mz_' . bin2hex(random_bytes(24)); }

    /** The settings and the two keys; made on first use. */
    private static function config()
    {
        static $c = null;
        if ($c !== null) { return $c; }
        $c = self::read('config', array());
        if (empty($c['key']) || empty($c['releaseKey'])) {
            $c = self::locked(function () {
                $c = self::read('config', array());
                if (empty($c['key'])) { $c['key'] = self::new_key(); }
                if (empty($c['releaseKey'])) { $c['releaseKey'] = self::new_key(); }
                $c += array('openUploads' => false, 'publicLinks' => false, 'sharing' => true);
                self::write('config', $c);
                return $c;
            });
        }
        $c += array('openUploads' => false, 'publicLinks' => false, 'sharing' => true);
        return $c;
    }

    private static function can() { return current_user_can('manage_woocommerce') || current_user_can('manage_options'); }

    /** At most $max calls an hour from one address, counted in a file. */
    private static function allowed($bucket, $max)
    {
        $ip = isset($_SERVER['REMOTE_ADDR']) ? (string) $_SERVER['REMOTE_ADDR'] : '';
        $slot = $bucket . ':' . md5($ip); $hour = (int) floor(time() / 3600);
        return self::locked(function () use ($slot, $hour, $max) {
            $r = self::read('rate', array());
            if (!isset($r['hour']) || (int) $r['hour'] !== $hour) { $r = array('hour' => $hour, 'n' => array()); }
            $n = isset($r['n'][$slot]) ? (int) $r['n'][$slot] : 0;
            if ($n >= $max) { return false; }
            $r['n'][$slot] = $n + 1;
            self::write('rate', $r);
            return true;
        });
    }

    /* ---------- REST ---------- */

    public static function routes()
    {
        $open = '__return_true';
        register_rest_route(self::NS, '/status', array('methods' => 'GET', 'callback' => array(__CLASS__, 'rest_status'), 'permission_callback' => $open));
        register_rest_route(self::NS, '/reports', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_report'), 'permission_callback' => array(__CLASS__, 'need_key')));
        register_rest_route(self::NS, '/bench/runs', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_runs'), 'permission_callback' => $open));
        register_rest_route(self::NS, '/share', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_share'), 'permission_callback' => $open));
        register_rest_route(self::NS, '/bench/index', array('methods' => 'GET', 'callback' => array(__CLASS__, 'rest_index'), 'permission_callback' => $open));
        register_rest_route(self::NS, '/benchdb/(?P<file>[A-Za-z0-9][A-Za-z0-9._@=-]*\.json)', array('methods' => 'GET', 'callback' => array(__CLASS__, 'rest_list'), 'permission_callback' => $open));
        register_rest_route(self::NS, '/release/chunk', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_chunk'), 'permission_callback' => array(__CLASS__, 'need_release_key')));
        register_rest_route(self::NS, '/release/commit', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_commit'), 'permission_callback' => array(__CLASS__, 'need_release_key')));
    }

    private static function key_state($req, $which = 'key')
    {
        $given = (string) $req->get_header('x_mazesta_key');
        if ($given === '') { return 'missing'; }
        $c = self::config();
        $want = isset($c[$which]) ? (string) $c[$which] : '';
        return $want !== '' && hash_equals($want, $given) ? 'ok' : 'wrong';
    }

    public static function need_key($req)
    {
        return self::key_state($req) === 'ok' ? true : new WP_Error('mazesta_key', 'The site key is missing or wrong.', array('status' => 401));
    }

    public static function need_release_key($req)
    {
        return self::key_state($req, 'releaseKey') === 'ok' ? true : new WP_Error('mazesta_key', 'The release key is missing or wrong.', array('status' => 401));
    }

    /** Answers that must never come from a cache (the page cache also caches REST answers). */
    private static function fresh($data, $status = 200)
    {
        $res = new WP_REST_Response($data, $status);
        $res->header('Cache-Control', 'no-store, no-cache, must-revalidate, max-age=0');
        $res->header('X-LiteSpeed-Cache-Control', 'no-cache');
        return $res;
    }

    public static function rest_status($req)
    {
        $key = self::key_state($req); $c = self::config();
        $out = array('name' => 'Mazesta Connect', 'version' => self::VERSION, 'key' => $key, 'openUploads' => !empty($c['openUploads']), 'sharing' => !empty($c['sharing']));
        if ($key === 'ok') {
            $approved = 0; $pending = 0;
            foreach (self::read('runs') as $r) { if (!empty($r['status'])) { $approved++; } else { $pending++; } }
            $out['reports'] = count(self::read('reports')); $out['runs'] = $approved; $out['pending'] = $pending;
        }
        return self::fresh($out);
    }

    /** A time as ISO 8601 in UTC; now when it does not read as one. */
    private static function when($iso)
    {
        $ts = is_string($iso) ? strtotime($iso) : false;
        return gmdate('Y-m-d\TH:i:s\Z', $ts ? $ts : time());
    }

    private static function clip($s, $n) { return is_string($s) ? mb_substr(trim(wp_strip_all_tags($s)), 0, $n) : ''; }

    public static function rest_report($req)
    {
        $p = $req->get_json_params();
        $id = isset($p['id']) ? (string) $p['id'] : '';
        $html = isset($p['html']) ? (string) $p['html'] : '';
        if (!preg_match('/^[A-Za-z0-9-]{8,64}$/', $id)) { return new WP_Error('mazesta_report', 'The report id is not valid.', array('status' => 400)); }
        if ($html === '' || strlen($html) > self::MAX_HTML) { return new WP_Error('mazesta_report', 'The summary is missing or too large.', array('status' => 400)); }
        // The page is kept as it came; it is only ever sent out under a policy that lets nothing in it run (see show_report). A "<?php" in it
        // would be read by PHP only if the file were run, which its first line prevents.
        $row = array(
            'title' => self::clip(isset($p['title']) ? $p['title'] : '', 255),
            'machine' => self::clip(isset($p['machine']) ? $p['machine'] : '', 190),
            'service' => self::clip(isset($p['service']) ? $p['service'] : '', 190),
            'verdict' => self::clip(isset($p['verdict']) ? $p['verdict'] : '', 40),
            'kind' => self::clip(isset($p['kind']) ? $p['kind'] : '', 40),
            'summary' => self::clip(isset($p['summary']) ? $p['summary'] : '', 2000),
            'app' => self::clip(isset($p['appVersion']) ? $p['appVersion'] : '', 40),
            'created' => self::when(isset($p['created']) ? $p['created'] : null),
            'received' => self::when(null),
        );
        $done = self::locked(function () use ($id, $row, $html) {
            $all = self::read('reports');
            $had = isset($all[$id]);
            $row['token'] = $had && !empty($all[$id]['token']) ? $all[$id]['token'] : bin2hex(random_bytes(16));
            if (!self::put('report-' . $id, $html)) { return null; }
            $all[$id] = $row;
            return self::write('reports', $all) ? array($had, $row['token']) : null;
        });
        if ($done === null) { return new WP_Error('mazesta_report', 'The report could not be saved.', array('status' => 500)); }
        $c = self::config();
        return self::fresh(array(
            'id' => $id, 'updated' => $done[0],
            'url' => admin_url('admin-post.php?action=mzc_report&id=' . $id),
            'link' => !empty($c['publicLinks']) ? home_url('/?mazesta_report=' . $done[1]) : null,
        ));
    }

    /** A run as the app logs it, or false when it is not one. */
    private static function valid_run($run)
    {
        return is_object($run) && isset($run->id, $run->benchmark, $run->part, $run->system, $run->value, $run->unit)
            && is_string($run->id) && preg_match('/^[A-Za-z0-9-]{8,64}$/', $run->id) && is_string($run->benchmark) && preg_match('/^[A-Za-z0-9._-]{1,80}$/', $run->benchmark)
            && is_string($run->part) && trim($run->part) !== '' && is_string($run->system) && is_string($run->unit)
            && (is_int($run->value) || is_float($run->value)) && $run->value > 0 && is_finite((float) $run->value);
    }

    /** Adds the runs not yet held (each once, by its id): approved when they came with the shop's key, else waiting for review. */
    private static function add_runs($runs, $higher, $trusted)
    {
        return self::locked(function () use ($runs, $higher, $trusted) {
            $all = self::read('runs'); $added = 0; $known = 0; $bad = 0;
            foreach ($runs as $run) {
                if (!self::valid_run($run)) { $bad++; continue; }
                $settings = isset($run->settings) && is_string($run->settings) ? $run->settings : '';
                $table = $run->benchmark . '@' . (isset($run->version) ? (int) $run->version : 1) . ($settings !== '' ? '|' . $settings : '');
                if (strlen($table) > 190) { $bad++; continue; }
                if (isset($all[$run->id])) { $known++; continue; }
                if (!$trusted) { unset($run->machine); }   // a stranger's computer name is not ours to keep
                $json = wp_json_encode($run, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
                if ($json === false || strlen($json) > 20000) { $bad++; continue; }
                $all[$run->id] = array(
                    'table' => $table, 'part' => mb_substr($run->part, 0, 190), 'value' => (float) $run->value, 'unit' => mb_substr($run->unit, 0, 40),
                    'higher' => !(isset($higher[$run->benchmark]) && !$higher[$run->benchmark]), 'status' => $trusted ? 1 : 0, 'featured' => false, 'oc' => null, 'note' => '', 'markAt' => null,
                    'runAt' => self::when(isset($run->at) ? $run->at : null), 'received' => self::when(null), 'json' => $json,
                );
                $added++;
            }
            if ($added > 0) { self::write('runs', $all); }
            return array($added, $known, $bad);
        });
    }

    private static function runs_of($req)
    {
        $body = json_decode($req->get_body());   // objects stay objects, so a run goes back out as it came in
        if (!is_object($body) || !isset($body->runs) || !is_array($body->runs)) { return new WP_Error('mazesta_runs', 'No runs in the request.', array('status' => 400)); }
        if (count($body->runs) > self::MAX_RUNS) { return new WP_Error('mazesta_runs', 'Too many runs in one request.', array('status' => 413)); }
        return $body;
    }

    public static function rest_runs($req)
    {
        $key = self::key_state($req); $c = self::config();
        $trusted = $key === 'ok';
        if (!$trusted) {
            if ($key === 'wrong' || empty($c['openUploads'])) { return new WP_Error('mazesta_key', 'The site key is missing or wrong.', array('status' => 401)); }
            if (!self::allowed('runs', 20)) { return new WP_Error('mazesta_busy', 'Too many uploads from this address; try again in an hour.', array('status' => 429)); }
        }
        $body = self::runs_of($req);
        if (is_wp_error($body)) { return $body; }
        $higher = isset($body->higher) && is_object($body->higher) ? (array) $body->higher : array();
        list($added, $known, $bad) = self::add_runs($body->runs, $higher, $trusted);
        // The shop's own marks (featured, overclocked) come with its runs; for a run marked on two copies the later mark wins.
        $marked = 0;
        if ($trusted && isset($body->marks) && is_object($body->marks)) {
            $marks = (array) $body->marks;
            $marked = self::locked(function () use ($marks) {
                $all = self::read('runs'); $n = 0;
                foreach ($marks as $id => $m) {
                    if (!is_object($m) || !isset($all[(string) $id])) { continue; }
                    $at = self::when(isset($m->at) ? $m->at : null);
                    if (!empty($all[$id]['markAt']) && strcmp((string) $all[$id]['markAt'], $at) >= 0) { continue; }
                    $all[$id]['featured'] = !empty($m->featured);
                    $all[$id]['oc'] = isset($m->overclocked) && is_bool($m->overclocked) ? $m->overclocked : null;
                    $all[$id]['note'] = isset($m->note) && is_string($m->note) ? mb_substr($m->note, 0, 190) : '';
                    $all[$id]['markAt'] = $at;
                    $n++;
                }
                if ($n > 0) { self::write('runs', $all); }
                return $n;
            });
        }
        $lists = ($trusted && ($added > 0 || $marked > 0)) ? self::rebuild() : null;
        return self::fresh(array('added' => $added, 'known' => $known, 'rejected' => $bad, 'pending' => !$trusted, 'marked' => $marked, 'lists' => $lists));
    }

    /**
     * A user's latest results, to pass on: kept as a page of its own under a link. Only numbers and short plain texts are taken (no HTML).
     * A computer has one page: sharing again replaces it and keeps its link. The runs also wait in the review queue when the shop takes
     * uploads without a key.
     */
    public static function rest_share($req)
    {
        $c = self::config();
        if (empty($c['sharing'])) { return new WP_Error('mazesta_share', 'Sharing is switched off on the site.', array('status' => 403)); }
        if (!self::allowed('share', 30)) { return new WP_Error('mazesta_busy', 'Too many uploads from this address; try again in an hour.', array('status' => 429)); }
        $body = self::runs_of($req);
        if (is_wp_error($body)) { return $body; }
        if (count($body->runs) > 60) { return new WP_Error('mazesta_runs', 'Too many runs in one request.', array('status' => 413)); }
        $names = isset($body->names) && is_object($body->names) ? (array) $body->names : array();
        $rows = array(); $system = '';
        foreach ($body->runs as $run) {
            if (!self::valid_run($run)) { continue; }
            if ($system === '') { $system = $run->system; }
            $rows[] = array(
                'benchmark' => $run->benchmark, 'name' => self::clip(isset($names[$run->benchmark]) ? $names[$run->benchmark] : $run->benchmark, 80),
                'settings' => self::clip(isset($run->settings) ? $run->settings : '', 120), 'value' => (float) $run->value, 'unit' => self::clip($run->unit, 20),
                'part' => self::clip(self::part_name($run->part), 120), 'at' => self::when(isset($run->at) ? $run->at : null),
            );
        }
        if (!$rows || !preg_match('/^[A-Za-z0-9+\/=_-]{8,128}$/', $system)) { return new WP_Error('mazesta_runs', 'No valid runs in the request.', array('status' => 400)); }
        $m = isset($body->machine) && is_object($body->machine) ? $body->machine : new stdClass();
        $share = array(
            'created' => self::when(null), 'app' => self::clip(isset($body->appVersion) ? $body->appVersion : '', 40),
            'cpu' => self::clip(isset($m->cpu) ? $m->cpu : '', 120), 'gpu' => self::clip(isset($m->gpu) ? $m->gpu : '', 120), 'os' => self::clip(isset($m->os) ? $m->os : '', 120),
            'ramGb' => isset($m->ramGb) && (is_int($m->ramGb) || is_float($m->ramGb)) && $m->ramGb > 0 && $m->ramGb < 100000 ? (float) $m->ramGb : null,
            'rows' => $rows,
        );
        $who = substr(hash('sha256', $system), 0, 24);
        $token = self::locked(function () use ($who, $share) {
            $index = self::read('shares');
            $token = isset($index[$who]['token']) ? (string) $index[$who]['token'] : bin2hex(random_bytes(12));
            if (!self::write('share-' . $token, $share)) { return null; }
            $index[$who] = array('token' => $token, 'created' => $share['created'], 'cpu' => $share['cpu'], 'gpu' => $share['gpu'], 'rows' => count($share['rows']));
            // The oldest pages go when there are too many.
            if (count($index) > self::MAX_SHARES) {
                uasort($index, function ($a, $b) { return strcmp((string) $b['created'], (string) $a['created']); });
                foreach (array_slice($index, self::MAX_SHARES, null, true) as $k => $old) { self::remove('share-' . $old['token']); unset($index[$k]); }
            }
            return self::write('shares', $index) ? $token : null;
        });
        if ($token === null) { return new WP_Error('mazesta_share', 'The results could not be saved.', array('status' => 500)); }
        $queued = 0;
        if (!empty($c['openUploads'])) {
            $higher = isset($body->higher) && is_object($body->higher) ? (array) $body->higher : array();
            list($queued) = self::add_runs($body->runs, $higher, false);
        }
        return self::fresh(array('link' => home_url('/?mazesta_share=' . $token), 'rows' => count($rows), 'queued' => $queued));
    }

    /* ---------- the comparison lists ---------- */

    private static function db_dir() { return self::dir() . '/benchdb'; }

    /** A part's name as the lists group it (BenchmarkPeers.PartName): vendor marks and CPU tails removed, spaces collapsed. */
    private static function part_name($raw)
    {
        $s = preg_replace('/\((R|TM|C)\)|®|™/iu', ' ', (string) $raw);
        $s = preg_replace('/(\s+CPU)?\s+@\s+[\d.]+\s*GHz$|\s+\d+-Core\s+Processor$|\s+Processor$/iu', '', trim(preg_replace('/\s+/u', ' ', $s)));
        return trim(preg_replace('/\s+/u', ' ', $s));
    }

    /** The list's file name (BenchmarkPeers.FileName): the settings folded into a short hash. */
    private static function file_name($table)
    {
        $bar = strpos($table, '|');
        if ($bar === false) { return str_replace('@', '-v', $table) . '.json'; }
        return str_replace('@', '-v', substr($table, 0, $bar)) . '-' . substr(hash('sha256', substr($table, $bar + 1)), 0, 10) . '.json';
    }

    private static function sample($run, $oc)
    {
        $s = array('value' => $run->value, 'overclocked' => $oc, 'at' => isset($run->at) ? $run->at : null);
        if (isset($run->metrics)) { $s['metrics'] = $run->metrics; }
        if (isset($run->details)) { $s['details'] = $run->details; }
        return $s;
    }

    /** Builds every list from the approved runs and writes them, with index.json (name, size, SHA-256 of each). Returns how many lists there are. */
    public static function rebuild()
    {
        if (!self::ensure()) { return 0; }
        $dir = self::db_dir();
        $tables = array();
        foreach (self::read('runs') as $row) {
            if (empty($row['status']) || !isset($row['json'], $row['table'])) { continue; }
            $run = json_decode((string) $row['json']);
            if (!is_object($run) || !isset($run->value, $run->part, $run->system, $run->unit)) { continue; }
            $oc = isset($row['oc']) && $row['oc'] !== null ? (bool) $row['oc'] : !empty($run->overclocked);
            $tables[$row['table']][] = array('run' => $run, 'oc' => $oc, 'higher' => !isset($row['higher']) || $row['higher'], 'featured' => !empty($row['featured']), 'note' => isset($row['note']) ? (string) $row['note'] : '', 'ts' => isset($run->at) ? (int) strtotime($run->at) : 0);
        }
        $flags = JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES;
        $files = array(); $built = gmdate('Y-m-d\TH:i:s\Z');
        foreach ($tables as $key => $list) {
            $first = $list[0]; $higher = $first['higher'];
            $units = array();
            foreach ($list as $x) { $u = $x['run']->unit; $units[$u] = isset($units[$u]) ? $units[$u] + 1 : 1; }
            arsort($units); $unit = (string) key($units);
            $parts = array(); $featured = array();
            foreach ($list as $x) {
                if ($x['run']->unit !== $unit) { continue; }
                $name = self::part_name($x['run']->part);
                if ($name === '') { continue; }
                $parts[mb_strtoupper($name) . "\x01" . ($x['oc'] ? '1' : '0')][] = $x;
                if ($x['featured']) {
                    $f = array('id' => $x['run']->id, 'part' => $name) + self::sample($x['run'], $x['oc']);
                    if ($x['note'] !== '') { $f['note'] = $x['note']; }
                    $featured[] = $f;
                }
            }
            $entries = array();
            foreach ($parts as $group) {
                $best = array();   // each system once, with its best run
                foreach ($group as $x) {
                    $sys = (string) $x['run']->system; $v = (float) $x['run']->value;
                    if (!isset($best[$sys]) || ($higher ? $v > (float) $best[$sys]['run']->value : $v < (float) $best[$sys]['run']->value)) { $best[$sys] = $x; }
                }
                $per = array_values($best);
                usort($per, function ($a, $b) { return (float) $a['run']->value <=> (float) $b['run']->value; });
                $n = count($per);
                $median = $n % 2 === 1 ? (float) $per[intdiv($n, 2)]['run']->value : ((float) $per[intdiv($n, 2) - 1]['run']->value + (float) $per[intdiv($n, 2)]['run']->value) / 2;
                $near = $per[0];
                foreach ($per as $x) { if (abs((float) $x['run']->value - $median) < abs((float) $near['run']->value - $median)) { $near = $x; } }
                $last = $group[0];
                foreach ($group as $x) { if ($x['ts'] > $last['ts']) { $last = $x; } }
                $entries[] = array(
                    'part' => self::part_name($group[0]['run']->part), 'median' => $median, 'best' => (float) ($higher ? $per[$n - 1]['run']->value : $per[0]['run']->value),
                    'systems' => $n, 'runs' => count($group), 'last' => isset($last['run']->at) ? $last['run']->at : $built, 'overclocked' => $group[0]['oc'],
                    'sample' => self::sample($near['run'], $near['oc']),
                );
            }
            if (!$entries) { continue; }
            usort($entries, function ($a, $b) use ($higher) {
                if ($a['median'] != $b['median']) { return $higher ? $b['median'] <=> $a['median'] : $a['median'] <=> $b['median']; }
                return strcasecmp($a['part'], $b['part']);
            });
            usort($featured, function ($a, $b) use ($higher) { return $higher ? $b['value'] <=> $a['value'] : $a['value'] <=> $b['value']; });
            $r = $first['run'];
            $json = wp_json_encode(array(
                'key' => $key, 'benchmark' => $r->benchmark, 'version' => isset($r->version) ? (int) $r->version : 1, 'settings' => isset($r->settings) ? (string) $r->settings : '',
                'unit' => $unit, 'higherIsBetter' => $higher, 'built' => $built, 'entries' => $entries, 'featured' => $featured,
            ), $flags);
            $name = self::file_name($key);
            if ($json === false || !preg_match('/^[A-Za-z0-9][A-Za-z0-9._@=-]*\.json$/', $name)) { continue; }
            if (file_put_contents($dir . '/' . $name . '.tmp', $json) === false || !rename($dir . '/' . $name . '.tmp', $dir . '/' . $name)) { continue; }
            $files[$name] = array('file' => 'benchdb/' . $name, 'size' => strlen($json), 'sha256' => hash('sha256', $json));
        }
        $have = glob($dir . '/*.json');
        foreach (is_array($have) ? $have : array() as $f) {
            $b = basename($f);
            if ($b !== 'index.json' && !isset($files[$b])) { @unlink($f); }
        }
        ksort($files);
        file_put_contents($dir . '/index.json', wp_json_encode(array('built' => $built, 'files' => array_values($files)), $flags));
        return count($files);
    }

    public static function rest_index()
    {
        $f = self::db_dir() . '/index.json';
        $index = is_readable($f) ? json_decode((string) file_get_contents($f), true) : null;
        return self::fresh(is_array($index) ? $index : array('built' => null, 'files' => array()));
    }

    /** A list is sent byte for byte as it was written: the app checks its size and SHA-256 against the index. */
    public static function rest_list($req)
    {
        $name = basename((string) $req['file']);
        $f = self::db_dir() . '/' . $name;
        if ($name === 'index.json' || !is_readable($f)) { return new WP_Error('mazesta_list', 'No such list.', array('status' => 404)); }
        nocache_headers();
        header('Content-Type: application/json; charset=utf-8');
        header('X-LiteSpeed-Cache-Control: no-cache');
        header('Content-Length: ' . filesize($f));
        readfile($f);
        exit;
    }

    /* ---------- the app's release (the signed update folder) ---------- */

    private static function release_dir() { return ABSPATH . 'mazesta'; }
    private static function release_name($name) { return is_string($name) && preg_match('/^(MazestaWeb-\d+\.\d+\.\d+\.zip|update\.json|update\.json\.sig|benchdb\/[A-Za-z0-9][A-Za-z0-9._@=-]*\.json)$/', $name); }

    /** One piece of a release file, appended at its offset into /mazesta/.incoming; a piece at 0 starts the file over. */
    public static function rest_chunk($req)
    {
        $name = (string) $req->get_param('name'); $offset = (int) $req->get_param('offset');
        if (!self::release_name($name) || $offset < 0) { return new WP_Error('mazesta_release', 'Not a release file name.', array('status' => 400)); }
        $dir = self::release_dir() . '/.incoming';
        if (!wp_mkdir_p($dir)) { return new WP_Error('mazesta_release', 'The update folder cannot be written.', array('status' => 500)); }
        $file = $dir . '/' . $name;
        if (!wp_mkdir_p(dirname($file))) { return new WP_Error('mazesta_release', 'The update folder cannot be written.', array('status' => 500)); }
        $have = $offset === 0 ? 0 : (is_file($file) ? filesize($file) : -1);
        if ($have !== $offset) { return self::fresh(array('error' => 'offset', 'have' => max(0, (int) $have)), 409); }
        $body = $req->get_body();
        if ($body === '' || file_put_contents($file, $body, $offset === 0 ? 0 : FILE_APPEND) === false) { return new WP_Error('mazesta_release', 'The piece could not be written.', array('status' => 500)); }
        clearstatcache(true, $file);
        return self::fresh(array('have' => filesize($file)));
    }

    /** Checks every uploaded file's size and SHA-256, then puts them in place: the zip first, update.json and its signature last. */
    public static function rest_commit($req)
    {
        $p = $req->get_json_params();
        $want = isset($p['files']) && is_array($p['files']) ? $p['files'] : array();
        $dir = self::release_dir(); $in = $dir . '/.incoming';
        $names = array();
        foreach ($want as $f) {
            $name = isset($f['name']) ? $f['name'] : '';
            if (!self::release_name($name) || !is_file($in . '/' . $name)) { return new WP_Error('mazesta_release', 'A file is missing: ' . sanitize_text_field((string) $name), array('status' => 400)); }
            if (!isset($f['size'], $f['sha256']) || filesize($in . '/' . $name) !== (int) $f['size'] || !hash_equals(strtolower((string) $f['sha256']), (string) hash_file('sha256', $in . '/' . $name))) {
                return new WP_Error('mazesta_release', 'A file arrived damaged: ' . $name, array('status' => 400));
            }
            $names[] = $name;
        }
        if (!in_array('update.json', $names, true) || !in_array('update.json.sig', $names, true)) { return new WP_Error('mazesta_release', 'update.json and its signature are required.', array('status' => 400)); }
        usort($names, function ($a, $b) {
            $ua = substr($a, 0, 6) === 'update' ? 1 : 0; $ub = substr($b, 0, 6) === 'update' ? 1 : 0;
            return $ua !== $ub ? $ua - $ub : strcmp($a, $b);
        });
        foreach ($names as $name) {
            if (!wp_mkdir_p(dirname($dir . '/' . $name)) || !rename($in . '/' . $name, $dir . '/' . $name)) { return new WP_Error('mazesta_release', 'Could not put ' . $name . ' in place.', array('status' => 500)); }
        }
        // The zip the manifest offers stays; older ones go.
        $manifest = json_decode((string) file_get_contents($dir . '/update.json'), true);
        $keep = is_array($manifest) && isset($manifest['app']['file']) ? (string) $manifest['app']['file'] : '';
        $zips = glob($dir . '/MazestaWeb-*.zip');
        foreach (is_array($zips) ? $zips : array() as $zip) { if (basename($zip) !== $keep) { @unlink($zip); } }
        // The same for the signed comparison lists: the ones the manifest no longer names go.
        $listed = array();
        if (is_array($manifest) && isset($manifest['data']) && is_array($manifest['data'])) { foreach ($manifest['data'] as $d) { if (isset($d['file'])) { $listed[(string) $d['file']] = true; } } }
        $lists = glob($dir . '/benchdb/*.json');
        foreach (is_array($lists) ? $lists : array() as $list) { if (!isset($listed['benchdb/' . basename($list)])) { @unlink($list); } }
        return self::fresh(array('ok' => true, 'version' => is_array($manifest) && isset($manifest['app']['version']) ? $manifest['app']['version'] : null));
    }

    /* ---------- pages: a report, a shared result ---------- */

    private static function page_headers($csp)
    {
        nocache_headers();
        header('Content-Type: text/html; charset=utf-8');
        header('X-Robots-Tag: noindex, nofollow');
        header('X-LiteSpeed-Cache-Control: no-cache');
        header('Content-Security-Policy: ' . $csp);
    }

    private static function show_report($id, $print)
    {
        $html = preg_match('/^[A-Za-z0-9-]{8,64}$/', (string) $id) ? self::raw('report-' . $id) : null;
        if ($html === null) { wp_die('گزارش پیدا نشد.', '', array('response' => 404)); }
        $nonce = bin2hex(random_bytes(12));
        // The summary came from outside: nothing in it may run or load. Only the print button's script, named by its nonce, runs.
        self::page_headers("default-src 'none'; style-src 'unsafe-inline'; font-src data:; img-src data:; script-src 'nonce-" . $nonce . "'");
        $html = preg_replace('/<meta\s+http-equiv="Content-Security-Policy"[^>]*>/i', '', $html);
        $bar = '<style>.mzc-bar{position:fixed;top:8px;left:8px;z-index:9;font:14px Tahoma,sans-serif}.mzc-bar button{padding:8px 18px;border:0;border-radius:6px;background:#0b6;color:#fff;cursor:pointer}@media print{.mzc-bar{display:none}}</style>'
            . '<div class="mzc-bar"><button id="mzc-print" type="button">چاپ</button></div>'
            . '<script nonce="' . $nonce . '">document.getElementById("mzc-print").addEventListener("click",function(){window.print()});' . ($print ? 'window.addEventListener("load",function(){window.print()});' : '') . '</script>';
        echo stripos($html, '</body>') !== false ? preg_replace('/<\/body>/i', $bar . '</body>', $html, 1) : $html . $bar;
        exit;
    }

    public static function act_report()
    {
        if (!self::can()) { wp_die('اجازه دیدن گزارش‌ها را ندارید.', '', array('response' => 403)); }
        self::show_report(isset($_GET['id']) ? (string) $_GET['id'] : '', !empty($_GET['print']));
    }

    private static function number($v)
    {
        $v = (float) $v;
        return rtrim(rtrim(number_format($v, abs($v) >= 100 ? 0 : 2, '.', ','), '0'), '.');
    }

    /** A user's shared results, drawn by the site from the numbers it kept (no HTML came from outside). */
    private static function show_share($token)
    {
        $s = preg_match('/^[0-9a-f]{24}$/', (string) $token) ? self::read('share-' . $token, null) : null;
        if (!is_array($s) || empty($s['rows'])) { wp_die('این نتیجه پیدا نشد.', '', array('response' => 404)); }
        self::page_headers("default-src 'none'; style-src 'unsafe-inline'");
        $e = 'esc_html';
        $spec = array();
        if ($s['cpu'] !== '') { $spec[] = array('پردازنده', $s['cpu']); }
        if ($s['gpu'] !== '') { $spec[] = array('کارت گرافیک', $s['gpu']); }
        if (!empty($s['ramGb'])) { $spec[] = array('حافظه', self::number($s['ramGb']) . ' GB'); }
        if ($s['os'] !== '') { $spec[] = array('سیستم‌عامل', $s['os']); }
        echo '<!doctype html><html lang="fa" dir="rtl"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="robots" content="noindex">'
            . '<title>نتایج بنچمارک Mazesta Test</title><style>'
            . 'body{margin:0;background:#0e1012;color:#e8eaed;font:15px/1.8 Tahoma,Vazirmatn,sans-serif}main{max-width:820px;margin:0 auto;padding:28px 16px}'
            . 'h1{font-size:22px;margin:0 0 4px}h1 b{color:#fdd400}.sub{color:#8a9097;margin:0 0 20px}.box{background:#16191c;border:1px solid #262a2f;border-radius:10px;padding:14px 18px;margin:0 0 16px}'
            . 'dl{display:grid;grid-template-columns:auto 1fr;gap:2px 16px;margin:0}dt{color:#8a9097}dd{margin:0;direction:ltr;text-align:right;unicode-bidi:isolate}'
            . 'table{width:100%;border-collapse:collapse}th,td{padding:9px 8px;border-bottom:1px solid #262a2f;text-align:right}th{color:#8a9097;font-weight:normal;font-size:13px}'
            . '.n{direction:ltr;unicode-bidi:isolate;font-weight:bold;color:#fff;white-space:nowrap}.n small{color:#fdd400;font-weight:normal}.p{direction:ltr;unicode-bidi:isolate;color:#c3c7cc;font-size:13px}'
            . 'footer{color:#8a9097;font-size:13px;margin-top:18px}a{color:#fdd400}'
            . '</style></head><body><main><h1>نتایج بنچمارک <b>Mazesta Test</b></h1><p class="sub">ثبت‌شده در ' . $e(get_date_from_gmt(gmdate('Y-m-d H:i:s', (int) strtotime($s['created'])), 'Y/m/d H:i')) . '</p>';
        if ($spec) {
            echo '<div class="box"><dl>';
            foreach ($spec as $row) { echo '<dt>' . $e($row[0]) . '</dt><dd>' . $e($row[1]) . '</dd>'; }
            echo '</dl></div>';
        }
        echo '<div class="box"><table><thead><tr><th>بنچمارک</th><th>نتیجه</th><th>قطعه</th><th>تاریخ اجرا</th></tr></thead><tbody>';
        foreach ($s['rows'] as $r) {
            echo '<tr><td>' . $e($r['name']) . ($r['settings'] !== '' ? ' <span class="p">' . $e($r['settings']) . '</span>' : '') . '</td><td class="n">' . $e(self::number($r['value'])) . ' <small>' . $e($r['unit']) . '</small></td>'
                . '<td class="p">' . $e($r['part']) . '</td><td>' . $e(get_date_from_gmt(gmdate('Y-m-d H:i:s', (int) strtotime($r['at'])), 'Y/m/d')) . '</td></tr>';
        }
        echo '</tbody></table></div><footer>این اعداد را برنامه Mazesta Test روی همین سیستم اندازه گرفته و کاربر آن فرستاده است. <a href="' . esc_url(home_url('/')) . '">' . $e(get_bloginfo('name')) . '</a></footer></main></body></html>';
        exit;
    }

    /** A shared result by its link; and a report by its link (a 128-bit random token), for a colleague without an account, only while the shop has switched links on. */
    public static function public_pages()
    {
        if (!empty($_GET['mazesta_share'])) { self::show_share((string) $_GET['mazesta_share']); }
        if (empty($_GET['mazesta_report'])) { return; }
        $token = (string) $_GET['mazesta_report']; $c = self::config();
        $id = null;
        if (!empty($c['publicLinks']) && preg_match('/^[0-9a-f]{32}$/', $token)) {
            foreach (self::read('reports') as $rid => $r) { if (isset($r['token']) && hash_equals((string) $r['token'], $token)) { $id = (string) $rid; break; } }
        }
        if ($id === null) { wp_die('گزارش پیدا نشد.', '', array('response' => 404)); }
        self::show_report($id, !empty($_GET['print']));
    }

    /* ---------- dashboard ---------- */

    public static function menu()
    {
        add_menu_page('Mazesta Connect', 'Mazesta Connect', class_exists('WooCommerce') ? 'manage_woocommerce' : 'manage_options', 'mazesta-connect', array(__CLASS__, 'page'), 'dashicons-desktop', 58);
    }

    private static function guard($action)
    {
        if (!self::can()) { wp_die('اجازه این کار را ندارید.', '', array('response' => 403)); }
        check_admin_referer('mzc_' . $action);
    }

    private static function back($tab, $msg = '')
    {
        wp_safe_redirect(add_query_arg(array('page' => 'mazesta-connect', 'tab' => $tab, 'msg' => $msg), admin_url('admin.php')));
        exit;
    }

    private static function post_link($action, $args, $label, $class = 'button button-small', $confirm = '')
    {
        $url = wp_nonce_url(add_query_arg(array('action' => 'mzc_' . $action) + $args, admin_url('admin-post.php')), 'mzc_' . $action);
        return '<a class="' . esc_attr($class) . '" href="' . esc_url($url) . '"' . ($confirm !== '' ? ' onclick="return confirm(\'' . esc_js($confirm) . '\')"' : '') . '>' . esc_html($label) . '</a>';
    }

    private static function arg($name) { return isset($_GET[$name]) ? sanitize_text_field(wp_unslash($_GET[$name])) : ''; }

    public static function act_report_delete()
    {
        self::guard('report_delete');
        $id = self::arg('id');
        if (preg_match('/^[A-Za-z0-9-]{8,64}$/', $id)) {
            self::locked(function () use ($id) { $all = self::read('reports'); unset($all[$id]); self::write('reports', $all); self::remove('report-' . $id); });
        }
        self::back('reports', 'deleted');
    }

    /** Changes the runs under the lock with $change(&$all) and builds the lists again. */
    private static function change_runs($change)
    {
        self::locked(function () use ($change) { $all = self::read('runs'); $change($all); self::write('runs', $all); });
        self::rebuild();
    }

    public static function act_run_approve()
    {
        self::guard('run_approve');
        $id = self::arg('id'); $every = !empty($_GET['all']);
        self::change_runs(function (&$all) use ($id, $every) {
            foreach ($all as $k => $r) { if ($every || (string) $k === $id) { $all[$k]['status'] = 1; } }
        });
        self::back('bench', 'approved');
    }

    public static function act_run_delete()
    {
        self::guard('run_delete');
        $id = self::arg('id');
        self::change_runs(function (&$all) use ($id) { unset($all[$id]); });
        self::back('bench', 'deleted');
    }

    public static function act_run_feature()
    {
        self::guard('run_feature');
        $id = self::arg('id'); $on = empty($_GET['off']);
        self::change_runs(function (&$all) use ($id, $on) {
            if (isset($all[$id])) { $all[$id]['featured'] = $on; $all[$id]['markAt'] = self::when(null); }
        });
        self::back('bench', 'saved');
    }

    public static function act_share_delete()
    {
        self::guard('share_delete');
        $token = self::arg('token');
        self::locked(function () use ($token) {
            $index = self::read('shares');
            foreach ($index as $k => $s) { if (isset($s['token']) && (string) $s['token'] === $token) { self::remove('share-' . $s['token']); unset($index[$k]); } }
            self::write('shares', $index);
        });
        self::back('shares', 'deleted');
    }

    public static function act_rebuild()
    {
        self::guard('rebuild');
        self::rebuild();
        self::back('bench', 'rebuilt');
    }

    public static function act_settings()
    {
        self::guard('settings');
        if (!current_user_can('manage_options')) { wp_die('فقط مدیر سایت می‌تواند تنظیمات را عوض کند.', '', array('response' => 403)); }
        self::config();
        self::locked(function () {
            $c = self::read('config');
            $c['openUploads'] = !empty($_POST['open_uploads']); $c['publicLinks'] = !empty($_POST['public_links']); $c['sharing'] = !empty($_POST['sharing']);
            if (!empty($_POST['new_key'])) { $c['key'] = self::new_key(); }
            if (!empty($_POST['new_release_key'])) { $c['releaseKey'] = self::new_key(); }
            self::write('config', $c);
        });
        self::back('settings', 'saved');
    }

    private static function local($iso) { return esc_html(get_date_from_gmt(gmdate('Y-m-d H:i:s', (int) strtotime((string) $iso)), 'Y/m/d H:i')); }

    public static function page()
    {
        if (!self::can()) { wp_die('اجازه دیدن این صفحه را ندارید.', '', array('response' => 403)); }
        $tab = isset($_GET['tab']) ? sanitize_key($_GET['tab']) : 'reports';
        $tabs = array('reports' => 'گزارش‌ها', 'bench' => 'بنچمارک‌ها', 'shares' => 'نتایج کاربران', 'settings' => 'تنظیمات و انتشار');
        if (!isset($tabs[$tab])) { $tab = 'reports'; }
        echo '<div class="wrap"><h1>Mazesta Connect</h1><h2 class="nav-tab-wrapper">';
        foreach ($tabs as $k => $label) {
            echo '<a class="nav-tab' . ($k === $tab ? ' nav-tab-active' : '') . '" href="' . esc_url(admin_url('admin.php?page=mazesta-connect&tab=' . $k)) . '">' . esc_html($label) . '</a>';
        }
        echo '</h2>';
        if (!self::ensure()) { echo '<div class="notice notice-error"><p>پوشه داده‌ها ساخته نشد: <code dir="ltr">' . esc_html(self::dir()) . '</code>. دسترسی نوشتن wp-content را بررسی کنید.</p></div>'; }
        if (!empty($_GET['msg'])) { echo '<div class="notice notice-success is-dismissible"><p>انجام شد.</p></div>'; }
        if ($tab === 'reports') { self::tab_reports(); } elseif ($tab === 'bench') { self::tab_bench(); } elseif ($tab === 'shares') { self::tab_shares(); } else { self::tab_settings(); }
        echo '</div>';
    }

    private static function tab_reports()
    {
        $s = self::arg('s'); $c = self::config();
        $paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 30;
        $rows = self::read('reports');
        if ($s !== '') {
            $rows = array_filter($rows, function ($r) use ($s) {
                foreach (array('title', 'machine', 'service', 'summary') as $f) { if (isset($r[$f]) && mb_stripos((string) $r[$f], $s) !== false) { return true; } }
                return false;
            });
        }
        uasort($rows, function ($a, $b) { return strcmp((string) $b['created'], (string) $a['created']); });
        $total = count($rows);
        $rows = array_slice($rows, ($paged - 1) * $per, $per, true);
        $links = !empty($c['publicLinks']);
        $verdicts = array('Passed' => 'سالم', 'Failed' => 'ایراد دارد', 'Incomplete' => 'ناقص', '' => 'بنچمارک');
        echo '<form method="get" style="margin:12px 0"><input type="hidden" name="page" value="mazesta-connect"><input type="hidden" name="tab" value="reports">'
            . '<input type="search" name="s" value="' . esc_attr($s) . '" placeholder="شماره سرویس، نام سیستم…"> <button class="button">جستجو</button> <span class="description">' . esc_html(number_format_i18n($total)) . ' گزارش</span></form>';
        echo '<table class="widefat striped"><thead><tr><th>تاریخ</th><th>شماره سرویس</th><th>سیستم</th><th>نتیجه</th><th>خلاصه</th><th></th></tr></thead><tbody>';
        if (!$rows) { echo '<tr><td colspan="6">هنوز گزارشی از برنامه نرسیده است. در برنامه: گزارش‌ها › ارسال به سایت.</td></tr>'; }
        foreach ($rows as $id => $r) {
            $view = admin_url('admin-post.php?action=mzc_report&id=' . rawurlencode((string) $id));
            echo '<tr><td>' . self::local($r['created']) . '</td><td><strong>' . esc_html($r['service']) . '</strong></td><td dir="ltr" style="text-align:right">' . esc_html($r['machine']) . '</td>'
                . '<td>' . esc_html(isset($verdicts[$r['verdict']]) ? $verdicts[$r['verdict']] : $r['verdict']) . '</td><td>' . esc_html(mb_substr((string) $r['summary'], 0, 140)) . '</td><td style="white-space:nowrap">'
                . '<a class="button button-small button-primary" target="_blank" href="' . esc_url($view . '&print=1') . '">چاپ</a> <a class="button button-small" target="_blank" href="' . esc_url($view) . '">دیدن</a> '
                . ($links && !empty($r['token']) ? '<a class="button button-small" target="_blank" href="' . esc_url(home_url('/?mazesta_report=' . $r['token'])) . '">پیوند</a> ' : '')
                . self::post_link('report_delete', array('id' => (string) $id), 'حذف', 'button button-small', 'این گزارش از سایت پاک شود؟') . '</td></tr>';
        }
        echo '</tbody></table>';
        $pages = (int) ceil($total / $per);
        if ($pages > 1) {
            echo '<div class="tablenav"><div class="tablenav-pages">' . paginate_links(array('base' => add_query_arg('paged', '%#%'), 'format' => '', 'current' => $paged, 'total' => $pages)) . '</div></div>';
        }
    }

    private static function value_text($r) { return esc_html(self::number($r['value']) . ' ' . $r['unit']); }

    private static function tab_bench()
    {
        $all = self::read('runs');
        $pending = array(); $lists = array(); $approved = 0;
        foreach ($all as $id => $r) {
            if (empty($r['status'])) { if (count($pending) < 200) { $pending[$id] = $r; } continue; }
            $approved++;
            $t = (string) $r['table'];
            if (!isset($lists[$t])) { $lists[$t] = array('runs' => 0, 'parts' => array(), 'last' => ''); }
            $lists[$t]['runs']++; $lists[$t]['parts'][$r['part']] = true;
            if (strcmp((string) $r['received'], $lists[$t]['last']) > 0) { $lists[$t]['last'] = (string) $r['received']; }
        }
        ksort($lists);
        echo '<p>' . esc_html(number_format_i18n($approved)) . ' اجرای تأییدشده در ' . esc_html(number_format_i18n(count($lists))) . ' فهرست. '
            . self::post_link('rebuild', array(), 'ساخت دوباره فهرست‌ها') . '</p>';
        echo '<p class="description">این‌جا فقط نتایجی است که خود برنامه Mazesta Test اندازه گرفته و فرستاده است؛ هیچ عددی دستی یا نمونه وارد نمی‌شود.</p>';
        echo '<h3>در انتظار بررسی (' . esc_html(number_format_i18n(count($pending))) . ')</h3>';
        if ($pending) {
            echo '<p>' . self::post_link('run_approve', array('all' => 1), 'تأیید همه', 'button button-primary', 'همه اجراهای در انتظار وارد فهرست‌ها شوند؟') . '</p>';
            echo '<table class="widefat striped"><thead><tr><th>بنچمارک</th><th>قطعه</th><th>نتیجه</th><th>زمان اجرا</th><th></th></tr></thead><tbody>';
            foreach ($pending as $id => $r) {
                echo '<tr><td dir="ltr" style="text-align:right">' . esc_html($r['table']) . '</td><td dir="ltr" style="text-align:right">' . esc_html($r['part']) . '</td><td dir="ltr" style="text-align:right">' . self::value_text($r) . '</td><td>' . self::local($r['runAt']) . '</td><td>'
                    . self::post_link('run_approve', array('id' => (string) $id), 'تأیید', 'button button-small button-primary') . ' ' . self::post_link('run_delete', array('id' => (string) $id), 'حذف', 'button button-small', 'این اجرا پاک شود؟') . '</td></tr>';
            }
            echo '</tbody></table>';
        } else { echo '<p class="description">چیزی در انتظار نیست. اجراهایی که با کلید فروشگاه می‌رسند مستقیم وارد فهرست می‌شوند.</p>'; }
        echo '<h3>فهرست‌ها</h3><table class="widefat striped"><thead><tr><th>فهرست</th><th>مدل قطعه</th><th>اجرا</th><th>آخرین دریافت</th></tr></thead><tbody>';
        if (!$lists) { echo '<tr><td colspan="4">هنوز اجرایی بارگذاری نشده است. در برنامه: بنچمارک › ارسال نتایج به سایت.</td></tr>'; }
        foreach ($lists as $t => $l) {
            echo '<tr><td dir="ltr" style="text-align:right"><a href="' . esc_url(admin_url('admin.php?page=mazesta-connect&tab=bench&list=' . rawurlencode($t))) . '">' . esc_html($t) . '</a></td><td>' . count($l['parts']) . '</td><td>' . (int) $l['runs'] . '</td><td>' . self::local($l['last']) . '</td></tr>';
        }
        echo '</tbody></table>';
        $key = self::arg('list');
        if ($key !== '') {
            $runs = array_filter($all, function ($r) use ($key) { return !empty($r['status']) && (string) $r['table'] === $key; });
            uasort($runs, function ($a, $b) { return (float) $b['value'] <=> (float) $a['value']; });
            echo '<h3 dir="ltr" style="text-align:right">' . esc_html($key) . '</h3><table class="widefat striped"><thead><tr><th>قطعه</th><th>نتیجه</th><th>زمان اجرا</th><th></th></tr></thead><tbody>';
            foreach (array_slice($runs, 0, 500, true) as $id => $r) {
                $star = !empty($r['featured']);
                echo '<tr><td dir="ltr" style="text-align:right">' . ($star ? '★ ' : '') . esc_html($r['part']) . '</td><td dir="ltr" style="text-align:right">' . self::value_text($r) . '</td><td>' . self::local($r['runAt']) . '</td><td>'
                    . self::post_link('run_feature', $star ? array('id' => (string) $id, 'off' => 1) : array('id' => (string) $id), $star ? 'برداشتن ستاره' : 'نتیجه شاخص') . ' '
                    . self::post_link('run_delete', array('id' => (string) $id), 'حذف', 'button button-small', 'این اجرا پاک شود؟') . '</td></tr>';
            }
            echo '</tbody></table>';
        }
    }

    private static function tab_shares()
    {
        $index = self::read('shares');
        uasort($index, function ($a, $b) { return strcmp((string) $b['created'], (string) $a['created']); });
        echo '<p class="description">نتیجه‌هایی که کاربران از برنامه با «اشتراک‌گذاری آخرین نتیجه» فرستاده‌اند. هر سیستم یک صفحه دارد و با فرستادن دوباره همان صفحه تازه می‌شود.</p>';
        echo '<table class="widefat striped"><thead><tr><th>تاریخ</th><th>پردازنده</th><th>کارت گرافیک</th><th>بنچمارک</th><th></th></tr></thead><tbody>';
        if (!$index) { echo '<tr><td colspan="5">هنوز کسی نتیجه‌ای به اشتراک نگذاشته است.</td></tr>'; }
        foreach (array_slice($index, 0, 300, true) as $s) {
            echo '<tr><td>' . self::local($s['created']) . '</td><td dir="ltr" style="text-align:right">' . esc_html($s['cpu']) . '</td><td dir="ltr" style="text-align:right">' . esc_html($s['gpu']) . '</td><td>' . (int) $s['rows'] . '</td><td>'
                . '<a class="button button-small" target="_blank" href="' . esc_url(home_url('/?mazesta_share=' . $s['token'])) . '">دیدن</a> '
                . self::post_link('share_delete', array('token' => (string) $s['token']), 'حذف', 'button button-small', 'این صفحه پاک شود؟') . '</td></tr>';
        }
        echo '</tbody></table>';
    }

    private static function tab_settings()
    {
        $admin = current_user_can('manage_options'); $c = self::config();
        $dir = self::release_dir(); $manifest = is_readable($dir . '/update.json') ? json_decode((string) file_get_contents($dir . '/update.json'), true) : null;
        echo '<h3>نسخه منتشرشده برنامه</h3>';
        if (is_array($manifest) && isset($manifest['app']['version'])) {
            $zip = $dir . '/' . basename((string) $manifest['app']['file']);
            echo '<p>نسخه <strong dir="ltr">' . esc_html($manifest['app']['version']) . '</strong>، ' . esc_html(size_format((int) $manifest['app']['size'])) . '، فایل <code dir="ltr">' . esc_html($manifest['app']['file']) . '</code> '
                . (is_file($zip) ? '✔' : '<strong style="color:#b32d2e">روی سایت نیست</strong>') . ' ' . (is_file($dir . '/update.json.sig') ? '' : '<strong style="color:#b32d2e">امضا (update.json.sig) نیست</strong>') . '</p>';
        } else {
            echo '<p>هنوز نسخه‌ای منتشر نشده است. روی سیستم فروشگاه: <code dir="ltr">pwsh tools/release.ps1 -App -NotesFa notes-fa.txt -Upload</code></p>';
        }
        echo '<p class="description">برنامه‌ها این نشانی را می‌خوانند: <code dir="ltr">' . esc_html(home_url('/mazesta/update.json')) . '</code> — این پوشه را از کش مستثنا کنید.</p>';
        echo '<p class="description">داده‌های این افزونه در پایگاه داده وردپرس نیست؛ در این پوشه است: <code dir="ltr">' . esc_html(self::dir()) . '</code> (برای پشتیبان‌گیری همین پوشه را نگه دارید).</p>';
        if (!$admin) { return; }
        echo '<h3>کلیدها و تنظیمات</h3><form method="post" action="' . esc_url(admin_url('admin-post.php')) . '"><input type="hidden" name="action" value="mzc_settings">';
        wp_nonce_field('mzc_settings');
        echo '<table class="form-table"><tr><th>کلید سایت</th><td><code dir="ltr" style="user-select:all">' . esc_html((string) $c['key']) . '</code><p class="description">در برنامه: به‌روزرسانی برنامه › اتصال به سایت فروشگاه. روی هر سیستمی که باید گزارش بفرستد یک بار وارد می‌شود و با پوشه Data برنامه جابه‌جا می‌شود.</p>'
            . '<label><input type="checkbox" name="new_key" value="1"> کلید تازه بساز (کلید قبلی از کار می‌افتد)</label></td></tr>'
            . '<tr><th>کلید انتشار</th><td><code dir="ltr" style="user-select:all">' . esc_html((string) $c['releaseKey']) . '</code><p class="description">فقط روی سیستمی که نسخه تازه برنامه را منتشر می‌کند (فایل <code dir="ltr">G:\\Mazesta-Keys\\site-release-key.txt</code>). به کسی ندهید.</p>'
            . '<label><input type="checkbox" name="new_release_key" value="1"> کلید تازه بساز</label></td></tr>'
            . '<tr><th>اشتراک‌گذاری کاربران</th><td><label><input type="checkbox" name="sharing" value="1"' . checked(!empty($c['sharing']), true, false) . '> کاربران بتوانند آخرین نتیجه بنچمارکشان را بدون کلید بفرستند و پیوند صفحه آن را بگیرند</label></td></tr>'
            . '<tr><th>بارگذاری بدون کلید</th><td><label><input type="checkbox" name="open_uploads" value="1"' . checked(!empty($c['openUploads']), true, false) . '> نتیجه کاربران بدون کلید به صف بررسی فهرست‌های مقایسه هم برود (بعد از تأیید شما وارد فهرست می‌شود)</label></td></tr>'
            . '<tr><th>پیوند گزارش</th><td><label><input type="checkbox" name="public_links" value="1"' . checked(!empty($c['publicLinks']), true, false) . '> هر گزارش با پیوند خودش بدون ورود به سایت هم باز شود (هر کس پیوند را داشته باشد گزارش را می‌بیند)</label></td></tr></table>';
        submit_button('ذخیره');
        echo '</form>';
    }
}

Mazesta_Connect::boot();
