<?php
if (!defined('ABSPATH')) { exit; }
/** Messages to the systems: write one (to every system or chosen ones) and see how many have fetched each. */

$installs = MZC_Stats::installs();
$acks = Mazesta_Connect::read('acks');
$to = isset($_GET['to']) && isset($installs[(string) $_GET['to']]) ? (string) $_GET['to'] : '';
uasort($installs, function ($a, $b) { return strcmp((string) (isset($b['last']) ? $b['last'] : ''), (string) (isset($a['last']) ? $a['last'] : '')); });

echo '<div class="mzc-title"><h2>پیام به سیستم‌ها</h2></div>';
echo '<p class="muted" style="max-width:90ch">پیام در برنامهٔ هر سیستم به شکل اعلان ویندوز می‌آید و در بخش «پیام‌های سیستم» برنامه می‌ماند تا کاربر بعداً دوباره بخواند. برنامه هر ۱۵ دقیقه می‌پرسد و فقط سیستم‌هایی پیام می‌گیرند که برنامه‌شان باز است و ارسال آمار ناشناس در تنظیماتشان روشن است.</p>';

echo '<div class="mzc-panel"><header><h3>پیام تازه</h3></header><div class="body">' . MZC_Admin::form_open('message_send', 'class="mzc-form"') . '<div class="mzc-fields">'
    . '<label class="wide">عنوان<input type="text" name="title" maxlength="120" required></label>'
    . '<label class="wide">متن پیام<textarea name="body" maxlength="2000" rows="4"></textarea></label>'
    . '<label class="wide">پیوند (اختیاری، فقط صفحه‌های dfmrendering.com)<input type="url" name="link" dir="ltr" placeholder="https://www.dfmrendering.com/…"></label>'
    . '<div class="mzc-field wide"><span>گیرنده</span><div><label style="display:inline-flex;gap:6px;font-weight:400"><input type="radio" name="target" value="all" data-mzc-target' . checked($to, '', false) . '> همهٔ سیستم‌ها (' . esc_html(number_format_i18n(count($installs))) . ')</label> &nbsp; '
    . '<label style="display:inline-flex;gap:6px;font-weight:400"><input type="radio" name="target" value="some" data-mzc-target' . checked($to !== '', true, false) . '> فقط سیستم‌های انتخابی</label></div></div></div>'
    . '<div id="mzc-some" style="' . ($to === '' ? 'display:none' : '') . '"><input type="search" data-mzc-filter="mzc-picks" placeholder="جستجوی سیستم…" style="margin-bottom:6px;width:100%;max-width:420px"><div class="mzc-picks" id="mzc-picks">';
foreach ($installs as $id => $i) {
    echo '<label><input type="checkbox" name="systems[]" value="' . esc_attr($id) . '"' . checked($to, $id, false) . '> <span dir="auto">' . esc_html((!empty($i['note']) ? $i['note'] . ' — ' : '') . MZC_Stats::label($i)) . '</span> <span class="muted num">' . esc_html(substr($id, 0, 6)) . '</span></label>';
}
echo '</div></div><p><button class="mzc-btn primary">ارسال پیام</button></p></form></div></div>';

echo '<div class="mzc-panel"><header><h3>پیام‌های فرستاده‌شده</h3></header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>زمان</th><th>عنوان و متن</th><th>گیرنده</th><th class="n">دریافت‌شده</th><th></th></tr></thead><tbody>';
$all = MZC_Messages::all();
if (!$all) { echo '<tr><td class="empty" colspan="5">هنوز پیامی فرستاده نشده است.</td></tr>'; }
foreach ($all as $m) {
    list($got, $of) = MZC_Messages::delivery($m, MZC_Stats::installs(), $acks);
    echo '<tr><td>' . MZC_Admin::when(strtotime($m['created'])) . '</td><td dir="auto"><strong>' . esc_html($m['title']) . '</strong><br><span class="muted">' . esc_html(mb_substr($m['body'], 0, 160)) . '</span></td>'
        . '<td>' . ($m['to'] === 'all' ? MZC_Admin::pill('همهٔ سیستم‌ها', 'info') : MZC_Admin::pill(count((array) $m['to']) . ' سیستم')) . '</td><td class="n"><span class="num">' . esc_html($got . ' / ' . $of) . '</span></td>'
        . '<td class="n"><a class="mzc-btn sm danger" href="' . esc_url(MZC_Admin::post_url('message_delete', array('id' => (int) $m['id']))) . '" onclick="return confirm(\'این پیام پاک شود؟\')">پاک</a></td></tr>';
}
echo '</tbody></table></div></div>';
