<?php
if (!defined('ABSPATH')) { exit; }
/** The ledger and trial balance on the site (the same figures MazestaDesk shows at #/ledger): worked out from the final invoices, payments, cheques and purchases; nothing is stored or edited here. */
if (!MZC_Admin::need_crm()) { return; }

$from = MZC_Crm::gdate(isset($_GET['from']) ? sanitize_text_field(wp_unslash($_GET['from'])) : '') ?: null;
$to = MZC_Crm::gdate(isset($_GET['to']) ? sanitize_text_field(wp_unslash($_GET['to'])) : '') ?: MZC_Crm::ymd();
$entries = MZC_Ledger::entries($from, $to);
if ($entries === null) { echo '<div class="mzc-notice err">' . esc_html(MZC_Crm_Db::error()) . '</div>'; return; }
$t = MZC_Ledger::trial($entries);
$m = function ($n) { return MZC_Admin::num($n); };

echo '<div class="mzc-title"><div><h2>دفتر کل و تراز آزمایشی</h2><p>' . esc_html(($from ? MZC_Crm::jdate($from) . ' تا ' : 'تا ') . MZC_Crm::jdate($to)) . ' — مبالغ به تومان</p></div>'
    . ($t['balanced'] ? MZC_Admin::pill('بدهکار = بستانکار', 'ok') : MZC_Admin::pill('تراز نیست: ' . implode('، ', $t['unbalanced']), 'bad')) . '</div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-ledger"><input type="text" name="from" dir="ltr" placeholder="از تاریخ 1405/01/01" value="' . esc_attr($from ? MZC_Crm::jdate($from) : '') . '"><input type="text" name="to" dir="ltr" placeholder="تا تاریخ" value="' . esc_attr(MZC_Crm::jdate($to)) . '"><button class="mzc-btn">نمایش</button></form>';

echo '<div class="mzc-panel"><div class="body"><table class="mzc-table"><thead><tr><th>کد</th><th>حساب</th><th>بدهکار</th><th>بستانکار</th><th>مانده (بدهکار − بستانکار)</th></tr></thead><tbody>';
foreach ($t['rows'] as $r) { echo '<tr><td class="num">' . esc_html($r['code']) . '</td><td>' . esc_html($r['name']) . '</td><td>' . $m($r['debit']) . '</td><td>' . $m($r['credit']) . '</td><td>' . $m($r['balance']) . '</td></tr>'; }
if (!$t['rows']) { echo '<tr><td colspan="5">در این بازه سندی نیست.</td></tr>'; }
echo '<tr><td></td><td><strong>جمع</strong></td><td>' . $m($t['debit']) . '</td><td>' . $m($t['credit']) . '</td><td></td></tr></tbody></table></div></div>';

echo '<div class="mzc-panel"><div class="body"><h3>اسناد (۳۰۰ سند آخر)</h3><table class="mzc-table"><thead><tr><th>تاریخ</th><th>سند</th><th>ردیف‌ها</th></tr></thead><tbody>';
foreach (array_reverse(array_slice($entries, -300)) as $e) {
    $rows = array_map(function ($l) { return $l[1] ? 'بد ' . $l[0] . ' ' . number_format($l[1]) : 'بس ' . $l[0] . ' ' . number_format($l[2]); }, $e['lines']);
    echo '<tr><td>' . esc_html(MZC_Crm::jdate($e['at'])) . '</td><td>' . esc_html($e['text']) . '</td><td dir="ltr">' . esc_html(implode(' | ', $rows)) . '</td></tr>';
}
echo '</tbody></table></div></div>';
echo '<p class="description">دفتر کل از خود فاکتورها، پرداخت‌ها، چک‌ها و فاکتورهای خرید حساب می‌شود و چیزی جدا ذخیره نمی‌کند. سرویس‌های قدیمی و پرداخت‌های آن‌ها در آن نیست و چک به وضعیت امروزش حساب می‌شود.</p>';
