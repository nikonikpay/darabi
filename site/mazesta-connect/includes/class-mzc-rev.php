<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The saved versions of a service (job), a new computer (build) and an invoice, and who has one open for editing.
 * After every save the whole record (its row and its lines) is kept as a version with who saved it and what changed since the version before; a version can be put back
 * (an invoice only while it and the version are drafts: a final one has a number and moved the stock). The first version of a record saved before 2.10 is taken when it is
 * first opened, so the change after it is still seen.
 * Lock: whoever opens a record for editing holds it for LOCK_SECONDS, renewed by the page while it stays open; nobody else may save it meanwhile (423), and it is let go
 * when the page is left. A person holds their own lock on any device.
 */
final class MZC_Rev
{
    const LOCK_SECONDS = 120;
    /** entity => [table, its lines (table => key column), staff area] */
    public static $kinds = array(
        'build' => array('builds', array('build_parts' => 'build_id'), 'builds'),
        'job' => array('jobs', array('job_parts' => 'job_id', 'job_pays' => 'job_id'), 'jobs'),
        'invoice' => array('invoices', array('invoice_items' => 'invoice_id'), 'accounting'),
        // 2.16: what an offline desk may also change (and so keep versions of): customers, stock products, warranty shipments, shortage lines
        'customer' => array('customers', array(), 'customers'),
        'product' => array('products', array(), 'stock'),
        'ship' => array('shipments', array(), 'ships'),
        'shortage' => array('shortages', array(), 'stock'),
    );
    /** "entity:id" => what an offline save is doing: at (its own time), mode (apply: it is the latest; side: an older one, kept only in the history), stale, cur (the record as it was). */
    private static $ctx = array();
    /** Set when the save just made was an offline one that met a change by someone else: sent to the program as X-MZC-Conflict (apply or side). */
    public static $flag = '';
    /** The fields as a person reads them in a change list; others by their column name. */
    private static $names = array(
        'customer_id' => 'مشتری', 'invoice_no' => 'شمارهٔ فاکتور', 'title' => 'عنوان', 'sold_at' => 'تاریخ فروش', 'notes' => 'یادداشت', 'status' => 'وضعیت', 'device_type' => 'نوع دستگاه', 'media' => 'لینک عکس و فیلم',
        'checks' => 'چک‌لیست', 'soft' => 'برنامه‌ها', 'report_id' => 'گزارش بعد از سرویس', 'report_pre' => 'گزارش قبل از سرویس', 'diagnosis' => 'نتیجهٔ عیب‌یابی', 'device' => 'دستگاه', 'device_serial' => 'سریال دستگاه', 'received_at' => 'تاریخ پذیرش', 'closed_at' => 'تاریخ تحویل',
        'due_at' => 'تاریخ قول', 'is_mazesta' => 'خرید از مازستا', 'has_warranty' => 'گارانتی', 'complaint' => 'مشکل', 'work_done' => 'کار انجام‌شده', 'labor_price' => 'هزینهٔ خدمات', 'discount' => 'تخفیف',
        'paid' => 'هزینهٔ پرداخت‌شده', 'paid_note' => 'توضیح هزینه', 'at' => 'تاریخ', 'valid_until' => 'اعتبار تا', 'tax_pct' => 'درصد مالیات', 'total' => 'جمع کل', 'no' => 'شماره', 'void_reason' => 'دلیل ابطال',
        'build_parts' => 'قطعات', 'job_parts' => 'قطعات', 'job_pays' => 'پرداخت‌ها', 'invoice_items' => 'ردیف‌ها',
        'name' => 'نام', 'mobile' => 'موبایل', 'mobile2' => 'موبایل دوم', 'mobiles' => 'موبایل‌های دیگر', 'phone' => 'تلفن', 'national_id' => 'کد ملی', 'email' => 'ایمیل', 'address' => 'نشانی', 'grp' => 'گروه',
        'category' => 'نوع کالا', 'brand' => 'برند', 'sku' => 'کد کالا', 'buy_price' => 'قیمت خرید', 'sell_price' => 'قیمت فروش', 'warranty_months' => 'گارانتی (ماه)',
        'part_name' => 'قطعه', 'serial' => 'سریال', 'problem' => 'خرابی', 'vendor_id' => 'گارانتی / فروشنده', 'sent_at' => 'تاریخ ارسال', 'tracking' => 'شمارهٔ ارسال', 'vendor_reply' => 'جواب شرکت', 'fix_notes' => 'کار انجام‌شده',
        'back_part' => 'قطعهٔ برگشتی', 'back_serial' => 'سریال برگشتی', 'shelf' => 'قفسه', 'picked_up' => 'تحویل شد', 'owner' => 'مالکیت', 'ref' => 'مربوط به', 'customer_reply' => 'جواب به مشتری', 'qty' => 'تعداد', 'kind' => 'نوع', 'note' => 'یادداشت',
    );
    /** Columns that change on every save or are the record's identity: not a change, and never put back. */
    private static $fixed = array('id', 'updated', 'created', 'service_no', 'subtotal', 'tax', 'delivered', 'service_done', 'kind', 'created_by', 'wc_order_id', 'wc_id', 'wp_user_id', 'old_id', 'asked_by', 'who', 'total');

    public static function boot()
    {
        add_action('rest_api_init', array(__CLASS__, 'routes'));
        // an offline save that met someone else's change tells the program so (header), once
        add_filter('rest_post_dispatch', function ($res) { if (self::$flag !== '' && $res instanceof WP_REST_Response) { $res->header('X-MZC-Conflict', self::$flag); } self::$flag = ''; return $res; });
        add_action('wp_ajax_mzc_lock', array(__CLASS__, 'ajax_lock'));
        add_action('admin_post_mzc_rev_restore', array(__CLASS__, 'act_restore'));
    }

    private static function t($n) { return MZC_Crm_Db::t($n); }
    private static function now() { return gmdate('Y-m-d H:i:s'); }
    public static function who($uid) { $u = $uid ? get_userdata($uid) : null; return $u ? mb_substr($u->display_name !== '' ? $u->display_name : $u->user_login, 0, 120) : 'سیستم'; }

    /** The record as it is now: its row and its lines (without their own ids). Null when there is none. */
    public static function snap($e, $id)
    {
        $db = MZC_Crm_Db::db(); if (!$db || !isset(self::$kinds[$e])) { return null; }
        list($tb, $lines) = self::$kinds[$e];
        $row = $db->get_row($db->prepare('SELECT * FROM `' . self::t($tb) . '` WHERE id = %d', (int) $id), ARRAY_A);
        if (!$row) { return null; }
        $out = array('row' => $row);
        foreach ($lines as $lt => $key) {
            $out[$lt] = array_map(function ($x) use ($key) { unset($x['id'], $x[$key]); return $x; }, (array) $db->get_results($db->prepare('SELECT * FROM `' . self::t($lt) . "` WHERE `$key` = %d ORDER BY id", (int) $id), ARRAY_A));
        }
        return $out;
    }

    /** What changed from $a to $b, as [field name, before, after] (lines: how many rows, and the names added and taken out). */
    public static function diff($a, $b)
    {
        $ch = array();
        foreach ($b['row'] as $k => $v) {
            if (in_array($k, self::$fixed, true)) { continue; }
            $was = $a && array_key_exists($k, $a['row']) ? $a['row'][$k] : null;
            if ((string) $was !== (string) $v) { $ch[] = array(isset(self::$names[$k]) ? self::$names[$k] : $k, self::short($was), self::short($v)); }
        }
        foreach ($b as $lt => $rows) {
            if ($lt === 'row') { continue; }
            $old = $a && isset($a[$lt]) ? $a[$lt] : array();
            if (wp_json_encode($old) === wp_json_encode($rows)) { continue; }
            $label = function ($x) { foreach (array('model', 'name', 'amount') as $k) { if (isset($x[$k]) && $x[$k] !== '') { return (string) $x[$k]; } } return '?'; };
            $o = array_map($label, $old); $n = array_map($label, $rows);
            $add = array_values(array_diff($n, $o)); $del = array_values(array_diff($o, $n));
            $ch[] = array(isset(self::$names[$lt]) ? self::$names[$lt] : $lt, count($old) . ' ردیف' . ($del ? ' (برداشته: ' . self::short(implode('، ', $del)) . ')' : ''), count($rows) . ' ردیف' . ($add ? ' (افزوده: ' . self::short(implode('، ', $add)) . ')' : '') . (!$add && !$del ? ' (جزئیات ردیف‌ها)' : ''));
        }
        return $ch;
    }
    private static function short($v) { $v = $v === null ? '—' : trim((string) $v); return $v === '' ? '—' : (mb_strlen($v) > 120 ? mb_substr($v, 0, 117) . '…' : $v); }

    /** The last version that is the record's state (a side version, kept only in the history, is not). */
    private static function last($e, $id)
    {
        $db = MZC_Crm_Db::db();
        $r = $db ? $db->get_row($db->prepare('SELECT id, snap FROM `' . self::t('revisions') . '` WHERE entity = %s AND entity_id = %d AND applied = 1 ORDER BY id DESC LIMIT 1', $e, (int) $id), ARRAY_A) : null;
        return $r ? json_decode($r['snap'], true) : null;
    }

    /** Keeps the record as it is now as a new version, when it differs from the last one. Returns the version's id, or 0 when nothing changed. An offline save (see stale()) is kept with its own time. */
    public static function record($e, $id, $uid, $action = 'update')
    {
        $db = MZC_Crm_Db::db(); $now = self::snap($e, $id); if (!$db || !$now) { return 0; }
        $key = $e . ':' . $id; $ctx = isset(self::$ctx[$key]) ? self::$ctx[$key] : null; unset(self::$ctx[$key]);
        $side = $ctx && $ctx['mode'] === 'side';
        $prev = self::last($e, $id);
        $ch = self::diff($prev, $now);
        if ($prev && !$ch) { if ($side) { self::write($e, $id, $ctx['cur'], false); } return 0; }
        $db->insert(self::t('revisions'), array('entity' => $e, 'entity_id' => (int) $id, 'user_id' => (int) $uid, 'who' => self::who($uid), 'action' => mb_substr((string) $action, 0, 12), 'at' => $ctx ? gmdate('Y-m-d H:i:s', $ctx['at']) : self::now(),
            'changes' => wp_json_encode($prev ? $ch : array(), JSON_UNESCAPED_UNICODE), 'snap' => wp_json_encode($now, JSON_UNESCAPED_UNICODE), 'applied' => $side ? 0 : 1, 'offline' => $ctx ? 1 : 0));
        $rid = (int) $db->insert_id;
        if ($side) { self::write($e, $id, $ctx['cur'], false); self::$flag = 'side'; }   // the older edit is kept in the history only: the record goes back to what the other person saved
        elseif ($ctx && $ctx['stale']) { self::$flag = 'apply'; }
        return $rid;
    }

    /**
     * The check of an offline-capable save, in place of the plain "someone saved first" refusal. true: refuse (409). false: go on and save. A save that carries `offline_at` (the time the
     * person pressed save, with no line to the site) is never refused: if the record was changed by someone else meanwhile, the save that is closer to now is the record's state and the
     * other is kept as a version in the history (compare() and pick() let a person take fields from it); an older offline save is put in the history only.
     */
    public static function stale($e, $id, $p, $cur_updated)
    {
        $same = !isset($p['updated']) || (string) $p['updated'] === (string) $cur_updated;
        if (empty($p['offline_at']) || !isset(self::$kinds[$e])) { return !$same; }
        $at = strtotime((string) $p['offline_at']); $at = $at ? min($at, time()) : time();
        $ctx = array('at' => $at, 'mode' => 'apply', 'stale' => !$same, 'cur' => null);
        if (!$same) {
            self::baseline($e, $id);   // a record from before versions were kept: the other person's state is saved first
            $ctx['cur'] = self::snap($e, $id);
            if ($at < strtotime($cur_updated . ' UTC')) { $ctx['mode'] = 'side'; }
        }
        self::$ctx[$e . ':' . $id] = $ctx;
        return false;
    }

    /** Writes a snapshot as the record's state (its row, and its lines when it has them). $stamp true: updated = now; false: the snapshot's own stamp is kept (nobody's open copy becomes stale). '' or an error text. */
    private static function write($e, $id, $snap, $stamp)
    {
        $db = MZC_Crm_Db::db(); if (!$db || !isset(self::$kinds[$e]) || !is_array($snap)) { return 'پرونده پیدا نشد.'; }
        list($tb, $lines) = self::$kinds[$e]; $cur = self::snap($e, $id); if (!$cur) { return 'این پرونده دیگر وجود ندارد.'; }
        $row = array_diff_key($snap['row'], array_flip(self::$fixed)); $row['updated'] = $stamp ? self::now() : $snap['row']['updated'];
        $row = array_intersect_key($row, $cur['row']);   // a column dropped since is not written
        $db->query('START TRANSACTION');
        $ok = $db->update(self::t($tb), $row, array('id' => (int) $id)) !== false;
        foreach ($lines as $lt => $key) {
            if (!$ok) { break; }
            $ok = $db->delete(self::t($lt), array($key => (int) $id)) !== false;
            foreach (isset($snap[$lt]) ? $snap[$lt] : array() as $x) { if (!$ok) { break; } $x[$key] = (int) $id; $ok = (bool) $db->insert(self::t($lt), $x); }
        }
        $err = $db->last_error; $db->query($ok ? 'COMMIT' : 'ROLLBACK');
        if ($ok && $e === 'invoice') {   // an invoice's money is worked out from its lines, never copied
            $v = $db->get_row($db->prepare('SELECT discount, tax_pct FROM `' . self::t('invoices') . '` WHERE id = %d', (int) $id), ARRAY_A);
            $t = MZC_Acc::totals((array) $db->get_results($db->prepare('SELECT qty, unit_price FROM `' . self::t('invoice_items') . '` WHERE invoice_id = %d', (int) $id), ARRAY_A), $v['discount'], $v['tax_pct']);
            $db->update(self::t('invoices'), array('subtotal' => $t['subtotal'], 'discount' => $t['discount'], 'tax' => $t['tax'], 'total' => $t['total']), array('id' => (int) $id));
        }
        return $ok ? '' : 'نوشته نشد: ' . $err;
    }

    /** A record saved before versions were kept gets its first one (as it is) when it is opened, so the next save shows what it changed. */
    public static function baseline($e, $id) { if (!self::last($e, $id)) { self::record($e, $id, 0, 'base'); } }

    /** The versions, newest first, without the whole records. */
    public static function versions($e, $id, $limit = 100)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return array(); }
        // newest by the time each was saved (an offline save carries its own time); `current` is the one the record is in now
        $rows = (array) $db->get_results($db->prepare('SELECT id, user_id, who, action, at, changes, applied, offline FROM `' . self::t('revisions') . '` WHERE entity = %s AND entity_id = %d ORDER BY at DESC, id DESC LIMIT %d', $e, (int) $id, (int) $limit), ARRAY_A);
        $cur = (int) $db->get_var($db->prepare('SELECT id FROM `' . self::t('revisions') . '` WHERE entity = %s AND entity_id = %d AND applied = 1 ORDER BY id DESC LIMIT 1', $e, (int) $id));
        return array_map(function ($r) use ($cur) { return array('id' => (int) $r['id'], 'who' => $r['who'], 'user_id' => (int) $r['user_id'], 'action' => $r['action'], 'at' => $r['at'], 'changes' => json_decode((string) $r['changes'], true) ?: array(),
            'offline' => (bool) $r['offline'], 'applied' => (bool) $r['applied'], 'current' => (int) $r['id'] === $cur); }, $rows);
    }

    /** The fields in which a version differs from the record now: [key, label, now, theirs]; lines (a list of rows) as a whole. Null when either is gone. */
    public static function compare($rev)
    {
        $db = MZC_Crm_Db::db(); $r = $db ? $db->get_row($db->prepare('SELECT * FROM `' . self::t('revisions') . '` WHERE id = %d', (int) $rev), ARRAY_A) : null;
        if (!$r || !isset(self::$kinds[$r['entity']])) { return null; }
        $cur = self::snap($r['entity'], (int) $r['entity_id']); $snap = json_decode($r['snap'], true); if (!$cur || !is_array($snap)) { return null; }
        $fields = array();
        foreach ($snap['row'] as $k => $v) {
            if (in_array($k, self::$fixed, true) || !array_key_exists($k, $cur['row']) || (string) $cur['row'][$k] === (string) $v) { continue; }
            $fields[] = array('key' => $k, 'label' => isset(self::$names[$k]) ? self::$names[$k] : $k, 'now' => self::short($cur['row'][$k]), 'theirs' => self::short($v), 'group' => false);
        }
        foreach (array_keys(self::$kinds[$r['entity']][1]) as $lt) {
            if (wp_json_encode(isset($cur[$lt]) ? $cur[$lt] : array()) === wp_json_encode(isset($snap[$lt]) ? $snap[$lt] : array())) { continue; }
            $lab = function ($rows) { return implode('، ', array_map(function ($x) { foreach (array('model', 'name', 'amount') as $k) { if (isset($x[$k]) && $x[$k] !== '') { return (string) $x[$k]; } } return '?'; }, (array) $rows)); };
            $fields[] = array('key' => $lt, 'label' => isset(self::$names[$lt]) ? self::$names[$lt] : $lt, 'now' => count($cur[$lt]) . ' ردیف: ' . self::short($lab($cur[$lt])), 'theirs' => count((array) $snap[$lt]) . ' ردیف: ' . self::short($lab(isset($snap[$lt]) ? $snap[$lt] : array())), 'group' => true);
        }
        return array('entity' => $r['entity'], 'id' => (int) $r['entity_id'], 'who' => $r['who'], 'at' => $r['at'], 'offline' => (bool) $r['offline'], 'fields' => $fields);
    }

    /** Takes the chosen fields (and line lists) of a version over the record, keeps that as a new version. Returns array(entity, id) or an error text. */
    public static function pick($rev, $keys, $uid)
    {
        $db = MZC_Crm_Db::db(); $r = $db ? $db->get_row($db->prepare('SELECT * FROM `' . self::t('revisions') . '` WHERE id = %d', (int) $rev), ARRAY_A) : null;
        if (!$r || !isset(self::$kinds[$r['entity']])) { return 'این نسخه پیدا نشد.'; }
        $e = $r['entity']; $id = (int) $r['entity_id']; $cur = self::snap($e, $id); $snap = json_decode($r['snap'], true);
        if (!$cur || !is_array($snap)) { return 'این پرونده دیگر وجود ندارد.'; }
        if ($h = self::blocker($e, $id, $uid)) { return self::busy_text($h); }
        if ($e === 'invoice' && $cur['row']['status'] !== 'draft') { return 'فقط پیش‌نویس فاکتور تغییر می‌کند.'; }
        $keys = array_values(array_filter((array) $keys, 'is_string')); if (!$keys) { return 'هیچ فیلدی انتخاب نشد.'; }
        $merged = $cur; $n = 0;
        foreach ($keys as $k) {
            if (isset(self::$kinds[$e][1][$k]) && isset($snap[$k])) { $merged[$k] = $snap[$k]; $n++; }
            elseif (array_key_exists($k, $snap['row']) && array_key_exists($k, $cur['row']) && !in_array($k, self::$fixed, true)) { $merged['row'][$k] = $snap['row'][$k]; $n++; }
        }
        if (!$n) { return 'فیلد انتخاب‌شده درست نیست.'; }
        $err = self::write($e, $id, $merged, true); if ($err !== '') { return $err; }
        self::record($e, $id, $uid, 'pick');
        return array($e, $id);
    }

    /** Puts a version back as the record's current state (and keeps that as a new version). Returns array(entity, id) or an error text. */
    public static function restore($rev, $uid)
    {
        $db = MZC_Crm_Db::db(); if (!$db) { return MZC_Crm_Db::error(); }
        $r = $db->get_row($db->prepare('SELECT * FROM `' . self::t('revisions') . '` WHERE id = %d', (int) $rev), ARRAY_A);
        if (!$r || !isset(self::$kinds[$r['entity']])) { return 'این نسخه پیدا نشد.'; }
        $e = $r['entity']; $id = (int) $r['entity_id']; $snap = json_decode($r['snap'], true); $cur = self::snap($e, $id);
        if (!$cur || !is_array($snap)) { return 'این پرونده دیگر وجود ندارد.'; }
        if ($h = self::blocker($e, $id, $uid)) { return self::busy_text($h); }
        if ($e === 'invoice' && ($cur['row']['status'] !== 'draft' || $snap['row']['status'] !== 'draft')) { return 'فقط پیش‌نویس فاکتور به نسخهٔ پیش‌نویس قبلی برمی‌گردد؛ فاکتور قطعی یا باطل شماره گرفته و انبار را جابه‌جا کرده است.'; }
        $err = self::write($e, $id, $snap, true);
        if ($err !== '') { MZC_Log::error('rev', 'restore failed', array('rev' => (int) $rev, 'error' => $err)); return 'برنگشت: ' . $err; }
        self::record($e, $id, $uid, 'restore');
        MZC_Log::info('rev', 'restored', array('entity' => $e, 'id' => $id, 'rev' => (int) $rev, 'user' => (int) $uid));
        return array($e, $id);
    }

    /* ---------- who has a record open ---------- */

    /** Takes or renews the lock for $uid. Null when it is theirs now, else the holder (who, since). */
    public static function lock($e, $id, $uid)
    {
        $db = MZC_Crm_Db::db(); if (!$db || !$id || !isset(self::$kinds[$e])) { return null; }
        $tb = self::t('edit_locks'); $t = time();
        $db->query($db->prepare("DELETE FROM `$tb` WHERE entity = %s AND entity_id = %d AND (until < %d OR user_id = %d)", $e, (int) $id, $t, (int) $uid));
        $db->query($db->prepare("INSERT IGNORE INTO `$tb` (entity, entity_id, user_id, who, since, until) VALUES (%s, %d, %d, %s, %d, %d)", $e, (int) $id, (int) $uid, self::who($uid), $t, $t + self::LOCK_SECONDS));
        return self::blocker($e, $id, $uid);
    }

    public static function unlock($e, $id, $uid)
    {
        $db = MZC_Crm_Db::db(); if ($db) { $db->delete(self::t('edit_locks'), array('entity' => $e, 'entity_id' => (int) $id, 'user_id' => (int) $uid)); }
    }

    /** The other person who has the record open (array who, since), or null. */
    public static function blocker($e, $id, $uid)
    {
        $db = MZC_Crm_Db::db(); if (!$db || !$id) { return null; }
        $h = $db->get_row($db->prepare('SELECT user_id, who, since FROM `' . self::t('edit_locks') . '` WHERE entity = %s AND entity_id = %d AND until >= %d AND user_id <> %d', $e, (int) $id, time(), (int) $uid), ARRAY_A);
        return $h ? array('who' => $h['who'], 'since' => gmdate('Y-m-d H:i:s', (int) $h['since'])) : null;
    }

    public static function busy_text($h) { return '«' . $h['who'] . '» این پرونده را برای ویرایش باز کرده است؛ تا وقتی از آن بیرون نرفته، تغییر داده نمی‌شود.'; }

    /* ---------- the staff apps ---------- */

    public static function routes()
    {
        foreach (array(array('/staff/lock', 'POST', 'lock'), array('/staff/revisions', 'GET', 'versions'), array('/staff/revisions/restore', 'POST', 'restore'), array('/staff/revisions/compare', 'GET', 'compare'), array('/staff/revisions/pick', 'POST', 'pick')) as $x) {
            register_rest_route(Mazesta_Connect::NS, $x[0], array('methods' => $x[1], 'callback' => array(__CLASS__, 'rest_' . $x[2]), 'permission_callback' => '__return_true'));
        }
    }

    private static function target($p, $req, $need)
    {
        $e = isset($p['entity']) ? (string) $p['entity'] : ''; $id = isset($p['id']) ? (int) $p['id'] : 0;
        if (!isset(self::$kinds[$e]) || !$id) { return array(null, 0, MZC_Api::fail('پرونده مشخص نیست.')); }
        $a = MZC_Staff::auth($req, self::$kinds[$e][2], $need);
        return $a instanceof WP_REST_Response ? array(null, 0, $a) : array($e, $id, $a);
    }

    /** {entity, id} takes or renews the lock (423 with the holder when someone else has it); {release: true} lets it go. Opening also takes the first version of an old record. */
    public static function rest_lock($req)
    {
        $p = MZC_Api::body($req); list($e, $id, $a) = self::target($p, $req, 2); if (!$e) { return $a; }
        if (!empty($p['release'])) { self::unlock($e, $id, $a['id']); return MZC_Api::ok(array('ok' => true)); }
        self::baseline($e, $id);
        $h = self::lock($e, $id, $a['id']);
        return $h ? MZC_Api::fail(self::busy_text($h), 423, array('holder' => $h)) : MZC_Api::ok(array('ok' => true, 'seconds' => self::LOCK_SECONDS));
    }

    public static function rest_versions($req)
    {
        list($e, $id, $a) = self::target($req->get_params(), $req, 1); if (!$e) { return $a; }
        return MZC_Api::ok(array('items' => array_map(function ($v) { $v['at'] = MZC_Crm::jtime(strtotime($v['at'] . ' UTC')); return $v; }, self::versions($e, $id))));
    }

    public static function rest_restore($req)
    {
        $p = MZC_Api::body($req); $db = MZC_Crm_Db::db();
        $e = $db ? (string) $db->get_var($db->prepare('SELECT entity FROM `' . self::t('revisions') . '` WHERE id = %d', isset($p['rev']) ? (int) $p['rev'] : 0)) : '';
        if (!isset(self::$kinds[$e])) { return MZC_Api::fail('این نسخه پیدا نشد.', 404); }
        $a = MZC_Staff::auth($req, self::$kinds[$e][2], 2); if ($a instanceof WP_REST_Response) { return $a; }
        $r = self::restore((int) $p['rev'], $a['id']);
        if (!is_array($r)) { return MZC_Api::fail($r, 422); }
        MZC_Api::log($a, 'restore', $r[0], $r[1], 'نسخهٔ ' . (int) $p['rev']);
        return MZC_Api::ok(array('entity' => $r[0], 'id' => $r[1]));
    }

    private static function rev_entity($rev) { $db = MZC_Crm_Db::db(); return $db ? (string) $db->get_var($db->prepare('SELECT entity FROM `' . self::t('revisions') . '` WHERE id = %d', (int) $rev)) : ''; }

    /** ?rev=: the fields in which that version differs from the record now (to choose from). */
    public static function rest_compare($req)
    {
        $rev = (int) $req->get_param('rev'); $e = self::rev_entity($rev); if (!isset(self::$kinds[$e])) { return MZC_Api::fail('این نسخه پیدا نشد.', 404); }
        $a = MZC_Staff::auth($req, self::$kinds[$e][2], 1); if ($a instanceof WP_REST_Response) { return $a; }
        $c = self::compare($rev); if (!$c) { return MZC_Api::fail('این نسخه یا پرونده پیدا نشد.', 404); }
        $c['at'] = MZC_Crm::jtime(strtotime($c['at'] . ' UTC'));
        return MZC_Api::ok($c);
    }

    /** {rev, fields: [keys]}: the chosen fields of that version become the record's. */
    public static function rest_pick($req)
    {
        $p = MZC_Api::body($req); $rev = isset($p['rev']) ? (int) $p['rev'] : 0; $e = self::rev_entity($rev);
        if (!isset(self::$kinds[$e])) { return MZC_Api::fail('این نسخه پیدا نشد.', 404); }
        $a = MZC_Staff::auth($req, self::$kinds[$e][2], 2); if ($a instanceof WP_REST_Response) { return $a; }
        $r = self::pick($rev, isset($p['fields']) ? $p['fields'] : array(), $a['id']); if (!is_array($r)) { return MZC_Api::fail($r, 422); }
        MZC_Api::log($a, 'pick', $r[0], $r[1], 'نسخهٔ ' . $rev);
        return MZC_Api::ok(array('entity' => $r[0], 'id' => $r[1]));
    }

    /* ---------- the plugin's own pages ---------- */

    /** On an edit page: takes the lock (and the first version of an old record). Returns '' when the page may save, else the notice that says who has it. */
    public static function admin_open($e, $id)
    {
        if (!$id) { return ''; }
        self::baseline($e, $id);
        $h = self::lock($e, $id, get_current_user_id());
        $ping = '<span class="mzc-lock" data-entity="' . esc_attr($e) . '" data-id="' . (int) $id . '" data-nonce="' . esc_attr(wp_create_nonce('mzc_lock')) . '" data-ajax="' . esc_url(admin_url('admin-ajax.php')) . '" hidden></span>';
        return $h ? '<div class="mzc-notice err mzc-locked">' . esc_html(self::busy_text($h)) . ' این صفحه فقط خواندنی است.</div>' : $ping;
    }

    /** The page renews its lock while it is open and lets it go when it is left (admin.js). */
    public static function ajax_lock()
    {
        check_ajax_referer('mzc_lock');
        $e = isset($_POST['entity']) ? sanitize_key($_POST['entity']) : ''; $id = isset($_POST['id']) ? (int) $_POST['id'] : 0;
        if (!isset(self::$kinds[$e]) || !current_user_can('manage_options') && !MZC_Staff::can(get_current_user_id(), self::$kinds[$e][2], 2)) { wp_send_json_error(null, 403); }
        if (!empty($_POST['release'])) { self::unlock($e, $id, get_current_user_id()); wp_send_json_success(); }
        $h = self::lock($e, $id, get_current_user_id());
        $h ? wp_send_json_error(array('message' => self::busy_text($h)), 423) : wp_send_json_success();
    }

    /** The versions of a record on its page, each with what changed and a button that puts it back. */
    public static function panel($e, $id)
    {
        $list = self::versions($e, $id, 50); if (!$list) { return ''; }
        $page = array('build' => 'mzc-builds', 'job' => 'mzc-jobs', 'invoice' => 'mzc-acc'); $acts = array('create' => 'ثبت', 'update' => 'ویرایش', 'status' => 'وضعیت', 'restore' => 'بازگشت', 'base' => 'نسخهٔ اولیه', 'final' => 'قطعی', 'void' => 'ابطال');
        $o = '<div class="mzc-panel"><header><h3>تاریخچهٔ تغییرات</h3></header><div class="body"><table class="mzc-table"><thead><tr><th>زمان</th><th>کاربر</th><th>کار</th><th>تغییرات</th><th></th></tr></thead><tbody>';
        foreach ($list as $i => $v) {
            $ch = array(); foreach ($v['changes'] as $c) { $ch[] = '<b>' . esc_html($c[0]) . '</b>: ' . esc_html($c[1]) . ' ← ' . esc_html($c[2]); }
            $url = wp_nonce_url(admin_url('admin-post.php?action=mzc_rev_restore&rev=' . $v['id'] . '&page_back=' . $page[$e]), 'mzc_rev_restore');
            $o .= '<tr><td>' . esc_html(MZC_Crm::jtime(strtotime($v['at'] . ' UTC'))) . '</td><td>' . esc_html($v['who']) . '</td><td>' . esc_html(isset($acts[$v['action']]) ? $acts[$v['action']] : $v['action']) . '</td><td>' . ($ch ? implode('<br>', $ch) : '—') . '</td><td>'
                . ($i > 0 ? '<a class="mzc-btn sm" href="' . esc_url($url) . '" onclick="return confirm(\'پرونده به همین نسخه برگردد؟ حالت فعلی هم به‌عنوان یک نسخه می‌ماند.\')">بازگشت به این نسخه</a>' : '<small>نسخهٔ فعلی</small>') . '</td></tr>';
        }
        return $o . '</tbody></table></div></div>';
    }

    public static function act_restore()
    {
        check_admin_referer('mzc_rev_restore');
        $page = isset($_GET['page_back']) ? sanitize_key($_GET['page_back']) : 'mzc-jobs'; $rev = isset($_GET['rev']) ? (int) $_GET['rev'] : 0;
        $db = MZC_Crm_Db::db(); $e = $db ? (string) $db->get_var($db->prepare('SELECT entity FROM `' . self::t('revisions') . '` WHERE id = %d', $rev)) : '';
        if (!isset(self::$kinds[$e]) || !current_user_can('manage_options') && !MZC_Staff::can(get_current_user_id(), self::$kinds[$e][2], 2)) { wp_die('اجازه این کار را ندارید.', '', array('response' => 403)); }
        $r = self::restore($rev, get_current_user_id());
        is_array($r) ? MZC_Admin::back($page, 'ok:پرونده به نسخهٔ انتخاب‌شده برگشت.', array('view' => $r[1])) : MZC_Admin::back($page, 'err:' . $r);
    }
}
