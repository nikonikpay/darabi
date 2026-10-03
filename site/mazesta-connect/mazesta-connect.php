<?php
/**
 * Plugin Name: Mazesta Connect
 * Description: پل ارتباط برنامه Mazesta Test با سایت: خلاصه گزارش‌های آزمون برای چاپ روی کیس‌های سرویسی، بانک نتایج بنچمارک و فهرست‌های مقایسه، و انتشار نسخه تازه برنامه.
 * Version: 1.0.0
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
 *   GET  bench/index, benchdb/* the comparison lists built from the approved runs, one file per benchmark, version and settings
 *   POST release/chunk, commit  the signed update folder (release key)     -> written to /mazesta/ in the site's root, where the app reads it
 * The lists are built the way the app builds them (BenchmarkPeers.Aggregate): one row per part model, the median of each system's best run.
 */
final class Mazesta_Connect
{
    const VERSION = '1.0.0';
    const DB_VERSION = 1;
    const NS = 'mazesta/v1';
    const CAP = 'mazesta_connect';
    const MAX_HTML = 800000;
    const MAX_RUNS = 500;

    public static function boot()
    {
        register_activation_hook(__FILE__, array(__CLASS__, 'install'));
        add_action('plugins_loaded', array(__CLASS__, 'upgrade'));
        add_action('rest_api_init', array(__CLASS__, 'routes'));
        add_action('admin_menu', array(__CLASS__, 'menu'));
        add_action('init', array(__CLASS__, 'public_report'));
        foreach (array('report', 'report_delete', 'run_approve', 'run_delete', 'run_feature', 'settings', 'rebuild') as $a) {
            add_action('admin_post_mzc_' . $a, array(__CLASS__, 'act_' . $a));
        }
    }

    /* ---------- storage ---------- */

    private static function t($name) { global $wpdb; return $wpdb->prefix . 'mazesta_' . $name; }

    public static function install()
    {
        global $wpdb;
        require_once ABSPATH . 'wp-admin/includes/upgrade.php';
        $c = $wpdb->get_charset_collate();
        dbDelta('CREATE TABLE ' . self::t('reports') . " (
            id bigint(20) unsigned NOT NULL AUTO_INCREMENT,
            report_id varchar(64) NOT NULL,
            token char(32) NOT NULL,
            title varchar(255) NOT NULL DEFAULT '',
            machine varchar(190) NOT NULL DEFAULT '',
            service varchar(190) NOT NULL DEFAULT '',
            verdict varchar(40) NOT NULL DEFAULT '',
            kind varchar(40) NOT NULL DEFAULT '',
            summary text NULL,
            app_version varchar(40) NOT NULL DEFAULT '',
            created_at datetime NOT NULL,
            received_at datetime NOT NULL,
            html longtext NULL,
            PRIMARY KEY  (id),
            UNIQUE KEY report_id (report_id),
            KEY token (token),
            KEY service (service)
        ) $c;");
        dbDelta('CREATE TABLE ' . self::t('runs') . " (
            id bigint(20) unsigned NOT NULL AUTO_INCREMENT,
            run_id varchar(64) NOT NULL,
            table_key varchar(190) NOT NULL DEFAULT '',
            part varchar(190) NOT NULL DEFAULT '',
            value double NOT NULL DEFAULT 0,
            unit varchar(40) NOT NULL DEFAULT '',
            higher tinyint(1) NOT NULL DEFAULT 1,
            status tinyint(1) NOT NULL DEFAULT 0,
            featured tinyint(1) NOT NULL DEFAULT 0,
            oc tinyint(1) NULL,
            note varchar(190) NULL,
            mark_at datetime NULL,
            run_at datetime NOT NULL,
            received_at datetime NOT NULL,
            data longtext NULL,
            PRIMARY KEY  (id),
            UNIQUE KEY run_id (run_id),
            KEY table_key (table_key),
            KEY status (status)
        ) $c;");
        if (!get_option('mzc_key')) { add_option('mzc_key', self::new_key(), '', 'no'); }
        if (!get_option('mzc_release_key')) { add_option('mzc_release_key', self::new_key(), '', 'no'); }
        add_option('mzc_open_uploads', '0', '', 'no');
        add_option('mzc_public_links', '0', '', 'no');
        update_option('mzc_db', self::DB_VERSION, 'no');
        foreach (array('administrator', 'shop_manager') as $r) {
            $role = get_role($r);
            if ($role) { $role->add_cap(self::CAP); }
        }
    }

    public static function upgrade()
    {
        if ((int) get_option('mzc_db') !== self::DB_VERSION) { self::install(); }
    }

    private static function new_key() { return 'mz_' . bin2hex(random_bytes(24)); }

    /* ---------- REST ---------- */

    public static function routes()
    {
        $open = '__return_true';
        register_rest_route(self::NS, '/status', array('methods' => 'GET', 'callback' => array(__CLASS__, 'rest_status'), 'permission_callback' => $open));
        register_rest_route(self::NS, '/reports', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_report'), 'permission_callback' => array(__CLASS__, 'need_key')));
        register_rest_route(self::NS, '/bench/runs', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_runs'), 'permission_callback' => $open));
        register_rest_route(self::NS, '/bench/index', array('methods' => 'GET', 'callback' => array(__CLASS__, 'rest_index'), 'permission_callback' => $open));
        register_rest_route(self::NS, '/benchdb/(?P<file>[A-Za-z0-9][A-Za-z0-9._@=-]*\.json)', array('methods' => 'GET', 'callback' => array(__CLASS__, 'rest_list'), 'permission_callback' => $open));
        register_rest_route(self::NS, '/release/chunk', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_chunk'), 'permission_callback' => array(__CLASS__, 'need_release_key')));
        register_rest_route(self::NS, '/release/commit', array('methods' => 'POST', 'callback' => array(__CLASS__, 'rest_commit'), 'permission_callback' => array(__CLASS__, 'need_release_key')));
    }

    private static function key_state($req, $option = 'mzc_key')
    {
        $given = (string) $req->get_header('x_mazesta_key');
        if ($given === '') { return 'missing'; }
        $want = (string) get_option($option);
        return $want !== '' && hash_equals($want, $given) ? 'ok' : 'wrong';
    }

    public static function need_key($req)
    {
        return self::key_state($req) === 'ok' ? true : new WP_Error('mazesta_key', 'The site key is missing or wrong.', array('status' => 401));
    }

    public static function need_release_key($req)
    {
        return self::key_state($req, 'mzc_release_key') === 'ok' ? true : new WP_Error('mazesta_key', 'The release key is missing or wrong.', array('status' => 401));
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
        global $wpdb;
        $key = self::key_state($req);
        $out = array('name' => 'Mazesta Connect', 'version' => self::VERSION, 'key' => $key, 'openUploads' => get_option('mzc_open_uploads') === '1');
        if ($key === 'ok') {
            $out['reports'] = (int) $wpdb->get_var('SELECT COUNT(*) FROM ' . self::t('reports'));
            $out['runs'] = (int) $wpdb->get_var('SELECT COUNT(*) FROM ' . self::t('runs') . ' WHERE status = 1');
            $out['pending'] = (int) $wpdb->get_var('SELECT COUNT(*) FROM ' . self::t('runs') . ' WHERE status = 0');
        }
        return self::fresh($out);
    }

    private static function when($iso)
    {
        $ts = is_string($iso) ? strtotime($iso) : false;
        return gmdate('Y-m-d H:i:s', $ts ? $ts : time());
    }

    private static function clip($s, $n) { return is_string($s) ? mb_substr(trim(wp_strip_all_tags($s)), 0, $n) : ''; }

    public static function rest_report($req)
    {
        global $wpdb;
        $p = $req->get_json_params();
        $id = isset($p['id']) ? (string) $p['id'] : '';
        $html = isset($p['html']) ? (string) $p['html'] : '';
        if (!preg_match('/^[A-Za-z0-9-]{8,64}$/', $id)) { return new WP_Error('mazesta_report', 'The report id is not valid.', array('status' => 400)); }
        if ($html === '' || strlen($html) > self::MAX_HTML) { return new WP_Error('mazesta_report', 'The summary is missing or too large.', array('status' => 400)); }
        $row = array(
            'title' => self::clip(isset($p['title']) ? $p['title'] : '', 255),
            'machine' => self::clip(isset($p['machine']) ? $p['machine'] : '', 190),
            'service' => self::clip(isset($p['service']) ? $p['service'] : '', 190),
            'verdict' => self::clip(isset($p['verdict']) ? $p['verdict'] : '', 40),
            'kind' => self::clip(isset($p['kind']) ? $p['kind'] : '', 40),
            'summary' => self::clip(isset($p['summary']) ? $p['summary'] : '', 2000),
            'app_version' => self::clip(isset($p['appVersion']) ? $p['appVersion'] : '', 40),
            'created_at' => self::when(isset($p['created']) ? $p['created'] : null),
            'received_at' => gmdate('Y-m-d H:i:s'),
            'html' => $html,
        );
        $have = $wpdb->get_row($wpdb->prepare('SELECT id, token FROM ' . self::t('reports') . ' WHERE report_id = %s', $id));
        if ($have) {
            $wpdb->update(self::t('reports'), $row, array('id' => $have->id));
            $rid = (int) $have->id; $token = $have->token;
        } else {
            $token = bin2hex(random_bytes(16));
            $row['report_id'] = $id; $row['token'] = $token;
            if (!$wpdb->insert(self::t('reports'), $row)) { return new WP_Error('mazesta_report', 'The report could not be saved.', array('status' => 500)); }
            $rid = (int) $wpdb->insert_id;
        }
        return self::fresh(array(
            'id' => $rid, 'updated' => (bool) $have,
            'url' => admin_url('admin-post.php?action=mzc_report&id=' . $rid),
            'link' => get_option('mzc_public_links') === '1' ? home_url('/?mazesta_report=' . $token) : null,
        ));
    }

    public static function rest_runs($req)
    {
        global $wpdb;
        $key = self::key_state($req);
        $trusted = $key === 'ok';
        if (!$trusted) {
            if ($key === 'wrong' || get_option('mzc_open_uploads') !== '1') { return new WP_Error('mazesta_key', 'The site key is missing or wrong.', array('status' => 401)); }
            $ip = isset($_SERVER['REMOTE_ADDR']) ? (string) $_SERVER['REMOTE_ADDR'] : '';
            $slot = 'mzc_up_' . md5($ip);
            $n = (int) get_transient($slot);
            if ($n >= 20) { return new WP_Error('mazesta_busy', 'Too many uploads from this address; try again in an hour.', array('status' => 429)); }
            set_transient($slot, $n + 1, HOUR_IN_SECONDS);
        }
        $body = json_decode($req->get_body());   // objects stay objects, so a run goes back out as it came in
        if (!is_object($body) || !isset($body->runs) || !is_array($body->runs)) { return new WP_Error('mazesta_runs', 'No runs in the request.', array('status' => 400)); }
        if (count($body->runs) > self::MAX_RUNS) { return new WP_Error('mazesta_runs', 'Too many runs in one request.', array('status' => 413)); }
        $higher = isset($body->higher) && is_object($body->higher) ? (array) $body->higher : array();
        $added = 0; $known = 0; $bad = 0;
        foreach ($body->runs as $run) {
            if (!is_object($run) || !isset($run->id, $run->benchmark, $run->part, $run->system, $run->value, $run->unit)
                || !is_string($run->id) || !preg_match('/^[A-Za-z0-9-]{8,64}$/', $run->id) || !is_string($run->benchmark) || !preg_match('/^[A-Za-z0-9._-]{1,80}$/', $run->benchmark)
                || !is_string($run->part) || trim($run->part) === '' || !is_string($run->system) || !is_string($run->unit)
                || !(is_int($run->value) || is_float($run->value)) || !($run->value > 0) || !is_finite((float) $run->value)) { $bad++; continue; }
            $settings = isset($run->settings) && is_string($run->settings) ? $run->settings : '';
            $version = isset($run->version) ? (int) $run->version : 1;
            $table = $run->benchmark . '@' . $version . ($settings !== '' ? '|' . $settings : '');
            if (strlen($table) > 190) { $bad++; continue; }
            if (!$trusted) { unset($run->machine); }   // a stranger's computer name is not ours to keep
            $ok = $wpdb->query($wpdb->prepare(
                'INSERT IGNORE INTO ' . self::t('runs') . ' (run_id, table_key, part, value, unit, higher, status, run_at, received_at, data) VALUES (%s, %s, %s, %f, %s, %d, %d, %s, %s, %s)',
                $run->id, $table, mb_substr($run->part, 0, 190), (float) $run->value, mb_substr($run->unit, 0, 40),
                isset($higher[$run->benchmark]) && !$higher[$run->benchmark] ? 0 : 1, $trusted ? 1 : 0,
                self::when(isset($run->at) ? $run->at : null), gmdate('Y-m-d H:i:s'), wp_json_encode($run, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES)
            ));
            if ($ok) { $added++; } else { $known++; }
        }
        // The shop's own marks (featured, overclocked) come with its runs; for a run marked on two copies the later mark wins.
        $marked = 0;
        if ($trusted && isset($body->marks) && is_object($body->marks)) {
            foreach ((array) $body->marks as $id => $m) {
                if (!is_object($m) || !preg_match('/^[A-Za-z0-9-]{8,64}$/', (string) $id)) { continue; }
                $at = self::when(isset($m->at) ? $m->at : null);
                $marked += (int) $wpdb->query($wpdb->prepare(
                    'UPDATE ' . self::t('runs') . ' SET featured = %d, oc = ' . (isset($m->overclocked) && is_bool($m->overclocked) ? ($m->overclocked ? '1' : '0') : 'NULL') . ', note = %s, mark_at = %s WHERE run_id = %s AND (mark_at IS NULL OR mark_at < %s)',
                    !empty($m->featured) ? 1 : 0, isset($m->note) && is_string($m->note) ? mb_substr($m->note, 0, 190) : '', $at, (string) $id, $at
                ));
            }
        }
        $lists = ($trusted && ($added > 0 || $marked > 0)) ? self::rebuild() : null;
        return self::fresh(array('added' => $added, 'known' => $known, 'rejected' => $bad, 'pending' => !$trusted, 'marked' => $marked, 'lists' => $lists));
    }

    /* ---------- the comparison lists ---------- */

    private static function db_dir()
    {
        $up = wp_upload_dir();
        return trailingslashit($up['basedir']) . 'mazesta-connect/benchdb';
    }

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
        global $wpdb;
        $dir = self::db_dir();
        if (!wp_mkdir_p($dir)) { return 0; }
        $tables = array();
        $rows = $wpdb->get_results('SELECT table_key, higher, featured, oc, note, data FROM ' . self::t('runs') . ' WHERE status = 1 ORDER BY id');
        foreach ($rows as $row) {
            $run = json_decode($row->data);
            if (!is_object($run) || !isset($run->value, $run->part, $run->system, $run->unit)) { continue; }
            $oc = $row->oc !== null ? (bool) (int) $row->oc : !empty($run->overclocked);
            $tables[$row->table_key][] = array('run' => $run, 'oc' => $oc, 'higher' => (bool) (int) $row->higher, 'featured' => (bool) (int) $row->featured, 'note' => $row->note, 'ts' => isset($run->at) ? (int) strtotime($run->at) : 0);
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
                    if ((string) $x['note'] !== '') { $f['note'] = $x['note']; }
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
        foreach (glob($dir . '/*.json') ?: array() as $f) {
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
        usort($names, function ($a, $b) { return (int) (substr($a, 0, 6) === 'update') <=> (int) (substr($b, 0, 6) === 'update') ?: strcmp($a, $b); });
        foreach ($names as $name) {
            if (!wp_mkdir_p(dirname($dir . '/' . $name)) || !rename($in . '/' . $name, $dir . '/' . $name)) { return new WP_Error('mazesta_release', 'Could not put ' . $name . ' in place.', array('status' => 500)); }
        }
        // The zip the manifest offers stays; older ones go.
        $manifest = json_decode((string) file_get_contents($dir . '/update.json'), true);
        $keep = is_array($manifest) && isset($manifest['app']['file']) ? (string) $manifest['app']['file'] : '';
        foreach (glob($dir . '/MazestaWeb-*.zip') ?: array() as $zip) { if (basename($zip) !== $keep) { @unlink($zip); } }
        // The same for the signed comparison lists: the ones the manifest no longer names go.
        $listed = array();
        if (is_array($manifest) && isset($manifest['data']) && is_array($manifest['data'])) { foreach ($manifest['data'] as $d) { if (isset($d['file'])) { $listed[(string) $d['file']] = true; } } }
        foreach (glob($dir . '/benchdb/*.json') ?: array() as $list) { if (!isset($listed['benchdb/' . basename($list)])) { @unlink($list); } }
        return self::fresh(array('ok' => true, 'version' => is_array($manifest) && isset($manifest['app']['version']) ? $manifest['app']['version'] : null));
    }

    /* ---------- a report's page ---------- */

    private static function show_report($row, $print)
    {
        if (!$row) { wp_die('گزارش پیدا نشد.', '', array('response' => 404)); }
        $nonce = bin2hex(random_bytes(12));
        nocache_headers();
        header('Content-Type: text/html; charset=utf-8');
        header('X-Robots-Tag: noindex, nofollow');
        header('X-LiteSpeed-Cache-Control: no-cache');
        // The summary came from outside: nothing in it may run or load. Only the print button's script, named by its nonce, runs.
        header("Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; font-src data:; img-src data:; script-src 'nonce-" . $nonce . "'");
        $html = preg_replace('/<meta\s+http-equiv="Content-Security-Policy"[^>]*>/i', '', (string) $row->html);
        $bar = '<style>.mzc-bar{position:fixed;top:8px;left:8px;z-index:9;font:14px Tahoma,sans-serif}.mzc-bar button{padding:8px 18px;border:0;border-radius:6px;background:#0b6;color:#fff;cursor:pointer}@media print{.mzc-bar{display:none}}</style>'
            . '<div class="mzc-bar"><button id="mzc-print" type="button">چاپ</button></div>'
            . '<script nonce="' . $nonce . '">document.getElementById("mzc-print").addEventListener("click",function(){window.print()});' . ($print ? 'window.addEventListener("load",function(){window.print()});' : '') . '</script>';
        echo stripos($html, '</body>') !== false ? preg_replace('/<\/body>/i', $bar . '</body>', $html, 1) : $html . $bar;
        exit;
    }

    public static function act_report()
    {
        global $wpdb;
        if (!current_user_can(self::CAP)) { wp_die('اجازه دیدن گزارش‌ها را ندارید.', '', array('response' => 403)); }
        $id = isset($_GET['id']) ? (int) $_GET['id'] : 0;
        self::show_report($wpdb->get_row($wpdb->prepare('SELECT html FROM ' . self::t('reports') . ' WHERE id = %d', $id)), !empty($_GET['print']));
    }

    /** A report by its link (a 128-bit random token), for a colleague without an account; only while the shop has switched links on. */
    public static function public_report()
    {
        global $wpdb;
        if (empty($_GET['mazesta_report'])) { return; }
        $token = (string) $_GET['mazesta_report'];
        if (get_option('mzc_public_links') !== '1' || !preg_match('/^[0-9a-f]{32}$/', $token)) { wp_die('گزارش پیدا نشد.', '', array('response' => 404)); }
        self::show_report($wpdb->get_row($wpdb->prepare('SELECT html FROM ' . self::t('reports') . ' WHERE token = %s', $token)), !empty($_GET['print']));
    }

    /* ---------- dashboard ---------- */

    public static function menu()
    {
        add_menu_page('Mazesta Connect', 'Mazesta Connect', self::CAP, 'mazesta-connect', array(__CLASS__, 'page'), 'dashicons-desktop', 58);
    }

    private static function guard($action)
    {
        if (!current_user_can(self::CAP)) { wp_die('اجازه این کار را ندارید.', '', array('response' => 403)); }
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

    public static function act_report_delete()
    {
        global $wpdb;
        self::guard('report_delete');
        $wpdb->delete(self::t('reports'), array('id' => isset($_GET['id']) ? (int) $_GET['id'] : 0));
        self::back('reports', 'deleted');
    }

    public static function act_run_approve()
    {
        global $wpdb;
        self::guard('run_approve');
        if (!empty($_GET['all'])) { $wpdb->query('UPDATE ' . self::t('runs') . ' SET status = 1 WHERE status = 0'); }
        else { $wpdb->update(self::t('runs'), array('status' => 1), array('id' => isset($_GET['id']) ? (int) $_GET['id'] : 0)); }
        self::rebuild();
        self::back('bench', 'approved');
    }

    public static function act_run_delete()
    {
        global $wpdb;
        self::guard('run_delete');
        $wpdb->delete(self::t('runs'), array('id' => isset($_GET['id']) ? (int) $_GET['id'] : 0));
        self::rebuild();
        self::back('bench', 'deleted');
    }

    public static function act_run_feature()
    {
        global $wpdb;
        self::guard('run_feature');
        $wpdb->update(self::t('runs'), array('featured' => empty($_GET['off']) ? 1 : 0, 'mark_at' => gmdate('Y-m-d H:i:s')), array('id' => isset($_GET['id']) ? (int) $_GET['id'] : 0));
        self::rebuild();
        self::back('bench', 'saved');
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
        update_option('mzc_open_uploads', empty($_POST['open_uploads']) ? '0' : '1', 'no');
        update_option('mzc_public_links', empty($_POST['public_links']) ? '0' : '1', 'no');
        if (!empty($_POST['new_key'])) { update_option('mzc_key', self::new_key(), 'no'); }
        if (!empty($_POST['new_release_key'])) { update_option('mzc_release_key', self::new_key(), 'no'); }
        self::back('settings', 'saved');
    }

    private static function local($utc) { return esc_html(get_date_from_gmt($utc, 'Y/m/d H:i')); }

    public static function page()
    {
        $tab = isset($_GET['tab']) ? sanitize_key($_GET['tab']) : 'reports';
        $tabs = array('reports' => 'گزارش‌ها', 'bench' => 'بنچمارک‌ها', 'settings' => 'تنظیمات و انتشار');
        if (!isset($tabs[$tab])) { $tab = 'reports'; }
        echo '<div class="wrap"><h1>Mazesta Connect</h1><h2 class="nav-tab-wrapper">';
        foreach ($tabs as $k => $label) {
            echo '<a class="nav-tab' . ($k === $tab ? ' nav-tab-active' : '') . '" href="' . esc_url(admin_url('admin.php?page=mazesta-connect&tab=' . $k)) . '">' . esc_html($label) . '</a>';
        }
        echo '</h2>';
        if (!empty($_GET['msg'])) { echo '<div class="notice notice-success is-dismissible"><p>انجام شد.</p></div>'; }
        if ($tab === 'reports') { self::tab_reports(); } elseif ($tab === 'bench') { self::tab_bench(); } else { self::tab_settings(); }
        echo '</div>';
    }

    private static function tab_reports()
    {
        global $wpdb;
        $s = isset($_GET['s']) ? sanitize_text_field(wp_unslash($_GET['s'])) : '';
        $paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 30;
        $where = '1=1'; $args = array();
        if ($s !== '') {
            $like = '%' . $wpdb->esc_like($s) . '%';
            $where = '(title LIKE %s OR machine LIKE %s OR service LIKE %s OR summary LIKE %s)'; $args = array($like, $like, $like, $like);
        }
        $sql = 'SELECT SQL_CALC_FOUND_ROWS id, token, title, machine, service, verdict, kind, summary, created_at, received_at FROM ' . self::t('reports') . " WHERE $where ORDER BY created_at DESC LIMIT %d OFFSET %d";
        $rows = $wpdb->get_results($wpdb->prepare($sql, array_merge($args, array($per, ($paged - 1) * $per))));
        $total = (int) $wpdb->get_var('SELECT FOUND_ROWS()');
        $links = get_option('mzc_public_links') === '1';
        $verdicts = array('Passed' => 'سالم', 'Failed' => 'ایراد دارد', 'Incomplete' => 'ناقص', '' => 'بنچمارک');
        echo '<form method="get" style="margin:12px 0"><input type="hidden" name="page" value="mazesta-connect"><input type="hidden" name="tab" value="reports">'
            . '<input type="search" name="s" value="' . esc_attr($s) . '" placeholder="شماره سرویس، نام سیستم…"> <button class="button">جستجو</button> <span class="description">' . esc_html(number_format_i18n($total)) . ' گزارش</span></form>';
        echo '<table class="widefat striped"><thead><tr><th>تاریخ</th><th>شماره سرویس</th><th>سیستم</th><th>نتیجه</th><th>خلاصه</th><th></th></tr></thead><tbody>';
        if (!$rows) { echo '<tr><td colspan="6">هنوز گزارشی از برنامه نرسیده است. در برنامه: گزارش‌ها › ارسال به سایت.</td></tr>'; }
        foreach ($rows as $r) {
            $view = admin_url('admin-post.php?action=mzc_report&id=' . (int) $r->id);
            echo '<tr><td>' . self::local($r->created_at) . '</td><td><strong>' . esc_html($r->service) . '</strong></td><td dir="ltr" style="text-align:right">' . esc_html($r->machine) . '</td>'
                . '<td>' . esc_html(isset($verdicts[$r->verdict]) ? $verdicts[$r->verdict] : $r->verdict) . '</td><td>' . esc_html(mb_substr((string) $r->summary, 0, 140)) . '</td><td style="white-space:nowrap">'
                . '<a class="button button-small button-primary" target="_blank" href="' . esc_url($view . '&print=1') . '">چاپ</a> <a class="button button-small" target="_blank" href="' . esc_url($view) . '">دیدن</a> '
                . ($links ? '<a class="button button-small" target="_blank" href="' . esc_url(home_url('/?mazesta_report=' . $r->token)) . '">پیوند</a> ' : '')
                . self::post_link('report_delete', array('id' => (int) $r->id), 'حذف', 'button button-small', 'این گزارش از سایت پاک شود؟') . '</td></tr>';
        }
        echo '</tbody></table>';
        $pages = (int) ceil($total / $per);
        if ($pages > 1) {
            echo '<div class="tablenav"><div class="tablenav-pages">' . paginate_links(array('base' => add_query_arg('paged', '%#%'), 'format' => '', 'current' => $paged, 'total' => $pages)) . '</div></div>';
        }
    }

    private static function tab_bench()
    {
        global $wpdb;
        $t = self::t('runs');
        $approved = (int) $wpdb->get_var("SELECT COUNT(*) FROM $t WHERE status = 1");
        $pending = $wpdb->get_results("SELECT id, table_key, part, value, unit, run_at, received_at FROM $t WHERE status = 0 ORDER BY id DESC LIMIT 200");
        $lists = $wpdb->get_results("SELECT table_key, COUNT(*) AS runs, COUNT(DISTINCT part) AS parts, MAX(received_at) AS last FROM $t WHERE status = 1 GROUP BY table_key ORDER BY table_key");
        echo '<p>' . esc_html(number_format_i18n($approved)) . ' اجرای تأییدشده در ' . esc_html(number_format_i18n(count($lists))) . ' فهرست. '
            . self::post_link('rebuild', array(), 'ساخت دوباره فهرست‌ها') . '</p>';
        echo '<h3>در انتظار بررسی (' . esc_html(number_format_i18n(count($pending))) . ')</h3>';
        if ($pending) {
            echo '<p>' . self::post_link('run_approve', array('all' => 1), 'تأیید همه', 'button button-primary', 'همه اجراهای در انتظار وارد فهرست‌ها شوند؟') . '</p>';
            echo '<table class="widefat striped"><thead><tr><th>بنچمارک</th><th>قطعه</th><th>نتیجه</th><th>زمان اجرا</th><th></th></tr></thead><tbody>';
            foreach ($pending as $r) {
                echo '<tr><td dir="ltr" style="text-align:right">' . esc_html($r->table_key) . '</td><td dir="ltr" style="text-align:right">' . esc_html($r->part) . '</td><td dir="ltr" style="text-align:right">' . esc_html(rtrim(rtrim(number_format((float) $r->value, 2, '.', ''), '0'), '.') . ' ' . $r->unit) . '</td><td>' . self::local($r->run_at) . '</td><td>'
                    . self::post_link('run_approve', array('id' => (int) $r->id), 'تأیید', 'button button-small button-primary') . ' ' . self::post_link('run_delete', array('id' => (int) $r->id), 'حذف', 'button button-small', 'این اجرا پاک شود؟') . '</td></tr>';
            }
            echo '</tbody></table>';
        } else { echo '<p class="description">چیزی در انتظار نیست. اجراهایی که با کلید فروشگاه می‌رسند مستقیم وارد فهرست می‌شوند.</p>'; }
        echo '<h3>فهرست‌ها</h3><table class="widefat striped"><thead><tr><th>فهرست</th><th>مدل قطعه</th><th>اجرا</th><th>آخرین دریافت</th></tr></thead><tbody>';
        if (!$lists) { echo '<tr><td colspan="4">هنوز اجرایی بارگذاری نشده است. در برنامه: بنچمارک › ارسال نتایج به سایت.</td></tr>'; }
        foreach ($lists as $l) {
            echo '<tr><td dir="ltr" style="text-align:right"><a href="' . esc_url(admin_url('admin.php?page=mazesta-connect&tab=bench&list=' . rawurlencode($l->table_key))) . '">' . esc_html($l->table_key) . '</a></td><td>' . (int) $l->parts . '</td><td>' . (int) $l->runs . '</td><td>' . self::local($l->last) . '</td></tr>';
        }
        echo '</tbody></table>';
        if (!empty($_GET['list'])) {
            $key = sanitize_text_field(wp_unslash($_GET['list']));
            $runs = $wpdb->get_results($wpdb->prepare("SELECT id, part, value, unit, featured, run_at FROM $t WHERE status = 1 AND table_key = %s ORDER BY value DESC LIMIT 500", $key));
            echo '<h3 dir="ltr" style="text-align:right">' . esc_html($key) . '</h3><table class="widefat striped"><thead><tr><th>قطعه</th><th>نتیجه</th><th>زمان اجرا</th><th></th></tr></thead><tbody>';
            foreach ($runs as $r) {
                echo '<tr><td dir="ltr" style="text-align:right">' . ($r->featured ? '★ ' : '') . esc_html($r->part) . '</td><td dir="ltr" style="text-align:right">' . esc_html(rtrim(rtrim(number_format((float) $r->value, 2, '.', ''), '0'), '.') . ' ' . $r->unit) . '</td><td>' . self::local($r->run_at) . '</td><td>'
                    . self::post_link('run_feature', $r->featured ? array('id' => (int) $r->id, 'off' => 1) : array('id' => (int) $r->id), $r->featured ? 'برداشتن ستاره' : 'نتیجه شاخص') . ' '
                    . self::post_link('run_delete', array('id' => (int) $r->id), 'حذف', 'button button-small', 'این اجرا پاک شود؟') . '</td></tr>';
            }
            echo '</tbody></table>';
        }
    }

    private static function tab_settings()
    {
        $admin = current_user_can('manage_options');
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
        if (!$admin) { return; }
        echo '<h3>کلیدها و تنظیمات</h3><form method="post" action="' . esc_url(admin_url('admin-post.php')) . '"><input type="hidden" name="action" value="mzc_settings">';
        wp_nonce_field('mzc_settings');
        echo '<table class="form-table"><tr><th>کلید سایت</th><td><code dir="ltr" style="user-select:all">' . esc_html((string) get_option('mzc_key')) . '</code><p class="description">در برنامه: تنظیمات › اتصال به سایت. با این کلید گزارش و نتیجه بنچمارک فرستاده می‌شود.</p>'
            . '<label><input type="checkbox" name="new_key" value="1"> کلید تازه بساز (کلید قبلی از کار می‌افتد)</label></td></tr>'
            . '<tr><th>کلید انتشار</th><td><code dir="ltr" style="user-select:all">' . esc_html((string) get_option('mzc_release_key')) . '</code><p class="description">فقط روی سیستمی که نسخه تازه برنامه را منتشر می‌کند (فایل <code dir="ltr">G:\\Mazesta-Keys\\site-release-key.txt</code>). به کسی ندهید.</p>'
            . '<label><input type="checkbox" name="new_release_key" value="1"> کلید تازه بساز</label></td></tr>'
            . '<tr><th>بارگذاری بدون کلید</th><td><label><input type="checkbox" name="open_uploads" value="1"' . checked(get_option('mzc_open_uploads'), '1', false) . '> نسخه‌های بدون کلید هم بتوانند نتیجه بنچمارک بفرستند (به صف بررسی می‌رود، نه مستقیم به فهرست‌ها)</label></td></tr>'
            . '<tr><th>پیوند گزارش</th><td><label><input type="checkbox" name="public_links" value="1"' . checked(get_option('mzc_public_links'), '1', false) . '> هر گزارش با پیوند خودش بدون ورود به سایت هم باز شود (هر کس پیوند را داشته باشد گزارش را می‌بیند)</label></td></tr></table>';
        submit_button('ذخیره');
        echo '</form>';
    }
}

Mazesta_Connect::boot();
