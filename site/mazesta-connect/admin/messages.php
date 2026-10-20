<?php
if (!defined('ABSPATH')) { exit; }
/** Messages to the systems: write one (to every system or chosen ones) and see how many have fetched each. */

$to = isset($_GET['to']) && MZC_Stats::install((string) $_GET['to']) ? (string) $_GET['to'] : '';
$group = isset($_GET['group']) ? sanitize_text_field(wp_unslash($_GET['group'])) : '';
list($installs) = MZC_Stats::systems('', 'last', 200);   // the chosen-systems list: the 200 latest, not every system (a bigger set is a group)
if ($to !== '' && !isset($installs[$to])) { $installs = MZC_Stats::names(array($to)) + $installs; }
$mode = $to !== '' ? 'some' : ($group !== '' ? 'group' : 'all');

echo '<div class="mzc-title"><h2>پیام به سیستم‌ها</h2></div>';
echo '<p class="muted mzc-prose">پیام در برنامهٔ هر سیستم به شکل اعلان ویندوز می‌آید و در بخش «پیام‌های سیستم» برنامه می‌ماند تا کاربر بعداً دوباره بخواند. برنامه یک دقیقه پس از باز شدن و بعد هر ۸ ساعت (روزی حداکثر سه بار) می‌پرسد و فقط سیستم‌هایی پیام می‌گیرند که برنامه‌شان باز است و ارسال آمار ناشناس در تنظیماتشان روشن است.</p>';

echo '<div class="mzc-panel"><header><h3>پیام تازه</h3></header><div class="body">' . MZC_Admin::form_open('message_send', 'class="mzc-form"') . '<div class="mzc-fields">'
    . '<label class="wide">عنوان<input type="text" name="title" maxlength="120" required></label>'
    . '<label class="wide">متن پیام<textarea name="body" maxlength="2000" rows="4"></textarea></label>'
    . '<label class="wide">پیوند (اختیاری، فقط صفحه‌های dfmrendering.com)<input type="url" name="link" dir="ltr" placeholder="https://www.dfmrendering.com/…"></label>'
    . '<div class="mzc-field wide"><span>گیرنده</span><div><label class="mzc-radio"><input type="radio" name="target" value="all" data-mzc-target' . checked($mode, 'all', false) . '> همهٔ سیستم‌ها (' . esc_html(number_format_i18n(MZC_Stats::count_systems())) . ')</label> &nbsp; '
    . '<label class="mzc-radio"><input type="radio" name="target" value="group" data-mzc-target' . checked($mode, 'group', false) . '> گروهی بر اساس سخت‌افزار</label> &nbsp; '
    . '<label class="mzc-radio"><input type="radio" name="target" value="some" data-mzc-target' . checked($mode, 'some', false) . '> فقط سیستم‌های انتخابی</label></div></div></div>'
    . '<div id="mzc-group" class="mzc-field" ' . ($mode === 'group' ? '' : 'hidden') . '><span>هر سیستمی که این کلمه‌ها در مشخصاتش باشد (کارت گرافیک، پردازنده، رم، مادربرد، ویندوز، درایور…)؛ سیستمی که بعداً به این گروه بپیوندد هم پیام را می‌گیرد</span>'
    . '<input type="text" name="group" maxlength="120" value="' . esc_attr($group) . '" placeholder="مثلاً: 3060 یا 4090 laptop یا 5900HS" class="mzc-w420"> '
    . ($group !== '' ? '<span class="muted">' . esc_html(number_format_i18n(MZC_Stats::systems($group, 'last', 1)[1])) . ' سیستم الان در این گروه است</span>' : '') . '</div>'
    . '<div id="mzc-some" ' . ($mode === 'some' ? '' : 'hidden') . '><input type="search" data-mzc-filter="mzc-picks" placeholder="جستجوی سیستم…" class="mzc-w420 mzc-mb6"><div class="mzc-picks" id="mzc-picks">';
foreach ($installs as $id => $i) {
    echo '<label><input type="checkbox" name="systems[]" value="' . esc_attr($id) . '"' . checked($to, $id, false) . '> <span dir="auto">' . esc_html((!empty($i['note']) ? $i['note'] . ' — ' : '') . MZC_Stats::label($i)) . '</span> <span class="muted num">' . esc_html(substr($id, 0, 6)) . '</span></label>';
}
echo '</div></div><p><button class="mzc-btn primary">ارسال پیام</button></p></form></div></div>';

echo '<div class="mzc-panel"><header><h3>پیام‌های فرستاده‌شده</h3></header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>زمان</th><th>عنوان و متن</th><th>گیرنده</th><th class="n">دریافت‌شده</th><th></th></tr></thead><tbody>';
$all = MZC_Messages::all();
if (!$all) { echo '<tr><td class="empty" colspan="5">هنوز پیامی فرستاده نشده است.</td></tr>'; }
foreach ($all as $m) {
    list($got, $of) = MZC_Messages::delivery($m);
    echo '<tr><td>' . MZC_Admin::when(strtotime($m['created'])) . '</td><td dir="auto"><strong>' . esc_html($m['title']) . '</strong><br><span class="muted">' . esc_html(mb_substr($m['body'], 0, 160)) . '</span></td>'
        . '<td>' . ($m['to'] === 'all' ? MZC_Admin::pill('همهٔ سیستم‌ها', 'info') : (isset($m['to']['q']) ? MZC_Admin::pill('گروه: ' . $m['to']['q'], 'info') : MZC_Admin::pill(count((array) $m['to']) . ' سیستم'))) . '</td><td class="n"><span class="num">' . esc_html($got . ' / ' . $of) . '</span></td>'
        . '<td class="n"><a class="mzc-btn sm danger" href="' . esc_url(MZC_Admin::post_url('message_delete', array('id' => (int) $m['id']))) . '" onclick="return confirm(\'این پیام پاک شود؟\')">پاک</a></td></tr>';
}
echo '</tbody></table></div></div>';
