<?php
if (!defined('ABSPATH')) { exit; }
/** Customers: the list, one customer's file (details and everything bought or serviced), and the form for a new one. */
if (!MZC_Admin::need_crm()) { return; }

$view = isset($_GET['view']) ? (int) $_GET['view'] : 0;
$add = !empty($_GET['add']);

$form = function ($c) {
    $v = function ($k) use ($c) { return $c && isset($c[$k]) ? esc_attr((string) $c[$k]) : ''; };
    echo MZC_Admin::form_open('customer_save', 'class="mzc-form"') . ($c ? '<input type="hidden" name="id" value="' . (int) $c['id'] . '">' : '') . '<div class="mzc-fields">'
        . '<label>نام و نام خانوادگی<input type="text" name="name" value="' . $v('name') . '" maxlength="190" required></label>'
        . '<label>موبایل<input type="text" name="mobile" dir="ltr" value="' . $v('mobile') . '" placeholder="09121234567"><small>ورود مشتری به پورتال با همین شماره است.</small></label>'
        . '<label>موبایل دوم<input type="text" name="mobile2" dir="ltr" value="' . $v('mobile2') . '"></label>'
        . '<label>تلفن ثابت<input type="text" name="phone" dir="ltr" value="' . $v('phone') . '"></label>'
        . '<label>کد ملی<input type="text" name="national_id" dir="ltr" value="' . $v('national_id') . '" maxlength="20"></label>'
        . '<label>ایمیل<input type="email" name="email" dir="ltr" value="' . $v('email') . '"></label>'
        . '<label class="wide">نشانی<textarea name="address" rows="2">' . esc_textarea($c && isset($c['address']) ? (string) $c['address'] : '') . '</textarea></label>'
        . '<label class="wide">یادداشت داخلی (مشتری نمی‌بیند)<textarea name="notes" rows="3">' . esc_textarea($c && isset($c['notes']) ? (string) $c['notes'] : '') . '</textarea></label></div>'
        . '<p><button class="mzc-btn primary">ذخیره</button></p></form>';
};

if ($add) {
    echo '<div class="mzc-title"><div><a href="' . esc_url(MZC_Admin::url('mzc-customers')) . '">‹ مشتریان</a><h2>مشتری تازه</h2></div></div><div class="mzc-panel"><div class="body">';
    $form(null);
    echo '</div></div>';
    return;
}

if ($view) {
    $c = MZC_Crm::customer($view);
    if (!$c) { echo '<div class="mzc-notice err">مشتری پیدا نشد.</div>'; return; }
    list($builds, $jobs) = MZC_Crm::history($view);
    $forms = MZC_Crm::forms_of($view);
    echo '<div class="mzc-title"><div><a href="' . esc_url(MZC_Admin::url('mzc-customers')) . '">‹ مشتریان</a><h2 dir="auto">' . esc_html($c['name']) . '</h2><span class="num muted">' . esc_html(trim($c['mobile'] . ' ' . $c['mobile2'] . ' ' . $c['phone'])) . '</span></div><div class="mzc-actions">'
        . '<a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-builds', array('add' => 1, 'customer' => $view))) . '">ثبت سیستم نو</a>'
        . '<a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-jobs', array('add' => 1, 'customer' => $view))) . '">ثبت سرویس</a>'
        . '<a class="mzc-btn danger" href="' . esc_url(MZC_Admin::post_url('customer_delete', array('id' => $view))) . '" onclick="return confirm(\'این مشتری پاک شود؟\')">حذف مشتری</a></div></div>';
    echo '<div class="mzc-grid2"><div class="mzc-panel"><header><h3>مشخصات</h3></header><div class="body">';
    $form($c);
    echo '</div></div><div><div class="mzc-panel"><header><h3>سیستم‌های خریداری‌شده (' . count($builds) . ')</h3></header><div class="body"><div class="mzc-timeline">';
    if (!$builds) { echo '<p class="muted">هنوز سیستمی ثبت نشده است.</p>'; }
    foreach ($builds as $b) {
        echo '<div class="mzc-item"><div class="top"><strong dir="auto">' . esc_html($b['title']) . '</strong>' . ($b['sold_at'] ? MZC_Admin::pill(MZC_Crm::jdate($b['sold_at'])) : '') . ($b['invoice_no'] ? MZC_Admin::pill('فاکتور ' . $b['invoice_no'], 'info') : '')
            . ($b['service_no'] ? MZC_Admin::pill('سرویس ' . $b['service_no']) : '') . '<a class="mzc-btn sm" href="' . esc_url(MZC_Admin::url('mzc-builds', array('view' => (int) $b['id']))) . '">باز کردن</a></div><span class="muted">' . count($b['parts']) . ' قطعه · ' . count($b['photos']) . ' عکس' . ($b['report_id'] ? ' · گزارش تست دارد' : '') . '</span></div>';
    }
    echo '</div></div></div><div class="mzc-panel"><header><h3>سرویس‌ها (' . count($jobs) . ')</h3></header><div class="body"><div class="mzc-timeline">';
    if (!$jobs) { echo '<p class="muted">هنوز سرویسی ثبت نشده است.</p>'; }
    foreach ($jobs as $j) {
        echo '<div class="mzc-item"><div class="top"><strong dir="auto">' . esc_html($j['device'] ?: 'سرویس') . '</strong>' . MZC_Admin::pill(MZC_Crm::$statuses[$j['status']], $j['status'] === 'delivered' ? 'ok' : ($j['status'] === 'cancelled' ? 'bad' : 'warn'))
            . ($j['received_at'] ? MZC_Admin::pill(MZC_Crm::jdate($j['received_at'])) : '') . ($j['service_no'] ? MZC_Admin::pill('سرویس ' . $j['service_no']) : '') . '<a class="mzc-btn sm" href="' . esc_url(MZC_Admin::url('mzc-jobs', array('view' => (int) $j['id']))) . '">باز کردن</a></div>'
            . '<span dir="auto">' . esc_html(mb_substr((string) $j['complaint'], 0, 140)) . '</span> <span class="muted">· ' . esc_html(MZC_Crm::money($j['total'])) . '</span></div>';
    }
    echo '</div></div>';
    if ($forms) {
        echo '<div class="mzc-panel"><header><h3>فرم‌های دیگر (' . count($forms) . ')</h3></header><div class="body"><div class="mzc-timeline">';
        foreach ($forms as $f) {
            echo '<div class="mzc-item"><div class="top"><strong dir="auto">' . esc_html(isset(MZC_Crm::$form_kinds[$f['kind']]) ? MZC_Crm::$form_kinds[$f['kind']] : $f['kind']) . '</strong>' . ($f['at'] ? MZC_Admin::pill(MZC_Crm::jdate($f['at'])) : '')
                . ($f['ref'] ? MZC_Admin::pill('فاکتور ' . $f['ref'], 'info') : '') . '</div>' . ($f['title'] ? '<span dir="auto">' . esc_html($f['title']) . '</span>' : '') . MZC_Admin::more($f['more'], 'جزئیات') . '</div>';
        }
        echo '</div></div></div>';
    }
    echo '</div></div>';
    return;
}

$q = isset($_GET['s']) ? sanitize_text_field(wp_unslash($_GET['s'])) : '';
$paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 30;
list($rows, $total) = MZC_Crm::customers($q, $paged, $per);
echo '<div class="mzc-title"><h2>مشتریان</h2><a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-customers', array('add' => 1))) . '">+ مشتری تازه</a></div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-customers"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="نام، موبایل یا کد ملی" style="min-width:260px"><button class="mzc-btn primary">جستجو</button><span class="muted">' . esc_html(number_format_i18n($total)) . ' مشتری</span></form>';
echo '<div class="mzc-panel"><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>نام</th><th>موبایل</th><th class="n">سیستم</th><th class="n">سرویس</th><th>ثبت</th></tr></thead><tbody>';
if (!$rows) { echo '<tr><td class="empty" colspan="5">مشتری‌ای پیدا نشد.</td></tr>'; }
foreach ($rows as $c) {
    $link = MZC_Admin::url('mzc-customers', array('view' => (int) $c['id']));
    echo '<tr class="click" data-href="' . esc_url($link) . '"><td dir="auto"><a href="' . esc_url($link) . '"><strong>' . esc_html($c['name']) . '</strong></a></td><td class="num">' . esc_html($c['mobile']) . '</td><td class="n">' . MZC_Admin::num($c['builds']) . '</td><td class="n">' . MZC_Admin::num($c['jobs']) . '</td><td>' . MZC_Admin::when(strtotime($c['created'] . ' UTC')) . '</td></tr>';
}
echo '</tbody></table></div></div>' . MZC_Admin::pager($total, $per, $paged);
