<?php
if (!defined('ABSPATH')) { exit; }
/** The systems with the most problems in a period (blue screens, sudden restarts, app crashes, failed tests), so a troubled PC is found at a glance. */

$days = isset($_GET['days']) ? (int) $_GET['days'] : 30; $days = in_array($days, array(7, 30, 90), true) ? $days : 30;
$kind = isset($_GET['kind']) ? sanitize_key($_GET['kind']) : ''; $kind = in_array($kind, array('bsod', 'power', 'crash', 'fail'), true) ? $kind : '';
$order = isset($_GET['o']) ? sanitize_key($_GET['o']) : 'total';
$q = isset($_GET['s']) ? sanitize_text_field(wp_unslash($_GET['s'])) : '';
$paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 40;
$rows = MZC_Stats::problems(time() - $days * 86400, $q, $kind, $order); $total = count($rows);
$kinds = array('bsod' => 'صفحهٔ آبی', 'power' => 'ری‌استارت ناگهانی', 'crash' => 'کرش برنامه', 'fail' => 'تست ناموفق');

echo '<div class="mzc-title"><h2>سیستم‌های مشکل‌دار</h2><span class="muted">' . esc_html(number_format_i18n($total)) . ' سیستم با مشکل در ' . esc_html(number_format_i18n($days)) . ' روز اخیر</span></div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-problems"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="جستجوی سخت‌افزار یا یادداشت: مثلاً 3060، B550…"><select name="days">';
foreach (array(7, 30, 90) as $d) { echo '<option value="' . $d . '"' . selected($days, $d, false) . '>' . esc_html(number_format_i18n($d)) . ' روز اخیر</option>'; }
echo '</select><select name="kind"><option value="">همهٔ مشکل‌ها</option>';
foreach ($kinds as $k => $l) { echo '<option value="' . $k . '"' . selected($kind, $k, false) . '>فقط ' . esc_html($l) . '</option>'; }
echo '</select><select name="o">';
foreach (array('total' => 'بیشترین مشکل', 'bsod' => 'بیشترین صفحهٔ آبی', 'power' => 'بیشترین ری‌استارت', 'crash' => 'بیشترین کرش', 'fail' => 'بیشترین تست ناموفق', 'last' => 'تازه‌ترین مشکل') as $k => $l) { echo '<option value="' . $k . '"' . selected($order, $k, false) . '>' . esc_html($l) . '</option>'; }
echo '</select><button class="mzc-btn primary">اعمال</button></form>';

$cell = function ($r, $k) {
    if ($r[$k] === null) { return '<td class="n"><span class="muted" title="معلوم نیست: نسخهٔ قدیمی برنامه تاریخ این مورد را نفرستاده یا ویندوز آن سیستم تاریخچه‌اش را نداده است">؟</span></td>'; }
    $v = (int) $r[$k]; $s = '<span class="num">' . esc_html(number_format_i18n($v) . (in_array($k, $r['more'], true) ? '+' : '')) . '</span>';   // the + inside the left-to-right run, or RTL puts it first
    return '<td class="n">' . ($v ? '<b>' . $s . '</b>' : '<span class="muted">' . $s . '</span>') . '</td>';
};
echo '<div class="mzc-panel"><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>سیستم</th><th class="n">صفحهٔ آبی</th><th class="n">ری‌استارت ناگهانی</th><th class="n">کرش برنامه</th><th class="n">تست ناموفق</th><th class="n">جمع</th><th>آخرین مشکل</th><th>بیشترین علت</th><th></th></tr></thead><tbody>';
if (!$rows) { echo '<tr><td class="empty" colspan="9">در این بازه سیستمی با مشکل گزارش نشده است.</td></tr>'; }
foreach (array_slice($rows, ($paged - 1) * $per, $per) as $r) {
    $i = $r['sys']; $link = MZC_Admin::url('mzc-systems', array('sys' => $r['id']));
    $name = $i && !empty($i['note']) ? $i['note'] : ($i ? MZC_Stats::label($i) : substr($r['id'], 0, 8));
    $why = array(); foreach (array_slice($r['causes'], 0, 3, true) as $c => $n) { $why[] = $c . ' (' . number_format_i18n($n) . ')'; }
    echo '<tr class="click" data-href="' . esc_url($link) . '"><td dir="auto"><a href="' . esc_url($link) . '"><strong>' . esc_html($name) . '</strong></a><br><span class="muted num">' . esc_html(substr($r['id'], 0, 8)) . '</span>'
        . ($i && !empty($i['note']) ? ' <span class="muted">' . esc_html(MZC_Stats::label($i)) . '</span>' : '') . '</td>'
        . $cell($r, 'bsod') . $cell($r, 'power') . $cell($r, 'crash') . $cell($r, 'fail') . '<td class="n">' . '<span class="num">' . MZC_Admin::pill(number_format_i18n($r['total']) . ($r['more'] ? '+' : ''), $r['bsod'] || $r['crash'] ? 'bad' : 'warn') . '</span>' . '</td>'
        . '<td>' . MZC_Admin::when($r['last']) . ' <span class="muted">(' . esc_html(MZC_Admin::ago($r['last'])) . ')</span></td><td dir="auto" title="' . esc_attr(implode('، ', $why)) . '">' . esc_html($r['cause']) . '</td>'
        . '<td class="n"><a class="mzc-btn sm" href="' . esc_url($link) . '">لاگ و جزئیات</a></td></tr>';
}
echo '</tbody></table></div></div>' . MZC_Admin::pager($total, $per, $paged, array('days' => $days, 'kind' => $kind, 'o' => $order, 's' => $q));
echo '<p class="muted">صفحهٔ آبی و ری‌استارت از تاریخ‌هایی است که برنامه از ویندوز خوانده و فرستاده؛ «؟» یعنی معلوم نیست (نسخهٔ قدیمی برنامه فقط جمع کل را فرستاده، یا ویندوز آن سیستم تاریخچه را نداده)، نه اینکه نبوده است. «+» یعنی دست‌کم این تعداد. کرش برنامه از گزارش‌های خطای ویندوز و تست ناموفق از نتیجهٔ تست‌ها و بنچمارک‌هاست.</p>';
