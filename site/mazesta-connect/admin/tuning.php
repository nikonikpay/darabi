<?php
if (!defined('ABSPATH')) { exit; }
/** Undervolt and overclock per graphics card: for each card model the runs, how many worked and the medians of what they found; then every run of the chosen card
 *  (or of all), with the settings found and the stock and tuned figures side by side. Read from the 'tuning.auto' events the app sends. */

$gpu = isset($_GET['gpu']) ? sanitize_text_field(wp_unslash($_GET['gpu'])) : '';
$kind = isset($_GET['kind']) && in_array($_GET['kind'], array('Undervolt', 'Overclock', 'OverclockPlus'), true) ? (string) $_GET['kind'] : '';
$paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 40;

$g = function ($d, $a, $b) { return isset($d[$a][$b]) && is_numeric($d[$a][$b]) ? (float) $d[$a][$b] : null; };
$sg = function ($v, $u, $dp = 0) { return $v === null ? '—' : '<span class="num">' . esc_html(($v > 0 ? '+' : '') . round($v, $dp) . $u) . '</span>'; };
$pair = function ($a, $b, $u) { return $a === null && $b === null ? '—' : '<span class="num">' . esc_html(($a === null ? '—' : round($a)) . ' → ' . ($b === null ? '—' : round($b)) . $u) . '</span>'; };

echo '<div class="mzc-title"><h2>آندرولت و اورکلاک</h2><span class="muted">نتیجهٔ جست‌وجوی خودکار برنامه روی هر کارت گرافیک</span></div>';

// ---- one line per card model: every run of it, in the database
$cards = array();
foreach (MZC_Stats::events(array('kind' => 'tuning.auto', 'limit' => 50000))[0] as $e) {
    $d = $e['d']; $name = MZC_Stats::short(isset($d['gpu']) ? $d['gpu'] : ''); if ($name === '') { $name = '—'; }
    $k = isset($d['kind']) ? (string) $d['kind'] : '?'; $ok = isset($d['verdict']) && $d['verdict'] === 'Improved';
    $x = &$cards[$name][$k];
    if (!$x) { $x = array('n' => 0, 'ok' => 0, 'sys' => array(), 'off' => array(), 'cap' => array(), 'pl' => array(), 'dt' => array(), 'dp' => array(), 'dc' => array(), 'df' => array()); }
    $x['n']++; $x['sys'][$e['i']] = 1;
    if ($ok) {
        $x['ok']++; $s = isset($d['settings']) && is_array($d['settings']) ? $d['settings'] : array();
        if (isset($s['coreOffset'])) { $x['off'][] = (float) $s['coreOffset']; } if (isset($s['capMHz'])) { $x['cap'][] = (float) $s['capMHz']; } if (isset($s['powerLimitW'])) { $x['pl'][] = (float) $s['powerLimitW']; }
        if (($a = $g($d, 'stock', 'tempC')) !== null && ($b = $g($d, 'tuned', 'tempC')) !== null) { $x['dt'][] = $b - $a; }
        if (($a = $g($d, 'stock', 'powerW')) !== null && ($b = $g($d, 'tuned', 'powerW')) !== null) { $x['dp'][] = $b - $a; }
        if (($a = $g($d, 'stock', 'clockMHz')) !== null && ($b = $g($d, 'tuned', 'clockMHz')) !== null) { $x['dc'][] = $b - $a; }
        if (($a = $g($d, 'sceneStock', 'score')) !== null && ($b = $g($d, 'sceneTuned', 'score')) !== null && $a > 0) { $x['df'][] = ($b - $a) / $a * 100; }
    }
    unset($x);
}
ksort($cards);
echo '<div class="mzc-panel"><header><h3>به تفکیک کارت گرافیک</h3><span class="muted">میانهٔ اجراهای موفق؛ تغییرها نسبت به حالت کارخانه (منفی = کمتر)</span></header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr>'
    . '<th>کارت گرافیک</th><th>نوع</th><th class="n">سیستم</th><th class="n">اجرا</th><th class="n">موفق</th><th class="n">آفست هسته</th><th class="n">سقف فرکانس</th><th class="n">توان</th><th class="n">دما</th><th class="n">مصرف</th><th class="n">فرکانس</th><th class="n">FPS صحنه</th><th></th></tr></thead><tbody>';
if (!$cards) { echo '<tr><td class="empty" colspan="13">هنوز هیچ آندرولت یا اورکلاک خودکاری گزارش نشده است.</td></tr>'; }
$med = function ($a, $u) { $m = MZC_Stats::median($a); return $m === null ? '—' : '<span class="num">' . esc_html(round($m) . $u) . '</span>'; };
foreach ($cards as $name => $kinds) {
    foreach ($kinds as $k => $x) {
        echo '<tr' . ($gpu !== '' && $gpu === $name ? ' class="on"' : '') . '><td dir="auto"><strong>' . esc_html($name) . '</strong></td><td>' . esc_html(MZC_Stats::labels('tuning', $k)) . '</td>'
            . '<td class="n">' . MZC_Admin::num(count($x['sys'])) . '</td><td class="n">' . MZC_Admin::num($x['n']) . '</td><td class="n">' . MZC_Admin::num($x['ok']) . '</td>'
            . '<td class="n">' . $med($x['off'], ' MHz') . '</td><td class="n">' . $med($x['cap'], ' MHz') . '</td><td class="n">' . $med($x['pl'], ' W') . '</td>'
            . '<td class="n">' . $sg(MZC_Stats::median($x['dt']), '°', 1) . '</td><td class="n">' . $sg(MZC_Stats::median($x['dp']), ' W', 1) . '</td><td class="n">' . $sg(MZC_Stats::median($x['dc']), ' MHz') . '</td><td class="n">' . $sg(MZC_Stats::median($x['df']), '%', 1) . '</td>'
            . '<td class="n"><a class="mzc-btn sm" href="' . esc_url(MZC_Admin::url('mzc-tuning', array('gpu' => $name, 'kind' => $k))) . '">اجراها</a></td></tr>';
    }
}
echo '</tbody></table></div></div>';

// ---- every run (of the chosen card), newest first
$f = array('kind' => 'tuning.auto', 'gpu' => $gpu === '—' ? '' : $gpu, 'item' => $kind, 'limit' => $per, 'offset' => ($paged - 1) * $per);
list($runs, $total) = MZC_Stats::events($f, true);
$names = MZC_Stats::names(array_column($runs, 'i'));
echo '<div class="mzc-panel"><header><h3>' . esc_html($gpu !== '' ? 'اجراهای ' . $gpu . ($kind !== '' ? ' — ' . MZC_Stats::labels('tuning', $kind) : '') : 'همهٔ اجراها') . '</h3><span class="muted">' . esc_html(number_format_i18n($total)) . ' اجرا</span>'
    . ($gpu !== '' ? ' <a href="' . esc_url(MZC_Admin::url('mzc-tuning')) . '">نمایش همه</a>' : '') . '</header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr>'
    . '<th>زمان</th><th>سیستم</th><th>کارت</th><th>نوع</th><th>نتیجه</th><th>تنظیم پیدا‌شده</th><th class="n">دما</th><th class="n">توان</th><th class="n">فرکانس</th><th class="n">ولتاژ</th><th class="n">FPS صحنه</th><th>توضیح</th></tr></thead><tbody>';
if (!$runs) { echo '<tr><td class="empty" colspan="12">اجرایی پیدا نشد.</td></tr>'; }
foreach ($runs as $e) {
    $d = $e['d']; $r = MZC_Stats::row($e); $i = isset($names[$e['i']]) ? $names[$e['i']] : null;
    $s = isset($d['settings']) && is_array($d['settings']) ? $d['settings'] : array(); $set = array();
    if (isset($s['coreOffset'])) { $set[] = 'آفست ' . (int) $s['coreOffset']; } if (isset($s['memoryOffset']) && (int) $s['memoryOffset'] !== 0) { $set[] = 'حافظه ' . (int) $s['memoryOffset']; }
    if (isset($s['capMHz'])) { $set[] = 'سقف ' . (int) $s['capMHz'] . ' MHz'; } if (isset($s['powerLimitW'])) { $set[] = (int) $s['powerLimitW'] . ' W'; }
    $volts = function ($w) use ($g, $d) { $v = $g($d, $w, 'volts'); return $v === null ? '—' : number_format($v, 3); };
    echo '<tr><td>' . MZC_Admin::when($e['ts']) . '</td><td><a href="' . esc_url(MZC_Admin::url('mzc-systems', array('sys' => $e['i']))) . '" dir="auto">' . esc_html($i ? (!empty($i['note']) ? $i['note'] : MZC_Stats::label($i)) : substr($e['i'], 0, 8)) . '</a></td>'
        . '<td dir="auto">' . esc_html($r['gpu'] !== '' ? $r['gpu'] : '—') . '</td><td>' . esc_html($r['item']) . '</td><td>' . ($r['result'] !== '' ? MZC_Admin::pill($r['result'], $r['ok'] === true ? 'ok' : ($r['ok'] === false ? 'bad' : '')) : '') . '</td>'
        . '<td dir="auto">' . esc_html($set ? implode(' · ', $set) : '—') . '</td>'
        . '<td class="n">' . $pair($g($d, 'stock', 'tempC'), $g($d, 'tuned', 'tempC'), '°') . '</td><td class="n">' . $pair($g($d, 'stock', 'powerW'), $g($d, 'tuned', 'powerW'), ' W') . '</td>'
        . '<td class="n">' . $pair($g($d, 'stock', 'clockMHz'), $g($d, 'tuned', 'clockMHz'), '') . '</td><td class="n"><span class="num">' . esc_html($volts('stock') . ' → ' . $volts('tuned')) . '</span></td>'
        . '<td class="n">' . $pair($g($d, 'sceneStock', 'score'), $g($d, 'sceneTuned', 'score'), '') . '</td>'
        . '<td>' . ($r['ok'] !== true && $r['note'] !== '' ? '<span dir="auto">' . esc_html($r['note']) . '</span> ' : '') . '<details class="mzc-details"><summary>جزئیات</summary><pre>' . esc_html((string) wp_json_encode($d, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_PRETTY_PRINT)) . '</pre></details></td></tr>';
}
echo '</tbody></table></div></div>' . MZC_Admin::pager($total, $per, $paged, array('gpu' => $gpu, 'kind' => $kind));
echo '<p class="muted">«کارخانه → تنظیم‌شده»: مقدارهایی که برنامه زیر بار، یک بار با تنظیم کارخانه و یک بار با تنظیم پیدا‌شده اندازه گرفته است. مقداری که برنامه نفرستاده خالی می‌ماند.</p>';
