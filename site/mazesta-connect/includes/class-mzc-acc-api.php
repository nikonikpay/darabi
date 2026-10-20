<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The accounting part of the staff API (the staff program and, through MZC_Mcp, the AI apps): invoices, proformas and templates, payments, cheques, the accounts of a customer
 * or a supplier, and the money of a period. Reading needs the "accounting" area at view level, writing at edit level - except that whoever handles payments may see a
 * customer's account and take a customer's deposit. Buy prices (profit) follow the same rule as everywhere else (MZC_Api::cost_seen / cost_edit).
 */
final class MZC_Acc_Api
{
    public static function boot() { add_action('rest_api_init', array(__CLASS__, 'routes')); }

    public static function routes()
    {
        $r = array(
            array('/staff/invoices', 'GET', 'invoices'), array('/staff/invoices/(?P<id>\d+)', 'GET', 'invoice'), array('/staff/invoices', 'POST', 'invoice_save'),
            array('/staff/invoices/finalize', 'POST', 'finalize'), array('/staff/invoices/void', 'POST', 'void'), array('/staff/invoices/copy', 'POST', 'copy'), array('/staff/invoices/delete', 'POST', 'delete'), array('/staff/invoices/from-build', 'POST', 'from_build'),
            array('/staff/payments', 'GET', 'payments'), array('/staff/payments', 'POST', 'payment_save'), array('/staff/payments/delete', 'POST', 'payment_delete'),
            array('/staff/cheques', 'GET', 'cheques'), array('/staff/cheques', 'POST', 'cheque_save'),
            array('/staff/customers/(?P<id>\d+)/account', 'GET', 'customer_account'), array('/staff/suppliers/(?P<id>\d+)/account', 'GET', 'supplier_account'),
            array('/staff/acc/summary', 'GET', 'summary'), array('/staff/acc/due', 'GET', 'due'),
        );
        foreach ($r as $x) { register_rest_route(Mazesta_Connect::NS, $x[0], array('methods' => $x[1], 'callback' => array(__CLASS__, 'rest_' . $x[2]), 'permission_callback' => '__return_true')); }
    }

    private static function auth($req, $need) { return MZC_Staff::auth($req, 'accounting', $need); }
    private static function fail($m, $st = 400, $more = array()) { return MZC_Api::fail($m, $st, $more); }
    private static function jd($d) { return MZC_Api::jd($d); }
    private static function filters($req, $keys)
    {
        $f = array();
        foreach ($keys as $k) { $v = $req->get_param($k); if ($v !== null && $v !== '' && is_scalar($v)) { $f[$k] = in_array($k, array('from', 'to'), true) ? MZC_Crm::gdate((string) $v) : (string) $v; } }
        return $f;
    }

    /** What the program needs to draw the accounting screens (merged into staff/config). */
    public static function config($a)
    {
        $s = MZC_Acc::settings();
        return array('acc' => array('kinds' => MZC_Acc::$kinds, 'statuses' => MZC_Acc::$statuses, 'methods' => MZC_Acc::$methods, 'chequeStatuses' => MZC_Acc::$cheque_statuses, 'expenses' => MZC_Acc::expense_heads(),
            'tax' => (float) $s['tax'], 'remind' => (int) $s['remind'], 'shop' => $s['shop'], 'phone' => $s['phone'], 'address' => $s['address'], 'foot' => $s['foot'], 'woo' => MZC_Woo::active() && !empty(MZC_Woo::settings()['on'])));
    }

    /* ---------- how records go out ---------- */

    public static function invoice_out($v, $a, $full)
    {
        $cost = MZC_Api::cost_seen($a);
        $o = array('id' => (int) $v['id'], 'kind' => $v['kind'], 'kindText' => MZC_Acc::$kinds[$v['kind']], 'no' => $v['no'] === null ? null : (int) $v['no'], 'status' => $v['status'], 'statusText' => MZC_Acc::$statuses[$v['status']],
            'customer_id' => (int) $v['customer_id'], 'customer_name' => (string) $v['customer_name'], 'customer_mobile' => (string) $v['customer_mobile'], 'title' => (string) $v['title'], 'at' => (string) $v['at'], 'at_j' => self::jd($v['at']),
            'valid_until' => (string) $v['valid_until'], 'valid_until_j' => self::jd($v['valid_until']), 'build_id' => (int) $v['build_id'], 'ref_id' => (int) $v['ref_id'], 'wc_order_id' => (int) $v['wc_order_id'],
            'subtotal' => (int) $v['subtotal'], 'discount' => (int) $v['discount'], 'tax_pct' => (float) $v['tax_pct'], 'tax' => (int) $v['tax'], 'total' => (int) $v['total'], 'paid' => (int) $v['paid'], 'remaining' => (int) $v['total'] - (int) $v['paid'],
            'void_reason' => (string) $v['void_reason'], 'updated' => $v['updated']);
        if (isset($v['line_count'])) { $o['lines'] = (int) $v['line_count']; }
        if (!$full) { return $o; }
        $o['notes'] = (string) $v['notes']; $o['customer_address'] = (string) $v['customer_address']; $o['customer_national_id'] = (string) $v['customer_national_id']; $o['items'] = array(); $o['payments'] = array();
        foreach ($v['items'] as $l) {
            $x = array('product_id' => $l['product_id'] === null ? null : (int) $l['product_id'], 'category' => $l['category'], 'name' => $l['name'], 'qty' => (int) $l['qty'], 'unit_price' => (int) $l['unit_price'], 'line_total' => (int) $l['qty'] * (int) $l['unit_price'],
                'serials' => array_values(array_filter(explode("\n", (string) $l['serials']))), 'warranty_months' => $l['warranty_months'] === null ? null : (int) $l['warranty_months']);
            if ($cost) { $x['buy_price'] = $l['buy_price'] === null ? null : (int) $l['buy_price']; }
            $o['items'][] = $x;
        }
        foreach ($v['payments'] as $p) { $o['payments'][] = self::payment_out($p); }
        return $o;
    }

    public static function payment_out($p)
    {
        return array('id' => (int) $p['id'], 'direction' => $p['direction'], 'amount' => (int) $p['amount'], 'method' => $p['method'], 'methodText' => isset(MZC_Acc::$methods[$p['method']]) ? MZC_Acc::$methods[$p['method']] : $p['method'], 'account' => (string) $p['account'],
            'at' => (string) $p['at'], 'at_j' => self::jd($p['at']), 'customer_id' => (int) $p['customer_id'], 'customer_name' => isset($p['customer_name']) ? (string) $p['customer_name'] : '', 'vendor_id' => (int) $p['vendor_id'], 'vendor_name' => isset($p['vendor_name']) ? (string) $p['vendor_name'] : '',
            'invoice_id' => (int) $p['invoice_id'], 'invoice_no' => isset($p['invoice_no']) && $p['invoice_no'] !== null ? (int) $p['invoice_no'] : null, 'purchase_id' => (int) $p['purchase_id'], 'category' => (string) $p['category'], 'tx' => (string) $p['tx'], 'notes' => (string) $p['notes'],
            'cheque' => $p['cheque_id'] ? array('id' => (int) $p['cheque_id'], 'no' => (string) $p['cheque_no'], 'status' => (string) $p['cheque_status'], 'statusText' => isset(MZC_Acc::$cheque_statuses[$p['cheque_status']]) ? MZC_Acc::$cheque_statuses[$p['cheque_status']] : '', 'due_at' => (string) $p['cheque_due'], 'due_at_j' => self::jd($p['cheque_due'])) : null,
            'updated' => $p['updated']);
    }

    public static function cheque_out($q)
    {
        $today = wp_date('Y-m-d');
        return array('id' => (int) $q['id'], 'direction' => $q['direction'], 'directionText' => $q['direction'] === 'out' ? 'پرداختنی (چک خودمان)' : 'دریافتنی', 'no' => $q['no'], 'bank' => (string) $q['bank'], 'account' => (string) $q['account'], 'amount' => (int) $q['amount'],
            'issued_at' => (string) $q['issued_at'], 'issued_at_j' => self::jd($q['issued_at']), 'due_at' => $q['due_at'], 'due_at_j' => self::jd($q['due_at']), 'days' => (int) floor((strtotime($q['due_at']) - strtotime($today)) / 86400),
            'customer_id' => (int) $q['customer_id'], 'customer_name' => (string) $q['customer_name'], 'vendor_id' => (int) $q['vendor_id'], 'vendor_name' => (string) $q['vendor_name'], 'party' => (string) $q['party'],
            'status' => $q['status'], 'statusText' => MZC_Acc::$cheque_statuses[$q['status']], 'notes' => (string) $q['notes'], 'updated' => $q['updated']);
    }

    /* ---------- invoices ---------- */

    public static function rest_invoices($req)
    {
        $a = self::auth($req, 1); if ($a instanceof WP_REST_Response) { return $a; }
        $pg = MZC_Api::page($req); $per = MZC_Api::per($req);
        list($rows, $total) = MZC_Acc::invoices(self::filters($req, array('kind', 'status', 'q', 'customer', 'from', 'to')), $pg, $per);
        return MZC_Api::ok(array('total' => $total, 'page' => $pg, 'per' => $per, 'items' => array_map(function ($v) use ($a) { return self::invoice_out($v, $a, false); }, $rows)));
    }

    public static function rest_invoice($req)
    {
        $a = self::auth($req, 1); if ($a instanceof WP_REST_Response) { return $a; }
        $v = MZC_Acc::invoice((int) $req['id']); if (!$v) { return self::fail('فاکتور پیدا نشد.', 404); }
        return MZC_Api::ok(self::invoice_out($v, $a, true));
    }

    /** The invoice of a write, after the `updated` check: array(row, null) or array(null, error response). */
    private static function current($p, $a)
    {
        $id = MZC_Api::n($p, 'id'); $v = $id ? MZC_Acc::invoice($id) : null;
        if (!$v) { return array(null, self::fail('فاکتور پیدا نشد.', 404)); }
        if (isset($p['updated']) && (string) $p['updated'] !== (string) $v['updated']) { return array(null, self::fail('این فاکتور در همین فاصله را کس دیگری عوض کرده است؛ دوباره بازش کنید.', 409, array('current' => self::invoice_out($v, $a, true)))); }
        return array($v, null);
    }

    public static function rest_invoice_save($req)
    {
        $a = self::auth($req, 2); if ($a instanceof WP_REST_Response) { return $a; }
        $p = MZC_Api::body($req); $id = MZC_Api::n($p, 'id');
        if ($id) { list(, $e) = self::current($p, $a); if ($e) { return $e; } }
        $d = array();
        foreach (array('kind', 'title', 'notes') as $k) { if (array_key_exists($k, $p)) { $d[$k] = MZC_Api::s($p, $k, $k === 'notes' ? 2000 : 190); } }
        foreach (array('at', 'valid_until') as $k) { if (array_key_exists($k, $p)) { $d[$k] = MZC_Api::d($p, $k); } }
        foreach (array('customer_id', 'discount') as $k) { if (array_key_exists($k, $p)) { $d[$k] = MZC_Api::n($p, $k); } }
        if (array_key_exists('tax_pct', $p) && is_scalar($p['tax_pct'])) { $d['tax_pct'] = (string) $p['tax_pct']; }
        if (!array_key_exists('customer_id', $p) && !empty($p['c_mobile'])) {   // a customer named by mobile (and name) is found, or made by whoever may make customers
            $m = MZC_Crm::mobile(MZC_Api::s($p, 'c_mobile', 30)); $c = $m !== '' ? MZC_Crm::customer_by_mobile($m) : null;
            if (!$c && $m !== '' && MZC_Staff::can($a['id'], 'customers', 2)) { $r = MZC_Crm::save_customer(0, MZC_Api::s($p, 'c_name', 190), $m); if (!is_int($r)) { return self::fail($r, 422); } MZC_Api::log($a, 'create', 'customer', $r, 'ثبت همراه فاکتور'); $c = array('id' => $r); }
            if (!$c) { return self::fail('این مشتری ثبت نشده است.', 404); }
            $d['customer_id'] = (int) $c['id'];
        }
        if (isset($p['items']) && is_array($p['items'])) {
            $ce = MZC_Api::cost_edit($a);
            $d['items'] = array_map(function ($x) use ($ce) { if (!is_array($x)) { return null; } if (!$ce) { unset($x['buy_price']); } return $x; }, $p['items']);
        }
        $r = MZC_Acc::save_invoice($id, $d, $a['id']);
        if (!is_int($r)) { return self::fail($r, 422); }
        $v = MZC_Acc::invoice($r);
        MZC_Api::log($a, $id ? 'update' : 'create', 'invoice', $r, $v['kind'] . ' ' . ($v['no'] ?: $v['title']));
        return MZC_Api::ok(self::invoice_out($v, $a, true), $id ? 200 : 201);
    }

    public static function rest_finalize($req)
    {
        $a = self::auth($req, 2); if ($a instanceof WP_REST_Response) { return $a; }
        $p = MZC_Api::body($req); list($v, $e) = self::current($p, $a); if ($e) { return $e; }
        $make = !empty($p['make_build']);
        if ($make && !MZC_Staff::can($a['id'], 'builds', 2)) { return self::fail('برای فرستادن سیستم به صف اسمبل دسترسی «سیستم‌های نو» لازم است.', 403); }
        $r = MZC_Acc::finalize((int) $v['id'], $make);
        if (!is_array($r)) { return self::fail($r, 422); }
        $v = MZC_Acc::invoice($r[0]);
        MZC_Api::log($a, 'final', 'invoice', $r[0], $v['kind'] . ' ' . $v['no'] . ($v['build_id'] ? ' → build ' . $v['build_id'] : ''));
        return MZC_Api::ok(self::invoice_out($v, $a, true) + array('warnings' => $r[1]));
    }

    public static function rest_void($req)
    {
        $a = self::auth($req, 2); if ($a instanceof WP_REST_Response) { return $a; }
        $p = MZC_Api::body($req); list($v, $e) = self::current($p, $a); if ($e) { return $e; }
        $r = MZC_Acc::void_invoice((int) $v['id'], MZC_Api::s($p, 'reason', 255));
        if (!is_int($r)) { return self::fail($r, 422); }
        MZC_Api::log($a, 'void', 'invoice', $r, MZC_Api::s($p, 'reason', 200));
        return MZC_Api::ok(self::invoice_out(MZC_Acc::invoice($r), $a, true));
    }

    public static function rest_copy($req)
    {
        $a = self::auth($req, 2); if ($a instanceof WP_REST_Response) { return $a; }
        $p = MZC_Api::body($req);
        $r = MZC_Acc::copy(MZC_Api::n($p, 'id'), MZC_Api::s($p, 'kind', 10), $a['id'], MZC_Api::n($p, 'customer_id'));
        if (!is_int($r)) { return self::fail($r, 422); }
        MZC_Api::log($a, 'create', 'invoice', $r, 'از ' . MZC_Api::n($p, 'id'));
        return MZC_Api::ok(self::invoice_out(MZC_Acc::invoice($r), $a, true), 201);
    }

    public static function rest_delete($req)
    {
        $a = self::auth($req, 2); if ($a instanceof WP_REST_Response) { return $a; }
        $p = MZC_Api::body($req); list($v, $e) = self::current($p, $a); if ($e) { return $e; }
        $r = MZC_Acc::delete_draft((int) $v['id']);
        if (!is_int($r)) { return self::fail($r, 422); }
        MZC_Api::log($a, 'delete', 'invoice', $r, $v['kind'] . ' ' . $v['title']);
        return MZC_Api::ok(array('deleted' => $r));
    }

    public static function rest_from_build($req)
    {
        $a = self::auth($req, 2); if ($a instanceof WP_REST_Response) { return $a; }
        if (!MZC_Staff::can($a['id'], 'builds', 1)) { return self::fail('دسترسی این کار را ندارید.', 403); }
        $r = MZC_Acc::from_build(MZC_Api::n(MZC_Api::body($req), 'build_id'), $a['id']);
        if (!is_int($r)) { return self::fail($r, 422); }
        return MZC_Api::ok(self::invoice_out(MZC_Acc::invoice($r), $a, true));
    }

    /* ---------- payments ---------- */

    /** Payments are seen with the accounting area; a customer's own payments also by whoever handles payments. */
    public static function rest_payments($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $f = self::filters($req, array('direction', 'customer', 'vendor', 'invoice', 'method', 'account', 'category', 'expense', 'from', 'to', 'q'));
        if (!MZC_Staff::can($a['id'], 'accounting', 1) && !(MZC_Staff::can($a['id'], 'payments', 1) && !empty($f['customer']))) { return self::fail('دسترسی این کار را ندارید.', 403); }
        $pg = MZC_Api::page($req); $per = MZC_Api::per($req); list($rows, $total) = MZC_Acc::payments($f, $pg, $per);
        return MZC_Api::ok(array('total' => $total, 'page' => $pg, 'per' => $per, 'items' => array_map(array(__CLASS__, 'payment_out'), $rows)));
    }

    public static function rest_payment_save($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $p = MZC_Api::body($req); $id = MZC_Api::n($p, 'id'); $old = $id ? MZC_Acc::payment($id) : null;
        if ($id && !$old) { return self::fail('پرداخت پیدا نشد.', 404); }
        $dir = isset($p['direction']) ? (string) $p['direction'] : ($old ? $old['direction'] : 'in');
        // a customer's deposit may be taken by whoever handles payments; everything else (refunds, suppliers, expenses, changing old entries) is the accountant's
        $deposit = !$old && $dir === 'in' && MZC_Api::n($p, 'customer_id') && !MZC_Api::n($p, 'vendor_id');
        if (!MZC_Staff::can($a['id'], 'accounting', 2) && !($deposit && MZC_Staff::can($a['id'], 'payments', 2))) { return self::fail('دسترسی این کار را ندارید.', 403); }
        if ($old && isset($p['updated']) && (string) $p['updated'] !== (string) $old['updated']) { return self::fail('این پرداخت در همین فاصله را کس دیگری عوض کرده است؛ دوباره بازش کنید.', 409, array('current' => self::payment_out($old))); }
        $d = array();
        foreach (array('direction' => 3, 'method' => 10, 'account' => 120, 'category' => 60, 'tx' => 80, 'notes' => 255) as $k => $max) { if (array_key_exists($k, $p)) { $d[$k] = MZC_Api::s($p, $k, $max); } }
        foreach (array('amount', 'customer_id', 'vendor_id', 'invoice_id', 'purchase_id') as $k) { if (array_key_exists($k, $p)) { $d[$k] = MZC_Api::n($p, $k); } }
        if (array_key_exists('at', $p)) { $d['at'] = MZC_Api::d($p, 'at'); }
        if (isset($p['cheque']) && is_array($p['cheque'])) { $c = $p['cheque']; $d['cheque'] = array('no' => MZC_Api::s($c, 'no', 40), 'bank' => MZC_Api::s($c, 'bank', 80), 'due_at' => MZC_Api::d($c, 'due_at')); if (!empty($c['account'])) { $d['cheque']['account'] = MZC_Api::s($c, 'account', 120); } }
        $r = MZC_Acc::save_payment($id, $d, $a['id']);
        if (!is_int($r)) { return self::fail($r, 422); }
        $y = MZC_Acc::payment($r);
        MZC_Api::log($a, $id ? 'update' : 'create', 'payment', $r, $y['direction'] . ' ' . $y['amount']);
        return MZC_Api::ok(self::payment_out($y), $id ? 200 : 201);
    }

    public static function rest_payment_delete($req)
    {
        $a = self::auth($req, 2); if ($a instanceof WP_REST_Response) { return $a; }
        $id = MZC_Api::n(MZC_Api::body($req), 'id'); $y = MZC_Acc::payment($id);
        $r = MZC_Acc::delete_payment($id); if (!is_int($r)) { return self::fail($r, 422); }
        MZC_Api::log($a, 'delete', 'payment', $id, $y['direction'] . ' ' . $y['amount'] . ' ' . $y['at']);
        return MZC_Api::ok(array('deleted' => $id));
    }

    /* ---------- cheques ---------- */

    public static function rest_cheques($req)
    {
        $a = self::auth($req, 1); if ($a instanceof WP_REST_Response) { return $a; }
        $pg = MZC_Api::page($req); $per = MZC_Api::per($req);
        list($rows, $total) = MZC_Acc::cheques(self::filters($req, array('direction', 'status', 'due', 'customer', 'vendor', 'q', 'from', 'to')), $pg, $per);
        return MZC_Api::ok(array('total' => $total, 'page' => $pg, 'per' => $per, 'items' => array_map(array(__CLASS__, 'cheque_out'), $rows)));
    }

    public static function rest_cheque_save($req)
    {
        $a = self::auth($req, 2); if ($a instanceof WP_REST_Response) { return $a; }
        $p = MZC_Api::body($req); $id = MZC_Api::n($p, 'id'); $old = $id ? MZC_Acc::cheque($id) : null;
        if ($id && !$old) { return self::fail('چک پیدا نشد.', 404); }
        if ($old && isset($p['updated']) && (string) $p['updated'] !== (string) $old['updated']) { return self::fail('این چک در همین فاصله را کس دیگری عوض کرده است؛ دوباره بازش کنید.', 409, array('current' => self::cheque_out($old))); }
        $d = array();
        foreach (array('direction' => 3, 'no' => 40, 'bank' => 80, 'account' => 120, 'party' => 190, 'status' => 10, 'notes' => 255) as $k => $max) { if (array_key_exists($k, $p)) { $d[$k] = MZC_Api::s($p, $k, $max); } }
        foreach (array('amount', 'customer_id', 'vendor_id') as $k) { if (array_key_exists($k, $p)) { $d[$k] = MZC_Api::n($p, $k); } }
        foreach (array('issued_at', 'due_at') as $k) { if (array_key_exists($k, $p)) { $d[$k] = MZC_Api::d($p, $k); } }
        $d['pay'] = !empty($p['pay']);
        $r = MZC_Acc::save_cheque($id, $d, $a['id']);
        if (!is_int($r)) { return self::fail($r, 422); }
        $q = MZC_Acc::cheque($r);
        MZC_Api::log($a, $id ? 'update' : 'create', 'cheque', $r, $q['no'] . ' ' . $q['status']);
        return MZC_Api::ok(self::cheque_out($q), $id ? 200 : 201);
    }

    /* ---------- accounts and the period ---------- */

    /** A customer's whole money story: the account (running balance), every invoice and proforma, the payments and the cheques. */
    public static function rest_customer_account($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        if (!MZC_Staff::can($a['id'], 'accounting', 1) && !MZC_Staff::can($a['id'], 'payments', 1)) { return self::fail('دسترسی این کار را ندارید.', 403); }
        $cid = (int) $req['id']; $c = MZC_Crm::customer($cid); if (!$c) { return self::fail('مشتری پیدا نشد.', 404); }
        $l = MZC_Acc::ledger($cid); list($inv) = MZC_Acc::invoices(array('customer' => $cid), 1, 200); list($pay) = MZC_Acc::payments(array('customer' => $cid), 1, 200); list($chq) = MZC_Acc::cheques(array('customer' => $cid), 1, 100);
        foreach ($l['rows'] as &$r) { $r['at_j'] = self::jd($r['at']); } unset($r);
        return MZC_Api::ok(array('customer_id' => $cid, 'balance' => $l['balance'], 'ledger' => $l['rows'], 'invoices' => array_map(function ($v) use ($a) { return self::invoice_out($v, $a, false); }, $inv),
            'payments' => array_map(array(__CLASS__, 'payment_out'), $pay), 'cheques' => array_map(array(__CLASS__, 'cheque_out'), $chq), 'site' => self::site_of($c)));
    }

    /** The customer's account on the site, when one is linked (an order made it, or the mobile matched). */
    private static function site_of($c)
    {
        if (empty($c['wp_user_id']) || !($u = get_userdata((int) $c['wp_user_id']))) { return null; }
        return array('id' => (int) $u->ID, 'login' => $u->user_login, 'name' => $u->display_name);
    }

    public static function rest_supplier_account($req)
    {
        $a = self::auth($req, 1); if ($a instanceof WP_REST_Response) { return $a; }
        $l = MZC_Acc::vendor_ledger((int) $req['id']); list($pay) = MZC_Acc::payments(array('vendor' => (int) $req['id']), 1, 200); list($chq) = MZC_Acc::cheques(array('vendor' => (int) $req['id']), 1, 100);
        foreach ($l['rows'] as &$r) { $r['at_j'] = self::jd($r['at']); } unset($r);
        return MZC_Api::ok(array('vendor_id' => (int) $req['id'], 'balance' => $l['balance'], 'ledger' => $l['rows'], 'payments' => array_map(array(__CLASS__, 'payment_out'), $pay), 'cheques' => array_map(array(__CLASS__, 'cheque_out'), $chq)));
    }

    /** ?from=&to= (Gregorian or Persian; default this Persian month) or ?period= as staff/stats. Profit only for those who see buy prices. */
    public static function rest_summary($req)
    {
        $a = self::auth($req, 1); if ($a instanceof WP_REST_Response) { return $a; }
        $per = MZC_Report::periods(); $k = (string) $req->get_param('period'); $def = isset($per[$k]) ? $per[$k] : $per['month'];
        $from = MZC_Crm::gdate((string) $req->get_param('from')) ?: $def[1]; $to = MZC_Crm::gdate((string) $req->get_param('to')) ?: $def[2];
        if ($from > $to) { list($from, $to) = array($to, $from); }
        $s = MZC_Acc::summary($from, $to); if (!$s) { return self::fail(MZC_Crm_Db::error(), 503); }
        if (!MZC_Api::cost_seen($a)) { unset($s['profit'], $s['missing_cost']); }
        $s['from_j'] = self::jd($from); $s['to_j'] = self::jd($to); $s['cheques'] = array_map(array(__CLASS__, 'cheque_out'), $s['cheques']);
        $s['periods'] = array_map(function ($x) { return array('label' => $x[0], 'from' => $x[1], 'to' => $x[2]); }, $per);
        return MZC_Api::ok($s);
    }

    /** The cheques to remind of (the home screen asks this once a sign-in). */
    public static function rest_due($req)
    {
        $a = self::auth($req, 1); if ($a instanceof WP_REST_Response) { return $a; }
        return MZC_Api::ok(array('remind' => (int) MZC_Acc::settings()['remind'], 'items' => array_map(array(__CLASS__, 'cheque_out'), MZC_Acc::due_soon())));
    }
}
