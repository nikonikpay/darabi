<?php
if (!defined('ABSPATH')) { exit; }
/** Every event, filterable and paged: which system, what ran, how it ended, the temperatures, clocks, frame rate and score. */

$q = isset($_GET['s']) ? sanitize_text_field(wp_unslash($_GET['s'])) : '';
$kind = isset($_GET['kind']) ? sanitize_text_field(wp_unslash($_GET['kind'])) : '';
$res = isset($_GET['res']) ? sanitize_key($_GET['res']) : '';
$sys = isset($_GET['sys']) && preg_match('/^[a-f0-9]{32}$/', (string) $_GET['sys']) ? (string) $_GET['sys'] : '';
$days = isset($_GET['days']) ? max(1, min(60, (int) $_GET['days'])) : 30;
$paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 40;
$since = time() - $days * 86400;
// the filters run in the database; only "how it ended" is read from each event, so with it the page reads the period's events (up to 20,000) and pages them itself
$f = array('since' => $since, 'not' => 'app.start', 'sys' => $sys, 'kind' => $kind, 'hw' => $q);
if ($res === '') {
    list($evs, $total) = MZC_Stats::events($f + array('limit' => $per, 'offset' => ($paged - 1) * $per), true);
    $rows = array_map(array('MZC_Stats', 'row'), $evs); $page = $rows;
} else {
    $rows = array();
    foreach (MZC_Stats::events($f + array('limit' => 20000))[0] as $e) { $r = MZC_Stats::row($e); if (($res === 'ok' && $r['ok'] === true) || ($res === 'bad' && $r['ok'] === false)) { $rows[] = $r; } }
    $total = count($rows); $page = array_slice($rows, ($paged - 1) * $per, $per);
}

echo '<div class="mzc-title"><h2>رویدادها</h2><span class="muted">' . esc_html(number_format_i18n($total)) . ' رویداد در این بازه</span></div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-events">';
if ($sys !== '') { echo '<input type="hidden" name="sys" value="' . esc_attr($sys) . '">'; }
echo '<input type="search" name="s" value="' . esc_attr($q) . '" placeholder="جستجو: کارت گرافیک، پردازنده، رم، نام تست…"><select name="kind"><option value="">همهٔ انواع</option>';
foreach (array('bench.run', 'test.run', 'tuning.auto', 'tuning.scene', 'tuning.apply', 'ai.ask') as $k) { echo '<option value="' . esc_attr($k) . '"' . selected($kind, $k, false) . '>' . esc_html(MZC_Stats::kind_label($k)) . '</option>'; }
echo '</select><select name="res"><option value="">هر نتیجه</option><option value="ok"' . selected($res, 'ok', false) . '>فقط سالم / موفق</option><option value="bad"' . selected($res, 'bad', false) . '>فقط ناموفق / ایراد</option></select><select name="days">';
foreach (array(7 => '۷ روز', 14 => '۱۴ روز', 30 => '۳۰ روز', 60 => '۶۰ روز') as $d => $l) { echo '<option value="' . $d . '"' . selected($days, $d, false) . '>' . esc_html($l) . '</option>'; }
echo '</select><button class="mzc-btn primary">اعمال</button>';
if ($sys !== '') { echo '<span class="mzc-pill info">فقط یک سیستم</span> <a href="' . esc_url(MZC_Admin::url('mzc-events')) . '">نمایش همه</a>'; }
echo '</form>';
echo '<div class="mzc-panel"><div class="body flush">' . MZC_Admin::events_table($page, true) . '</div></div>';
echo MZC_Admin::pager($total, $per, $paged);
