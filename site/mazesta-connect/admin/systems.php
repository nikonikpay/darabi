<?php
if (!defined('ABSPATH')) { exit; }
/** The systems that run the app: a list with a short summary each, and one page per system with its full specification and what it did. */

$installs = MZC_Stats::installs();
$sys = isset($_GET['sys']) ? sanitize_text_field(wp_unslash($_GET['sys'])) : '';

if ($sys !== '' && isset($installs[$sys])) {
    $i = $installs[$sys]; $m = isset($i['machine']) && is_array($i['machine']) ? $i['machine'] : array();
    $last = !empty($i['last']) ? strtotime($i['last']) : 0; $first = !empty($i['first']) ? strtotime($i['first']) : 0;
    $kv = function ($k, $v) { if ($v === null || $v === '' || $v === array()) { return; } echo '<dt>' . esc_html($k) . '</dt><dd dir="auto">' . (is_array($v) ? implode('<br>', array_map('esc_html', $v)) : esc_html((string) $v)) . '</dd>'; };

    echo '<div class="mzc-title"><div><a href="' . esc_url(MZC_Admin::url('mzc-systems')) . '">‹ همهٔ سیستم‌ها</a><h2 dir="auto">' . esc_html(MZC_Stats::label($i)) . '</h2><span class="muted">شناسه: <span class="num">' . esc_html(substr($sys, 0, 8)) . '…</span></span></div>'
        . '<div class="mzc-actions"><a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-messages', array('to' => $sys))) . '">ارسال پیام به این سیستم</a>'
        . '<a class="mzc-btn" href="' . esc_url(MZC_Admin::url('mzc-events', array('sys' => $sys))) . '">همهٔ رویدادها</a></div></div>';

    echo '<div class="mzc-grid2"><div class="mzc-panel"><header><h3>مشخصات</h3></header><div class="body"><dl class="mzc-kv">';
    $kv('پردازنده', isset($m['cpu']) ? $m['cpu'] : null);
    $kv('هسته / رشته', isset($m['cores']) ? $m['cores'] . ' / ' . (isset($m['threads']) ? $m['threads'] : '—') : null);
    $kv('کارت گرافیک', isset($m['gpus']) ? $m['gpus'] : null);
    $kv('نسخهٔ درایور گرافیک', isset($m['gpuDriver']) ? $m['gpuDriver'] : null);
    $kv('مادربرد', isset($m['board']) ? $m['board'] : null);
    $kv('حافظهٔ رم', isset($m['ramGb']) ? $m['ramGb'] . ' GB' : null);
    $kv('ماژول‌های رم', isset($m['ramModules']) ? $m['ramModules'] : null);
    $kv('ذخیره‌سازها', isset($m['storage']) ? $m['storage'] : null);
    $kv('ویندوز', isset($m['os']) ? $m['os'] : null);
    echo '</dl>' . (isset($m['board']) || isset($m['storage']) ? '' : '<p class="muted">مادربرد، رم و ذخیره‌ساز را فقط نسخه‌های تازه‌تر برنامه می‌فرستند؛ با اولین ارسال بعدی اینجا پر می‌شود.</p>') . '</div></div>';

    echo '<div class="mzc-panel"><header><h3>برنامه و فعالیت</h3></header><div class="body"><dl class="mzc-kv">';
    $kv('نسخهٔ برنامه', isset($i['app']) ? $i['app'] : null); $kv('نوع نسخه', isset($i['edition']) ? ($i['edition'] === 'company' ? 'شرکتی' : 'کاربران') : null);
    $kv('زبان', isset($i['lang']) ? $i['lang'] : null);
    echo '<dt>اولین دیده شدن</dt><dd>' . MZC_Admin::when($first) . '</dd><dt>آخرین فعالیت</dt><dd>' . MZC_Admin::when($last) . ' <span class="muted">(' . esc_html(MZC_Admin::ago($last)) . ')</span></dd>';
    $kv('تعداد رویدادها', isset($i['n']) ? number_format_i18n($i['n']) : null);
    echo '</dl>' . MZC_Admin::form_open('system_note', 'style="margin-top:14px"') . '<input type="hidden" name="id" value="' . esc_attr($sys) . '"><div class="mzc-field"><span>یادداشت شما دربارهٔ این سیستم</span>'
        . '<input type="text" name="note" maxlength="300" value="' . esc_attr(isset($i['note']) ? $i['note'] : '') . '" placeholder="مثلاً: سیستم آقای احمدی، کیس شمارهٔ ۱۲"></div><p><button class="mzc-btn">ذخیرهٔ یادداشت</button></p></form></div></div></div>';

    $rows = array();
    foreach (MZC_Stats::events(2) as $e) { if ($e['i'] === $sys && $e['k'] !== 'app.start') { $rows[] = MZC_Stats::row($e); } }
    echo '<div class="mzc-panel"><header><h3>آخرین رویدادهای این سیستم</h3><span class="muted">' . esc_html(number_format_i18n(count($rows))) . ' رویداد (دو ماه اخیر)</span></header><div class="body flush">' . MZC_Admin::events_table(array_slice($rows, 0, 40), false) . '</div></div>';
    return;
}

$q = isset($_GET['s']) ? sanitize_text_field(wp_unslash($_GET['s'])) : '';
$order = isset($_GET['o']) ? sanitize_key($_GET['o']) : 'last';
$paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 30;
$list = array();
foreach ($installs as $id => $i) {
    $m = isset($i['machine']) && is_array($i['machine']) ? $i['machine'] : array();
    $hay = implode(' ', array(MZC_Stats::label($i), isset($m['cpu']) ? $m['cpu'] : '', implode(' ', isset($m['gpus']) ? (array) $m['gpus'] : array()), isset($i['note']) ? $i['note'] : '', isset($m['board']) ? $m['board'] : ''));
    if ($q === '' || mb_stripos($hay, $q) !== false) { $list[$id] = $i; }
}
uasort($list, function ($a, $b) use ($order) {
    if ($order === 'n') { return (int) (isset($b['n']) ? $b['n'] : 0) <=> (int) (isset($a['n']) ? $a['n'] : 0); }
    return strcmp((string) ($order === 'first' ? (isset($b['first']) ? $b['first'] : '') : (isset($b['last']) ? $b['last'] : '')), (string) ($order === 'first' ? (isset($a['first']) ? $a['first'] : '') : (isset($a['last']) ? $a['last'] : '')));
});
$total = count($list);

echo '<div class="mzc-title"><h2>سیستم‌ها</h2><span class="muted">' . esc_html(number_format_i18n($total)) . ' سیستم</span></div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-systems"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="جستجو: کارت گرافیک، پردازنده، مادربرد، یادداشت…"><select name="o">'
    . '<option value="last"' . selected($order, 'last', false) . '>آخرین فعالیت</option><option value="first"' . selected($order, 'first', false) . '>تازه‌ترین نصب</option><option value="n"' . selected($order, 'n', false) . '>بیشترین رویداد</option></select><button class="mzc-btn primary">اعمال</button></form>';
echo '<div class="mzc-panel"><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>سیستم</th><th>پردازنده</th><th>کارت گرافیک</th><th class="n">رم</th><th>ویندوز</th><th>نسخه</th><th class="n">رویداد</th><th>آخرین فعالیت</th><th></th></tr></thead><tbody>';
if (!$list) { echo '<tr><td class="empty" colspan="9">' . ($installs ? 'سیستمی با این جستجو پیدا نشد.' : 'هنوز هیچ سیستمی آمار نفرستاده است.') . '</td></tr>'; }
foreach (array_slice($list, ($paged - 1) * $per, $per, true) as $id => $i) {
    $m = isset($i['machine']) && is_array($i['machine']) ? $i['machine'] : array(); $last = !empty($i['last']) ? strtotime($i['last']) : 0;
    $link = MZC_Admin::url('mzc-systems', array('sys' => $id));
    echo '<tr class="click" data-href="' . esc_url($link) . '"><td dir="auto"><a href="' . esc_url($link) . '"><strong>' . esc_html(!empty($i['note']) ? $i['note'] : substr($id, 0, 8)) . '</strong></a>' . (!empty($i['note']) ? '<br><span class="muted num">' . esc_html(substr($id, 0, 8)) . '</span>' : '') . '</td>'
        . '<td dir="auto">' . esc_html(isset($m['cpu']) ? MZC_Stats::short($m['cpu']) : '—') . '</td><td dir="auto">' . esc_html(isset($m['gpus']) ? implode(' + ', array_map(array('MZC_Stats', 'short'), (array) $m['gpus'])) : '—') . '</td>'
        . '<td class="n">' . (isset($m['ramGb']) ? esc_html($m['ramGb'] . ' GB') : '—') . '</td><td dir="auto">' . esc_html(isset($m['os']) ? preg_replace('/^Microsoft /', '', $m['os']) : '—') . '</td><td class="n">' . esc_html(isset($i['app']) ? $i['app'] : '—') . '</td>'
        . '<td class="n">' . MZC_Admin::num(isset($i['n']) ? $i['n'] : 0) . '</td><td>' . esc_html(MZC_Admin::ago($last)) . '</td>'
        . '<td class="n"><a class="mzc-btn sm" href="' . esc_url(MZC_Admin::url('mzc-messages', array('to' => $id))) . '">پیام</a></td></tr>';
}
echo '</tbody></table></div></div>' . MZC_Admin::pager($total, $per, $paged);
