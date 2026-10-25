<?php
if (!defined('ABSPATH')) { exit; }
/** Accounts and the Pro plan: what the app locks (nothing, until "enforce" is on), and each account's plan, end date and linked computers. */

$s = MZC_Access::settings();
$cat = MZC_Access::catalog();
$q = isset($_GET['q']) ? sanitize_text_field(wp_unslash($_GET['q'])) : '';

echo '<div class="mzc-title"><h2>اشتراک و حساب‌ها</h2></div>';
echo '<p class="muted mzc-prose">تا وقتی «قفل‌گذاری» خاموش است همهٔ نسخه‌های برنامه رایگان و کامل‌اند و کاربر چیزی از این سامانه نمی‌بیند. با روشن‌کردنش، نسخه‌هایی که به سایت وصل شوند منوها و زبانه‌های علامت‌خورده را برای حساب‌های بدون اشتراک پرو قفل می‌بینند؛ حساب با شمارهٔ موبایل و کد پیامکی (همان درگاه پیامک پورتال مشتری) ساخته می‌شود و سیستم‌های آن شماره به آن وصل می‌شوند. داشبورد، پیام‌ها، تنظیمات، به‌روزرسانی برنامه و راهنما هرگز قفل نمی‌شوند.</p>';

echo '<div class="mzc-panel"><header><h3>قفل‌گذاری</h3></header><div class="body">' . MZC_Admin::form_open('access_save', 'class="mzc-form"') . '<div class="mzc-fields">'
    . '<label class="mzc-radio wide"><input type="checkbox" name="enforce" value="1"' . checked(!empty($s['enforce']), true, false) . '> قفل‌گذاری روشن باشد (خاموش: همه چیز رایگان)</label>'
    . '<label class="mzc-radio wide"><input type="checkbox" name="login" value="1"' . checked(!empty($s['login']), true, false) . '> برنامه ورود با شمارهٔ موبایل را پیشنهاد دهد (پیامک: ' . (MZC_Sms::configured() ? 'تنظیم شده' : 'هنوز تنظیم نشده؛ از «تنظیمات»') . ')</label>'
    . '<label class="wide">متنی که روی صفحهٔ قفل نشان داده می‌شود (اختیاری)<textarea name="message" maxlength="300" rows="2">' . esc_textarea((string) $s['message']) . '</textarea></label>'
    . '<div class="mzc-field wide"><span>قفل برای حساب‌های بدون پرو</span><div class="mzc-picks">';
foreach ($cat as $key => $name) {
    echo '<label><input type="checkbox" name="locks[]" value="' . esc_attr($key) . '"' . checked(in_array($key, (array) $s['locks'], true), true, false) . '> ' . esc_html($name) . ' <span class="muted lat">' . esc_html($key) . '</span></label>';
}
echo '</div></div></div><p><button class="mzc-btn primary">ذخیره</button></p></form></div></div>';

echo '<div class="mzc-panel"><header><h3>حساب‌ها</h3></header><div class="body"><form method="get" class="mzc-form"><input type="hidden" name="page" value="mzc-access"><input type="search" name="q" value="' . esc_attr($q) . '" placeholder="بخشی از شمارهٔ موبایل" class="mzc-w420"> <button class="mzc-btn">جست‌وجو</button></form></div>'
    . '<div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>موبایل</th><th>اشتراک</th><th>سیستم‌ها</th><th>آخرین فعالیت</th><th>عملیات</th></tr></thead><tbody>';
$rows = MZC_Access::accounts($q);
if (!$rows) { echo '<tr><td class="empty" colspan="5">هنوز حسابی ساخته نشده است.</td></tr>'; }
foreach ($rows as $r) {
    $pro = MZC_Access::is_pro($r);
    $status = !empty($r['disabled']) ? MZC_Admin::pill('غیرفعال', 'bad') : ($pro ? MZC_Admin::pill('پرو' . (!empty($r['pro_until']) ? ' تا ' . MZC_Admin::when(strtotime($r['pro_until'] . ' UTC')) : ' (بدون پایان)'), 'ok') : MZC_Admin::pill($r['plan'] === 'pro' ? 'پرو (منقضی)' : 'رایگان'));
    $sys = array();
    $names = $r['installs'] ? MZC_Stats::names($r['installs']) : array();
    foreach ($r['installs'] as $i) { $sys[] = '<a href="' . esc_url(MZC_Admin::url('mzc-installs', array('id' => $i))) . '" dir="auto">' . esc_html(isset($names[$i]) ? $names[$i] : substr($i, 0, 8)) . '</a>'; }
    echo '<tr><td class="lat" dir="ltr">' . esc_html($r['mobile']) . '</td><td>' . $status . '</td><td>' . ($sys ? implode('<br>', $sys) : '<span class="muted">—</span>') . '</td>'
        . '<td>' . ($r['last_seen'] ? MZC_Admin::when(strtotime($r['last_seen'] . ' UTC')) : '—') . '</td><td>'
        . MZC_Admin::form_open('account_save', 'class="mzc-inline"') . '<input type="hidden" name="id" value="' . (int) $r['id'] . '"><input type="number" name="days" min="1" max="3650" value="30" class="mzc-w80" title="روز"> '
        . '<button name="do" value="pro" class="mzc-btn sm primary">افزودن روز پرو</button> <button name="do" value="free" class="mzc-btn sm">رایگان</button> '
        . (!empty($r['disabled']) ? '<button name="do" value="enable" class="mzc-btn sm">فعال‌کردن</button>' : '<button name="do" value="disable" class="mzc-btn sm danger">غیرفعال‌کردن</button>') . '</form></td></tr>';
}
echo '</tbody></table></div></div>';
