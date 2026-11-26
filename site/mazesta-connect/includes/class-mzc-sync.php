<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The CRM database's connection. Two things differ from a plain wpdb: it never stops the request with an error page (a wrong password leaves `ready` false, so one connection does
 * for the test and the work), and every row it inserts, changes or deletes in a synced table is noted for the staff programs (MZC_Sync), whichever page or API wrote it.
 * A write made with raw SQL through query() is not seen: note it with MZC_Sync::mark().
 */
final class MZC_Db extends wpdb
{
    public function db_connect($allow_bail = true) { return parent::db_connect(false); }
    public function check_connection($allow_bail = true) { return parent::check_connection(false); }
    public function bail($message, $error_code = '500') { return false; }

    public function insert($table, $data, $format = null)
    {
        $r = parent::insert($table, $data, $format);
        if ($r) { MZC_Sync::wrote($this, $table, array($data + array('id' => $this->insert_id))); }
        return $r;
    }

    public function replace($table, $data, $format = null)
    {
        $r = parent::replace($table, $data, $format);
        if ($r) { MZC_Sync::wrote($this, $table, array($data + array('id' => $this->insert_id))); }
        return $r;
    }

    public function update($table, $data, $where, $format = null, $where_format = null)
    {
        $was = MZC_Sync::rows($this, $table, $where);
        $r = parent::update($table, $data, $where, $format, $where_format);
        if ($r) { MZC_Sync::wrote($this, $table, array_merge($was, array_map(function ($x) use ($data) { return $data + $x; }, $was))); }   // before and after: a line moved to another product marks both
        // a product's new selling price goes into the ready-made invoices that hold it, whichever page or program changed it
        if ($r && array_key_exists('sell_price', $data) && $table === MZC_Crm_Db::t('products')) { MZC_Acc::reprice_templates(array_column($was, 'id')); }
        return $r;
    }

    public function delete($table, $where, $where_format = null)
    {
        $was = MZC_Sync::rows($this, $table, $where);
        $r = parent::delete($table, $where, $where_format);
        if ($r) { MZC_Sync::wrote($this, $table, $was); }
        return $r;
    }
}

/**
 * What the staff programs need to keep their own copy of the CRM current without asking for it again and again.
 * Every write notes (kind, id) in the `changes` table under a rising number; the newest number is also written to a small static file, which a program reads every minute without
 * WordPress being started at all. When the number moved, GET staff/sync gives only the records changed since the number the program has, each as that person may see it.
 * A program with no copy yet gets everything the person may see, a page at a time, through the same route. The cursor it sends back is opaque to it.
 */
final class MZC_Sync
{
    const PAGE = 150;          // records per answer
    const SETTLE = 15;         // seconds: a change younger than this is sent again next time, so one committed late under a smaller number is not skipped
    const KEEP_DAYS = 90;      // older changes are dropped; a program further behind than that takes the whole copy again
    const SHAPE = 1;           // raise when what a synced record holds changes (a new field): every program takes its copy again after the plugin is updated

    /** table => the (kind, column) pairs a row of it changes */
    private static $map = array(
        'customers' => array(array('customers', 'id')), 'vendors' => array(array('vendors', 'id')), 'products' => array(array('products', 'id')),
        'builds' => array(array('builds', 'id')), 'build_parts' => array(array('builds', 'build_id'), array('products', 'product_id')),
        'jobs' => array(array('jobs', 'id')), 'job_parts' => array(array('jobs', 'job_id'), array('products', 'product_id')), 'job_pays' => array(array('jobs', 'job_id')),
        'job_calls' => array(array('jobs', 'job_id'), array('builds', 'build_id')),
        'purchases' => array(array('purchases', 'id')), 'purchase_items' => array(array('purchases', 'purchase_id'), array('products', 'product_id')),
        'invoices' => array(array('invoices', 'id')), 'invoice_items' => array(array('invoices', 'invoice_id'), array('products', 'product_id')),
        'payments' => array(array('payments', 'id'), array('invoices', 'invoice_id')), 'cheques' => array(array('cheques', 'id')),
        'shipments' => array(array('ships', 'id')), 'shortages' => array(array('shortages', 'id')),
        'tasks' => array(array('tasks', 'id')), 'task_log' => array(array('tasks', 'task_id')), 'flows' => array(array('flows', 'id')),
    );
    /** kind => the table its records live in, in the order a first copy takes them */
    private static $kinds = array('customers' => 'customers', 'vendors' => 'vendors', 'products' => 'products', 'builds' => 'builds', 'jobs' => 'jobs', 'purchases' => 'purchases', 'invoices' => 'invoices',
        'payments' => 'payments', 'cheques' => 'cheques', 'ships' => 'shipments', 'shortages' => 'shortages', 'tasks' => 'tasks', 'flows' => 'flows');
    private static $dirty = false;

    public static function boot()
    {
        add_action('rest_api_init', function () { register_rest_route(Mazesta_Connect::NS, '/staff/sync', array('methods' => 'GET', 'callback' => array(__CLASS__, 'rest_sync'), 'permission_callback' => '__return_true')); });
        add_filter('rest_pre_dispatch', array(__CLASS__, 'unzip'), 5, 3);
    }

    /** A staff program sends a large body gzipped (X-MZC-Gzip: 1, as plain bytes): opened here, before the route reads it. At most 4 MB comes out. */
    public static function unzip($res, $server, $req)
    {
        if ($res !== null || !$req->get_header('x_mzc_gzip') || strpos($req->get_route(), '/' . Mazesta_Connect::NS . '/staff/') !== 0) { return $res; }
        $raw = (string) $req->get_body();
        $text = strlen($raw) <= 1048576 && function_exists('gzdecode') ? @gzdecode($raw, 4194304) : false;
        if ($text === false) { return MZC_Api::fail('درخواست فشرده باز نشد.', 415); }
        $req->set_header('content_type', 'application/json'); $req->set_body($text);
        return $res;
    }

    private static function table($name) { $p = MZC_Crm_Db::prefix(); return strncmp($name, $p, strlen($p)) === 0 ? substr($name, strlen($p)) : ''; }
    private static function sql($db, $q) { $r = mysqli_query($db->dbh, $q); if (!($r instanceof mysqli_result)) { return array(); } $out = array(); while ($x = mysqli_fetch_assoc($r)) { $out[] = $x; } mysqli_free_result($r); return $out; }
    private static function val($db, $v) { return $v === null ? null : "'" . mysqli_real_escape_string($db->dbh, (string) $v) . "'"; }

    /** The key columns of the rows a coming update or delete will touch (read with mysqli directly, so wpdb's own last result and insert id are left alone). */
    public static function rows($db, $table, $where)
    {
        $t = self::table($table);
        if (!$db->dbh || (!isset(self::$map[$t]) && $t !== 'photos')) { return array(); }
        $cols = $t === 'photos' ? array('entity', 'entity_id') : array_unique(array_merge(array('id'), array_column(self::$map[$t], 1)));
        if ($t === 'job_calls') { $cols = array('id', 'job_id'); if (self::sql($db, "SHOW COLUMNS FROM `$table` LIKE 'build_id'")) { $cols[] = 'build_id'; } }
        $cond = array();
        foreach ((array) $where as $k => $v) { if (!preg_match('/^[a-z0-9_]+$/i', $k)) { return array(); } $x = self::val($db, $v); $cond[] = $x === null ? "`$k` IS NULL" : "`$k` = $x"; }
        return $cond ? self::sql($db, 'SELECT `' . implode('`, `', $cols) . "` FROM `$table` WHERE " . implode(' AND ', $cond) . ' LIMIT 2000') : array();
    }

    /** Notes what the rows of a table changed. */
    public static function wrote($db, $table, $rows)
    {
        $t = self::table($table); if (!$db->dbh || (!isset(self::$map[$t]) && $t !== 'photos')) { return; }
        $mark = array();
        foreach ($rows as $r) {
            if ($t === 'photos') { if (!empty($r['entity_id']) && isset($r['entity'])) { $mark[$r['entity'] === 'job' ? 'jobs' : 'builds'][(int) $r['entity_id']] = true; } continue; }
            foreach (self::$map[$t] as $m) { if (!empty($r[$m[1]])) { $mark[$m[0]][(int) $r[$m[1]]] = true; } }
        }
        $in = function ($k) use (&$mark) { return empty($mark[$k]) ? '' : implode(',', array_map('intval', array_keys($mark[$k]))); };
        // what follows from a record: a final or voided invoice moves its products' stock and shows on its payments; a cheque's state changes what was paid
        if ($t === 'cheques' && ($ids = $in('cheques'))) { foreach (self::sql($db, 'SELECT id, invoice_id FROM `' . MZC_Crm_Db::t('payments') . "` WHERE cheque_id IN ($ids)") as $x) { $mark['payments'][(int) $x['id']] = true; if ($x['invoice_id']) { $mark['invoices'][(int) $x['invoice_id']] = true; } } }
        if ($t === 'invoices' && ($ids = $in('invoices'))) {
            foreach (self::sql($db, 'SELECT DISTINCT product_id FROM `' . MZC_Crm_Db::t('invoice_items') . "` WHERE invoice_id IN ($ids) AND product_id IS NOT NULL") as $x) { $mark['products'][(int) $x['product_id']] = true; }
            foreach (self::sql($db, 'SELECT id FROM `' . MZC_Crm_Db::t('payments') . "` WHERE invoice_id IN ($ids)") as $x) { $mark['payments'][(int) $x['id']] = true; }
        }
        foreach ($mark as $kind => $ids) { self::note($db, $kind, array_keys($ids)); }
    }

    private static function note($db, $kind, $ids)
    {
        if (!$ids || !isset(self::$kinds[$kind])) { return; }
        $now = time(); $vals = array(); foreach ($ids as $id) { $vals[] = "('$kind', " . (int) $id . ", $now)"; }
        mysqli_query($db->dbh, 'INSERT INTO `' . MZC_Crm_Db::t('changes') . '` (kind, rid, at) VALUES ' . implode(',', $vals));
        if (!self::$dirty) { self::$dirty = true; register_shutdown_function(array(__CLASS__, 'flush')); }
    }

    /** For a write made with raw SQL. */
    public static function mark($kind, $ids) { $db = MZC_Crm_Db::db(); if ($db) { self::note($db, $kind, array_filter(array_map('intval', (array) $ids))); } }

    /* ---------- the number and its static file ---------- */

    /** {epoch, name}: the copy's generation (a wiped or restored database starts a new one, so every program takes it again) and the flag file's unguessable name. */
    private static function meta($new = false)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return null; }
        $tb = MZC_Crm_Db::t('meta'); $m = json_decode((string) $db->get_var("SELECT v FROM `$tb` WHERE k = 'sync'"), true);
        if (!is_array($m) || empty($m['epoch']) || empty($m['name']) || $new) {
            $m = array('epoch' => bin2hex(random_bytes(4)), 'name' => is_array($m) && !empty($m['name']) ? $m['name'] : bin2hex(random_bytes(16)));
            $db->query($db->prepare("REPLACE INTO `$tb` (k, v) VALUES ('sync', %s)", wp_json_encode($m)));
        }
        return $m;
    }

    public static function head() { $db = MZC_Crm_Db::db(); return $db ? (int) $db->get_var('SELECT MAX(seq) FROM `' . MZC_Crm_Db::t('changes') . '`') : 0; }

    /** Every program takes the whole copy again (after the database was emptied, imported over or restored). */
    public static function reset() { self::meta(true); self::flush(); }

    /** The flag file's address, written when it is missing or behind. '' when the uploads folder cannot be written (the programs then ask the route itself). */
    public static function flush()
    {
        $m = self::meta(); if (!$m) { return ''; }
        $up = wp_upload_dir(null, false); if (!empty($up['error'])) { return ''; }
        $dir = $up['basedir'] . '/mazesta-sync'; if (!is_dir($dir) && !wp_mkdir_p($dir)) { return ''; }
        if (!is_file($dir . '/.htaccess')) { @file_put_contents($dir . '/.htaccess', "<IfModule mod_headers.c>\nHeader set Cache-Control \"no-store, max-age=0\"\n</IfModule>\nOptions -Indexes\n"); @file_put_contents($dir . '/index.php', "<?php // nothing here\n"); }
        $file = $dir . '/' . $m['name'] . '.json'; $text = wp_json_encode(array('seq' => self::head(), 'epoch' => $m['epoch']));
        if (!is_file($file) || file_get_contents($file) !== $text) { $tmp = $file . '.' . bin2hex(random_bytes(3)) . '.tmp'; if (@file_put_contents($tmp, $text) === false || !@rename($tmp, $file)) { @unlink($tmp); return ''; } }
        return $up['baseurl'] . '/mazesta-sync/' . $m['name'] . '.json';
    }

    /* ---------- what a person's copy holds ---------- */

    /** kind => a mark of what this person sees in it; when the mark changes (a role was changed) that kind is taken again. A kind left out is not theirs. */
    private static function visible($a)
    {
        $u = $a['id']; $can = function ($area) use ($u) { return MZC_Staff::can($u, $area, 1); };
        $money = MZC_Api::money_seen($a) ? 'm' : ''; $cost = MZC_Api::cost_seen($a) ? 'c' : ''; $v = array();
        if ($can('customers')) { $v['customers'] = '1'; }
        if ($can('stock') || $can('ships') || $can('accounting')) { $v['vendors'] = '1'; }
        if ($can('stock') || MZC_Staff::can($u, 'builds', 2) || MZC_Staff::can($u, 'jobs', 2) || $can('accounting')) { $v['products'] = '1' . $cost; }
        if ($can('builds')) { $v['builds'] = '1' . $money . $cost; }
        if ($can('jobs')) { $v['jobs'] = '1' . $money . $cost; }
        if ($can('stock')) { $v['purchases'] = '1' . $cost; }
        if ($can('accounting')) { $v['invoices'] = '1' . $cost; $v['payments'] = '1'; $v['cheques'] = '1'; }
        if ($can('ships')) { $v['ships'] = '1'; }
        $v['shortages'] = '1';
        $rec = MZC_Staff::record($u);   // which tasks are a person's follows from their roles
        $v['tasks'] = '1' . (MZC_Tasks::manager($u) ? 'a' : '') . implode(',', $rec['roles']); $v['flows'] = '1';
        return array_map(function ($x) { return self::SHAPE . ':' . $x; }, $v);
    }

    /** One record as this person's copy keeps it (what the record's own route answers, with the names the lists show), or null when it is gone or not theirs. */
    private static function record($kind, $id, $a)
    {
        $with = function ($o, $cid) { if ($cid && ($c = MZC_Crm::customer($cid))) { $o['customer_name'] = $c['name']; $o['customer_mobile'] = (string) $c['mobile']; } return $o; };
        $db = MZC_Crm_Db::db();
        switch ($kind) {
            case 'customers': $c = MZC_Crm::customer($id); return $c ? MZC_Api::customer_out($c) : null;
            case 'vendors': $v = $db->get_row($db->prepare('SELECT * FROM `' . MZC_Crm_Db::t('vendors') . '` WHERE id = %d', $id), ARRAY_A);
                return $v ? array('id' => (int) $v['id'], 'name' => $v['name'], 'kind' => $v['kind'], 'kindText' => isset(MZC_Ships::$kinds[$v['kind']]) ? MZC_Ships::$kinds[$v['kind']] : '', 'phone' => (string) $v['phone'], 'notes' => (string) $v['notes']) : null;
            case 'products': $p = MZC_Stock::product($id); return $p ? MZC_Api::product_out($p, $a) : null;
            case 'builds': $b = MZC_Crm::build($id); return $b ? $with(MZC_Api::build_out($b, true, $a), $b['customer_id']) : null;
            case 'jobs': $j = MZC_Crm::job($id); if (!$j) { return null; } $money = MZC_Api::money_seen($a); $o = MZC_Api::job_out($j, true, $money, MZC_Api::cost_seen($a)); if ($money) { $o['total'] = (int) $j['total']; } return $with($o, $j['customer_id']);
            case 'purchases': $u = MZC_Stock::purchase($id); if (!$u) { return null; } $u['line_count'] = count($u['items']); return MZC_Api::purchase_out($u, $a, true);
            case 'invoices': $v = MZC_Acc::invoice($id); if (!$v) { return null; } $v['line_count'] = count($v['items']); return MZC_Acc_Api::invoice_out($v, $a, true);
            case 'payments': $y = MZC_Acc::payment($id); if (!$y) { return null; }
                if ($y['invoice_id']) { $y['invoice_no'] = $db->get_var($db->prepare('SELECT no FROM `' . MZC_Crm_Db::t('invoices') . '` WHERE id = %d', $y['invoice_id'])); }
                if ($y['vendor_id']) { $y['vendor_name'] = $db->get_var($db->prepare('SELECT name FROM `' . MZC_Crm_Db::t('vendors') . '` WHERE id = %d', $y['vendor_id'])); }
                return $with(MZC_Acc_Api::payment_out($y), $y['customer_id']);
            case 'cheques': $q = MZC_Acc::cheque($id); return $q ? MZC_Acc_Api::cheque_out($q) : null;
            case 'ships': $s = MZC_Ships::ship($id); return $s ? MZC_Desk_Api::ship_out($s) : null;
            case 'shortages': $x = MZC_Desk_Api::shortage($id); return $x ? MZC_Desk_Api::short_out($x) : null;
            case 'tasks': return MZC_Tasks::record($id, $a);
            case 'flows': return MZC_Tasks::flow_record($id);
        }
        return null;
    }

    private static function pack($s) { return rtrim(strtr(base64_encode(wp_json_encode($s)), '+/', '-_'), '='); }
    private static function unpack($c) { $s = $c === '' ? null : json_decode((string) base64_decode(strtr($c, '-_', '+/')), true); return is_array($s) && isset($s['e'], $s['s'], $s['u'], $s['v'], $s['p'], $s['a']) && is_array($s['v']) && is_array($s['p']) ? $s : null; }

    /**
     * ?c=<cursor of the last answer, empty the first time>. Answers {cursor, more, reset, drop: [kinds to empty], up: {kind: [records]}, del: {kind: [ids]}, flag: the static file}.
     * `reset`: throw the whole copy away first. Asked again while `more`.
     */
    public static function rest_sync($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $db = MZC_Crm_Db::db(); $m = $db ? self::meta() : null; if (!$m) { return MZC_Api::fail(MZC_Crm_Db::error(), 503); }
        @set_time_limit(120);
        $ch = MZC_Crm_Db::t('changes'); $head = self::head(); $want = self::visible($a);
        $s = self::unpack((string) $req->get_param('c'));
        if ($s !== null && ($s['e'] !== $m['epoch'] || (int) $s['u'] !== (int) $a['id'])) { $s = null; }
        if ($s !== null) {
            $oldest = (int) $db->get_var("SELECT MIN(seq) FROM `$ch`");
            if ((int) $s['s'] > $head || ($oldest && (int) $s['s'] < $oldest - 1)) { $s = null; }   // further back than the kept changes, or a database restored to an earlier time
        }
        $reset = $s === null;
        if ($reset) { $s = array('e' => $m['epoch'], 's' => $head, 'u' => (int) $a['id'], 'v' => array(), 'p' => array(), 'a' => 0); }
        $drop = array();
        foreach (array_keys($s['v']) as $k) {
            if (isset($want[$k])) { continue; }
            $drop[] = $k; unset($s['v'][$k]); if (isset($s['p'][0]) && $s['p'][0] === $k) { $s['a'] = 0; } $s['p'] = array_values(array_diff($s['p'], array($k)));
        }
        foreach ($want as $k => $sig) {
            if (isset($s['v'][$k]) && $s['v'][$k] === $sig) { continue; }
            if (isset($s['v'][$k])) { $drop[] = $k; }
            $s['v'][$k] = $sig; if (isset($s['p'][0]) && $s['p'][0] === $k) { $s['a'] = 0; } elseif (!in_array($k, $s['p'], true)) { $s['p'][] = $k; }
        }
        $up = array(); $del = array(); $n = 0;
        if ($s['p']) {   // the first copy (or a kind taken again): by id, kind after kind; what changes meanwhile comes with the numbers afterwards
            while ($s['p'] && $n < self::PAGE) {
                $k = $s['p'][0]; if (!isset(self::$kinds[$k])) { array_shift($s['p']); $s['a'] = 0; continue; }
                $room = self::PAGE - $n;
                $ids = $db->get_col($db->prepare('SELECT id FROM `' . MZC_Crm_Db::t(self::$kinds[$k]) . '` WHERE id > %d ORDER BY id LIMIT %d', (int) $s['a'], $room));
                foreach ($ids as $id) { if ($r = self::record($k, (int) $id, $a)) { $up[$k][] = $r; } $s['a'] = (int) $id; $n++; }
                if (count($ids) < $room) { array_shift($s['p']); $s['a'] = 0; }
            }
            $more = (bool) $s['p'] || $head > (int) $s['s'];
        } else {
            $rows = $db->get_results($db->prepare("SELECT kind, rid, MAX(seq) AS n FROM `$ch` WHERE seq > %d GROUP BY kind, rid ORDER BY n LIMIT %d", (int) $s['s'], self::PAGE), ARRAY_A) ?: array();
            $last = (int) $s['s'];
            foreach ($rows as $x) {
                $k = $x['kind']; $last = (int) $x['n'];
                if (!isset($want[$k])) { continue; }
                if ($r = self::record($k, (int) $x['rid'], $a)) { $up[$k][] = $r; } else { $del[$k][] = (int) $x['rid']; }
            }
            if (count($rows) < self::PAGE) { $last = max($last, $head); }
            $young = (int) $db->get_var($db->prepare("SELECT MIN(seq) FROM `$ch` WHERE seq > %d AND at > %d", (int) $s['s'], time() - self::SETTLE));
            $next = $young ? min($last, $young - 1) : $last;
            $more = count($rows) === self::PAGE && $next > (int) $s['s'];
            $s['s'] = max((int) $s['s'], $next);
            if (mt_rand(1, 500) === 1) { $db->query($db->prepare("DELETE FROM `$ch` WHERE at < %d AND seq < %d LIMIT 20000", time() - self::KEEP_DAYS * 86400, $head)); }
        }
        return MZC_Api::ok(array('cursor' => self::pack($s), 'more' => $more, 'reset' => $reset, 'drop' => array_values(array_unique($drop)), 'up' => (object) $up, 'del' => (object) $del, 'kinds' => array_keys($s['v']),
            'seq' => (int) $s['s'], 'head' => $head, 'epoch' => $m['epoch'], 'flag' => self::flush()));
    }
}
