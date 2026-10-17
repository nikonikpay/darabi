<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The money and the counts of a period: services by the day they were received, new computers by the day they were sold (or registered).
 * A service's price to the customer is parts + labour + the costs the shop paid out for it - discount; its profit leaves out what was paid out and what the parts cost.
 * A computer's sale is the sum of its parts' selling prices, its profit that minus their buying prices. A part without a buying price is counted (missing_cost), never
 * taken as free: the page says the profit is incomplete instead of showing a profit that is too high. Shown on the plugin's report page, the staff apps and the MCP tool.
 */
final class MZC_Report
{
    private static function t($n) { return MZC_Crm_Db::t($n); }

    /** The quick periods the pages offer: key => array(label, from, to), in the Persian calendar's months and years. */
    public static function periods()
    {
        $j = MZC_Crm::jdate(gmdate('Y-m-d')); list($y, $m) = array((int) substr($j, 0, 4), (int) substr($j, 5, 2));
        $start = function ($yy, $mm) { return MZC_Crm::gdate(sprintf('%04d/%02d/01', $yy, $mm)); };
        $py = $m === 1 ? $y - 1 : $y; $pm = $m === 1 ? 12 : $m - 1; $today = gmdate('Y-m-d');
        return array(
            'month' => array('این ماه', $start($y, $m), $today),
            'last' => array('ماه قبل', $start($py, $pm), gmdate('Y-m-d', strtotime($start($y, $m) . ' -1 day'))),
            'd30' => array('۳۰ روز اخیر', gmdate('Y-m-d', time() - 29 * 86400), $today),
            'year' => array('امسال', $start($y, 1), $today),
        );
    }

    /** The report of $from..$to (Y-m-d, both days counted), or null without the CRM database. */
    public static function build($from, $to)
    {
        $db = MZC_Crm::db(); if (!$db) { return null; }
        $jp = self::t('job_parts');
        $jobs = $db->get_results($db->prepare("SELECT j.id, j.status, j.received_at AS at, j.labor_price, j.paid, j.discount,
                COALESCE((SELECT SUM(p.qty * p.unit_price) FROM `$jp` p WHERE p.job_id = j.id AND p.kind <> 'in'), 0) AS sell,
                COALESCE((SELECT SUM(p.qty * p.buy_price) FROM `$jp` p WHERE p.job_id = j.id AND p.kind <> 'in' AND p.buy_price IS NOT NULL), 0) AS buy,
                (SELECT COUNT(*) FROM `$jp` p WHERE p.job_id = j.id AND p.kind <> 'in' AND p.buy_price IS NULL) AS nocost,
                COALESCE((SELECT SUM(y.amount) FROM `" . self::t('job_pays') . "` y WHERE y.job_id = j.id), 0) AS got
            FROM `" . self::t('jobs') . "` j WHERE COALESCE(j.received_at, DATE(j.created)) BETWEEN %s AND %s", $from, $to), ARRAY_A) ?: array();
        $bp = self::t('build_parts');
        $builds = $db->get_results($db->prepare("SELECT b.id, b.status, COALESCE(b.sold_at, DATE(b.created)) AS at,
                COALESCE((SELECT SUM(p.sell_price) FROM `$bp` p WHERE p.build_id = b.id), 0) AS sell,
                COALESCE((SELECT SUM(p.buy_price) FROM `$bp` p WHERE p.build_id = b.id AND p.buy_price IS NOT NULL), 0) AS buy,
                (SELECT COUNT(*) FROM `$bp` p WHERE p.build_id = b.id AND p.buy_price IS NULL) AS nocost,
                (SELECT COUNT(*) FROM `$bp` p WHERE p.build_id = b.id AND p.sell_price IS NULL) AS noprice
            FROM `" . self::t('builds') . "` b WHERE COALESCE(b.sold_at, DATE(b.created)) BETWEEN %s AND %s", $from, $to), ARRAY_A) ?: array();

        $J = array('count' => 0, 'by_status' => array(), 'labor' => 0, 'paid_out' => 0, 'discount' => 0, 'parts_sell' => 0, 'parts_buy' => 0, 'total' => 0, 'received' => 0, 'profit' => 0, 'missing_cost' => 0);
        $B = array('count' => 0, 'by_status' => array(), 'sell' => 0, 'buy' => 0, 'profit' => 0, 'missing_cost' => 0, 'missing_price' => 0);
        $months = array();
        $month = function ($at) use (&$months) {
            $k = substr(MZC_Crm::jdate($at), 0, 7);
            if (!isset($months[$k])) { $months[$k] = array('month' => $k, 'jobs' => 0, 'jobs_total' => 0, 'jobs_profit' => 0, 'builds' => 0, 'builds_sell' => 0, 'builds_profit' => 0); }
            return $k;
        };
        foreach ($jobs as $r) {
            $total = (int) $r['labor_price'] + (int) $r['paid'] - (int) $r['discount'] + (int) $r['sell'];
            $profit = (int) $r['labor_price'] - (int) $r['discount'] + (int) $r['sell'] - (int) $r['buy'];
            $J['count']++; $J['by_status'][$r['status']] = (isset($J['by_status'][$r['status']]) ? $J['by_status'][$r['status']] : 0) + 1;
            $J['labor'] += (int) $r['labor_price']; $J['paid_out'] += (int) $r['paid']; $J['discount'] += (int) $r['discount']; $J['parts_sell'] += (int) $r['sell']; $J['parts_buy'] += (int) $r['buy'];
            $J['total'] += $total; $J['received'] += (int) $r['got']; $J['profit'] += $profit; $J['missing_cost'] += (int) $r['nocost'];
            $k = $month($r['at']); $months[$k]['jobs']++; $months[$k]['jobs_total'] += $total; $months[$k]['jobs_profit'] += $profit;
        }
        foreach ($builds as $r) {
            $B['count']++; $B['by_status'][$r['status']] = (isset($B['by_status'][$r['status']]) ? $B['by_status'][$r['status']] : 0) + 1;
            $B['sell'] += (int) $r['sell']; $B['buy'] += (int) $r['buy']; $B['profit'] += (int) $r['sell'] - (int) $r['buy']; $B['missing_cost'] += (int) $r['nocost']; $B['missing_price'] += (int) $r['noprice'];
            $k = $month($r['at']); $months[$k]['builds']++; $months[$k]['builds_sell'] += (int) $r['sell']; $months[$k]['builds_profit'] += (int) $r['sell'] - (int) $r['buy'];
        }
        ksort($months);

        // the parts that sold most in the period (computers and services together), by revenue
        $top = $db->get_results($db->prepare("SELECT name, SUM(qty) AS qty, SUM(rev) AS revenue FROM (
                SELECT p.model AS name, 1 AS qty, COALESCE(p.sell_price, 0) AS rev FROM `$bp` p JOIN `" . self::t('builds') . "` b ON b.id = p.build_id WHERE COALESCE(b.sold_at, DATE(b.created)) BETWEEN %s AND %s
                UNION ALL SELECT p.name, p.qty, p.qty * p.unit_price FROM `$jp` p JOIN `" . self::t('jobs') . "` j ON j.id = p.job_id WHERE p.kind <> 'in' AND COALESCE(j.received_at, DATE(j.created)) BETWEEN %s AND %s
            ) x GROUP BY name ORDER BY revenue DESC, qty DESC LIMIT 10", $from, $to, $from, $to), ARRAY_A) ?: array();
        foreach ($top as &$x) { $x['qty'] = (int) $x['qty']; $x['revenue'] = (int) $x['revenue']; } unset($x);

        return array('from' => $from, 'to' => $to, 'from_j' => MZC_Crm::jdate($from), 'to_j' => MZC_Crm::jdate($to), 'jobs' => $J, 'builds' => $B, 'months' => array_values($months), 'top' => $top);
    }

    /** $r with only what a person may see: counts per area they can view; money with 'payments'; buying costs and profit with the cost right as well. */
    public static function visible($r, $jobs, $builds, $money, $cost)
    {
        $keep = function ($x, $moneyKeys, $costKeys) use ($money, $cost) {
            if (!$money) { foreach ($moneyKeys as $k) { unset($x[$k]); } }
            if (!$cost) { foreach ($costKeys as $k) { unset($x[$k]); } }
            return $x;
        };
        $r['jobs'] = $jobs ? $keep($r['jobs'], array('labor', 'paid_out', 'discount', 'parts_sell', 'total', 'received', 'profit', 'parts_buy', 'missing_cost'), array('parts_buy', 'profit', 'missing_cost')) : null;
        $r['builds'] = $builds ? $keep($r['builds'], array('sell', 'buy', 'profit', 'missing_cost', 'missing_price'), array('buy', 'profit', 'missing_cost')) : null;
        foreach ($r['months'] as &$m) {
            if (!$jobs) { unset($m['jobs'], $m['jobs_total'], $m['jobs_profit']); } if (!$builds) { unset($m['builds'], $m['builds_sell'], $m['builds_profit']); }
            $m = $keep($m, array('jobs_total', 'jobs_profit', 'builds_sell', 'builds_profit'), array('jobs_profit', 'builds_profit'));
        } unset($m);
        if (!$jobs || !$builds) { $r['top'] = array(); }   // the best sellers mix both areas
        if (!$money) { $r['top'] = array_map(function ($x) { unset($x['revenue']); return $x; }, $r['top']); }
        return $r;
    }
}
