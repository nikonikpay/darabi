<?php
if (!defined('ABSPATH')) { exit; }

/**
 * What the app's anonymous usage statistics say, shaped for the pages: the installs (one row per random id, with the parts' names the app sent), the events (a month's file
 * each) and one flat row per event - the system, what ran, how it ended, and the figures the machine measured (temperatures, clocks, frame rate, score).
 * Nothing here is invented: a figure the app did not send stays empty.
 */
final class MZC_Stats
{
    private static $labels = null;

    public static function labels($group = null, $key = null)
    {
        if (self::$labels === null) { self::$labels = require MZC_DIR . 'includes/data-labels.php'; }
        if ($group === null) { return self::$labels; }
        $g = isset(self::$labels[$group]) ? self::$labels[$group] : array();
        return $key === null ? $g : (isset($g[$key]) ? $g[$key] : $key);
    }

    public static function installs() { return Mazesta_Connect::read('installs'); }

    public static function install($id) { $in = self::installs(); return isset($in[$id]) ? $in[$id] : null; }

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

    /** Events of the last $months months (at most $cap), newest first; each is the file's array plus 'ts'. */
    public static function events($months = 2, $cap = 150000)
    {
        $out = array();
        for ($i = 0; $i < $months; $i++) {
            $f = Mazesta_Connect::usage_file(gmdate('Y-m', strtotime('first day of -' . $i . ' month')));
            if (!is_readable($f)) { continue; }
            $h = fopen($f, 'r'); if (!$h) { continue; }
            fgets($h);   // the guard line
            $month = array();
            while (($line = fgets($h)) !== false && count($out) + count($month) < $cap) {
                $e = json_decode($line, true);
                if (is_array($e) && isset($e['k'], $e['i'])) { $e['ts'] = (int) strtotime(isset($e['at']) ? (string) $e['at'] : 'now'); $month[] = $e; }
            }
            fclose($h);
            $out = array_merge($out, array_reverse($month));
        }
        return $out;
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
