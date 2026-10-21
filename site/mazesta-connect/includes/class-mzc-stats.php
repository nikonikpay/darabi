<?php
if (!defined('ABSPATH')) { exit; }

/**
 * What the app's anonymous usage statistics say, kept in the CRM's database (MZC_Crm_Db makes the tables; never in WordPress' own):
 *   systems       - one row per random installation id: the parts' names the app sent, a lower-case 'hw' text of them all for the hardware search, the version, the note
 *             written here, and 'ack' (the newest message the system has fetched);
 *   system_events - one row per event (what ran, how it ended, the card) with its figures as JSON; indexed by time, system, kind and card, so a page reads only its own rows;
 *   system_logs   - pieces of the app's own log and Windows' crash records of it (sent with the statistics), kept 90 days.
 * Nothing here is invented: a figure the app did not send stays empty.
 */
final class MZC_Stats
{
    const LOG_DAYS = 90;
    private static $labels = null;

    public static function labels($group = null, $key = null)
    {
        if (self::$labels === null) { self::$labels = require MZC_DIR . 'includes/data-labels.php'; }
        if ($group === null) { return self::$labels; }
        $g = isset(self::$labels[$group]) ? self::$labels[$group] : array();
        return $key === null ? $g : (isset($g[$key]) ? $g[$key] : $key);
    }

    /* ---------- storage ---------- */

    private static $map = array('systems' => 'systems', 'events' => 'system_events', 'logs' => 'system_logs');
    public static function t($n) { return MZC_Crm_Db::t(self::$map[$n]); }
    private static function db() { return MZC_Crm_Db::db(); }
    /** The CRM database is there and up to date (its install() makes these tables too). */
    public static function ready() { return MZC_Crm_Db::ready(); }

    /** An ISO time as the database's UTC DATETIME (now when it is not one). */
    public static function dt($iso) { $t = strtotime((string) $iso); return gmdate('Y-m-d H:i:s', $t ? $t : time()); }

    /** The searchable columns of a system from what the app sent: the processor, the cards, RAM, Windows, and 'hw', every part's name in lower case. */
    public static function system_row($m)
    {
        $gpus = isset($m['gpus']) ? array_values(array_filter((array) $m['gpus'], 'is_string')) : array();
        $hw = array(isset($m['cpu']) ? $m['cpu'] : '', implode(' | ', $gpus), isset($m['board']) ? $m['board'] : '', isset($m['ramGb']) ? $m['ramGb'] . ' GB RAM' : '',
            isset($m['ramModules']) ? implode(' | ', (array) $m['ramModules']) : '', isset($m['storage']) ? implode(' | ', (array) $m['storage']) : '', isset($m['os']) ? $m['os'] : '', isset($m['gpuDriver']) ? 'driver ' . $m['gpuDriver'] : '');
        return array('cpu' => isset($m['cpu']) ? mb_substr((string) $m['cpu'], 0, 120) : null, 'gpus' => $gpus ? mb_substr(implode(' | ', $gpus), 0, 500) : null,
            'ram_gb' => isset($m['ramGb']) && is_numeric($m['ramGb']) ? (int) $m['ramGb'] : null, 'os' => isset($m['os']) ? mb_substr((string) $m['os'], 0, 120) : null,
            'hw' => mb_strtolower(implode(' ', array_filter($hw))), 'machine' => wp_json_encode($m, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES));
    }

    /** Event rows ({i, at, k, d}) into the table, a multi-row INSERT at a time; the card and the item are copied out of d for the indexes. */
    public static function insert_events($events)
    {
        $wpdb = self::db();
        $vals = array();
        foreach ($events as $e) {
            $d = isset($e['d']) && is_array($e['d']) ? $e['d'] : array(); $k = (string) $e['k'];
            $item = isset($d['benchmark']) ? $d['benchmark'] : (isset($d['test']) ? $d['test'] : (isset($d['kind']) ? $d['kind'] : null));
            $gpu = isset($d['gpu']) ? (string) $d['gpu'] : (isset($d['options']['gpu']) ? explode('|', (string) $d['options']['gpu'])[0] : null);
            $vals[] = $wpdb->prepare('(%s, %s, %s, %s, %s, %s)', (string) $e['i'], self::dt(isset($e['at']) ? $e['at'] : ''), mb_substr($k, 0, 16), $item === null ? '' : mb_substr((string) $item, 0, 64),
                $gpu === null ? '' : mb_substr(self::short($gpu), 0, 120), wp_json_encode($d, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES));
        }
        if ($vals) { $wpdb->query('INSERT INTO `' . self::t('events') . '` (sys, at, kind, item, gpu, d) VALUES ' . implode(',', $vals)); }
    }

    /** What the app sent (see Mazesta_Connect::rest_usage): the system's row made or brought up to date, its events added. */
    public static function record($id, $machine, $app, $edition, $lang, $events)
    {
        $wpdb = self::db(); if (!self::ready()) { return false; }
        $now = gmdate('Y-m-d H:i:s');
        $row = self::system_row($machine) + array('last_at' => $now, 'app' => $app, 'edition' => $edition, 'lang' => $lang);
        $row = array('id' => $id, 'first_at' => $now, 'n' => count($events)) + $row;
        $vals = array(); $upd = array('n = n + VALUES(n)');
        foreach ($row as $k => $v) { $vals[] = $v === null ? 'NULL' : $wpdb->prepare('%s', $v); if (!in_array($k, array('id', 'first_at', 'n'), true)) { $upd[] = "`$k` = VALUES(`$k`)"; } }
        $wpdb->query('INSERT INTO `' . self::t('systems') . '` (`' . implode('`, `', array_keys($row)) . '`) VALUES (' . implode(', ', $vals) . ') ON DUPLICATE KEY UPDATE ' . implode(', ', $upd));
        self::insert_events($events);
        return true;
    }

    /** A piece of a system's log and its crash records; logs older than LOG_DAYS go now and then. */
    public static function add_log($id, $app, $file, $body, $crashes)
    {
        $wpdb = self::db(); if (!self::ready()) { return false; }
        if (!$wpdb->get_var($wpdb->prepare('SELECT 1 FROM `' . self::t('systems') . '` WHERE id = %s', $id))) { return false; }   // only systems that sent statistics
        $wpdb->insert(self::t('logs'), array('sys' => $id, 'at' => gmdate('Y-m-d H:i:s'), 'app' => $app, 'file' => $file, 'body' => $body, 'crashes' => $crashes ? wp_json_encode($crashes, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES) : null));
        if (mt_rand(1, 50) === 1) { $wpdb->query($wpdb->prepare('DELETE FROM `' . self::t('logs') . '` WHERE at < %s LIMIT 5000', gmdate('Y-m-d H:i:s', time() - self::LOG_DAYS * 86400))); }
        return true;
    }

    public static function logs($id, $limit = 30)
    {
        $wpdb = self::db(); if (!self::ready()) { return array(); }
        return $wpdb->get_results($wpdb->prepare('SELECT at, app, file, body, crashes FROM `' . self::t('logs') . '` WHERE sys = %s ORDER BY id DESC LIMIT %d', $id, $limit), ARRAY_A) ?: array();
    }

    /** A system row shaped as the pages use it: first, last, n, app, edition, lang, note, machine (array), ack. */
    private static function shape($r)
    {
        $m = json_decode((string) $r['machine'], true);
        return array('id' => $r['id'], 'first' => $r['first_at'] . 'Z', 'last' => $r['last_at'] . 'Z', 'n' => (int) $r['n'], 'app' => $r['app'], 'edition' => $r['edition'], 'lang' => $r['lang'],
            'note' => $r['note'], 'machine' => is_array($m) ? $m : array(), 'ack' => (int) $r['ack']);
    }

    public static function install($id)
    {
        $wpdb = self::db(); if (!self::ready() || !preg_match('/^[a-f0-9]{32}$/', (string) $id)) { return null; }
        $r = $wpdb->get_row($wpdb->prepare('SELECT * FROM `' . self::t('systems') . '` WHERE id = %s', $id), ARRAY_A);
        return $r ? self::shape($r) : null;
    }

    /** The WHERE of a hardware search: every word must be in the parts' names (or the note); empty: every system. */
    public static function where($q)
    {
        $wpdb = self::db(); $w = array('1=1');
        foreach (preg_split('/\s+/u', mb_strtolower(trim((string) $q)), -1, PREG_SPLIT_NO_EMPTY) as $word) {
            $like = '%' . $wpdb->esc_like($word) . '%';
            $w[] = $wpdb->prepare('(hw LIKE %s OR note LIKE %s OR id LIKE %s)', $like, $like, $wpdb->esc_like($word) . '%');
        }
        return implode(' AND ', $w);
    }

    /** Systems matching a search, newest activity first (or 'first', 'n'): array(rows keyed by id, total). */
    public static function systems($q = '', $order = 'last', $limit = 30, $offset = 0)
    {
        $wpdb = self::db(); if (!self::ready()) { return array(array(), 0); }
        $w = self::where($q); $by = $order === 'first' ? 'first_at DESC' : ($order === 'n' ? 'n DESC' : 'last_at DESC');
        $rows = $wpdb->get_results($wpdb->prepare('SELECT * FROM `' . self::t('systems') . "` WHERE $w ORDER BY $by LIMIT %d OFFSET %d", $limit, $offset), ARRAY_A) ?: array();
        $out = array(); foreach ($rows as $r) { $out[$r['id']] = self::shape($r); }
        return array($out, (int) $wpdb->get_var('SELECT COUNT(*) FROM `' . self::t('systems') . "` WHERE $w"));
    }

    /** Whether a system is in a search's group (a message to a group is matched when the system asks). */
    public static function matches($id, $q)
    {
        $wpdb = self::db(); if (!self::ready()) { return false; }
        return (bool) $wpdb->get_var($wpdb->prepare('SELECT 1 FROM `' . self::t('systems') . '` WHERE id = %s AND ' . self::where($q), $id));
    }

    /** The short labels of a few systems (for a table of events), by id. */
    public static function names($ids)
    {
        $wpdb = self::db(); $ids = array_values(array_unique(array_filter($ids, function ($x) { return preg_match('/^[a-f0-9]{32}$/', (string) $x); })));
        if (!$ids || !self::ready()) { return array(); }
        $rows = $wpdb->get_results('SELECT * FROM `' . self::t('systems') . "` WHERE id IN ('" . implode("','", $ids) . "')", ARRAY_A) ?: array();
        $out = array(); foreach ($rows as $r) { $out[$r['id']] = self::shape($r); }
        return $out;
    }

    public static function set_note($id, $note)
    {
        $wpdb = self::db(); if (self::ready()) { $wpdb->update(self::t('systems'), array('note' => $note), array('id' => $id)); }
    }

    /** Remembers the newest message a system holds (only when it moved: the usual call writes nothing). */
    public static function ack($id, $after)
    {
        $wpdb = self::db(); if (!self::ready()) { return; }
        $wpdb->query($wpdb->prepare('UPDATE `' . self::t('systems') . '` SET ack = %d WHERE id = %s AND ack < %d', $after, $id, $after));
    }

    /** Counts of systems by a column (cpu, ram_gb, app, gpus), biggest first. */
    public static function count_by($col, $top = 8)
    {
        $wpdb = self::db(); if (!self::ready() || !in_array($col, array('cpu', 'ram_gb', 'app', 'gpus'), true)) { return array(); }
        return $wpdb->get_results("SELECT `$col` AS v, COUNT(*) AS n FROM `" . self::t('systems') . "` WHERE `$col` IS NOT NULL AND `$col` <> '' GROUP BY `$col` ORDER BY n DESC LIMIT " . (int) $top, ARRAY_A) ?: array();
    }

    public static function count_systems($since = 0)
    {
        $wpdb = self::db(); if (!self::ready()) { return 0; }
        return (int) ($since ? $wpdb->get_var($wpdb->prepare('SELECT COUNT(*) FROM `' . self::t('systems') . '` WHERE last_at >= %s', gmdate('Y-m-d H:i:s', $since))) : $wpdb->get_var('SELECT COUNT(*) FROM `' . self::t('systems') . '`'));
    }

    /**
     * Installs day by day between two unix times, in the site's time zone: 'new' systems first seen that day, 'active' systems that sent anything,
     * 'runs' app starts. Returns date (Y-m-d) => array(new, active, runs), every day of the range present (a day with nothing is zeros, not missing).
     */
    public static function daily($from, $to)
    {
        $wpdb = self::db(); $out = array();
        $tz = wp_timezone(); $off = $tz->getOffset(new DateTime('@' . $to)); $o = sprintf('%s%02d:%02d', $off < 0 ? '-' : '+', abs($off) / 3600, abs($off) % 3600 / 60);
        for ($d = new DateTime('@' . $from); $d->getTimestamp() <= $to; $d->modify('+1 day')) { $out[wp_date('Y-m-d', $d->getTimestamp())] = array('new' => 0, 'active' => 0, 'runs' => 0); }
        if (!self::ready()) { return $out; }
        $a = gmdate('Y-m-d H:i:s', $from); $b = gmdate('Y-m-d H:i:s', $to);
        $put = function ($rows, $col) use (&$out) { foreach ($rows as $r) { if (isset($out[$r['d']])) { $out[$r['d']][$col] = (int) $r['n']; } } };
        $put($wpdb->get_results($wpdb->prepare("SELECT DATE(CONVERT_TZ(first_at, '+00:00', %s)) AS d, COUNT(*) AS n FROM `" . self::t('systems') . '` WHERE first_at BETWEEN %s AND %s GROUP BY d', $o, $a, $b), ARRAY_A) ?: array(), 'new');
        foreach ($wpdb->get_results($wpdb->prepare("SELECT DATE(CONVERT_TZ(at, '+00:00', %s)) AS d, COUNT(DISTINCT sys) AS n, SUM(kind = 'app.start') AS r FROM `" . self::t('events') . '` WHERE at BETWEEN %s AND %s GROUP BY d', $o, $a, $b), ARRAY_A) ?: array() as $r) {
            if (isset($out[$r['d']])) { $out[$r['d']]['active'] = (int) $r['n']; $out[$r['d']]['runs'] = (int) $r['r']; }
        }
        return $out;
    }

    /** How many systems sent anything between two unix times. */
    public static function active($from, $to)
    {
        $wpdb = self::db(); if (!self::ready()) { return 0; }
        return (int) $wpdb->get_var($wpdb->prepare('SELECT COUNT(DISTINCT sys) FROM `' . self::t('events') . '` WHERE at BETWEEN %s AND %s', gmdate('Y-m-d H:i:s', $from), gmdate('Y-m-d H:i:s', $to)));
    }

    /** New systems in a range counted by a column (app, edition, lang), biggest first. */
    public static function new_by($col, $from, $to)
    {
        $wpdb = self::db(); if (!self::ready() || !in_array($col, array('app', 'edition', 'lang'), true)) { return array(); }
        $c = array();
        foreach ($wpdb->get_results($wpdb->prepare("SELECT `$col` AS v, COUNT(*) AS n FROM `" . self::t('systems') . "` WHERE first_at BETWEEN %s AND %s GROUP BY `$col` ORDER BY n DESC LIMIT 10", gmdate('Y-m-d H:i:s', $from), gmdate('Y-m-d H:i:s', $to)), ARRAY_A) ?: array() as $r) { $c[$r['v'] !== null && $r['v'] !== '' ? $r['v'] : 'نامشخص'] = (int) $r['n']; }
        return $c;
    }

    /** Messages delivered: how many systems (all, or a group's) hold message $id. */
    public static function delivered($id, $q = null)
    {
        $wpdb = self::db(); if (!self::ready()) { return array(0, 0); }
        $w = $q === null ? '1=1' : self::where($q);
        $r = $wpdb->get_row($wpdb->prepare('SELECT SUM(ack >= %d) AS got, COUNT(*) AS n FROM `' . self::t('systems') . "` WHERE $w", $id), ARRAY_A);
        return array((int) $r['got'], (int) $r['n']);
    }

    /**
     * Events newest first, shaped as the files had them ({i, at, k, d, ts}). $f: since (unix time), sys, kind (one or a list), gpu (words in the card's name), item, hw (a search of the card, the item and the system's parts),
     * not (a kind left out), limit, offset. Returns array(events, total).
     */
    public static function events($f = array(), $count = false)
    {
        $wpdb = self::db(); if (!self::ready()) { return array(array(), 0); }
        $w = array('1=1');
        if (!empty($f['since'])) { $w[] = $wpdb->prepare('at >= %s', gmdate('Y-m-d H:i:s', (int) $f['since'])); }
        if (!empty($f['sys'])) { $w[] = $wpdb->prepare('sys = %s', $f['sys']); }
        if (!empty($f['kind'])) { $k = (array) $f['kind']; $w[] = 'kind IN (' . implode(',', array_map(function ($x) use ($wpdb) { return $wpdb->prepare('%s', $x); }, $k)) . ')'; }
        if (!empty($f['not'])) { $w[] = $wpdb->prepare('kind <> %s', $f['not']); }
        if (!empty($f['item'])) { $w[] = $wpdb->prepare('item = %s', $f['item']); }
        if (!empty($f['hw'])) { $like = '%' . $wpdb->esc_like(trim($f['hw'])) . '%'; $w[] = '(' . $wpdb->prepare('gpu LIKE %s OR item LIKE %s', $like, $like) . ' OR sys IN (SELECT id FROM `' . self::t('systems') . '` WHERE ' . self::where($f['hw']) . '))'; }
        if (!empty($f['gpu'])) { foreach (preg_split('/\s+/u', trim($f['gpu']), -1, PREG_SPLIT_NO_EMPTY) as $word) { $w[] = $wpdb->prepare('gpu LIKE %s', '%' . $wpdb->esc_like($word) . '%'); } }
        $where = implode(' AND ', $w);
        $limit = isset($f['limit']) ? (int) $f['limit'] : 150000; $offset = isset($f['offset']) ? (int) $f['offset'] : 0;
        $rows = $wpdb->get_results('SELECT sys, at, kind, d FROM `' . self::t('events') . "` WHERE $where ORDER BY at DESC, id DESC LIMIT $limit OFFSET $offset", ARRAY_A) ?: array();
        $out = array();
        foreach ($rows as $r) { $d = json_decode((string) $r['d'], true); $out[] = array('i' => $r['sys'], 'at' => $r['at'] . 'Z', 'k' => $r['kind'], 'd' => is_array($d) ? $d : array(), 'ts' => (int) strtotime($r['at'] . ' UTC')); }
        return array($out, $count ? (int) $wpdb->get_var('SELECT COUNT(*) FROM `' . self::t('events') . "` WHERE $where") : count($out));
    }

    /** The cards that ran an automatic undervolt / overclock, with how many runs each. */
    public static function tuned_gpus()
    {
        $wpdb = self::db(); if (!self::ready()) { return array(); }
        return $wpdb->get_results("SELECT gpu, COUNT(*) AS n, COUNT(DISTINCT sys) AS systems FROM `" . self::t('events') . "` WHERE kind = 'tuning.auto' AND gpu <> '' GROUP BY gpu ORDER BY n DESC", ARRAY_A) ?: array();
    }

    /** A short name for a system: its card and its processor. */
    public static function label($row)
    {
        $m = isset($row['machine']) && is_array($row['machine']) ? $row['machine'] : array();
        $gpu = !empty($m['gpus'][0]) ? self::short($m['gpus'][0]) : '';
        $cpu = !empty($m['cpu']) ? self::short($m['cpu']) : '';
        return trim(($gpu !== '' ? $gpu : '—') . ' · ' . ($cpu !== '' ? $cpu : '—'));
    }

    public static function short($name)
    {
        $name = preg_replace('/\((R|TM|tm|r)\)|\bCPU\b|\bProcessor\b|\bwith Radeon Graphics\b|@.*$|\b\d+-Core\b|NVIDIA |AMD |Intel\(R\) /i', '', (string) $name);
        return trim(preg_replace('/\s+/', ' ', $name));
    }


    private static function num($a, $path)
    {
        foreach ($path as $p) { if (!is_array($a) || !isset($a[$p])) { return null; } $a = $a[$p]; }
        return is_numeric($a) ? (float) $a : null;
    }

    private static function first($candidates)
    {
        foreach ($candidates as $c) { if ($c !== null) { return $c; } }
        return null;
    }

    /**
     * One flat row for an event: kind, what ran, result, the figures. For a benchmark or a test the processor's and card's measured temperature and clock come from the
     * 'sensors' block of the event (newer apps) or, for a benchmark, from its own metrics; for an automatic search the clocks, power and temperature are those of the tuned run.
     */
    public static function row($e)
    {
        $d = isset($e['d']) && is_array($e['d']) ? $e['d'] : array();
        $k = (string) $e['k'];
        $r = array('ts' => $e['ts'], 'id' => $e['i'], 'kind' => $k, 'item' => '', 'result' => '', 'ok' => null, 'gpu' => '', 'seconds' => isset($d['seconds']) ? (int) $d['seconds'] : null,
            'cpuTemp' => null, 'gpuTemp' => null, 'cpuClock' => null, 'gpuClock' => null, 'fps' => null, 'score' => null, 'scoreUnit' => '', 'power' => null, 'note' => '', 'd' => $d);
        $s = isset($d['sensors']) && is_array($d['sensors']) ? $d['sensors'] : array();
        $m = isset($d['metrics']) && is_array($d['metrics']) ? $d['metrics'] : array();
        $mv = function ($key) use ($m) { return isset($m[$key]) && is_numeric($m[$key]) ? (float) $m[$key] : null; };
        if ($k === 'bench.run' || $k === 'test.run') {
            $id = (string) (isset($d['benchmark']) ? $d['benchmark'] : (isset($d['test']) ? $d['test'] : '?'));
            $r['item'] = self::labels('tests', $id);
            $res = (string) (isset($d['status']) ? $d['status'] : (isset($d['outcome']) ? $d['outcome'] : ''));
            $r['result'] = self::labels('outcomes', $res);
            $r['ok'] = in_array($res, array('Passed', 'Completed'), true) ? true : (in_array($res, array('Failed', 'Error'), true) ? false : null);
            $r['cpuTemp'] = self::first(array(self::num($s, array('cpuTempMax')), $mv('Bench_Cpu_TempMax')));
            $r['gpuTemp'] = self::first(array(self::num($s, array('gpuTempMax')), $mv('Bench_Gpu_TempMax')));
            $r['cpuClock'] = self::first(array(self::num($s, array('cpuClock')), $mv('Bench_Cpu_Clock')));
            $r['gpuClock'] = self::first(array(self::num($s, array('gpuClock')), $mv('Bench_Gpu_Clock')));
            $r['power'] = self::first(array(self::num($s, array('gpuPower')), $mv('Bench_Gpu_Power'), self::num($s, array('cpuPower')), $mv('Bench_Cpu_Power')));
            $r['fps'] = self::first(array($mv('Bench_Gpu_Rt_Fps'), $mv('Bench_Gpu_Scene_Fps'), $mv('Bench_Scene_GpuFps')));
            $h = self::labels('headline'); $h = isset($h[$id]) ? $h[$id] : null;
            if ($h && $mv($h[0]) !== null) { $r['score'] = $mv($h[0]); $r['scoreUnit'] = $h[1]; }
            $r['gpu'] = isset($d['options']['gpu']) ? self::short(explode('|', (string) $d['options']['gpu'])[0]) : '';
        } elseif ($k === 'tuning.auto') {
            $kind = (string) (isset($d['kind']) ? $d['kind'] : '?');
            $r['item'] = self::labels('tuning', $kind);
            $v = (string) (isset($d['verdict']) ? $d['verdict'] : '');
            $r['result'] = self::labels('outcomes', $v); $r['ok'] = $v === 'Improved' ? true : ($v === 'Failed' ? false : null);
            $t = isset($d['tuned']) && is_array($d['tuned']) ? $d['tuned'] : array();
            $r['gpuTemp'] = self::first(array(self::num($t, array('hotSpotC')), self::num($t, array('tempC'))));
            $r['gpuClock'] = self::num($t, array('clockMHz')); $r['power'] = self::num($t, array('powerW'));
            $r['fps'] = self::num($d, array('sceneTuned', 'score')); $r['score'] = self::num($t, array('score'));
            $r['gpu'] = self::short(isset($d['gpu']) ? $d['gpu'] : '');
            if (!empty($d['settings']) && is_array($d['settings'])) {
                $x = $d['settings']; $parts = array();
                if (isset($x['coreOffset'])) { $parts[] = 'آفست هسته ' . (int) $x['coreOffset']; }
                if (isset($x['capMHz'])) { $parts[] = 'سقف ' . (int) $x['capMHz'] . ' MHz'; }
                if (isset($x['powerLimitW'])) { $parts[] = (int) $x['powerLimitW'] . ' W'; }
                $r['note'] = implode(' · ', $parts);
            } elseif ($v !== 'Improved') { $r['note'] = self::labels('reasons', isset($d['reason']) ? (string) $d['reason'] : ''); }
        } elseif ($k === 'tuning.scene') {
            $r['item'] = 'تست صحنه'; $r['result'] = !empty($d['clean']) ? 'سالم' : 'مشکل'; $r['ok'] = !empty($d['clean']);
            $res = isset($d['result']) && is_array($d['result']) ? $d['result'] : array();
            $r['gpuClock'] = self::num($res, array('clockMHz')); $r['gpuTemp'] = self::first(array(self::num($res, array('hotSpotC')), self::num($res, array('tempC'))));
            $r['power'] = self::num($res, array('powerW')); $r['fps'] = self::num($res, array('score')); $r['gpu'] = self::short(isset($d['gpu']) ? $d['gpu'] : '');
        } elseif ($k === 'tuning.apply') { $r['item'] = 'اعمال تنظیم کارت'; $r['gpu'] = self::short(isset($d['gpu']) ? $d['gpu'] : '');
        } elseif ($k === 'app.start') { $r['item'] = 'اجرای برنامه'; $r['note'] = isset($d['version']) ? (string) $d['version'] : '';
        } elseif ($k === 'ai.ask') { $r['item'] = 'پرسش از دستیار'; }
        else { $r['item'] = $k; }
        return $r;
    }

    public static function kind_label($k)
    {
        $m = array('bench.run' => 'بنچمارک', 'test.run' => 'تست', 'tuning.auto' => 'آندرولت / اورکلاک', 'tuning.scene' => 'تست صحنه', 'tuning.apply' => 'اعمال تنظیم', 'app.start' => 'اجرای برنامه', 'ai.ask' => 'دستیار');
        return isset($m[$k]) ? $m[$k] : $k;
    }

    /** Counts of the values a function gives for each row, biggest first. */
    public static function tally($rows, $fn, $top = 8)
    {
        $c = array();
        foreach ($rows as $row) { foreach ((array) $fn($row) as $v) { if ($v !== '' && $v !== null) { $c[$v] = (isset($c[$v]) ? $c[$v] : 0) + 1; } } }
        arsort($c);
        return array_slice($c, 0, $top, true);
    }

    public static function median($a)
    {
        $a = array_values(array_filter($a, 'is_numeric')); if (!$a) { return null; }
        sort($a); $n = count($a); return $n % 2 ? $a[($n - 1) / 2] : ($a[$n / 2 - 1] + $a[$n / 2]) / 2;
    }
}
