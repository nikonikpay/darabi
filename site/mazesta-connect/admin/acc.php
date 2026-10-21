<?php
if (!defined('ABSPATH')) { exit; }
/** The accounting on the site: invoices (a printable view, voiding), payments and cheques, and the period's money. Invoices are written in the staff program (MazestaDesk), which has the keyboard-driven form. */
if (!MZC_Admin::need_crm()) { return; }

$tab = isset($_GET['tab']) ? sanitize_key($_GET['tab']) : 'invoices';
$view = isset($_GET['view']) ? (int) $_GET['view'] : 0;
$money = function ($n) { return MZC_Admin::num($n); };
$jd = function ($d) { return $d ? esc_html(MZC_Crm::jdate($d)) : '—'; };
$stPill = function ($v) { return MZC_Admin::pill(MZC_Acc::$statuses[$v['status']], $v['status'] === 'final' ? 'ok' : ($v['status'] === 'void' ? 'bad' : 'warn')); };

if ($view) {
    $v = MZC_Acc::invoice($view);
    if (!$v) { echo '<div class="mzc-notice err">فاکتور پیدا نشد.</div>'; return; }
    $s = MZC_Acc::settings();
    echo '<div class="mzc-title"><div><a href="' . esc_url(MZC_Admin::url('mzc-acc', array('kind' => $v['kind']))) . '">‹ حسابداری</a><h2>' . esc_html(MZC_Acc::$kinds[$v['kind']] . ' ' . ($v['no'] ?: '')) . ' ' . $stPill($v) . '</h2></div>'
        . '<div class="mzc-actions"><button class="mzc-btn" onclick="window.print()">چاپ</button></div></div>';
    if ($v['status'] === 'void') { echo '<div class="mzc-notice err">باطل شد: ' . esc_html($v['void_reason']) . '</div>'; }
    echo '<div class="mzc-panel mzc-print"><div class="body"><div class="mzc-cards">'
        . '<div class="mzc-card"><span>' . esc_html($s['shop'] ?: get_bloginfo('name')) . '</span><b dir="auto">' . esc_html($s['phone']) . '</b></div>'
        . '<div class="mzc-card"><span>مشتری</span><b>' . ($v['customer_id'] ? '<a href="' . esc_url(MZC_Admin::url('mzc-customers', array('view' => (int) $v['customer_id']))) . '">' . esc_html($v['customer_name']) . '</a>' : '—') . '</b><small class="num">' . esc_html((string) $v['customer_mobile']) . '</small></div>'
        . '<div class="mzc-card"><span>تاریخ</span><b class="num">' . $jd($v['at']) . '</b>' . ($v['valid_until'] ? '<small>اعتبار تا ' . $jd($v['valid_until']) . '</small>' : '') . '</div>'
        . ($v['build_id'] ? '<div class="mzc-card"><span>سیستم نو</span><b><a href="' . esc_url(MZC_Admin::url('mzc-builds', array('view' => (int) $v['build_id']))) . '">باز کردن</a></b></div>' : '')
        . ($v['wc_order_id'] ? '<div class="mzc-card"><span>سفارش سایت</span><b><a href="' . esc_url(admin_url('post.php?post=' . (int) $v['wc_order_id'] . '&action=edit')) . '">#' . (int) $v['wc_order_id'] . '</a></b></div>' : '') . '</div>';
    echo '<table class="mzc-table"><thead><tr><th>#</th><th>شرح کالا / خدمات</th><th>سریال</th><th class="n">تعداد</th><th class="n">قیمت واحد</th><th class="n">جمع</th></tr></thead><tbody>';
    foreach ($v['items'] as $i => $l) {
        echo '<tr><td class="num">' . ($i + 1) . '</td><td dir="auto">' . ($l['product_id'] ? '<a href="' . esc_url(MZC_Admin::url('mzc-stock', array('view' => (int) $l['product_id']))) . '">' . esc_html($l['name']) . '</a>' : esc_html($l['name'])) . ($l['warranty_months'] ? ' <small class="muted">' . (int) $l['warranty_months'] . ' ماه گارانتی</small>' : '') . '</td>'
            . '<td class="num">' . esc_html(str_replace("\n", '، ', (string) $l['serials'])) . '</td><td class="n">' . $money($l['qty']) . '</td><td class="n">' . $money($l['unit_price']) . '</td><td class="n">' . $money((int) $l['qty'] * (int) $l['unit_price']) . '</td></tr>';
    }
    echo '</tbody><tfoot><tr><td colspan="5">جمع</td><td class="n">' . $money($v['subtotal']) . '</td></tr>' . ($v['discount'] ? '<tr><td colspan="5">تخفیف</td><td class="n">' . $money($v['discount']) . '</td></tr>' : '')
        . ($v['tax'] ? '<tr><td colspan="5">مالیات بر ارزش افزوده (' . esc_html((float) $v['tax_pct']) . '٪)</td><td class="n">' . $money($v['tax']) . '</td></tr>' : '') . '<tr><td colspan="5"><b>مبلغ قابل پرداخت (تومان)</b></td><td class="n"><b>' . $money($v['total']) . '</b></td></tr>'
        . ($v['kind'] === 'sale' || $v['kind'] === 'return' ? '<tr><td colspan="5">پرداخت‌شده</td><td class="n">' . $money($v['paid']) . '</td></tr><tr><td colspan="5">مانده</td><td class="n">' . $money((int) $v['total'] - (int) $v['paid']) . '</td></tr>' : '') . '</tfoot></table>';
    if ($v['notes']) { echo '<p dir="auto">' . nl2br(esc_html($v['notes'])) . '</p>'; }
    if ($s['foot']) { echo '<p class="muted">' . esc_html($s['foot']) . '</p>'; }
    echo '</div></div>';
    if ($v['payments']) {
        echo '<div class="mzc-panel"><header><h3>پرداخت‌ها</h3></header><div class="body flush"><table class="mzc-table"><thead><tr><th>تاریخ</th><th>روش</th><th>حساب</th><th class="n">مبلغ</th><th>چک</th></tr></thead><tbody>';
        foreach ($v['payments'] as $p) { echo '<tr><td>' . $jd($p['at']) . '</td><td>' . esc_html(($p['direction'] === 'out' ? 'پس‌دادن — ' : '') . MZC_Acc::$methods[$p['method']]) . '</td><td dir="auto">' . esc_html((string) $p['account']) . '</td><td class="n">' . $money($p['amount']) . '</td><td>' . ($p['cheque_no'] ? esc_html($p['cheque_no'] . ' — ' . MZC_Acc::$cheque_statuses[$p['cheque_status']]) : '') . '</td></tr>'; }
        echo '</tbody></table></div></div>';
    }
    if ($v['status'] !== 'void' && $v['kind'] !== 'template' && ($v['status'] === 'final' || $v['kind'] === 'proforma')) {
        echo '<div class="mzc-panel"><header><h3>ابطال</h3></header><div class="body">' . MZC_Admin::form_open('invoice_void', 'class="mzc-filter" onsubmit="return confirm(\'این فاکتور باطل شود؟ شماره‌اش دیگر به کار نمی‌رود.\')"')
            . '<input type="hidden" name="id" value="' . (int) $v['id'] . '"><input type="text" name="reason" placeholder="دلیل ابطال" required maxlength="255" class="mzc-search"><button class="mzc-btn danger">باطل شود</button></form></div></div>';
    }
    return;
}

$tabs = array('invoices' => 'فاکتورها', 'payments' => 'دریافت و پرداخت', 'cheques' => 'چک‌ها', 'summary' => 'خلاصهٔ مالی');
echo '<div class="mzc-title"><h2>حسابداری</h2><span class="muted">فاکتور، پیش‌فاکتور و دریافت را در برنامهٔ «میز کار مازستا» ثبت کنید (با کلیدهای F1 تا F12).</span></div><div class="mzc-filter">';
foreach ($tabs as $k => $l) { echo '<a class="mzc-btn sm' . ($k === $tab ? ' primary' : '') . '" href="' . esc_url(MZC_Admin::url('mzc-acc', array('tab' => $k))) . '">' . esc_html($l) . '</a>'; }
echo '</div>';
$paged = max(1, isset($_GET['paged']) ? (int) $_GET['paged'] : 1); $per = 50;
$q = isset($_GET['s']) ? sanitize_text_field(wp_unslash($_GET['s'])) : '';

if ($tab === 'invoices') {
    $kind = isset($_GET['kind']) ? sanitize_key($_GET['kind']) : ''; $st = isset($_GET['status']) ? sanitize_key($_GET['status']) : '';
    list($rows, $total) = MZC_Acc::invoices(array('kind' => $kind, 'status' => $st, 'q' => $q), $paged, $per);
    echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-acc"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="شماره، مشتری، کالا یا سریال"><select name="kind"><option value="">همهٔ فاکتورها</option>';
    foreach (MZC_Acc::$kinds as $k => $l) { echo '<option value="' . esc_attr($k) . '"' . selected($kind, $k, false) . '>' . esc_html($l) . '</option>'; }
    echo '</select><select name="status"><option value="">همهٔ وضعیت‌ها</option>';
    foreach (MZC_Acc::$statuses as $k => $l) { echo '<option value="' . esc_attr($k) . '"' . selected($st, $k, false) . '>' . esc_html($l) . '</option>'; }
    echo '</select><button class="mzc-btn primary">اعمال</button><span class="muted">' . esc_html(number_format_i18n($total)) . ' مورد</span></form>';
    echo '<div class="mzc-panel"><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>نوع</th><th>شماره</th><th>تاریخ</th><th>مشتری / نام</th><th>وضعیت</th><th class="n">مبلغ</th><th class="n">مانده</th></tr></thead><tbody>';
    if (!$rows) { echo '<tr><td class="empty" colspan="7">فاکتوری نیست.</td></tr>'; }
    foreach ($rows as $v) {
        echo '<tr class="click" data-href="' . esc_url(MZC_Admin::url('mzc-acc', array('view' => (int) $v['id']))) . '"><td>' . esc_html(MZC_Acc::$kinds[$v['kind']]) . ($v['wc_order_id'] ? ' ' . MZC_Admin::pill('سایت', 'info') : '') . '</td><td class="num">' . esc_html((string) $v['no']) . '</td><td>' . $jd($v['at']) . '</td>'
            . '<td dir="auto">' . esc_html($v['customer_name'] ?: (string) $v['title']) . '</td><td>' . $stPill($v) . '</td><td class="n">' . $money($v['total']) . '</td><td class="n">' . ($v['kind'] === 'sale' && $v['status'] === 'final' ? $money((int) $v['total'] - (int) $v['paid']) : '') . '</td></tr>';
    }
    echo '</tbody></table></div></div>' . MZC_Admin::pager($total, $per, $paged);
} elseif ($tab === 'payments') {
    $f = array('q' => $q, 'customer' => isset($_GET['customer']) ? (int) $_GET['customer'] : 0, 'direction' => isset($_GET['direction']) ? sanitize_key($_GET['direction']) : '');
    list($rows, $total) = MZC_Acc::payments($f, $paged, $per);
    echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-acc"><input type="hidden" name="tab" value="payments"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="مشتری، فروشنده، شمارهٔ تراکنش، شرح">'
        . '<select name="direction"><option value="">دریافت و پرداخت</option><option value="in"' . selected($f['direction'], 'in', false) . '>دریافت</option><option value="out"' . selected($f['direction'], 'out', false) . '>پرداخت</option></select><button class="mzc-btn primary">اعمال</button></form>';
    echo '<div class="mzc-panel"><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>تاریخ</th><th>نوع</th><th>طرف حساب / سرفصل</th><th>روش</th><th>حساب</th><th class="n">مبلغ</th><th>فاکتور</th><th></th></tr></thead><tbody>';
    if (!$rows) { echo '<tr><td class="empty" colspan="8">پرداختی نیست.</td></tr>'; }
    foreach ($rows as $p) {
        echo '<tr><td>' . $jd($p['at']) . '</td><td>' . ($p['direction'] === 'in' ? MZC_Admin::pill('دریافت', 'ok') : MZC_Admin::pill('پرداخت', 'warn')) . '</td><td dir="auto">' . esc_html($p['customer_name'] ?: ($p['vendor_name'] ?: (string) $p['category'])) . '</td>'
            . '<td>' . esc_html(MZC_Acc::$methods[$p['method']] . ($p['cheque_no'] ? ' ' . $p['cheque_no'] . ' (' . MZC_Acc::$cheque_statuses[$p['cheque_status']] . ')' : '')) . '</td><td dir="auto">' . esc_html((string) $p['account']) . '</td><td class="n">' . $money($p['amount']) . '</td>'
            . '<td>' . ($p['invoice_id'] ? '<a href="' . esc_url(MZC_Admin::url('mzc-acc', array('view' => (int) $p['invoice_id']))) . '" class="num">' . esc_html((string) $p['invoice_no']) . '</a>' : '') . '</td>'
            . '<td>' . (current_user_can('manage_options') ? '<a class="mzc-btn sm danger" href="' . esc_url(MZC_Admin::post_url('payment_delete', array('id' => (int) $p['id']))) . '" onclick="return confirm(\'این پرداخت پاک شود؟\')">پاک</a>' : '') . '</td></tr>';
    }
    echo '</tbody></table></div></div>' . MZC_Admin::pager($total, $per, $paged);
} elseif ($tab === 'cheques') {
    $due = isset($_GET['due']) ? sanitize_key($_GET['due']) : ''; $st = isset($_GET['status']) ? sanitize_key($_GET['status']) : '';
    list($rows, $total) = MZC_Acc::cheques(array('q' => $q, 'due' => $due, 'status' => $st), $paged, $per);
    echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-acc"><input type="hidden" name="tab" value="cheques"><input type="search" name="s" value="' . esc_attr($q) . '" placeholder="شمارهٔ چک، بانک، طرف حساب">'
        . '<select name="due"><option value="">همه</option><option value="soon"' . selected($due, 'soon', false) . '>نزدیک سررسید</option><option value="late"' . selected($due, 'late', false) . '>سررسید گذشته</option></select><select name="status"><option value="">همهٔ وضعیت‌ها</option>';
    foreach (MZC_Acc::$cheque_statuses as $k => $l) { echo '<option value="' . esc_attr($k) . '"' . selected($st, $k, false) . '>' . esc_html($l) . '</option>'; }
    echo '</select><button class="mzc-btn primary">اعمال</button></form>';
    echo '<div class="mzc-panel"><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>سررسید</th><th>نوع</th><th>شماره</th><th>بانک</th><th>طرف حساب</th><th class="n">مبلغ</th><th>وضعیت</th><th></th></tr></thead><tbody>';
    if (!$rows) { echo '<tr><td class="empty" colspan="8">چکی نیست.</td></tr>'; }
    $today = MZC_Crm::ymd();
    foreach ($rows as $c) {
        $late = $c['status'] === 'pending' && $c['due_at'] < $today;
        echo '<tr><td>' . $jd($c['due_at']) . ($late ? ' ' . MZC_Admin::pill('گذشته', 'bad') : '') . '</td><td>' . ($c['direction'] === 'out' ? 'پرداختنی' : 'دریافتنی') . '</td><td class="num">' . esc_html($c['no']) . '</td><td dir="auto">' . esc_html((string) $c['bank']) . '</td>'
            . '<td dir="auto">' . esc_html($c['customer_name'] ?: ($c['vendor_name'] ?: (string) $c['party'])) . '</td><td class="n">' . $money($c['amount']) . '</td><td>' . esc_html(MZC_Acc::$cheque_statuses[$c['status']]) . '</td><td>';
        if ($c['status'] === 'pending') { foreach (array('cleared' => 'پاس شد', 'bounced' => 'برگشت خورد') as $k => $l) { echo '<a class="mzc-btn sm" href="' . esc_url(MZC_Admin::post_url('cheque_status', array('id' => (int) $c['id'], 'st' => $k))) . '">' . esc_html($l) . '</a> '; } }
        echo '</td></tr>';
    }
    echo '</tbody></table></div></div>' . MZC_Admin::pager($total, $per, $paged);
} else {
    $pr = MZC_Report::periods(); $k = isset($_GET['period'], $pr[$_GET['period']]) ? sanitize_key($_GET['period']) : 'month';
    $s = MZC_Acc::summary($pr[$k][1], $pr[$k][2]);
    echo '<div class="mzc-filter">'; foreach ($pr as $pk => $p) { echo '<a class="mzc-btn sm' . ($pk === $k ? ' primary' : '') . '" href="' . esc_url(MZC_Admin::url('mzc-acc', array('tab' => 'summary', 'period' => $pk))) . '">' . esc_html($p[0]) . '</a>'; } echo MZC_Admin::pill('مبالغ به تومان') . '</div>';
    echo '<div class="mzc-cards"><div class="mzc-card"><span>فروش (' . (int) $s['sales']['count'] . ' فاکتور)</span><b class="num">' . esc_html(number_format($s['sales']['total'])) . '</b></div><div class="mzc-card"><span>برگشت از فروش</span><b class="num">' . esc_html(number_format($s['returns']['total'])) . '</b></div>'
        . '<div class="mzc-card"><span>فروش خالص</span><b class="num">' . esc_html(number_format($s['net'])) . '</b></div><div class="mzc-card"><span>سود فاکتورها' . ($s['missing_cost'] ? ' (ناقص: ' . (int) $s['missing_cost'] . ' ردیف بی‌قیمت خرید)' : '') . '</span><b class="num">' . esc_html(number_format($s['profit'])) . '</b></div></div>';
    $list = function ($title, $rows, $l, $v) { echo '<div class="mzc-panel"><header><h3>' . esc_html($title) . '</h3></header><div class="body">' . ($rows ? MZC_Admin::bars(array_combine(array_map($l, $rows), array_map($v, $rows))) : '<p class="muted">چیزی نیست.</p>') . '</div></div>'; };
    $list('دریافت‌ها به روش', array_values(array_filter($s['flow'], function ($x) { return $x['direction'] === 'in'; })), function ($x) { return $x['methodText']; }, function ($x) { return $x['amount']; });
    $list('هزینه‌ها', $s['expenses'], function ($x) { return $x['category']; }, function ($x) { return $x['amount']; });
    echo '<div class="mzc-panel"><header><h3>موجودی حساب‌ها (از ابتدا)</h3></header><div class="body flush"><table class="mzc-table"><tbody>';
    foreach ($s['accounts'] as $a) { echo '<tr><td dir="auto">' . esc_html($a['account']) . '</td><td class="n">' . $money($a['balance']) . '</td></tr>'; }
    echo '</tbody></table></div></div><div class="mzc-panel"><header><h3>بیشترین بدهی مشتریان</h3></header><div class="body flush"><table class="mzc-table"><tbody>';
    foreach ($s['debtors'] as $d) { echo '<tr class="click" data-href="' . esc_url(MZC_Admin::url('mzc-customers', array('view' => $d['customer_id']))) . '"><td dir="auto">' . esc_html($d['name']) . '</td><td class="num">' . esc_html($d['mobile']) . '</td><td class="n">' . $money($d['balance']) . '</td></tr>'; }
    echo '</tbody></table></div></div>';
}
