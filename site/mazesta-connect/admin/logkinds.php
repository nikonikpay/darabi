<?php
if (!defined('ABSPATH')) { exit; }
/** The warnings and errors in the systems' logs, grouped by what they say: a fault that many PCs share is one row with a count, so it is found without opening each system. */

$days = isset($_GET['days']) ? (int) $_GET['days'] : 30; $days = in_array($days, array(7, 30, 90), true) ? $days : 30;
$lv = isset($_GET['lv']) ? strtoupper(sanitize_key($_GET['lv'])) : ''; $lv = in_array($lv, array('ERR', 'WRN', 'FTL'), true) ? $lv : '';
$q = isset($_GET['s']) ? sanitize_text_field(wp_unslash($_GET['s'])) : '';
$paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 40;
$res = MZC_Stats::log_kinds(time() - $days * 86400);
$rows = array_values(array_filter($res['kinds'], function ($k) use ($lv, $q) { return ($lv === '' || $k['level'] === $lv) && ($q === '' || stripos($k['text'], $q) !== false); })); $total = count($rows);

echo '<div class="mzc-title"><h2>خلاصهٔ خطاهای لاگ</h2><span class="muted">' . esc_html(number_format_i18n($total)) . ' نوع پیام از ' . esc_html(number_format_i18n($res['systems'])) . ' سیستم</span></div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-logkinds"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="متن پیام: مثلاً fan، PawnIO، OpenRGB"><select name="days">';
foreach (array(7, 30, 90) as $d) { echo '<option value="' . $d . '"' . selected($days, $d, false) . '>' . esc_html(number_format_i18n($d)) . ' روز اخیر</option>'; }
echo '</select><select name="lv"><option value="">خطا و هشدار</option>';
foreach (array('ERR' => 'فقط خطا', 'WRN' => 'فقط هشدار', 'FTL' => 'فقط خطای مرگبار') as $k => $l) { echo '<option value="' . $k . '"' . selected($lv, $k, false) . '>' . esc_html($l) . '</option>'; }
echo '</select><button class="mzc-btn primary">اعمال</button></form>';

echo '<div class="mzc-panel"><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>سطح</th><th>پیام (عددها #)</th><th class="n">سیستم</th><th class="n">بار</th><th>نمونه سیستم‌ها</th></tr></thead><tbody>';
if (!$rows) { echo '<tr><td class="empty" colspan="5">در این بازه پیامی با این فیلتر نیست.</td></tr>'; }
foreach (array_slice($rows, ($paged - 1) * $per, $per) as $r) {
    $links = array(); foreach ($r['ids'] as $id) { $links[] = '<a class="num" href="' . esc_url(MZC_Admin::url('mzc-systems', array('sys' => $id))) . '">' . esc_html(substr($id, 0, 8)) . '</a>'; }
    echo '<tr><td>' . MZC_Admin::pill($r['level'] === 'WRN' ? 'هشدار' : 'خطا', $r['level'] === 'WRN' ? 'warn' : 'bad') . '</td><td dir="ltr" title="' . esc_attr($r['example']) . '">' . esc_html(mb_substr($r['text'], 0, 160)) . '</td>'
        . '<td class="n"><b class="num">' . esc_html(number_format_i18n($r['count'])) . '</b></td><td class="n"><span class="num">' . esc_html(number_format_i18n($r['n'])) . '</span></td><td>' . implode('، ', $links) . '</td></tr>';
}
echo '</tbody></table></div></div>' . MZC_Admin::pager($total, $per, $paged, array('days' => $days, 'lv' => $lv, 's' => $q));
echo '<p class="muted">از ' . esc_html(number_format_i18n($res['read'])) . ' لاگ خوانده شد' . ($res['cut'] ? '؛ لاگ‌های قدیمی‌تر از این تعداد خوانده نشد، پس شمارش‌ها کمینه است' : '') . '. نتیجه تا ده دقیقه ذخیره می‌ماند. با نگه‌داشتن ماوس روی پیام، یک نمونهٔ کامل آن دیده می‌شود.</p>';
