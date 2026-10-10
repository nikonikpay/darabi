<?php
if (!defined('ABSPATH')) { exit; }
/** Parts and systems sent to a warranty company or a repair shop: what went, where, when, what they said, when it came back. */
if (!MZC_Admin::need_crm()) { return; }

$edit = isset($_GET['edit']) ? (int) $_GET['edit'] : 0;
$add = !empty($_GET['add']);

if ($add || $edit) {
    $s = $edit ? MZC_Ships::ship($edit) : null;
    if ($edit && !$s) { echo '<div class="mzc-notice err">این مورد پیدا نشد.</div>'; return; }
    $cid = $s ? (int) $s['customer_id'] : (isset($_GET['customer']) ? (int) $_GET['customer'] : 0);
    $customer = $cid ? MZC_Crm::customer($cid) : null;
    $v = function ($k) use ($s) { return $s && isset($s[$k]) ? esc_attr((string) $s[$k]) : ''; };
    $ta = function ($k) use ($s) { return esc_textarea($s && isset($s[$k]) ? (string) $s[$k] : ''); };
    $owner = $s ? $s['owner'] : ($cid ? 'customer' : 'mazesta');
    echo '<div class="mzc-title"><div><a href="' . esc_url(MZC_Admin::url('mzc-ships')) . '">‹ ارسال به گارانتی</a><h2>' . ($s ? 'ویرایش' : 'ارسال تازه') . '</h2></div></div>';
    echo '<div class="mzc-panel"><div class="body">' . MZC_Admin::form_open('ship_save', 'class="mzc-form"') . ($s ? '<input type="hidden" name="id" value="' . (int) $s['id'] . '">' : '')
        . ($cid ? '<input type="hidden" name="customer_id" value="' . (int) $cid . '">' : '') . '<div class="mzc-fields">'
        . '<label>نام و مدل قطعه<input type="text" name="part_name" value="' . $v('part_name') . '" maxlength="190" required></label>'
        . '<label>سریال قطعه<input type="text" name="serial" value="' . $v('serial') . '" dir="ltr" maxlength="120"></label>'
        . '<label class="wide">مشکل قطعه<textarea name="problem" rows="2">' . $ta('problem') . '</textarea></label>'
        . '<label>گارانتی / تعمیرگاه<select name="vendor_id"><option value="">— انتخاب کنید —</option>';
    foreach (MZC_Ships::vendors() as $vd) { echo '<option value="' . (int) $vd['id'] . '"' . selected($s ? (int) $s['vendor_id'] : 0, (int) $vd['id'], false) . '>' . esc_html($vd['name'] . ' (' . MZC_Ships::$kinds[$vd['kind']] . ')') . '</option>'; }
    echo '</select></label><label>یا نام تازه<input type="text" name="vendor_new" maxlength="120" placeholder="اگر در فهرست نیست، اینجا بنویسید"></label>'
        . '<label>مال چه کسی است<select name="owner"><option value="mazesta"' . selected($owner, 'mazesta', false) . '>مازستا</option><option value="customer"' . selected($owner, 'customer', false) . '>مشتری</option></select></label>'
        . '<label>شمارهٔ سرویس یا فاکتور (برای قطعهٔ مشتری)<input type="text" name="ref" value="' . $v('ref') . '" dir="ltr" maxlength="60">'
        . ($customer ? '<small>مشتری: ' . esc_html($customer['name']) . '</small>' : '<small>مشتری از روی همین شماره پیدا می‌شود.</small>') . '</label>'
        . '<label>تاریخ ارسال<input type="date" name="sent_at" value="' . ($s ? $v('sent_at') : esc_attr(gmdate('Y-m-d'))) . '"></label>'
        . '<label>شمارهٔ پیگیری<input type="text" name="tracking" value="' . $v('tracking') . '" dir="ltr" maxlength="80"></label>'
        . '<label class="wide">نتیجهٔ تماس با گارانتی<textarea name="vendor_reply" rows="2">' . $ta('vendor_reply') . '</textarea></label>'
        . '<label>تاریخ دریافت از گارانتی<input type="date" name="received_at" value="' . $v('received_at') . '"><small>تا پر نشود، قطعه «در گارانتی» می‌ماند.</small></label>'
        . '<label class="wide">توضیح رفع عیب<textarea name="fix_notes" rows="2">' . $ta('fix_notes') . '</textarea></label>'
        . '<label>قطعهٔ دریافتی (اگر عوض شد)<input type="text" name="back_part" value="' . $v('back_part') . '" maxlength="190"></label>'
        . '<label>سریال قطعهٔ دریافتی<input type="text" name="back_serial" value="' . $v('back_serial') . '" dir="ltr" maxlength="120"></label>'
        . '<label>جای قطعه در دفتر<input type="text" name="shelf" value="' . $v('shelf') . '" maxlength="60"></label>'
        . '<label class="chk"><input type="checkbox" name="picked_up" value="1"' . checked($s ? (int) $s['picked_up'] : 0, 1, false) . '> قطعه تحویل گرفته شد</label>'
        . '<label class="wide">نتیجهٔ تماس با مشتری<textarea name="customer_reply" rows="2">' . $ta('customer_reply') . '</textarea></label></div>'
        . '<p><button class="mzc-btn primary">ذخیره</button>' . ($s ? ' <a class="mzc-btn danger" href="' . esc_url(MZC_Admin::post_url('ship_delete', array('id' => (int) $s['id']))) . '" onclick="return confirm(\'پاک شود؟\')">حذف</a>' : '') . '</p></form></div></div>';
    return;
}

$q = isset($_GET['s']) ? sanitize_text_field(wp_unslash($_GET['s'])) : '';
$st = isset($_GET['st']) ? sanitize_key($_GET['st']) : '';
$vendor = isset($_GET['vendor']) ? (int) $_GET['vendor'] : 0;
$paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 30;
list($rows, $total) = MZC_Ships::ships($q, $st, $vendor, $paged, $per);
$linkOf = function ($r) { return MZC_Admin::url('mzc-ships', array('edit' => (int) $r['id'])); };
$defs = array(
    'part' => array('قطعه', 1, function ($r) use ($linkOf) { return '<a dir="auto" href="' . esc_url($linkOf($r)) . '"><strong>' . esc_html($r['part_name']) . '</strong></a>'; }),
    'serial' => array('سریال', 1, function ($r) { return '<span class="num">' . esc_html($r['serial'] ?: '—') . '</span>'; }),
    'vendor' => array('گارانتی / تعمیرگاه', 1, function ($r) { return '<span dir="auto">' . esc_html($r['vendor_name'] ?: '—') . '</span>'; }),
    'owner' => array('مال', 1, function ($r) { return $r['owner'] === 'customer' ? '<span dir="auto">' . esc_html($r['customer_name'] ?: 'مشتری') . '</span>' . ($r['ref'] ? ' <span class="num muted">' . esc_html($r['ref']) . '</span>' : '') : 'مازستا'; }),
    'sent' => array('ارسال', 1, function ($r) { return esc_html($r['sent_at'] ? MZC_Crm::jdate($r['sent_at']) : '—'); }),
    'status' => array('وضعیت', 1, function ($r) { return $r['received_at'] ? MZC_Admin::pill('برگشت', 'ok') : MZC_Admin::pill('در گارانتی', 'warn'); }),
    'received' => array('دریافت', 1, function ($r) { return esc_html($r['received_at'] ? MZC_Crm::jdate($r['received_at']) : '—'); }),
    'problem' => array('مشکل', 0, function ($r) { return '<span dir="auto">' . esc_html(mb_substr((string) $r['problem'], 0, 90)) . '</span>'; }),
    'tracking' => array('پیگیری', 0, function ($r) { return '<span class="num">' . esc_html($r['tracking'] ?: '—') . '</span>'; }),
    'back' => array('قطعهٔ دریافتی', 0, function ($r) { return '<span dir="auto">' . esc_html($r['back_part'] ?: '—') . '</span>'; }),
    'shelf' => array('جا', 0, function ($r) { return esc_html($r['shelf'] ?: '—'); }),
);
echo '<div class="mzc-title"><h2>ارسال به گارانتی و تعمیرگاه</h2><a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-ships', array('add' => 1))) . '">+ ارسال تازه</a></div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-ships"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="قطعه، سریال، مشکل، پیگیری یا مشتری" style="min-width:280px"><select name="st"><option value="">همه</option>'
    . '<option value="sent"' . selected($st, 'sent', false) . '>هنوز بیرون است</option><option value="back"' . selected($st, 'back', false) . '>برگشته</option></select><select name="vendor"><option value="">همهٔ گارانتی‌ها و تعمیرگاه‌ها</option>';
$vendors = MZC_Ships::vendors();
foreach ($vendors as $vd) { echo '<option value="' . (int) $vd['id'] . '"' . selected($vendor, (int) $vd['id'], false) . '>' . esc_html($vd['name']) . '</option>'; }
echo '</select><button class="mzc-btn primary">اعمال</button><span class="muted">' . esc_html(number_format_i18n($total)) . ' مورد</span><span class="mzc-bar-actions">' . MZC_Admin::cols_ui('mzc-ships', $defs) . '</span></form>';
echo MZC_Admin::table('mzc-ships', $defs, $rows, $linkOf, 'موردی پیدا نشد.') . MZC_Admin::pager($total, $per, $paged);

echo '<div class="mzc-panel"><header><h3>گارانتی‌ها و تعمیرگاه‌ها (' . count($vendors) . ')</h3></header><div class="body">'
    . MZC_Admin::form_open('vendor_save', 'class="mzc-filter"') . '<input type="text" name="name" placeholder="نام" maxlength="120" required><select name="kind">';
foreach (MZC_Ships::$kinds as $k => $l) { echo '<option value="' . esc_attr($k) . '">' . esc_html($l) . '</option>'; }
echo '</select><input type="text" name="phone" dir="ltr" placeholder="تلفن (اختیاری)" maxlength="60"><button class="mzc-btn">افزودن</button></form><div class="mzc-timeline">';
foreach ($vendors as $vd) {
    echo '<div class="mzc-item"><div class="top"><strong dir="auto">' . esc_html($vd['name']) . '</strong>' . MZC_Admin::pill(MZC_Ships::$kinds[$vd['kind']]) . MZC_Admin::pill($vd['sent'] . ' ارسال', 'info')
        . ($vd['sent'] ? '' : '<a class="mzc-btn sm danger" href="' . esc_url(MZC_Admin::post_url('vendor_delete', array('id' => (int) $vd['id']))) . '" onclick="return confirm(\'پاک شود؟\')">حذف</a>') . '</div></div>';
}
echo '</div></div></div>';
