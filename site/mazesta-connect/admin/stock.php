<?php
if (!defined('ABSPATH')) { exit; }
/** The stock: products with how many are left, and for each one where it was bought (supplier, invoice, cost) and where it went (customer, system or service, price). */
if (!MZC_Admin::need_crm()) { return; }

$view = isset($_GET['view']) ? (int) $_GET['view'] : 0;
$edit = isset($_GET['edit']) ? (int) $_GET['edit'] : 0;
$add = !empty($_GET['add']);
$back = '<a href="' . esc_url(MZC_Admin::url('mzc-stock')) . '">‹ انبار و کالاها</a>';
$stockPill = function ($n) { $n = (int) $n; return MZC_Admin::pill($n . ' عدد', $n > 0 ? 'ok' : ($n < 0 ? 'bad' : 'warn')); };

if ($add || $edit) {
    $p = $edit ? MZC_Stock::product($edit) : null;
    if ($edit && !$p) { echo '<div class="mzc-notice err">کالا پیدا نشد.</div>'; return; }
    $v = function ($k) use ($p) { return $p && isset($p[$k]) ? esc_attr((string) $p[$k]) : ''; };
    echo '<div class="mzc-title"><div>' . $back . '<h2>' . ($p ? 'ویرایش کالا' : 'کالای تازه') . '</h2></div></div>';
    echo '<div class="mzc-panel"><div class="body">' . MZC_Admin::form_open('product_save', 'class="mzc-form"') . ($p ? '<input type="hidden" name="id" value="' . (int) $p['id'] . '">' : '') . '<div class="mzc-fields">'
        . '<label class="wide">نام کالا<input type="text" name="name" value="' . $v('name') . '" maxlength="190" required placeholder="مثلاً RTX 4070 SUPER ASUS DUAL 12GB"></label>'
        . '<label>دسته<select name="category">' . implode('', array_map(function ($k, $l) use ($p) { return '<option value="' . esc_attr($k) . '"' . selected($p ? $p['category'] : 'other', $k, false) . '>' . esc_html($l) . '</option>'; }, array_keys(MZC_Crm::$categories), MZC_Crm::$categories)) . '</select></label>'
        . '<label>برند<input type="text" name="brand" value="' . $v('brand') . '" maxlength="80"></label>'
        . '<label>پارت‌نامبر / بارکد<input type="text" name="sku" value="' . $v('sku') . '" dir="ltr" maxlength="120"></label>'
        . '<label>قیمت خرید (تومان)<input type="text" name="buy_price" value="' . $v('buy_price') . '" dir="ltr" inputmode="numeric"><small>با هر فاکتور خرید به آخرین قیمت خرید عوض می‌شود.</small></label>'
        . '<label>قیمت فروش (تومان)<input type="text" name="sell_price" value="' . $v('sell_price') . '" dir="ltr" inputmode="numeric"><small>در فرم‌ها خودکار پر می‌شود.</small></label>'
        . '<label>گارانتی (ماه)<input type="number" name="warranty_months" value="' . $v('warranty_months') . '" min="0" max="120"></label>'
        . '<label class="wide">یادداشت<input type="text" name="notes" value="' . $v('notes') . '" maxlength="255"></label></div>'
        . '<p class="muted">موجودی را دستی وارد نکنید: از فاکتورهای خرید و قطعه‌های به‌کاررفته در سیستم‌ها و سرویس‌ها حساب می‌شود.</p>'
        . '<p><button class="mzc-btn primary">ذخیره</button></p></form></div></div>';
    return;
}

if ($view) {
    $r = MZC_Stock::report($view);
    if (!$r) { echo '<div class="mzc-notice err">کالا پیدا نشد.</div>'; return; }
    $p = $r['product']; $tt = $r['totals'];
    echo '<div class="mzc-title"><div>' . $back . '<h2 dir="auto">' . esc_html($p['name']) . '</h2></div><div class="mzc-actions"><a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-stock', array('edit' => $view))) . '">ویرایش</a>'
        . '<a class="mzc-btn" href="' . esc_url(MZC_Admin::url('mzc-purchases', array('add' => 1, 'product' => $view))) . '">+ خرید این کالا</a>'
        . '<a class="mzc-btn danger" href="' . esc_url(MZC_Admin::post_url('product_delete', array('id' => $view))) . '" onclick="return confirm(\'این کالا پاک شود؟\')">حذف</a></div></div>';
    echo '<div class="mzc-cards"><div class="mzc-card"><span>موجودی</span><b>' . $stockPill($tt['stock']) . '</b></div><div class="mzc-card"><span>خریده‌شده</span><b class="num">' . (int) $tt['bought'] . '</b></div><div class="mzc-card"><span>به‌کاررفته / فروخته</span><b class="num">' . (int) $tt['used'] . '</b></div>'
        . '<div class="mzc-card"><span>قیمت خرید / فروش</span><b class="num">' . esc_html($p['buy_price'] === null ? '—' : number_format((int) $p['buy_price'])) . ' / ' . esc_html($p['sell_price'] === null ? '—' : number_format((int) $p['sell_price'])) . '</b></div>'
        . '<div class="mzc-card"><span>جمع خرید / جمع فروش</span><b class="num">' . esc_html(number_format($tt['spent'])) . ' / ' . esc_html(number_format($tt['sold'])) . '</b></div></div>';
    echo '<div class="mzc-panel"><header><h3>از کجا خریده‌ایم</h3></header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>تاریخ</th><th>فروشنده</th><th>شمارهٔ فاکتور</th><th class="n">تعداد</th><th class="n">قیمت واحد</th><th>سریال‌ها</th></tr></thead><tbody>';
    if (!$r['bought']) { echo '<tr><td class="empty" colspan="6">در هیچ فاکتور خریدی نیامده است.</td></tr>'; }
    foreach ($r['bought'] as $x) {
        echo '<tr class="click" data-href="' . esc_url(MZC_Admin::url('mzc-purchases', array('view' => (int) $x['purchase_id']))) . '"><td>' . esc_html($x['bought_at'] ? MZC_Crm::jdate($x['bought_at']) : '—') . '</td><td dir="auto">' . esc_html($x['vendor_name'] ?: '—') . '</td><td class="num">' . esc_html($x['invoice_no'] ?: '—') . '</td>'
            . '<td class="n">' . MZC_Admin::num($x['qty']) . '</td><td class="n">' . MZC_Admin::num($x['unit_cost']) . '</td><td class="num">' . esc_html(str_replace("\n", '، ', (string) $x['serials'])) . '</td></tr>';
    }
    echo '</tbody></table></div></div>';
    echo '<div class="mzc-panel"><header><h3>به چه کسی رفته</h3></header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>تاریخ</th><th>مشتری</th><th>پرونده</th><th class="n">تعداد</th><th class="n">قیمت فروش</th><th class="n">قیمت خرید</th><th>سریال</th></tr></thead><tbody>';
    if (!$r['used']) { echo '<tr><td class="empty" colspan="7">هنوز در هیچ سیستم یا سرویسی به کار نرفته است.</td></tr>'; }
    foreach ($r['used'] as $x) {
        $link = $x['kind'] === 'build' ? MZC_Admin::url('mzc-builds', array('view' => (int) $x['id'])) : MZC_Admin::url('mzc-jobs', array('view' => (int) $x['id']));
        echo '<tr class="click" data-href="' . esc_url($link) . '"><td>' . esc_html($x['at'] ? MZC_Crm::jdate($x['at']) : '—') . '</td><td dir="auto">' . esc_html($x['customer_name']) . '</td><td>' . ($x['kind'] === 'build' ? 'سیستم نو ' : 'سرویس ') . '<span class="num">' . esc_html($x['number'] ?: '#' . $x['id']) . '</span></td>'
            . '<td class="n">' . MZC_Admin::num($x['qty']) . '</td><td class="n">' . MZC_Admin::num($x['price']) . '</td><td class="n">' . MZC_Admin::num($x['cost']) . '</td><td class="num">' . esc_html($x['serial'] ?: '—') . '</td></tr>';
    }
    echo '</tbody></table></div></div>';
    return;
}

$q = isset($_GET['s']) ? sanitize_text_field(wp_unslash($_GET['s'])) : '';
$cat = isset($_GET['cat']) ? sanitize_key($_GET['cat']) : '';
$instock = !empty($_GET['instock']);
$paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 50;
list($rows, $total) = MZC_Stock::products($q, $paged, $per, $cat, $instock);
$linkOf = function ($p) { return MZC_Admin::url('mzc-stock', array('view' => (int) $p['id'])); };
$defs = array(
    'name' => array('کالا', 1, function ($p) use ($linkOf) { return '<a dir="auto" href="' . esc_url($linkOf($p)) . '"><strong>' . esc_html($p['name']) . '</strong></a>'; }),
    'category' => array('دسته', 1, function ($p) { return esc_html(isset(MZC_Crm::$categories[$p['category']]) ? MZC_Crm::$categories[$p['category']] : $p['category']); }),
    'stock' => array('موجودی', 1, function ($p) use ($stockPill) { return $stockPill($p['stock']); }),
    'buy' => array('قیمت خرید', 1, function ($p) { return MZC_Admin::num($p['buy_price']); }, 'n'),
    'sell' => array('قیمت فروش', 1, function ($p) { return MZC_Admin::num($p['sell_price']); }, 'n'),
    'brand' => array('برند', 0, function ($p) { return '<span dir="auto">' . esc_html($p['brand'] ?: '—') . '</span>'; }),
    'sku' => array('پارت‌نامبر', 0, function ($p) { return '<span class="num">' . esc_html($p['sku'] ?: '—') . '</span>'; }),
    'warranty' => array('گارانتی (ماه)', 0, function ($p) { return MZC_Admin::num($p['warranty_months']); }, 'n'),
);
echo '<div class="mzc-title"><h2>انبار و کالاها</h2><div class="mzc-actions"><a class="mzc-btn" href="' . esc_url(MZC_Admin::url('mzc-purchases', array('add' => 1))) . '">+ فاکتور خرید</a><a class="mzc-btn primary" href="' . esc_url(MZC_Admin::url('mzc-stock', array('add' => 1))) . '">+ کالای تازه</a></div></div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-stock"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="نام کالا، برند، پارت‌نامبر" style="min-width:300px"><select name="cat"><option value="">همهٔ دسته‌ها</option>';
foreach (MZC_Crm::$categories as $k => $l) { echo '<option value="' . esc_attr($k) . '"' . selected($cat, $k, false) . '>' . esc_html($l) . '</option>'; }
echo '</select><label class="chk"><input type="checkbox" name="instock" value="1"' . checked($instock, true, false) . '> فقط موجودها</label><button class="mzc-btn primary">اعمال</button><span class="muted">' . esc_html(number_format_i18n($total)) . ' کالا</span>'
    . '<span class="mzc-bar-actions">' . MZC_Admin::cols_ui('mzc-stock', $defs) . '</span></form>';
echo MZC_Admin::table('mzc-stock', $defs, $rows, $linkOf, 'کالایی پیدا نشد. کالاها با «فاکتور خرید» یا «کالای تازه» ثبت می‌شوند.') . MZC_Admin::pager($total, $per, $paged);

// a serial number's whole history, from the same page
$sq = isset($_GET['serial']) ? sanitize_text_field(wp_unslash($_GET['serial'])) : '';
echo '<div class="mzc-panel"><header><h3>ردیابی سریال</h3></header><div class="body"><form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-stock"><input type="search" name="serial" value="' . esc_attr($sq) . '" dir="ltr" placeholder="سریال قطعه"><button class="mzc-btn">پیدا کن</button></form>';
if (mb_strlen($sq) >= 3) {
    $t = MZC_Stock::trace($sq); $li = array();
    foreach ($t['bought'] as $x) { $li[] = 'خرید: <a href="' . esc_url(MZC_Admin::url('mzc-purchases', array('view' => (int) $x['purchase_id']))) . '">' . esc_html(($x['vendor_name'] ?: '—') . ' — فاکتور ' . ($x['invoice_no'] ?: '#' . $x['purchase_id']) . ' — ' . ($x['bought_at'] ? MZC_Crm::jdate($x['bought_at']) : '')) . '</a> <span dir="auto">' . esc_html($x['product_name']) . '</span>'; }
    foreach ($t['builds'] as $x) { $li[] = 'سیستم نو: <a href="' . esc_url(MZC_Admin::url('mzc-builds', array('view' => (int) $x['id']))) . '">' . esc_html(($x['invoice_no'] ?: $x['service_no']) . ' — ' . $x['customer_name']) . '</a> <span dir="auto">' . esc_html($x['model']) . '</span>'; }
    foreach ($t['jobs'] as $x) { $li[] = ($x['kind'] === 'in' ? 'قطعهٔ مشتری در سرویس: ' : 'قطعه در سرویس: ') . '<a href="' . esc_url(MZC_Admin::url('mzc-jobs', array('view' => (int) $x['id']))) . '">' . esc_html($x['service_no'] . ' — ' . $x['customer_name']) . '</a> <span dir="auto">' . esc_html($x['part']) . '</span>'; }
    foreach ($t['ships'] as $x) { $li[] = 'ارسال به گارانتی: <a href="' . esc_url(MZC_Admin::url('mzc-ships', array('edit' => (int) $x['id']))) . '">' . esc_html(($x['vendor_name'] ?: '—') . ' — ' . ($x['sent_at'] ? MZC_Crm::jdate($x['sent_at']) : '')) . '</a> <span dir="auto">' . esc_html($x['part_name']) . '</span>'; }
    echo $li ? '<ul><li>' . implode('</li><li>', $li) . '</li></ul>' : '<p class="muted">این سریال جایی ثبت نشده است.</p>';
}
echo '</div></div>';
