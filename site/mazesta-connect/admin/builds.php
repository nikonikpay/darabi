<?php
if (!defined('ABSPATH')) { exit; }
/** New computers the shop built and sold: parts with serial numbers and warranty, the accounting invoice's number, the app's test report, photos. */
if (!MZC_Admin::need_crm()) { return; }

$view = isset($_GET['view']) ? (int) $_GET['view'] : 0;
$edit = isset($_GET['edit']) ? (int) $_GET['edit'] : 0;
$add = !empty($_GET['add']);

if ($add || $edit) {
    $b = $edit ? MZC_Crm::build($edit) : null;
    if ($edit && !$b) { echo '<div class="mzc-notice err">سیستم پیدا نشد.</div>'; return; }
    $cid = $b ? (int) $b['customer_id'] : (isset($_GET['customer']) ? (int) $_GET['customer'] : 0);
    $customer = $cid ? MZC_Crm::customer($cid) : null;
    $v = function ($k) use ($b) { return $b && isset($b[$k]) ? esc_attr((string) $b[$k]) : ''; };
    echo '<div class="mzc-title"><div><a href="' . esc_url(MZC_Admin::url('mzc-builds')) . '">‹ سیستم‌های نو</a><h2>' . ($b ? 'ویرایش سیستم' : 'ثبت سیستم نو') . '</h2></div></div>';
    echo '<div class="mzc-panel"><div class="body">' . MZC_Admin::form_open('build_save', 'class="mzc-form" enctype="multipart/form-data"') . ($b ? '<input type="hidden" name="id" value="' . (int) $b['id'] . '">' : '') . '<div class="mzc-fields">'
        . MZC_Admin::customer_picker($customer)
        . '<label>عنوان سیستم<input type="text" name="title" value="' . $v('title') . '" maxlength="190" placeholder="مثلاً سیستم رندر ۱۲۸ گیگ"></label>'
        . '<label>شمارهٔ سرویس<input type="text" name="service_no" value="' . $v('service_no') . '" dir="ltr" maxlength="40"></label>'
        . '<label>شمارهٔ فاکتور (سیستم حسابداری)<input type="text" name="invoice_no" value="' . $v('invoice_no') . '" dir="ltr" maxlength="60"></label>'
        . '<label>تاریخ فروش<input type="date" name="sold_at" value="' . $v('sold_at') . '"></label>'
        . MZC_Admin::report_select($b ? $b['report_id'] : '', $b ? (string) $b['service_no'] : '')
        . '<label>وضعیت<select name="status">' . implode('', array_map(function ($k, $l) use ($b) { return '<option value="' . esc_attr($k) . '"' . selected($b ? $b['status'] : 'queue', $k, false) . '>' . esc_html($l) . '</option>'; }, array_keys(MZC_Crm::$build_statuses), MZC_Crm::$build_statuses)) . '</select></label><label class="wide">یادداشت داخلی<textarea name="notes" rows="2">' . esc_textarea($b ? (string) $b['notes'] : '') . '</textarea></label></div>';
    echo '<div class="mzc-field"><span>قطعات (مدل، سریال و گارانتی هر قطعه)</span><div class="mzc-rows" id="mzc-parts" data-next="' . ($b ? count($b['parts']) : 0) . '">';
    $row = function ($n, $p) {
        $o = '<div class="mzc-row"><select name="parts[' . $n . '][category]">';
        foreach (MZC_Crm::$categories as $k => $l) { $o .= '<option value="' . esc_attr($k) . '"' . selected($p['category'], $k, false) . '>' . esc_html($l) . '</option>'; }
        return $o . '</select><input type="text" name="parts[' . $n . '][model]" value="' . esc_attr($p['model']) . '" placeholder="مدل قطعه" maxlength="190"><input type="text" name="parts[' . $n . '][serial]" value="' . esc_attr($p['serial']) . '" placeholder="سریال" dir="ltr" maxlength="120">'
            . '<input type="number" name="parts[' . $n . '][warranty_months]" value="' . esc_attr($p['warranty_months']) . '" placeholder="گارانتی (ماه)" min="0" max="120"><input type="text" name="parts[' . $n . '][vendor]" value="' . esc_attr($p['vendor']) . '" placeholder="شرکت گارانتی" maxlength="120">'
            . '<label class="chk"><input type="checkbox" name="parts[' . $n . '][has_warranty]" value="1"' . checked((int) $p['has_warranty'], 1, false) . '> گارانتی</label><label class="chk"><input type="checkbox" name="parts[' . $n . '][has_box]" value="1"' . checked((int) $p['has_box'], 1, false) . '> جعبه</label>'
            . '<label class="chk"><input type="checkbox" name="parts[' . $n . '][qc]" value="1"' . checked((int) $p['qc'], 1, false) . '> QC</label>'
            . '<button type="button" class="mzc-btn sm danger x" data-mzc-remove>حذف</button></div>';
    };
    if ($b) { foreach ($b['parts'] as $n => $p) { echo $row($n, $p); } }
    echo '</div><template id="mzc-parts-tpl">' . str_replace(array('[0]', '[1]'), '[__N__]', $row(0, array('category' => 'cpu', 'model' => '', 'serial' => '', 'warranty_months' => '', 'vendor' => '', 'has_warranty' => 0, 'has_box' => 0, 'qc' => 0))) . '</template>';
    echo '<p><button type="button" class="mzc-btn" data-mzc-add="mzc-parts">+ قطعهٔ تازه</button></p></div>';
    echo MZC_Admin::photos_block($b ? $b['photos'] : array());
    echo '<p><button class="mzc-btn primary">ذخیره</button></p></form></div></div>';
    return;
}

if ($view) {
    $b = MZC_Crm::build($view);
    if (!$b) { echo '<div class="mzc-notice err">سیستم پیدا نشد.</div>'; return; }
    $c = MZC_Crm::customer($b['customer_id']);
    echo '<div class="mzc-title"><div><a href="' . esc_url(MZC_Admin::url('mzc-builds')) . '">‹ سیستم‌های نو</a><h2 dir="auto">' . esc_html($b['title']) . '</h2></div><div class="mzc-actions">'
        . '<a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-builds', array('edit' => $view))) . '">ویرایش</a>'
        . '<a class="mzc-btn danger" href="' . esc_url(MZC_Admin::post_url('build_delete', array('id' => $view))) . '" onclick="return confirm(\'این سیستم پاک شود؟\')">حذف</a></div></div>';
    echo '<div class="mzc-grid2"><div class="mzc-panel"><header><h3>مشخصات</h3></header><div class="body"><dl class="mzc-kv"><dt>مشتری</dt><dd>' . ($c ? '<a href="' . esc_url(MZC_Admin::url('mzc-customers', array('view' => (int) $c['id']))) . '">' . esc_html($c['name']) . '</a> <span class="num muted">' . esc_html($c['mobile']) . '</span>' : '—') . '</dd>'
        . '<dt>تاریخ فروش</dt><dd>' . esc_html($b['sold_at'] ? MZC_Crm::jdate($b['sold_at']) : '—') . '</dd><dt>شمارهٔ فاکتور</dt><dd class="num">' . esc_html($b['invoice_no'] ?: '—') . '</dd><dt>شمارهٔ سرویس</dt><dd class="num">' . esc_html($b['service_no'] ?: '—') . '</dd>'
        . '<dt>گزارش تست</dt><dd>' . ($b['report_id'] ? '<a target="_blank" href="' . esc_url(admin_url('admin-post.php?action=mzc_report&id=' . rawurlencode($b['report_id']))) . '">خلاصهٔ گزارش</a> · <a target="_blank" href="' . esc_url(admin_url('admin-post.php?action=mzc_report&view=full&id=' . rawurlencode($b['report_id']))) . '">گزارش کامل</a>' : '—') . '</dd>'
        . '<dt>وضعیت</dt><dd>' . MZC_Admin::pill(isset(MZC_Crm::$build_statuses[$b['status']]) ? MZC_Crm::$build_statuses[$b['status']] : $b['status'], $b['status'] === 'delivered' ? 'ok' : ($b['status'] === 'tested' ? 'info' : 'warn')) . '</dd><dt>یادداشت داخلی</dt><dd dir="auto">' . nl2br(esc_html((string) $b['notes'])) . '</dd></dl></div></div>';
    echo '<div class="mzc-panel"><header><h3>عکس‌ها</h3></header><div class="body">';
    if (!$b['photos']) { echo '<p class="muted">عکسی ثبت نشده است.</p>'; }
    echo '<div class="mzc-photos">'; foreach ($b['photos'] as $p) { $src = admin_url('admin-post.php?action=mzc_photo&id=' . (int) $p['id']); echo '<figure><a target="_blank" href="' . esc_url($src) . '"><img loading="lazy" src="' . esc_url($src) . '" alt=""></a></figure>'; }
    echo '</div></div></div></div>';
    echo '<div class="mzc-panel"><header><h3>قطعات و گارانتی</h3></header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>دسته</th><th>مدل</th><th>سریال</th><th>شرکت گارانتی</th><th>گارانتی تا</th></tr></thead><tbody>';
    if (!$b['parts']) { echo '<tr><td class="empty" colspan="5">قطعه‌ای ثبت نشده است.</td></tr>'; }
    foreach ($b['parts'] as $p) {
        list($until, $left) = MZC_Crm::warranty($b['sold_at'], $p['warranty_months']);
        $w = $until === null ? '—' : esc_html(MZC_Crm::jdate($until)) . ' ' . ($left < 0 ? MZC_Admin::pill('تمام شده', 'bad') : MZC_Admin::pill($left . ' روز مانده', $left < 60 ? 'warn' : 'ok'));
        echo '<tr><td>' . esc_html(MZC_Crm::$categories[$p['category']]) . '</td><td dir="auto">' . esc_html($p['model']) . '</td><td class="num">' . esc_html($p['serial'] ?: '—') . '</td><td dir="auto">' . esc_html($p['vendor'] ?: '—') . ($p['has_box'] ? ' ' . MZC_Admin::pill('جعبه') : '') . ($p['qc'] ? ' ' . MZC_Admin::pill('QC', 'ok') : '') . '</td><td>' . ($until === null && $p['has_warranty'] ? MZC_Admin::pill('گارانتی دارد', 'ok') : $w) . '</td></tr>';
    }
    echo '</tbody></table></div></div>' . MZC_Admin::more($b['more']);
    return;
}

$q = isset($_GET['s']) ? sanitize_text_field(wp_unslash($_GET['s'])) : '';
$st = isset($_GET['st']) ? sanitize_key($_GET['st']) : '';
$paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 30;
list($rows, $total) = MZC_Crm::builds($q, $paged, $per, 0, $st);
$linkOf = function ($b) { return MZC_Admin::url('mzc-builds', array('view' => (int) $b['id'])); };
$pillOf = function ($s) { return MZC_Admin::pill(isset(MZC_Crm::$build_statuses[$s]) ? MZC_Crm::$build_statuses[$s] : $s, $s === 'delivered' ? 'ok' : ($s === 'tested' ? 'info' : 'warn')); };
$defs = array(
    'invoice' => array('شمارهٔ فاکتور', 1, function ($b) use ($linkOf) { return '<a class="num" href="' . esc_url($linkOf($b)) . '"><strong>' . esc_html($b['invoice_no'] ?: '—') . '</strong></a>'; }),
    'customer' => array('نام مشتری', 1, function ($b) { return '<span dir="auto">' . esc_html($b['customer_name']) . '</span>'; }),
    'status' => array('وضعیت', 1, function ($b) use ($pillOf) { return $pillOf($b['status']); }),
    'created' => array('تاریخ ثبت', 1, function ($b) { return esc_html(MZC_Crm::jdate(substr((string) $b['created'], 0, 10))); }),
    'report' => array('گزارش', 1, function ($b) { return MZC_Admin::report_link($b['report_id']); }),
    'title' => array('سیستم', 0, function ($b) { return '<span dir="auto">' . esc_html($b['title']) . '</span>'; }),
    'mobile' => array('موبایل', 0, function ($b) { return '<span class="num">' . esc_html($b['customer_mobile']) . '</span>'; }),
    'sold' => array('تاریخ فروش', 0, function ($b) { return esc_html($b['sold_at'] ? MZC_Crm::jdate($b['sold_at']) : '—'); }),
    'service_no' => array('شمارهٔ سرویس', 0, function ($b) { return '<span class="num">' . esc_html($b['service_no'] ?: '—') . '</span>'; }),
    'parts' => array('قطعه', 0, function ($b) { return MZC_Admin::num($b['parts']); }, 'n'),
);
echo '<div class="mzc-title"><h2>سیستم‌های نو</h2><a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-builds', array('add' => 1))) . '">+ ثبت سیستم نو</a></div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-builds"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="شمارهٔ سرویس، فاکتور، سریال قطعه، نام یا موبایل مشتری" style="min-width:340px"><select name="st"><option value="">همهٔ وضعیت‌ها</option>';
foreach (MZC_Crm::$build_statuses as $k => $l) { echo '<option value="' . esc_attr($k) . '"' . selected($st, $k, false) . '>' . esc_html($l) . '</option>'; }
echo '</select><button class="mzc-btn primary">اعمال</button><span class="muted">' . esc_html(number_format_i18n($total)) . ' سیستم</span><span class="mzc-bar-actions">' . MZC_Admin::cols_ui('mzc-builds', $defs) . '</span></form>';
echo MZC_Admin::table('mzc-builds', $defs, $rows, $linkOf, 'سیستمی پیدا نشد.') . MZC_Admin::pager($total, $per, $paged);
