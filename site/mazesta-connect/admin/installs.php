<?php
if (!defined('ABSPATH')) { exit; }
/** Installs by date: new systems, active systems, app starts and reported uninstalls per day (or per Persian month), for a preset span or a chosen from/to. */

$ymd = function ($k) { $v = isset($_GET[$k]) ? sanitize_text_field(wp_unslash($_GET[$k])) : ''; return preg_match('/^\d{4}-\d{2}-\d{2}$/', $v) ? $v : ''; };
$days = isset($_GET['days']) ? max(1, min(730, (int) $_GET['days'])) : 30;
$fromS = $ymd('from'); $toS = $ymd('to'); $tz = wp_timezone();
if ($fromS !== '' && $toS !== '') {
    $from = (new DateTime($fromS . ' 00:00:00', $tz))->getTimestamp(); $to = (new DateTime($toS . ' 23:59:59', $tz))->getTimestamp();
    if ($to < $from) { list($from, $to) = array($to - 86399, $from + 86399); }
    $to = min($to, $from + 730 * 86400); $days = 0;
} else {
    $to = time(); $from = (new DateTime(MZC_Crm::ymd($to - ($days - 1) * 86400) . ' 00:00:00', $tz))->getTimestamp();
}
$group = isset($_GET['group']) && $_GET['group'] === 'month' ? 'month' : 'day';

$daily = MZC_Stats::daily($from, $to);
$rows = array();   // label => new, active (the most in one day of the group), runs, gone
foreach ($daily as $d => $x) {
    $j = MZC_Crm::jdate($d); $k = $group === 'month' ? substr($j, 0, 7) : $j;
    if (!isset($rows[$k])) { $rows[$k] = array('new' => 0, 'active' => 0, 'runs' => 0, 'gone' => 0); }
    $rows[$k]['new'] += $x['new']; $rows[$k]['runs'] += $x['runs']; $rows[$k]['gone'] += $x['gone']; $rows[$k]['active'] = max($rows[$k]['active'], $x['active']);
}
$sum = function ($c) use ($daily) { return array_sum(array_column($daily, $c)); };
$active = MZC_Stats::active($from, $to);

echo '<div class="mzc-title"><h2>نصب‌ها</h2><form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-installs"><label>بازه:</label><select name="days" onchange="this.form.from.value=this.form.to.value=\'\';this.form.submit()">';
if (!$days) { echo '<option value="" selected>بازهٔ دلخواه</option>'; }
foreach (array(7 => '۷ روز اخیر', 30 => '۳۰ روز اخیر', 90 => '۹۰ روز اخیر', 180 => '۶ ماه اخیر', 365 => 'یک سال اخیر', 730 => 'دو سال اخیر') as $d => $l) { echo '<option value="' . $d . '"' . selected($days, $d, false) . '>' . esc_html($l) . '</option>'; }
echo '</select><label>از</label><input type="date" name="from" value="' . esc_attr($fromS) . '"><label>تا</label><input type="date" name="to" value="' . esc_attr($toS) . '">'
    . '<select name="group"><option value="day"' . selected($group, 'day', false) . '>روزانه</option><option value="month"' . selected($group, 'month', false) . '>ماهانه</option></select>'
    . '<button class="mzc-btn sm">نمایش</button></form></div>';

echo '<div class="mzc-cards">';
$kpi = function ($k, $v, $s = '') { echo '<div class="mzc-card mzc-kpi"><div class="k">' . esc_html($k) . '</div><div class="v">' . $v . '</div><div class="s">' . esc_html($s) . '</div></div>'; };
$kpi('نصب‌های جدید', MZC_Admin::num($sum('new')), 'سیستم‌هایی که اولین بار در این بازه دیده شدند');
$kpi('سیستم‌های فعال', MZC_Admin::num($active), 'در این بازه چیزی فرستادند');
$kpi('حذف برنامه', MZC_Admin::num($sum('gone')), $sum('new') ? 'نسبت به نصب‌های جدید: ' . round($sum('gone') / $sum('new') * 100) . '٪' : 'حذف‌هایی که برنامه گزارش داد');
$kpi('اجرای برنامه', MZC_Admin::num($sum('runs')), 'دفعات باز شدن برنامه');
$kpi('همهٔ سیستم‌ها', MZC_Admin::num(MZC_Stats::count_systems()), 'از ابتدا تا امروز');
echo '</div>';

// columns: the height is new installs; the title gives every figure
$max = max(1, max(array_column($rows, 'new') ?: array(0)));
echo '<div class="mzc-panel"><header><h3>نصب جدید ' . ($group === 'month' ? 'در هر ماه' : 'در هر روز') . '</h3><span class="muted">از ' . esc_html(MZC_Crm::jdate(MZC_Crm::ymd($from))) . ' تا ' . esc_html(MZC_Crm::jdate(MZC_Crm::ymd($to))) . '</span></header><div class="body"><div class="mzc-colchart" role="img" aria-label="نمودار نصب‌های جدید">';
foreach ($rows as $k => $x) { echo '<span title="' . esc_attr($k . ': ' . $x['new'] . ' نصب جدید، ' . $x['active'] . ' فعال، ' . $x['runs'] . ' اجرا، ' . $x['gone'] . ' حذف') . '"><i style="height:' . round($x['new'] / $max * 100, 1) . '%"></i></span>'; }
echo '</div></div></div>';

echo '<div class="mzc-grid2">';
$box = function ($title, $rowsx) { echo '<div class="mzc-panel"><header><h3>' . esc_html($title) . '</h3></header><div class="body">' . MZC_Admin::bars($rowsx, 'در این بازه نصبی نبوده است.') . '</div></div>'; };
$box('نصب‌های جدید بر اساس نسخهٔ برنامه', MZC_Stats::new_by('app', $from, $to));
$box('نصب‌های جدید بر اساس ویرایش (مازستا / کاربران)', MZC_Stats::new_by('edition', $from, $to));
echo '</div><div class="mzc-grid2">';
$box2 = function ($title, $rowsx) { echo '<div class="mzc-panel"><header><h3>' . esc_html($title) . '</h3></header><div class="body">' . MZC_Admin::bars($rowsx, 'در این بازه حذفی گزارش نشده است.') . '</div></div>'; };
$box2('حذف‌ها بر اساس نسخهٔ برنامه', MZC_Stats::removed_by_app($from, $to));
$box2('چقدر پس از نصب حذف شد', MZC_Stats::removed_after($from, $to));
echo '</div>';

echo '<div class="mzc-panel"><header><h3>جدول ' . ($group === 'month' ? 'ماهانه' : 'روزانه') . '</h3><span class="muted">«فعال»: سیستم‌هایی که در آن روز چیزی فرستادند' . ($group === 'month' ? ' (بیشترین روز ماه)' : '') . '</span></header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>تاریخ</th><th class="n">نصب جدید</th><th class="n">فعال</th><th class="n">اجرای برنامه</th><th class="n">حذف</th></tr></thead><tbody>';
foreach (array_reverse($rows, true) as $k => $x) { echo '<tr><td class="num">' . esc_html($k) . '</td><td class="n">' . MZC_Admin::num($x['new']) . '</td><td class="n">' . MZC_Admin::num($x['active']) . '</td><td class="n">' . MZC_Admin::num($x['runs']) . '</td><td class="n">' . MZC_Admin::num($x['gone']) . '</td></tr>'; }
echo '</tbody></table></div></div>';
echo '<p class="muted">هر سیستم با شناسهٔ تصادفی‌اش یک بار شمرده می‌شود؛ نصبی که آمار ناشناس را خاموش کرده باشد اینجا دیده نمی‌شود. «حذف» را خود حذف‌کنندهٔ برنامه (از تنظیمات ویندوز) می‌فرستد و فقط نسخه‌های بعد از 0.15.1؛ حذف بدون اینترنت، پاک کردن دستی پوشه یا نسخهٔ قابل حمل (بدون نصب) گزارش نمی‌شود. تاریخ‌ها به وقت سایت است.</p>';
