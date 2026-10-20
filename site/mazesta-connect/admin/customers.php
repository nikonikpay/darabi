<?php
if (!defined('ABSPATH')) { exit; }
/** Customers: the list, one customer's file (details and everything bought or serviced), and the form for a new one. */
if (!MZC_Admin::need_crm()) { return; }

$view = isset($_GET['view']) ? (int) $_GET['view'] : 0;
$add = !empty($_GET['add']);

/** Any number of mobile numbers: the first is the main one (SMS); the customer signs in to their page with any of them. */
$mobiles = function ($c) {
    $row = function ($n) { return '<div class="mzc-row"><input type="text" name="mobiles[]" dir="ltr" value="' . esc_attr($n) . '" placeholder="09121234567" inputmode="tel"><button type="button" class="mzc-btn sm danger x" data-mzc-remove>حذف</button></div>'; };
    $list = $c ? MZC_Crm::mobiles_of($c) : array(); if (!$list) { $list = array(''); }
    return '<div class="mzc-field wide"><span>موبایل‌ها (اولی شمارهٔ اصلی و پیامک است؛ ورود مشتری به پورتال با هر کدام)</span><div class="mzc-rows" id="mzc-mob" data-next="' . count($list) . '">' . implode('', array_map($row, $list))
        . '</div><template id="mzc-mob-tpl">' . $row('') . '</template><p><button type="button" class="mzc-btn sm" data-mzc-add="mzc-mob">+ موبایل</button></p></div>';
};
$form = function ($c) use ($mobiles) {
    $v = function ($k) use ($c) { return $c && isset($c[$k]) ? esc_attr((string) $c[$k]) : ''; };
    echo MZC_Admin::form_open('customer_save', 'class="mzc-form"') . ($c ? '<input type="hidden" name="id" value="' . (int) $c['id'] . '">' : '') . '<div class="mzc-fields">'
        . '<label>نام و نام خانوادگی<input type="text" name="name" value="' . $v('name') . '" maxlength="190" required></label>'
        . '<label>گروه<select name="grp">' . implode('', array_map(function ($k, $l) use ($c) { return '<option value="' . esc_attr($k) . '"' . selected($c && isset($c['grp']) ? $c['grp'] : 'customer', $k, false) . '>' . esc_html($l) . '</option>'; }, array_keys(MZC_Crm::$groups), MZC_Crm::$groups)) . '</select></label>'
        . $mobiles($c)
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
    echo '<div class="mzc-title"><div><a href="' . esc_url(MZC_Admin::url('mzc-customers')) . '">‹ مشتریان</a><h2 dir="auto">' . esc_html($c['name']) . '</h2><span class="num muted">' . esc_html(trim(implode(' · ', MZC_Crm::mobiles_of($c)) . ' ' . $c['phone'])) . '</span></div><div class="mzc-actions">'
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
    list($ships) = MZC_Ships::ships('', '', 0, 1, 50, $view);
    echo '<div class="mzc-panel"><header><h3>ارسال به گارانتی (' . count($ships) . ')</h3><a class="mzc-btn sm" href="' . esc_url(MZC_Admin::url('mzc-ships', array('add' => 1, 'customer' => $view))) . '">+ ارسال تازه</a></header><div class="body"><div class="mzc-timeline">';
    if (!$ships) { echo '<p class="muted">قطعه‌ای از این مشتری به گارانتی نرفته است.</p>'; }
    foreach ($ships as $sh) {
        echo '<div class="mzc-item"><div class="top"><strong dir="auto">' . esc_html($sh['part_name']) . '</strong>' . ($sh['vendor_name'] ? MZC_Admin::pill($sh['vendor_name']) : '') . ($sh['sent_at'] ? MZC_Admin::pill(MZC_Crm::jdate($sh['sent_at'])) : '')
            . ($sh['received_at'] ? MZC_Admin::pill('برگشت', 'ok') : MZC_Admin::pill('در گارانتی', 'warn')) . '<a class="mzc-btn sm" href="' . esc_url(MZC_Admin::url('mzc-ships', array('edit' => (int) $sh['id']))) . '">باز کردن</a></div></div>';
    }
    echo '</div></div></div>';
    // the customer's money: every invoice and payment, and the running balance (services count once they are ready or delivered)
    $led = MZC_Acc::ledger($view); list($invs) = MZC_Acc::invoices(array('customer' => $view), 1, 100);
    echo '<div class="mzc-panel"><header><h3>حساب مشتری</h3>' . MZC_Admin::pill(($led['balance'] > 0 ? 'بدهکار ' : ($led['balance'] < 0 ? 'بستانکار ' : 'تسویه ')) . number_format(abs($led['balance'])) . ' تومان', $led['balance'] > 0 ? 'warn' : 'ok') . '</header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>تاریخ</th><th>شرح</th><th class="n">بدهکار</th><th class="n">بستانکار</th><th class="n">مانده</th></tr></thead><tbody>';
    if (!$led['rows']) { echo '<tr><td class="empty" colspan="5">گردش مالی ثبت نشده است.</td></tr>'; }
    foreach ($led['rows'] as $r) {
        $href = $r['ref'] === 'invoice' ? MZC_Admin::url('mzc-acc', array('view' => $r['id'])) : ($r['ref'] === 'job' ? MZC_Admin::url('mzc-jobs', array('view' => $r['id'])) : MZC_Admin::url('mzc-acc', array('tab' => 'payments', 'customer' => $view)));
        echo '<tr class="click' . (!empty($r['dead']) ? ' muted' : '') . '" data-href="' . esc_url($href) . '"><td>' . esc_html($r['at'] ? MZC_Crm::jdate($r['at']) : '—') . '</td><td>' . esc_html($r['text']) . '</td><td class="n">' . ($r['debit'] ? MZC_Admin::num($r['debit']) : '') . '</td><td class="n">' . ($r['credit'] ? MZC_Admin::num($r['credit']) : '') . '</td><td class="n">' . MZC_Admin::num($r['balance']) . '</td></tr>';
    }
    echo '</tbody></table></div></div>';
    if ($invs) {
        echo '<div class="mzc-panel"><header><h3>فاکتورها و پیش‌فاکتورها (' . count($invs) . ')</h3></header><div class="body"><div class="mzc-timeline">';
        foreach ($invs as $v) { echo '<div class="mzc-item"><div class="top"><strong>' . esc_html(MZC_Acc::$kinds[$v['kind']] . ' ' . ($v['no'] ?: '(پیش‌نویس)')) . '</strong>' . MZC_Admin::pill(MZC_Acc::$statuses[$v['status']], $v['status'] === 'final' ? 'ok' : ($v['status'] === 'void' ? 'bad' : 'warn')) . ($v['at'] ? MZC_Admin::pill(MZC_Crm::jdate($v['at'])) : '') . MZC_Admin::pill(number_format((int) $v['total']) . ' تومان') . ($v['wc_order_id'] ? MZC_Admin::pill('سفارش سایت', 'info') : '') . '<a class="mzc-btn sm" href="' . esc_url(MZC_Admin::url('mzc-acc', array('view' => (int) $v['id']))) . '">باز کردن</a></div></div>'; }
        echo '</div></div></div>';
    }
    $acc = MZC_Site::accounts_for($c);
    if ($acc) {
        echo '<div class="mzc-panel"><header><h3>حساب در سایت</h3></header><div class="body"><div class="mzc-timeline">';
        foreach ($acc as $a) { echo '<div class="mzc-item"><div class="top"><strong dir="auto">' . esc_html($a['name']) . '</strong>' . MZC_Admin::pill($a['email']) . '<a class="mzc-btn sm" href="' . esc_url($a['url']) . '">پروفایل</a></div></div>'; }
        echo '</div></div></div>';
    }
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
$grp = isset($_GET['grp']) ? sanitize_key(wp_unslash($_GET['grp'])) : '';
list($rows, $total) = MZC_Crm::customers($q, $paged, $per, $grp);
$linkOf = function ($c) { return MZC_Admin::url('mzc-customers', array('view' => (int) $c['id'])); };
$defs = array(
    'name' => array('نام', 1, function ($c) use ($linkOf) { return '<a dir="auto" href="' . esc_url($linkOf($c)) . '"><strong>' . esc_html($c['name']) . '</strong></a>'; }),
    'grp' => array('گروه', 1, function ($c) { return esc_html(isset(MZC_Crm::$groups[$c['grp']]) ? MZC_Crm::$groups[$c['grp']] : MZC_Crm::$groups['customer']); }),
    'mobile' => array('موبایل', 1, function ($c) { return '<span class="num">' . esc_html($c['mobile']) . '</span>'; }),
    'builds' => array('سیستم', 1, function ($c) { return MZC_Admin::num($c['builds']); }, 'n'),
    'jobs' => array('سرویس', 1, function ($c) { return MZC_Admin::num($c['jobs']); }, 'n'),
    'created' => array('ثبت', 1, function ($c) { return MZC_Admin::when(strtotime($c['created'] . ' UTC')); }),
    'mobile2' => array('موبایل دوم', 0, function ($c) { return '<span class="num">' . esc_html($c['mobile2']) . '</span>'; }),
    'phone' => array('تلفن', 0, function ($c) { return '<span class="num">' . esc_html($c['phone']) . '</span>'; }),
    'national_id' => array('کد ملی', 0, function ($c) { return '<span class="num">' . esc_html($c['national_id']) . '</span>'; }),
);
echo '<div class="mzc-title"><h2>مشتریان</h2><a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-customers', array('add' => 1))) . '">+ مشتری تازه</a></div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-customers"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="نام، موبایل یا کد ملی" class="mzc-search"><select name="grp" onchange="this.form.submit()"><option value="">همهٔ گروه‌ها</option>'
    . implode('', array_map(function ($k, $l) use ($grp) { return '<option value="' . esc_attr($k) . '"' . selected($grp, $k, false) . '>' . esc_html($l) . '</option>'; }, array_keys(MZC_Crm::$groups), MZC_Crm::$groups))
    . '</select><button class="mzc-btn primary">جستجو</button><span class="muted">' . esc_html(number_format_i18n($total)) . ' مشتری</span><span class="mzc-bar-actions">' . MZC_Admin::cols_ui('mzc-customers', $defs) . '</span></form>';
echo MZC_Admin::table('mzc-customers', $defs, $rows, $linkOf, 'مشتری‌ای پیدا نشد.') . MZC_Admin::pager($total, $per, $paged);
