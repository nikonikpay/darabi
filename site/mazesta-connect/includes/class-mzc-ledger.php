<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The double-entry ledger, worked out from the documents the accounting already keeps (final sale and return invoices, payments, cheques, purchase invoices). It stores nothing:
 * there is no second set of figures to edit, so it cannot drift from the documents. Each document becomes one journal entry whose debits equal its credits (balanced() says so; the
 * trial balance repeats it). Not included: the old-style service jobs and their payments (job_pays); the chart is small on purpose.
 * Simplification: a cheque's entry follows the cheque's status today (pending, cleared) and keeps the payment's date; a bounced or cancelled cheque makes no entry.
 */
final class MZC_Ledger
{
    public static $accounts = array(
        '1010' => 'صندوق', '1020' => 'بانک و کارتخوان', '1030' => 'اسناد دریافتنی (چک‌های در راه)', '1100' => 'حساب‌های دریافتنی (مشتریان)', '1200' => 'موجودی کالا',
        '2010' => 'حساب‌های پرداختنی (فروشندگان)', '2020' => 'اسناد پرداختنی (چک‌های صادرشده)', '2100' => 'مالیات بر ارزش افزوده',
        '4010' => 'فروش', '4900' => 'درآمد متفرقه', '5010' => 'بهای تمام‌شدهٔ کالای فروخته‌شده', '6000' => 'هزینه‌ها',
    );

    private static function t($n) { return MZC_Crm_Db::t($n); }

    /** A line is array(account, debit, credit); amounts are whole tomans. */
    private static function line($acct, $dr, $cr) { return array((string) $acct, (int) $dr, (int) $cr); }

    public static function balanced($lines)
    {
        $d = 0; $c = 0; foreach ($lines as $l) { $d += $l[1]; $c += $l[2]; }
        return $d === $c;
    }

    /** $v: kind (sale|return), total, tax, cogs (what the sold lines cost; 0 when no cost is known). A return reverses the sale. */
    public static function post_invoice($v)
    {
        $total = (int) $v['total']; $tax = max(0, (int) $v['tax']); $cogs = max(0, (int) $v['cogs']);
        $lines = array(self::line('1100', $total, 0), self::line('4010', 0, $total - $tax));
        if ($tax) { $lines[] = self::line('2100', 0, $tax); }
        if ($cogs) { $lines[] = self::line('5010', $cogs, 0); $lines[] = self::line('1200', 0, $cogs); }
        if ($v['kind'] === 'return') { $lines = array_map(function ($l) { return array($l[0], $l[2], $l[1]); }, $lines); }
        return $lines;
    }

    /** $p: direction (in|out), amount, method, customer_id, vendor_id; $chequeStatus: null (no cheque) or the cheque's status. Returns no lines for a dead cheque. */
    public static function post_payment($p, $chequeStatus)
    {
        $amount = (int) $p['amount']; $in = $p['direction'] === 'in';
        if ($chequeStatus !== null && in_array($chequeStatus, array('bounced', 'cancelled'), true)) { return array(); }
        if ($p['method'] === 'cheque') { $asset = $chequeStatus === 'cleared' ? '1020' : ($in ? '1030' : '2020'); }
        else { $asset = $p['method'] === 'cash' ? '1010' : '1020'; }
        $other = !empty($p['customer_id']) ? '1100' : (!empty($p['vendor_id']) ? '2010' : ($in ? '4900' : '6000'));
        return $in ? array(self::line($asset, $amount, 0), self::line($other, 0, $amount)) : array(self::line($other, $amount, 0), self::line($asset, 0, $amount));
    }

    /** A supplier's purchase invoice: the goods come into stock, the shop owes the supplier (paid later by a payment to them). */
    public static function post_purchase($total) { $total = (int) $total; return $total > 0 ? array(self::line('1200', $total, 0), self::line('2010', 0, $total)) : array(); }

    /** Every entry from $from to $to (Y-m-d; null $from = from the start), oldest first: array(at, ref, id, text, lines). */
    public static function entries($from, $to)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return null; }
        $from = $from ?: '1900-01-01'; $out = array();
        $iv = self::t('invoices'); $ii = self::t('invoice_items'); $py = self::t('payments'); $ch = self::t('cheques'); $pu = self::t('purchases'); $pi = self::t('purchase_items');
        foreach ($db->get_results($db->prepare("SELECT v.id, v.kind, v.no, v.at, v.total, v.tax, (SELECT COALESCE(SUM(i.qty * i.buy_price), 0) FROM `$ii` i WHERE i.invoice_id = v.id AND i.buy_price IS NOT NULL) AS cogs FROM `$iv` v WHERE v.status = 'final' AND v.kind IN ('sale','return') AND v.at BETWEEN %s AND %s", $from, $to), ARRAY_A) ?: array() as $v) {
            $out[] = array('at' => (string) $v['at'], 'ref' => 'invoice', 'id' => (int) $v['id'], 'text' => MZC_Acc::$kinds[$v['kind']] . ' ' . $v['no'], 'lines' => self::post_invoice($v));
        }
        foreach ($db->get_results($db->prepare("SELECT y.*, q.status AS cheque_status FROM `$py` y LEFT JOIN `$ch` q ON q.id = y.cheque_id WHERE y.at BETWEEN %s AND %s", $from, $to), ARRAY_A) ?: array() as $p) {
            $lines = self::post_payment($p, $p['cheque_status']); if (!$lines) { continue; }
            $out[] = array('at' => (string) $p['at'], 'ref' => 'payment', 'id' => (int) $p['id'], 'text' => ($p['direction'] === 'in' ? 'دریافت ' : 'پرداخت ') . MZC_Acc::$methods[$p['method']], 'lines' => $lines);
        }
        foreach ($db->get_results($db->prepare("SELECT u.id, u.invoice_no, u.bought_at, (SELECT COALESCE(SUM(x.qty * x.unit_cost), 0) FROM `$pi` x WHERE x.purchase_id = u.id) AS total FROM `$pu` u WHERE u.bought_at BETWEEN %s AND %s", $from, $to), ARRAY_A) ?: array() as $u) {
            $lines = self::post_purchase($u['total']); if (!$lines) { continue; }
            $out[] = array('at' => (string) $u['bought_at'], 'ref' => 'purchase', 'id' => (int) $u['id'], 'text' => 'فاکتور خرید ' . $u['invoice_no'], 'lines' => $lines);
        }
        usort($out, function ($a, $b) { return strcmp($a['at'], $b['at']) ?: strcmp($a['ref'] . $a['id'], $b['ref'] . $b['id']); });
        return $out;
    }

    /** The trial balance of entries: each account's debit, credit and balance (debit minus credit), and whether the books balance; unbalanced entries are named. */
    public static function trial($entries)
    {
        $acc = array(); $bad = array();
        foreach ($entries as $e) {
            if (!self::balanced($e['lines'])) { $bad[] = $e['ref'] . ' ' . $e['id']; continue; }
            foreach ($e['lines'] as $l) { if (!isset($acc[$l[0]])) { $acc[$l[0]] = array(0, 0); } $acc[$l[0]][0] += $l[1]; $acc[$l[0]][1] += $l[2]; }
        }
        ksort($acc); $rows = array(); $d = 0; $c = 0;
        foreach ($acc as $k => $x) { $rows[] = array('code' => (string) $k, 'name' => self::$accounts[$k], 'debit' => $x[0], 'credit' => $x[1], 'balance' => $x[0] - $x[1]); $d += $x[0]; $c += $x[1]; }
        return array('rows' => $rows, 'debit' => $d, 'credit' => $c, 'balanced' => $d === $c && !$bad, 'unbalanced' => $bad);
    }
}
