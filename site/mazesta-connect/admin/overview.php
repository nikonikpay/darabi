<?php
if (!defined('ABSPATH')) { exit; }
/** Overview: the installs and what they did, in a few boxes. The long lists are on the Systems and Events pages. */

$days = isset($_GET['days']) ? max(1, min(60, (int) $_GET['days'])) : 30;
$since = time() - $days * 86400;
$installs = MZC_Stats::installs();
$rows = array();
foreach (MZC_Stats::events(2) as $e) { if ($e['ts'] >= $since) { $rows[] = MZC_Stats::row($e); } }

$active = 0; foreach ($installs as $i) { if (!empty($i['last']) && strtotime($i['last']) >= $since) { $active++; } }
$by = array('bench.run' => 0, 'test.run' => 0, 'tuning.auto' => 0, 'tuning.scene' => 0);
foreach ($rows as $r) { if (isset($by[$r['kind']])) { $by[$r['kind']]++; } }

echo '<div class="mzc-title"><h2>نمای کلی</h2><form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-overview"><label>بازه:</label><select name="days" onchange="this.form.submit()">';
foreach (array(7 => '۷ روز اخیر', 14 => '۱۴ روز اخیر', 30 => '۳۰ روز اخیر', 60 => '۶۰ روز اخیر') as $d => $l) { echo '<option value="' . $d . '"' . selected($days, $d, false) . '>' . esc_html($l) . '</option>'; }
echo '</select></form></div>';

echo '<div class="mzc-cards">';
$kpi = function ($k, $v, $s = '') { echo '<div class="mzc-card mzc-kpi"><div class="k">' . esc_html($k) . '</div><div class="v">' . $v . '</div><div class="s">' . esc_html($s) . '</div></div>'; };
$kpi('سیستم‌های شناخته‌شده', MZC_Admin::num(count($installs)), 'فعال در این بازه: ' . number_format_i18n($active));
$kpi('بنچمارک‌ها', MZC_Admin::num($by['bench.run']), 'اجرا در این بازه');
$kpi('تست‌ها', MZC_Admin::num($by['test.run']), 'اجرا در این بازه');
$kpi('آندرولت / اورکلاک', MZC_Admin::num($by['tuning.auto']), 'جست‌وجوی خودکار');
$kpi('تست صحنه', MZC_Admin::num($by['tuning.scene']), 'مقایسهٔ پروفایل‌ها');
echo '</div>';

// ---- what the systems are
$list = array_values($installs);
echo '<div class="mzc-grid2">';
$box = function ($title, $rowsx) { echo '<div class="mzc-panel"><header><h3>' . esc_html($title) . '</h3></header><div class="body">' . MZC_Admin::bars($rowsx) . '</div></div>'; };
$box('کارت‌های گرافیک', MZC_Stats::tally($list, function ($i) { return isset($i['machine']['gpus']) ? array_map(array('MZC_Stats', 'short'), (array) $i['machine']['gpus']) : array(); }));
$box('پردازنده‌ها', MZC_Stats::tally($list, function ($i) { return isset($i['machine']['cpu']) ? array(MZC_Stats::short($i['machine']['cpu'])) : array(); }));
$box('حافظهٔ رم (گیگابایت)', MZC_Stats::tally($list, function ($i) { return isset($i['machine']['ramGb']) ? array($i['machine']['ramGb'] . ' GB') : array(); }, 6));
$box('نسخهٔ برنامه', MZC_Stats::tally($list, function ($i) { return isset($i['app']) && $i['app'] !== '' ? array($i['app']) : array(); }, 6));
echo '</div>';

// ---- tests and benchmarks, one line each
$items = array();
foreach ($rows as $r) {
    if ($r['kind'] !== 'bench.run' && $r['kind'] !== 'test.run') { continue; }
    $x = &$items[$r['kind'] . '|' . $r['item']];
    if (!$x) { $x = array('kind' => $r['kind'], 'item' => $r['item'], 'n' => 0, 'ok' => 0, 'bad' => 0, 'ct' => array(), 'gt' => array(), 'cc' => array(), 'gc' => array(), 'fps' => array()); }
    $x['n']++; if ($r['ok'] === true) { $x['ok']++; } elseif ($r['ok'] === false) { $x['bad']++; }
    foreach (array('ct' => 'cpuTemp', 'gt' => 'gpuTemp', 'cc' => 'cpuClock', 'gc' => 'gpuClock', 'fps' => 'fps') as $a => $b) { if ($r[$b] !== null) { $x[$a][] = $r[$b]; } }
    unset($x);
}
uasort($items, function ($a, $b) { return $b['n'] <=> $a['n']; });
echo '<div class="mzc-panel"><header><h3>تست‌ها و بنچمارک‌ها</h3><span class="muted">میانهٔ مقدارهای اندازه‌گیری‌شده؛ برای دیدن هر اجرا به صفحهٔ «رویدادها» بروید</span></header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>مورد</th><th>نوع</th><th class="n">اجرا</th><th>نتیجه</th><th class="n">دمای CPU</th><th class="n">دمای GPU</th><th class="n">فرکانس CPU</th><th class="n">فرکانس GPU</th><th class="n">FPS</th></tr></thead><tbody>';
if (!$items) { echo '<tr><td class="empty" colspan="9">در این بازه اجرایی نرسیده است.</td></tr>'; }
$med = function ($a, $u = '') { $m = MZC_Stats::median($a); return $m === null ? '—' : '<span class="num">' . esc_html(round($m) . $u) . '</span>'; };
foreach (array_slice($items, 0, 60) as $x) {
    $done = $x['ok'] + $x['bad'];
    $rate = $done ? '<span class="mzc-rate" title="سالم ' . (int) $x['ok'] . ' از ' . (int) $done . '"><i style="width:' . (int) round($x['ok'] / $done * 100) . '%"></i></span> <span class="num muted">' . (int) round($x['ok'] / $done * 100) . '%</span>' : '<span class="muted">—</span>';
    echo '<tr><td dir="auto">' . esc_html($x['item']) . '</td><td>' . esc_html(MZC_Stats::kind_label($x['kind'])) . '</td><td class="n">' . MZC_Admin::num($x['n']) . '</td><td>' . $rate . '</td><td class="n">' . $med($x['ct'], '°') . '</td><td class="n">' . $med($x['gt'], '°') . '</td><td class="n">' . $med($x['cc']) . '</td><td class="n">' . $med($x['gc']) . '</td><td class="n">' . $med($x['fps']) . '</td></tr>';
}
echo '</tbody></table></div></div>';

// ---- undervolt and overclock
$tune = array(); $reasons = array();
foreach ($rows as $r) {
    if ($r['kind'] !== 'tuning.auto') { continue; }
    $d = $r['d']; $k = $r['item'];
    $x = &$tune[$k]; if (!$x) { $x = array('n' => 0, 'ok' => 0, 'dt' => array(), 'dp' => array(), 'dc' => array(), 'df' => array()); }
    $x['n']++;
    if ($r['ok'] === true) {
        $x['ok']++;
        if (isset($d['stock']['tempC'], $d['tuned']['tempC'])) { $x['dt'][] = $d['stock']['tempC'] - $d['tuned']['tempC']; }
        if (isset($d['stock']['powerW'], $d['tuned']['powerW'])) { $x['dp'][] = $d['stock']['powerW'] - $d['tuned']['powerW']; }
        if (isset($d['stock']['clockMHz'], $d['tuned']['clockMHz'])) { $x['dc'][] = $d['tuned']['clockMHz'] - $d['stock']['clockMHz']; }
        if (isset($d['sceneStock']['score'], $d['sceneTuned']['score'])) { $x['df'][] = $d['sceneTuned']['score'] - $d['sceneStock']['score']; }
    } else { $why = $r['note'] !== '' ? $r['note'] : 'نامشخص'; $reasons[$why] = (isset($reasons[$why]) ? $reasons[$why] : 0) + 1; }
    unset($x);
}
echo '<div class="mzc-grid2"><div class="mzc-panel"><header><h3>آندرولت و اورکلاک خودکار</h3></header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>نوع</th><th class="n">اجرا</th><th class="n">موفق</th><th class="n">کاهش دما</th><th class="n">کاهش توان</th><th class="n">تغییر فرکانس</th><th class="n">تغییر FPS</th></tr></thead><tbody>';
if (!$tune) { echo '<tr><td class="empty" colspan="7">در این بازه اجرایی نرسیده است.</td></tr>'; }
$sg = function ($a, $u) { $m = MZC_Stats::median($a); return $m === null ? '—' : '<span class="num">' . esc_html(($m > 0 ? '+' : '') . round($m, 1) . $u) . '</span>'; };
foreach ($tune as $k => $x) { echo '<tr><td>' . esc_html($k) . '</td><td class="n">' . MZC_Admin::num($x['n']) . '</td><td class="n">' . MZC_Admin::num($x['ok']) . '</td><td class="n">' . $sg($x['dt'], '°') . '</td><td class="n">' . $sg($x['dp'], ' W') . '</td><td class="n">' . $sg($x['dc'], ' MHz') . '</td><td class="n">' . $sg($x['df'], '') . '</td></tr>'; }
echo '</tbody></table></div></div>';
arsort($reasons);
echo '<div class="mzc-panel"><header><h3>چرا ناموفق بود</h3></header><div class="body">' . MZC_Admin::bars(array_slice($reasons, 0, 8, true), 'ناموفقی نبوده است.') . '</div></div></div>';

// ---- the latest events
echo '<div class="mzc-panel"><header><h3>آخرین رویدادها</h3><a class="mzc-btn sm" href="' . esc_url(MZC_Admin::url('mzc-events')) . '">همهٔ رویدادها</a></header><div class="body flush">'
    . MZC_Admin::events_table(array_slice(array_values(array_filter($rows, function ($r) { return $r['kind'] !== 'app.start'; })), 0, 12), true, $installs) . '</div></div>';
echo '<p class="muted">آمار ناشناس است: هر سیستم فقط یک شناسهٔ تصادفی دارد؛ نام کاربر، نام کامپیوتر، سریال و آدرس شبکه ذخیره نمی‌شود. رویدادها دو ماه اخیر خوانده می‌شود.</p>';
