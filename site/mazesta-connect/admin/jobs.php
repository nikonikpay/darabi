<?php
if (!defined('ABSPATH')) { exit; }
/** Service jobs: what the customer asked for, what was done, the parts added with their prices, the photos and the app's report. */
if (!MZC_Admin::need_crm()) { return; }

$view = isset($_GET['view']) ? (int) $_GET['view'] : 0;
$edit = isset($_GET['edit']) ? (int) $_GET['edit'] : 0;
$add = !empty($_GET['add']);
$pillOf = function ($s) { return MZC_Admin::pill(isset(MZC_Crm::$statuses[$s]) ? MZC_Crm::$statuses[$s] : $s, $s === 'delivered' ? 'ok' : ($s === 'cancelled' ? 'bad' : ($s === 'ready' ? 'info' : 'warn'))); };

if ($add || $edit) {
    $j = $edit ? MZC_Crm::job($edit) : null;
    if ($edit && !$j) { echo '<div class="mzc-notice err">سرویس پیدا نشد.</div>'; return; }
    $cid = $j ? (int) $j['customer_id'] : (isset($_GET['customer']) ? (int) $_GET['customer'] : 0);
    $customer = $cid ? MZC_Crm::customer($cid) : null;
    $v = function ($k) use ($j) { return $j && isset($j[$k]) ? esc_attr((string) $j[$k]) : ''; };
    echo '<div class="mzc-title"><div><a href="' . esc_url(MZC_Admin::url('mzc-jobs')) . '">‹ سرویس‌ها</a><h2>' . ($j ? 'ویرایش سرویس' : 'پذیرش سرویس تازه') . '</h2></div></div>';
    echo '<div class="mzc-panel"><div class="body">' . MZC_Admin::form_open('job_save', 'class="mzc-form" enctype="multipart/form-data"') . ($j ? '<input type="hidden" name="id" value="' . (int) $j['id'] . '">' : '') . '<div class="mzc-fields">'
        . MZC_Admin::customer_picker($customer)
        . '<label>دستگاه<input type="text" name="device" value="' . $v('device') . '" maxlength="190" placeholder="مثلاً کیس رندر / لپ‌تاپ ایسوس"></label>'
        . ($j ? '<label>شمارهٔ سرویس<input type="text" name="service_no" value="' . $v('service_no') . '" dir="ltr" maxlength="40"></label>'
            : '<label>شمارهٔ سرویس<input type="text" value="خودکار؛ بعد از ثبت داده می‌شود" disabled><small>از ادامهٔ شماره‌های قبلی (R-…) شماره‌گذاری می‌شود.</small></label>')
        . '<label>تاریخ پذیرش<input type="date" name="received_at" value="' . ($j ? $v('received_at') : esc_attr(gmdate('Y-m-d'))) . '"></label>'
        . '<label>وضعیت<select name="status">';
    foreach (MZC_Crm::$statuses as $k => $l) { echo '<option value="' . esc_attr($k) . '"' . selected($j ? $j['status'] : 'received', $k, false) . '>' . esc_html($l) . '</option>'; }
    echo '</select></label><label>تاریخ تحویل<input type="date" name="closed_at" value="' . $v('closed_at') . '"><small>با وضعیت «تحویل شد» خودکار پر می‌شود.</small></label>'
        . '<label>موعد تحویل<input type="date" name="due_at" value="' . $v('due_at') . '"></label>'
        . '<label>شمارهٔ فاکتور (حسابداری)<input type="text" name="invoice_no" value="' . $v('invoice_no') . '" dir="ltr" maxlength="60"></label>'
        . '<label class="chk"><input type="checkbox" name="is_mazesta" value="1"' . checked($j ? (int) $j['is_mazesta'] : 0, 1, false) . '> سیستم مازستا است</label>'
        . '<label class="chk"><input type="checkbox" name="has_warranty" value="1"' . checked($j ? (int) $j['has_warranty'] : 0, 1, false) . '> گارانتی دارد</label>'
        . '<label class="wide">درخواست و مشکل مشتری<textarea name="complaint" rows="3">' . esc_textarea($j ? (string) $j['complaint'] : '') . '</textarea></label>'
        . '<label class="wide">کارهای انجام‌شده<textarea name="work_done" rows="3">' . esc_textarea($j ? (string) $j['work_done'] : '') . '</textarea></label>'
        . '<label>اجرت (تومان)<input type="text" name="labor_price" value="' . $v('labor_price') . '" dir="ltr" inputmode="numeric"></label>'
        . '<label>تخفیف (تومان)<input type="text" name="discount" value="' . $v('discount') . '" dir="ltr" inputmode="numeric"></label>'
        . '<label>پیش‌پرداخت / پرداخت‌شده (تومان)<input type="text" name="paid" value="' . $v('paid') . '" dir="ltr" inputmode="numeric"><small>پرداخت‌های جداگانه را پایین ثبت کنید.</small></label>'
        . MZC_Admin::report_select($j ? $j['report_id'] : '', $j ? (string) $j['service_no'] : '')
        . '<label class="wide">یادداشت داخلی<textarea name="notes" rows="2">' . esc_textarea($j ? (string) $j['notes'] : '') . '</textarea></label></div>';
    echo '<div class="mzc-field"><span>قطعات اضافه‌شده (نام، تعداد، قیمت واحد)</span><div class="mzc-rows" id="mzc-parts" data-next="' . ($j ? count($j['parts']) : 0) . '">';
    $row = function ($n, $p) {
        return '<div class="mzc-row"><input type="text" name="parts[' . $n . '][name]" value="' . esc_attr($p['name']) . '" placeholder="نام قطعه" maxlength="190"><input type="number" name="parts[' . $n . '][qty]" value="' . esc_attr($p['qty']) . '" placeholder="تعداد" min="1">'
            . '<input type="text" name="parts[' . $n . '][unit_price]" value="' . esc_attr($p['unit_price']) . '" placeholder="قیمت واحد (تومان)" dir="ltr" inputmode="numeric"><input type="text" name="parts[' . $n . '][serial]" value="' . esc_attr($p['serial']) . '" placeholder="سریال" dir="ltr" maxlength="120">'
            . '<input type="number" name="parts[' . $n . '][warranty_months]" value="' . esc_attr($p['warranty_months']) . '" placeholder="گارانتی (ماه)" min="0" max="120"><button type="button" class="mzc-btn sm danger x" data-mzc-remove>حذف</button></div>';
    };
    if ($j) { foreach ($j['parts'] as $n => $p) { echo $row($n, $p); } }
    echo '</div><template id="mzc-parts-tpl">' . str_replace('[0]', '[__N__]', $row(0, array('name' => '', 'qty' => 1, 'unit_price' => '', 'serial' => '', 'warranty_months' => ''))) . '</template>';
    echo '<p><button type="button" class="mzc-btn" data-mzc-add="mzc-parts">+ قطعهٔ تازه</button></p></div>';
    $rrow = function ($n, $p) {
        return '<div class="mzc-row"><input type="text" name="recv[' . $n . '][name]" value="' . esc_attr($p['name']) . '" placeholder="نام قطعه" maxlength="190"><input type="text" name="recv[' . $n . '][serial]" value="' . esc_attr($p['serial']) . '" placeholder="سریال" dir="ltr" maxlength="120">'
            . '<input type="text" name="recv[' . $n . '][note]" value="' . esc_attr($p['note']) . '" placeholder="توضیح / مشکل ظاهری" maxlength="255"><label class="chk"><input type="checkbox" name="recv[' . $n . '][has_box]" value="1"' . checked((int) $p['has_box'], 1, false) . '> جعبه</label>'
            . '<label class="chk"><input type="checkbox" name="recv[' . $n . '][has_warranty]" value="1"' . checked((int) $p['has_warranty'], 1, false) . '> گارانتی</label><button type="button" class="mzc-btn sm danger x" data-mzc-remove>حذف</button></div>';
    };
    $blank = array('name' => '', 'serial' => '', 'note' => '', 'has_box' => 0, 'has_warranty' => 0);
    echo '<div class="mzc-field"><span>قطعات دریافتی از مشتری / بازشده از کیس</span><div class="mzc-rows" id="mzc-recv" data-next="' . ($j ? count($j['recv']) : 0) . '">';
    if ($j) { foreach ($j['recv'] as $n => $p) { echo $rrow($n, $p); } }
    echo '</div><template id="mzc-recv-tpl">' . str_replace('[0]', '[__N__]', $rrow(0, $blank)) . '</template><p><button type="button" class="mzc-btn" data-mzc-add="mzc-recv">+ قطعهٔ دریافتی</button></p></div>';
    $prow = function ($n, $p) {
        return '<div class="mzc-row"><input type="text" name="pays[' . $n . '][amount]" value="' . esc_attr($p['amount']) . '" placeholder="مبلغ (تومان)" dir="ltr" inputmode="numeric"><input type="text" name="pays[' . $n . '][account]" value="' . esc_attr($p['account']) . '" placeholder="واریز به حساب (پوز، کارت ...)" maxlength="120">'
            . '<input type="text" name="pays[' . $n . '][tx]" value="' . esc_attr($p['tx']) . '" placeholder="شمارهٔ تراکنش" dir="ltr" maxlength="80"><input type="text" name="pays[' . $n . '][holder]" value="' . esc_attr($p['holder']) . '" placeholder="نام صاحب حساب" maxlength="120"><button type="button" class="mzc-btn sm danger x" data-mzc-remove>حذف</button></div>';
    };
    echo '<div class="mzc-field"><span>پرداخت‌ها</span><div class="mzc-rows" id="mzc-pays" data-next="' . ($j ? count($j['pays']) : 0) . '">';
    if ($j) { foreach ($j['pays'] as $n => $p) { echo $prow($n, $p); } }
    echo '</div><template id="mzc-pays-tpl">' . str_replace('[0]', '[__N__]', $prow(0, array('amount' => '', 'account' => '', 'tx' => '', 'holder' => ''))) . '</template><p><button type="button" class="mzc-btn" data-mzc-add="mzc-pays">+ پرداخت</button></p></div>';
    echo MZC_Admin::photos_block($j ? $j['photos'] : array());
    echo '<p><button class="mzc-btn primary">ذخیره</button></p></form></div></div>';
    return;
}

if ($view) {
    $j = MZC_Crm::job($view);
    if (!$j) { echo '<div class="mzc-notice err">سرویس پیدا نشد.</div>'; return; }
    $c = MZC_Crm::customer($j['customer_id']);
    echo '<div class="mzc-title"><div><a href="' . esc_url(MZC_Admin::url('mzc-jobs')) . '">‹ سرویس‌ها</a><h2 dir="auto">' . esc_html($j['device'] ?: 'سرویس') . '</h2> ' . $pillOf($j['status']) . '</div><div class="mzc-actions">'
        . '<a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-jobs', array('edit' => $view))) . '">ویرایش</a>'
        . '<a class="mzc-btn danger" href="' . esc_url(MZC_Admin::post_url('job_delete', array('id' => $view))) . '" onclick="return confirm(\'این سرویس پاک شود؟\')">حذف</a></div></div>';
    echo '<div class="mzc-grid2"><div class="mzc-panel"><header><h3>مشخصات</h3></header><div class="body"><dl class="mzc-kv"><dt>مشتری</dt><dd>' . ($c ? '<a href="' . esc_url(MZC_Admin::url('mzc-customers', array('view' => (int) $c['id']))) . '">' . esc_html($c['name']) . '</a> <span class="num muted">' . esc_html($c['mobile']) . '</span>' : '—') . '</dd>'
        . '<dt>شمارهٔ سرویس</dt><dd class="num">' . esc_html($j['service_no'] ?: '—') . '</dd><dt>تاریخ پذیرش</dt><dd>' . esc_html($j['received_at'] ? MZC_Crm::jdate($j['received_at']) : '—') . '</dd><dt>تاریخ تحویل</dt><dd>' . esc_html($j['closed_at'] ? MZC_Crm::jdate($j['closed_at']) : '—') . '</dd><dt>موعد تحویل</dt><dd>' . esc_html($j['due_at'] ? MZC_Crm::jdate($j['due_at']) : '—') . '</dd><dt>فاکتور</dt><dd class="num">' . esc_html($j['invoice_no'] ?: '—') . '</dd>'
        . '<dt>درخواست مشتری</dt><dd dir="auto">' . nl2br(esc_html((string) $j['complaint'])) . '</dd><dt>کارهای انجام‌شده</dt><dd dir="auto">' . nl2br(esc_html((string) $j['work_done'])) . '</dd>'
        . '<dt>گزارش تست</dt><dd>' . ($j['report_id'] ? '<a target="_blank" href="' . esc_url(admin_url('admin-post.php?action=mzc_report&id=' . rawurlencode($j['report_id']))) . '">خلاصهٔ گزارش</a> · <a target="_blank" href="' . esc_url(admin_url('admin-post.php?action=mzc_report&view=full&id=' . rawurlencode($j['report_id']))) . '">گزارش کامل</a>' : '—') . '</dd>'
        . '<dt>یادداشت داخلی</dt><dd dir="auto">' . nl2br(esc_html((string) $j['notes'])) . '</dd></dl></div></div>';
    echo '<div class="mzc-panel"><header><h3>عکس‌ها</h3></header><div class="body">';
    if (!$j['photos']) { echo '<p class="muted">عکسی ثبت نشده است.</p>'; }
    echo '<div class="mzc-photos">'; foreach ($j['photos'] as $p) { $src = admin_url('admin-post.php?action=mzc_photo&id=' . (int) $p['id']); echo '<figure><a target="_blank" href="' . esc_url($src) . '"><img loading="lazy" src="' . esc_url($src) . '" alt=""></a></figure>'; }
    echo '</div></div></div></div>';
    echo '<div class="mzc-panel"><header><h3>قطعات و هزینه</h3></header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>قطعه</th><th>سریال</th><th class="n">تعداد</th><th class="n">قیمت واحد</th><th class="n">جمع</th></tr></thead><tbody>';
    if (!$j['parts']) { echo '<tr><td class="empty" colspan="5">قطعه‌ای اضافه نشده است.</td></tr>'; }
    foreach ($j['parts'] as $p) { echo '<tr><td dir="auto">' . esc_html($p['name']) . ($p['warranty_months'] ? ' ' . MZC_Admin::pill('گارانتی ' . (int) $p['warranty_months'] . ' ماه') : ($p['has_warranty'] ? ' ' . MZC_Admin::pill('گارانتی') : '')) . ($p['has_box'] ? ' ' . MZC_Admin::pill('جعبه') : '') . '</td><td class="num">' . esc_html($p['serial'] ?: '—') . '</td><td class="n">' . MZC_Admin::num($p['qty']) . '</td><td class="n">' . esc_html(number_format((int) $p['unit_price'])) . '</td><td class="n">' . esc_html(number_format((int) $p['qty'] * (int) $p['unit_price'])) . '</td></tr>'; }
    echo '<tr><td colspan="4"><strong>اجرت</strong></td><td class="n">' . esc_html(number_format((int) $j['labor_price'])) . '</td></tr>'
        . ((int) $j['discount'] ? '<tr><td colspan="4"><strong>تخفیف</strong></td><td class="n">- ' . esc_html(number_format((int) $j['discount'])) . '</td></tr>' : '')
        . '<tr><td colspan="4"><strong>جمع کل</strong></td><td class="n"><strong>' . esc_html(MZC_Crm::money($j['total'])) . '</strong></td></tr></tbody></table></div></div>';
    $paidSum = 0; foreach ($j['pays'] as $p) { $paidSum += (int) $p['amount']; }
    if ($j['pays']) {
        echo '<div class="mzc-panel"><header><h3>پرداخت‌ها</h3>' . MZC_Admin::pill('جمع: ' . MZC_Crm::money($paidSum), $paidSum >= (int) $j['total'] ? 'ok' : 'warn') . '</header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>حساب</th><th>تراکنش</th><th>صاحب حساب</th><th class="n">مبلغ</th></tr></thead><tbody>';
        foreach ($j['pays'] as $p) { echo '<tr><td dir="auto">' . esc_html($p['account'] ?: '—') . '</td><td class="num">' . esc_html($p['tx'] ?: '—') . '</td><td dir="auto">' . esc_html($p['holder'] ?: '—') . '</td><td class="n">' . esc_html(number_format((int) $p['amount'])) . '</td></tr>'; }
        echo '</tbody></table></div></div>';
    }
    if ($j['recv']) {
        echo '<div class="mzc-panel"><header><h3>قطعات دریافتی از مشتری</h3></header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>قطعه</th><th>سریال</th><th>توضیح</th><th></th></tr></thead><tbody>';
        foreach ($j['recv'] as $p) { echo '<tr><td dir="auto">' . esc_html($p['name']) . '</td><td class="num">' . esc_html($p['serial'] ?: '—') . '</td><td dir="auto">' . esc_html($p['note'] ?: '—') . '</td><td>' . ($p['has_box'] ? MZC_Admin::pill('جعبه') : '') . ($p['has_warranty'] ? MZC_Admin::pill('گارانتی') : '') . '</td></tr>'; }
        echo '</tbody></table></div></div>';
    }
    if ($j['shipping']) {
        echo '<div class="mzc-panel"><header><h3>ارسال</h3></header><div class="body"><dl class="mzc-kv">';
        foreach ($j['shipping'] as $k => $v) { echo '<dt>' . esc_html($k) . '</dt><dd dir="auto">' . esc_html((string) $v) . '</dd>'; }
        echo '</dl></div></div>';
    }
    echo MZC_Admin::more($j['more']);
    return;
}

$q = isset($_GET['s']) ? sanitize_text_field(wp_unslash($_GET['s'])) : '';
$st = isset($_GET['st']) ? sanitize_key($_GET['st']) : '';
$paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 30;
list($rows, $total) = MZC_Crm::jobs($q, $st, $paged, $per);
$linkOf = function ($j) { return MZC_Admin::url('mzc-jobs', array('view' => (int) $j['id'])); };
$defs = array(
    'service_no' => array('شمارهٔ سرویس', 1, function ($j) use ($linkOf) { return '<a class="num" href="' . esc_url($linkOf($j)) . '"><strong>' . esc_html($j['service_no'] ?: '—') . '</strong></a>'; }),
    'customer' => array('نام مشتری', 1, function ($j) { return '<span dir="auto">' . esc_html($j['customer_name']) . '</span>'; }),
    'status' => array('وضعیت', 1, function ($j) use ($pillOf) { return $pillOf($j['status']); }),
    'received' => array('تاریخ دریافت', 1, function ($j) { return esc_html($j['received_at'] ? MZC_Crm::jdate($j['received_at']) : '—'); }),
    'total' => array('هزینه', 1, function ($j) { return esc_html(number_format((int) $j['total'])); }, 'n'),
    'report' => array('گزارش', 1, function ($j) { return MZC_Admin::report_link($j['report_id']); }),
    'mobile' => array('موبایل', 0, function ($j) { return '<span class="num">' . esc_html($j['customer_mobile']) . '</span>'; }),
    'device' => array('دستگاه', 0, function ($j) { return '<span dir="auto">' . esc_html($j['device'] ?: '—') . '</span>'; }),
    'complaint' => array('مشکل', 0, function ($j) { return '<span dir="auto">' . esc_html(mb_substr((string) $j['complaint'], 0, 90)) . '</span>'; }),
    'invoice' => array('شمارهٔ فاکتور', 0, function ($j) { return '<span class="num">' . esc_html($j['invoice_no'] ?: '—') . '</span>'; }),
    'due' => array('موعد تحویل', 0, function ($j) { return esc_html($j['due_at'] ? MZC_Crm::jdate($j['due_at']) : '—'); }),
    'closed' => array('تاریخ تحویل', 0, function ($j) { return esc_html($j['closed_at'] ? MZC_Crm::jdate($j['closed_at']) : '—'); }),
);
echo '<div class="mzc-title"><h2>سرویس‌ها</h2><a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-jobs', array('add' => 1))) . '">+ پذیرش سرویس</a></div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-jobs"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="شمارهٔ سرویس، دستگاه، مشکل، نام یا موبایل مشتری" style="min-width:320px"><select name="st"><option value="">همهٔ وضعیت‌ها</option>';
foreach (MZC_Crm::$statuses as $k => $l) { echo '<option value="' . esc_attr($k) . '"' . selected($st, $k, false) . '>' . esc_html($l) . '</option>'; }
echo '</select><button class="mzc-btn primary">اعمال</button><span class="muted">' . esc_html(number_format_i18n($total)) . ' سرویس</span><span class="mzc-bar-actions">' . MZC_Admin::cols_ui('mzc-jobs', $defs) . '</span></form>';
echo MZC_Admin::table('mzc-jobs', $defs, $rows, $linkOf, 'سرویسی پیدا نشد.') . MZC_Admin::pager($total, $per, $paged);
