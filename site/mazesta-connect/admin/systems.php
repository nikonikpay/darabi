<?php
if (!defined('ABSPATH')) { exit; }
/** The systems that run the app: a list with a short summary each, and one page per system with its full specification and what it did. */

$sys = isset($_GET['sys']) ? sanitize_text_field(wp_unslash($_GET['sys'])) : '';

if ($sys !== '' && ($i = MZC_Stats::install($sys))) {
    $m = isset($i['machine']) && is_array($i['machine']) ? $i['machine'] : array();
    $last = !empty($i['last']) ? strtotime($i['last']) : 0; $first = !empty($i['first']) ? strtotime($i['first']) : 0;
    $kv = function ($k, $v) { if ($v === null || $v === '' || $v === array()) { return; } echo '<dt>' . esc_html($k) . '</dt><dd dir="auto">' . (is_array($v) ? implode('<br>', array_map('esc_html', $v)) : esc_html((string) $v)) . '</dd>'; };

    echo '<div class="mzc-title"><div><a href="' . esc_url(MZC_Admin::url('mzc-systems')) . '">‹ همهٔ سیستم‌ها</a><h2 dir="auto">' . esc_html(MZC_Stats::label($i)) . '</h2><span class="muted">شناسه: <span class="num">' . esc_html(substr($sys, 0, 8)) . '…</span></span>'
        . (($gone = MZC_Stats::removed(array($sys))) ? ' ' . MZC_Admin::pill('برنامه حذف شده: ' . MZC_Crm::jdate(MZC_Crm::ymd($gone[$sys])), 'bad') : '') . '</div>'
        . '<div class="mzc-actions"><a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-messages', array('to' => $sys))) . '">ارسال پیام به این سیستم</a>'
        . '<a class="mzc-btn" href="' . esc_url(MZC_Admin::url('mzc-events', array('sys' => $sys))) . '">همهٔ رویدادها</a></div></div>';

    // problems first: from every test, benchmark and tuning run the system sent, its log and the health it reported
    list($all) = MZC_Stats::events(array('sys' => $sys, 'kind' => array('bench.run', 'test.run', 'tuning.auto', 'tuning.scene'), 'limit' => 3000));
    $logs = MZC_Stats::logs($sys, 200);
    $warn = MZC_Stats::warnings($i, array_map(array('MZC_Stats', 'row'), $all), $logs);
    echo '<div class="mzc-panel"><header><h3>هشدارها</h3><span class="muted">از آنچه همین سیستم فرستاده: دما، کرش، صفحهٔ آبی، سلامت قطعات و تنظیمات رم</span></header><div class="body">';
    if (!$warn) { echo '<p class="muted">' . (isset($m['health']) ? 'مشکلی گزارش نشده است.' : 'مشکلی گزارش نشده است. سلامت قطعات، صفحهٔ آبی و پروفایل رم را فقط نسخه‌های بعد از 0.15.1 می‌فرستند.') . '</p>'; }
    else { echo '<ul class="mzc-warns">'; foreach ($warn as $x) { echo '<li class="' . esc_attr($x[0]) . '"><b>' . esc_html($x[1]) . '</b>' . ($x[2] !== '' ? '<span dir="auto">' . esc_html($x[2]) . '</span>' : '') . '</li>'; } echo '</ul>'; }
    echo '</div></div>';

    echo '<div class="mzc-grid2"><div class="mzc-panel"><header><h3>مشخصات</h3></header><div class="body"><dl class="mzc-kv">';
    $kv('پردازنده', isset($m['cpu']) ? $m['cpu'] : null);
    $kv('هسته / رشته', isset($m['cores']) ? $m['cores'] . ' / ' . (isset($m['threads']) ? $m['threads'] : '—') : null);
    $kv('کارت گرافیک', isset($m['gpus']) ? $m['gpus'] : null);
    $kv('نسخهٔ درایور گرافیک', isset($m['gpuDriver']) ? $m['gpuDriver'] : null);
    $kv('مادربرد', isset($m['board']) ? $m['board'] : null);
    $kv('حافظهٔ رم', isset($m['ramGb']) ? $m['ramGb'] . ' GB' : null);
    $kv('ماژول‌های رم', isset($m['ramModules']) ? $m['ramModules'] : null);
    $kv('ذخیره‌سازها', isset($m['storage']) ? $m['storage'] : null);
    $kv('سلامت ذخیره‌سازها', isset($m['drives']) ? array_map(function ($d) { return $d['n'] . ' — ' . (isset($d['health']) ? $d['health'] : 'نامشخص') . (isset($d['wear']) ? '، فرسودگی ' . $d['wear'] . '٪' : ''); }, $m['drives']) : null);
    $kv('صفحهٔ آبی / خاموشی ناگهانی', isset($m['health']['bsod']) ? $m['health']['bsod'] . ' / ' . (isset($m['health']['powerLosses']) ? $m['health']['powerLosses'] : '—') : null);
    $kv('ویندوز', isset($m['os']) ? $m['os'] : null);
    echo '</dl>' . (isset($m['board']) || isset($m['storage']) ? '' : '<p class="muted">مادربرد، رم و ذخیره‌ساز را فقط نسخه‌های تازه‌تر برنامه می‌فرستند؛ با اولین ارسال بعدی اینجا پر می‌شود.</p>') . '</div></div>';

    echo '<div class="mzc-panel"><header><h3>برنامه و فعالیت</h3></header><div class="body"><dl class="mzc-kv">';
    $kv('نسخهٔ برنامه', isset($i['app']) ? $i['app'] : null); $kv('نوع نسخه', isset($i['edition']) ? ($i['edition'] === 'company' ? 'شرکتی' : 'کاربران') : null);
    $kv('زبان', isset($i['lang']) ? $i['lang'] : null);
    echo '<dt>اولین دیده شدن</dt><dd>' . MZC_Admin::when($first) . '</dd><dt>آخرین فعالیت</dt><dd>' . MZC_Admin::when($last) . ' <span class="muted">(' . esc_html(MZC_Admin::ago($last)) . ')</span></dd>';
    $kv('تعداد رویدادها', isset($i['n']) ? number_format_i18n($i['n']) : null);
    echo '</dl>' . MZC_Admin::form_open('system_note', 'class="mzc-mt14"') . '<input type="hidden" name="id" value="' . esc_attr($sys) . '"><div class="mzc-field"><span>یادداشت شما دربارهٔ این سیستم</span>'
        . '<input type="text" name="note" maxlength="300" value="' . esc_attr(isset($i['note']) ? $i['note'] : '') . '" placeholder="مثلاً: سیستم آقای احمدی، کیس شمارهٔ ۱۲"></div><p><button class="mzc-btn">ذخیرهٔ یادداشت</button></p></form></div></div></div>';

    list($evs, $evn) = MZC_Stats::events(array('sys' => $sys, 'limit' => 80), true);
    $rows = array_map(array('MZC_Stats', 'row'), $evs);
    echo '<div class="mzc-panel"><header><h3>آخرین رویدادهای این سیستم</h3><span class="muted">' . esc_html(number_format_i18n($evn)) . ' رویداد</span></header><div class="body flush">' . MZC_Admin::events_table($rows, false) . '</div></div>';

    // the app's own log and its crashes, as the system sent them (newest first)
    echo '<div class="mzc-panel"><header><h3>لاگ برنامه و کرش‌ها</h3><span class="muted">' . esc_html(MZC_Stats::LOG_DAYS) . ' روز اخیر؛ مسیر پوشهٔ کاربر و نام کامپیوتر را خود برنامه حذف می‌کند</span></header><div class="body">';
    if (!$logs) { echo '<p class="muted">هنوز لاگی نرسیده است (فقط نسخه‌های تازه‌تر برنامه، با ارسال آمار روشن، لاگ می‌فرستند).</p>'; }
    foreach (array_slice($logs, 0, 30) as $l) {
        $cr = json_decode((string) $l['crashes'], true); $cr = is_array($cr) ? $cr : array();
        echo '<details class="mzc-details"><summary>' . MZC_Admin::when(strtotime($l['at'] . ' UTC')) . ' · <span class="num">' . esc_html($l['file'] . ' · ' . $l['app']) . '</span>'
            . ($cr ? ' ' . MZC_Admin::pill(count($cr) . ' کرش', 'bad') : '') . '</summary>';
        foreach ($cr as $c) { echo '<pre dir="ltr">' . esc_html(wp_json_encode($c, JSON_UNESCAPED_SLASHES | JSON_PRETTY_PRINT)) . '</pre>'; }
        echo '<pre dir="ltr" class="mzc-pre-scroll">' . esc_html((string) $l['body']) . '</pre></details>';
    }
    echo '</div></div>';
    return;
}

$q = isset($_GET['s']) ? sanitize_text_field(wp_unslash($_GET['s'])) : '';
$order = isset($_GET['o']) ? sanitize_key($_GET['o']) : 'last';
$paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 30;
list($list, $total) = MZC_Stats::systems($q, $order, $per, ($paged - 1) * $per);

echo '<div class="mzc-title"><h2>سیستم‌ها</h2><span class="muted">' . esc_html(number_format_i18n($total)) . ' سیستم</span></div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-systems"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="جستجوی سخت‌افزار: مثلاً 3060، 5900HS، 32 GB، B550، یادداشت… (چند کلمه = همه با هم)"><select name="o">'
    . '<option value="last"' . selected($order, 'last', false) . '>آخرین فعالیت</option><option value="first"' . selected($order, 'first', false) . '>تازه‌ترین نصب</option><option value="n"' . selected($order, 'n', false) . '>بیشترین رویداد</option></select><button class="mzc-btn primary">اعمال</button>' . ($q !== '' && $total ? ' <a class="mzc-btn" href="' . esc_url(MZC_Admin::url('mzc-messages', array('group' => $q))) . '">پیام به این ' . esc_html(number_format_i18n($total)) . ' سیستم</a>' : '') . '</form>';
echo '<div class="mzc-panel"><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>سیستم</th><th>پردازنده</th><th>کارت گرافیک</th><th class="n">رم</th><th>ویندوز</th><th>نسخه</th><th class="n">رویداد</th><th>تاریخ نصب</th><th>آخرین فعالیت</th><th>وضعیت</th><th></th></tr></thead><tbody>';
$gone = MZC_Stats::removed(array_keys($list));
if (!$list) { echo '<tr><td class="empty" colspan="11">' . ($q !== '' ? 'سیستمی با این جستجو پیدا نشد.' : 'هنوز هیچ سیستمی آمار نفرستاده است.') . '</td></tr>'; }
foreach ($list as $id => $i) {
    $m = isset($i['machine']) && is_array($i['machine']) ? $i['machine'] : array(); $last = !empty($i['last']) ? strtotime($i['last']) : 0; $first = !empty($i['first']) ? strtotime($i['first']) : 0;
    $nw = MZC_Stats::warn_count($i);
    $state = isset($gone[$id]) ? MZC_Admin::pill('حذف شده', 'bad') : ($nw ? MZC_Admin::pill($nw . ' هشدار', 'warn') : '<span class="muted">—</span>');
    $link = MZC_Admin::url('mzc-systems', array('sys' => $id));
    echo '<tr class="click" data-href="' . esc_url($link) . '"><td dir="auto"><a href="' . esc_url($link) . '"><strong>' . esc_html(!empty($i['note']) ? $i['note'] : substr($id, 0, 8)) . '</strong></a>' . (!empty($i['note']) ? '<br><span class="muted num">' . esc_html(substr($id, 0, 8)) . '</span>' : '') . '</td>'
        . '<td dir="auto">' . esc_html(isset($m['cpu']) ? MZC_Stats::short($m['cpu']) : '—') . '</td><td dir="auto">' . esc_html(isset($m['gpus']) ? implode(' + ', array_map(array('MZC_Stats', 'short'), (array) $m['gpus'])) : '—') . '</td>'
        . '<td class="n">' . (isset($m['ramGb']) ? esc_html($m['ramGb'] . ' GB') : '—') . '</td><td dir="auto">' . esc_html(isset($m['os']) ? preg_replace('/^Microsoft /', '', $m['os']) : '—') . '</td><td class="n">' . esc_html(isset($i['app']) ? $i['app'] : '—') . '</td>'
        . '<td class="n">' . MZC_Admin::num(isset($i['n']) ? $i['n'] : 0) . '</td><td>' . MZC_Admin::when($first) . '</td><td>' . esc_html(MZC_Admin::ago($last)) . '</td><td>' . $state . '</td>'
        . '<td class="n"><a class="mzc-btn sm" href="' . esc_url(MZC_Admin::url('mzc-messages', array('to' => $id))) . '">پیام</a></td></tr>';
}
echo '</tbody></table></div></div>' . MZC_Admin::pager($total, $per, $paged);
