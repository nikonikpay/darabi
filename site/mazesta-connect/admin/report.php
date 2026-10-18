<?php
if (!defined('ABSPATH')) { exit; }
/** The money and counts of a period: services (by the day received) and new computers (by the day sold), profit, month by month, the best-selling parts. */
if (!MZC_Admin::need_crm()) { return; }

$per = MZC_Report::periods();
$k = isset($_GET['p']) && isset($per[$_GET['p']]) ? (string) $_GET['p'] : 'month';
$from = isset($_GET['from']) ? MZC_Crm::gdate(sanitize_text_field(wp_unslash($_GET['from']))) : null;
$to = isset($_GET['to']) ? MZC_Crm::gdate(sanitize_text_field(wp_unslash($_GET['to']))) : null;
if (!$from || !$to) { $from = $per[$k][1]; $to = $per[$k][2]; } else { $k = ''; }
if ($from > $to) { list($from, $to) = array($to, $from); }
$r = MZC_Report::build($from, $to);
$m = function ($n) { return esc_html(number_format((int) $n)); };
$card = function ($label, $value, $note = '') { return '<div class="mzc-card mzc-kpi"><div class="k">' . esc_html($label) . '</div><div class="v num">' . $value . '</div>' . ($note !== '' ? '<div class="s">' . $note . '</div>' : '') . '</div>'; };

echo '<div class="mzc-title"><h2>گزارش مالی و آمار</h2><span class="muted">' . esc_html($r['from_j'] . ' تا ' . $r['to_j']) . '</span></div>';
echo '<form method="get" class="mzc-filter"><input type="hidden" name="page" value="mzc-report">';
foreach ($per as $pk => $p) { echo '<a class="mzc-btn' . ($pk === $k ? ' primary' : '') . '" href="' . esc_url(MZC_Admin::url('mzc-report', array('p' => $pk))) . '">' . esc_html($p[0]) . '</a>'; }
echo '<label>از <input type="text" name="from" value="' . esc_attr($r['from_j']) . '" dir="ltr" size="10" placeholder="1405/07/01"></label><label>تا <input type="text" name="to" value="' . esc_attr($r['to_j']) . '" dir="ltr" size="10"></label><button class="mzc-btn primary">نمایش</button>'
    . '<span class="muted">تاریخ شمسی یا میلادی</span></form>';

$J = $r['jobs']; $B = $r['builds'];
$incomplete = function ($n) { return $n ? '<span class="mzc-pill warn">ناقص: ' . (int) $n . ' قطعه قیمت خرید ندارد</span>' : ''; };
echo '<div class="mzc-panel"><header><h3>سرویس‌ها</h3><span class="muted">بر اساس تاریخ پذیرش</span></header><div class="body"><div class="mzc-cards">'
    . $card('تعداد سرویس', $m($J['count'])) . $card('هزینهٔ نهایی (از مشتری)', $m($J['total'])) . $card('دریافت‌شده', $m($J['received']), $J['total'] > $J['received'] ? 'مانده: ' . $m($J['total'] - $J['received']) : '')
    . $card('هزینهٔ خدمات', $m($J['labor'])) . $card('فروش قطعات', $m($J['parts_sell'])) . $card('هزینه‌های پرداخت‌شده', $m($J['paid_out'])) . $card('تخفیف', $m($J['discount']))
    . $card('خرید قطعات', $m($J['parts_buy'])) . $card('سود خدمات', $m($J['service_profit']), 'خدمات − تخفیف − هزینه‌های پرداخت‌شده') . $card('سود قطعات', $m($J['parts_profit']), $incomplete($J['missing_cost'])) . $card('سود کل سرویس‌ها', $m($J['profit'])) . '</div><p>';
foreach ($J['by_status'] as $s => $n) { echo MZC_Admin::pill((isset(MZC_Crm::$statuses[$s]) ? MZC_Crm::$statuses[$s] : $s) . ': ' . (int) $n) . ' '; }
echo '</p></div></div>';

echo '<div class="mzc-panel"><header><h3>سیستم‌های نو</h3><span class="muted">بر اساس تاریخ فروش</span></header><div class="body"><div class="mzc-cards">'
    . $card('تعداد سیستم', $m($B['count'])) . $card('فروش (جمع قیمت قطعات)', $m($B['sell']), $B['missing_price'] ? '<span class="mzc-pill warn">' . (int) $B['missing_price'] . ' قطعه قیمت فروش ندارد</span>' : '')
    . $card('خرید قطعات', $m($B['buy'])) . $card('سود قطعات', $m($B['profit']), $incomplete($B['missing_cost'])) . '</div><p>';
foreach ($B['by_status'] as $s => $n) { echo MZC_Admin::pill((isset(MZC_Crm::$build_statuses[$s]) ? MZC_Crm::$build_statuses[$s] : $s) . ': ' . (int) $n) . ' '; }
echo '</p></div></div>';

$S = $r['sum'];
echo '<div class="mzc-panel"><header><h3>جمع سود این بازه</h3></header><div class="body"><div class="mzc-cards">' . $card('سود خدمات', $m($S['service_profit'])) . $card('سود قطعات (سرویس‌ها و سیستم‌های نو)', $m($S['parts_profit']), $incomplete($S['missing_cost']) . ($S['missing_price'] ? ' <span class="mzc-pill warn">' . (int) $S['missing_price'] . ' قطعه قیمت فروش ندارد</span>' : '')) . $card('سود کل', $m($S['profit'])) . '</div></div></div>';
echo '<div class="mzc-panel"><header><h3>ماه به ماه</h3></header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>ماه</th><th class="n">سرویس</th><th class="n">مبلغ سرویس‌ها</th><th class="n">سود سرویس‌ها</th><th class="n">سیستم نو</th><th class="n">فروش سیستم‌ها</th><th class="n">سود سیستم‌ها</th><th class="n">جمع سود</th></tr></thead><tbody>';
if (!$r['months']) { echo '<tr><td class="empty" colspan="8">در این بازه چیزی ثبت نشده است.</td></tr>'; }
foreach ($r['months'] as $x) { echo '<tr><td class="num">' . esc_html($x['month']) . '</td><td class="n">' . $m($x['jobs']) . '</td><td class="n">' . $m($x['jobs_total']) . '</td><td class="n">' . $m($x['jobs_profit']) . '</td><td class="n">' . $m($x['builds']) . '</td><td class="n">' . $m($x['builds_sell']) . '</td><td class="n">' . $m($x['builds_profit']) . '</td><td class="n"><strong>' . $m($x['jobs_profit'] + $x['builds_profit']) . '</strong></td></tr>'; }
echo '</tbody></table></div></div>';

echo '<div class="mzc-panel"><header><h3>پرفروش‌ترین قطعات</h3></header><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr><th>قطعه</th><th class="n">تعداد</th><th class="n">مبلغ فروش</th></tr></thead><tbody>';
if (!$r['top']) { echo '<tr><td class="empty" colspan="3">—</td></tr>'; }
foreach ($r['top'] as $x) { echo '<tr><td dir="auto">' . esc_html($x['name']) . '</td><td class="n">' . $m($x['qty']) . '</td><td class="n">' . $m($x['revenue']) . '</td></tr>'; }
echo '</tbody></table></div></div>';
echo '<p class="muted">مشتری فقط هزینهٔ خدمات و قطعات (منهای تخفیف) را می‌پردازد. سود خدمات = هزینهٔ خدمات − تخفیف − هزینه‌های پرداخت‌شده (خرج خودمان برای آن سرویس). سود قطعات = قیمت فروش − قیمت خرید، در سرویس‌ها و سیستم‌های نو. سود قطعه‌ای که قیمت خرید ندارد درست حساب نمی‌شود؛ گزارش آن را «ناقص» نشان می‌دهد و سود واقعی از عدد نشان‌داده‌شده کمتر است.</p>';
