<?php
if (!defined('ABSPATH')) { exit; }
/** Purchase invoices: what the shop bought, from which supplier, on which invoice, at what cost, with the serial numbers. Saving one brings its products into the stock. */
if (!MZC_Admin::need_crm()) { return; }

$view = isset($_GET['view']) ? (int) $_GET['view'] : 0;
$edit = isset($_GET['edit']) ? (int) $_GET['edit'] : 0;
$add = !empty($_GET['add']);
$back = '<a href="' . esc_url(MZC_Admin::url('mzc-purchases')) . '">‹ فاکتورهای خرید</a>';

if ($add || $edit) {
    $u = $edit ? MZC_Stock::purchase($edit) : null;
    if ($edit && !$u) { echo '<div class="mzc-notice err">فاکتور خرید پیدا نشد.</div>'; return; }
    $items = $u ? $u['items'] : array();
    if (!$u && !empty($_GET['product']) && ($pp = MZC_Stock::product((int) $_GET['product']))) { $items[] = array('product_id' => $pp['id'], 'product_name' => $pp['name'], 'category' => $pp['category'], 'qty' => 1, 'unit_cost' => $pp['buy_price'], 'serials' => ''); }
    echo '<div class="mzc-title"><div>' . $back . '<h2>' . ($u ? 'ویرایش فاکتور خرید' : 'فاکتور خرید تازه') . '</h2></div></div>';
    echo '<div class="mzc-panel"><div class="body">' . MZC_Admin::form_open('purchase_save', 'class="mzc-form"') . ($u ? '<input type="hidden" name="id" value="' . (int) $u['id'] . '">' : '') . '<div class="mzc-fields">'
        . '<label>فروشنده<select name="vendor_id"><option value="">— انتخاب کنید —</option>';
    foreach (MZC_Stock::suppliers() as $vd) { echo '<option value="' . (int) $vd['id'] . '"' . selected($u ? (int) $u['vendor_id'] : 0, (int) $vd['id'], false) . '>' . esc_html($vd['name'] . ($vd['kind'] !== 'supplier' ? ' (' . MZC_Ships::$kinds[$vd['kind']] . ')' : '')) . '</option>'; }
    echo '</select></label><label>یا فروشندهٔ تازه<input type="text" name="vendor_new" maxlength="120" placeholder="اگر در فهرست نیست، اینجا بنویسید"></label>'
        . '<label>شمارهٔ فاکتور خرید<input type="text" name="invoice_no" value="' . esc_attr($u ? (string) $u['invoice_no'] : '') . '" dir="ltr" maxlength="60"></label>'
        . '<label>تاریخ خرید<input type="date" name="bought_at" value="' . esc_attr($u ? (string) $u['bought_at'] : gmdate('Y-m-d')) . '"></label>'
        . '<label class="wide">یادداشت<textarea name="notes" rows="2">' . esc_textarea($u ? (string) $u['notes'] : '') . '</textarea></label>'
        . '<label class="chk"><input type="checkbox" name="force" value="1"> با وجود شمارهٔ تکراری ثبت شود</label></div>';
    echo '<div class="mzc-field"><span>کالاها (یکی را از فهرست انتخاب کنید یا نام کالای تازه را بنویسید؛ سریال‌ها هر کدام در یک خط)</span><div class="mzc-rows" id="mzc-items" data-next="' . count($items) . '">';
    $row = function ($n, $x) {
        $o = '<div class="mzc-row" data-mzc-part><input type="hidden" name="items[' . $n . '][product_id]" value="' . esc_attr($x['product_id']) . '" data-f="pid">'
            . '<input type="text" name="items[' . $n . '][name]" value="' . esc_attr($x['product_name']) . '" placeholder="کالا" maxlength="190" data-mzc-pq autocomplete="off" data-f="name"><select name="items[' . $n . '][category]" data-f="cat">';
        foreach (MZC_Crm::$categories as $k => $l) { $o .= '<option value="' . esc_attr($k) . '"' . selected($x['category'] ?: 'other', $k, false) . '>' . esc_html($l) . '</option>'; }
        return $o . '</select><input type="number" name="items[' . $n . '][qty]" value="' . esc_attr($x['qty']) . '" placeholder="تعداد" min="1"><input type="text" name="items[' . $n . '][unit_cost]" value="' . esc_attr($x['unit_cost']) . '" placeholder="قیمت خرید واحد" dir="ltr" inputmode="numeric" data-f="buy">'
            . '<input type="text" name="items[' . $n . '][sell_price]" value="" placeholder="قیمت فروش تازه (اختیاری)" dir="ltr" inputmode="numeric"><textarea name="items[' . $n . '][serials]" rows="1" dir="ltr" placeholder="سریال‌ها">' . esc_textarea((string) $x['serials']) . '</textarea>'
            . '<button type="button" class="mzc-btn sm danger x" data-mzc-remove>حذف</button></div>';
    };
    foreach ($items as $n => $x) { echo $row($n, $x); }
    echo '</div><template id="mzc-items-tpl">' . str_replace('[0]', '[__N__]', $row(0, array('product_id' => '', 'product_name' => '', 'category' => 'other', 'qty' => 1, 'unit_cost' => '', 'serials' => ''))) . '</template>'
        . '<p><button type="button" class="mzc-btn" data-mzc-add="mzc-items">+ کالا</button></p></div>' . MZC_Admin::product_list();
    echo '<p><button class="mzc-btn primary">ذخیره</button></p></form></div></div>';
    if (!$items) { echo '<script>document.addEventListener("DOMContentLoaded",function(){var b=document.querySelector("[data-mzc-add=mzc-items]");if(b)b.click();});</script>'; }
    return;
}

if ($view) {
    $u = MZC_Stock::purchase($view);
    if (!$u) { echo '<div class="mzc-notice err">فاکتور خرید پیدا نشد.</div>'; return; }
    echo '<div class="mzc-title"><div>' . $back . '<h2>فاکتور خرید <span class="num">' . esc_html($u['invoice_no'] ?: '#' . $u['id']) . '</span></h2></div><div class="mzc-actions"><a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-purchases', array('edit' => $view))) . '">ویرایش</a>'
        . '<a class="mzc-btn danger" href="' . esc_url(MZC_Admin::post_url('purchase_delete', array('id' => $view))) . '" onclick="return confirm(\'این فاکتور خرید پاک شود؟ کالاهایش از موجودی کم می‌شوند.\')">حذف</a></div></div>';
    echo '<div class="mzc-panel"><div class="body"><dl class="mzc-kv"><dt>فروشنده</dt><dd dir="auto"><a href="' . esc_url(MZC_Admin::url('mzc-purchases', array('vendor' => (int) $u['vendor_id']))) . '">' . esc_html($u['vendor_name'] ?: '—') . '</a></dd><dt>تاریخ خرید</dt><dd>' . esc_html($u['bought_at'] ? MZC_Crm::jdate($u['bought_at']) : '—') . '</dd>'
        . '<dt>جمع فاکتور</dt><dd>' . esc_html(MZC_Crm::money($u['total'])) . '</dd><dt>یادداشت</dt><dd dir="auto">' . nl2br(esc_html((string) $u['notes'])) . '</dd></dl></div></div>';
    echo '<div class="mzc-panel"><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>کالا</th><th class="n">تعداد</th><th class="n">قیمت واحد</th><th class="n">جمع</th><th>سریال‌ها</th></tr></thead><tbody>';
    foreach ($u['items'] as $i) {
        echo '<tr class="click" data-href="' . esc_url(MZC_Admin::url('mzc-stock', array('view' => (int) $i['product_id']))) . '"><td dir="auto">' . esc_html($i['product_name'] ?: '—') . '</td><td class="n">' . MZC_Admin::num($i['qty']) . '</td><td class="n">' . MZC_Admin::num($i['unit_cost']) . '</td>'
            . '<td class="n">' . MZC_Admin::num((int) $i['qty'] * (int) $i['unit_cost']) . '</td><td class="num">' . esc_html(implode('، ', $i['serial_list'])) . '</td></tr>';
    }
    echo '</tbody></table></div></div>';
    return;
}

$q = isset($_GET['s']) ? sanitize_text_field(wp_unslash($_GET['s'])) : '';
$vendor = isset($_GET['vendor']) ? (int) $_GET['vendor'] : 0;
$paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 30;
list($rows, $total) = MZC_Stock::purchases($q, $vendor, $paged, $per);
$linkOf = function ($u) { return MZC_Admin::url('mzc-purchases', array('view' => (int) $u['id'])); };
$defs = array(
    'date' => array('تاریخ خرید', 1, function ($u) use ($linkOf) { return '<a href="' . esc_url($linkOf($u)) . '">' . esc_html($u['bought_at'] ? MZC_Crm::jdate($u['bought_at']) : '—') . '</a>'; }),
    'vendor' => array('فروشنده', 1, function ($u) { return '<span dir="auto">' . esc_html($u['vendor_name'] ?: '—') . '</span>'; }),
    'invoice' => array('شمارهٔ فاکتور', 1, function ($u) { return '<span class="num">' . esc_html($u['invoice_no'] ?: '—') . '</span>'; }),
    'lines' => array('ردیف', 1, function ($u) { return MZC_Admin::num($u['line_count']); }, 'n'),
    'total' => array('جمع (تومان)', 1, function ($u) { return MZC_Admin::num($u['total']); }, 'n'),
    'notes' => array('یادداشت', 0, function ($u) { return '<span dir="auto">' . esc_html(mb_substr((string) $u['notes'], 0, 80)) . '</span>'; }),
);
echo '<div class="mzc-title"><h2>فاکتورهای خرید</h2><a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-purchases', array('add' => 1))) . '">+ فاکتور خرید</a></div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-purchases"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="شمارهٔ فاکتور، فروشنده، کالا یا سریال" class="mzc-search"><select name="vendor"><option value="">همهٔ فروشنده‌ها</option>';
foreach (MZC_Stock::suppliers() as $vd) { if ((int) $vd['purchases'] || $vd['kind'] === 'supplier') { echo '<option value="' . (int) $vd['id'] . '"' . selected($vendor, (int) $vd['id'], false) . '>' . esc_html($vd['name'] . ' (' . (int) $vd['purchases'] . ')') . '</option>'; } }
echo '</select><button class="mzc-btn primary">اعمال</button><span class="muted">' . esc_html(number_format_i18n($total)) . ' فاکتور</span><span class="mzc-bar-actions">' . MZC_Admin::cols_ui('mzc-purchases', $defs) . '</span></form>';
echo MZC_Admin::table('mzc-purchases', $defs, $rows, $linkOf, 'فاکتور خریدی ثبت نشده است.') . MZC_Admin::pager($total, $per, $paged);
