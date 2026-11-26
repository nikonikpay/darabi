<?php
if (!defined('ABSPATH')) { exit; }

/**
 * Tasks and workflows. A task is a list of steps, worked one after another; a simple task has one. Each step is for people and/or roles (anyone of them may finish it, or
 * `all` of them must). The people of the current step report progress, say it is done or could not be done, or send it back a step; when the last step is done the task is
 * finished, or waits for whoever made it to confirm. A workflow (flows) is a saved list of steps a manager draws; a task takes its own copy, so changing a workflow never
 * disturbs a task already running. A workflow marked `auto` starts by itself for each new computer (or service), and a step may set that record's status when it starts.
 */
final class MZC_Tasks
{
    public static $statuses = array('open' => 'در جریان', 'blocked' => 'انجام نشد', 'review' => 'منتظر تأیید', 'done' => 'انجام شد', 'cancelled' => 'لغو شد');
    public static $actions = array('create' => 'ساخته شد', 'progress' => 'پیشرفت', 'note' => 'یادداشت', 'mark' => 'سهم خود را انجام داد', 'done' => 'مرحله انجام شد', 'fail' => 'انجام نشد', 'resume' => 'دوباره در جریان', 'back' => 'برگشت به مرحلهٔ قبل',
        'confirm' => 'تأیید شد', 'reject' => 'تأیید نشد', 'cancel' => 'لغو شد', 'reopen' => 'دوباره باز شد', 'edit' => 'ویرایش شد');
    public static $kinds = array('build' => 'سیستم نو', 'job' => 'سرویس', 'general' => 'کار عمومی');

    public static function boot() { add_action('rest_api_init', array(__CLASS__, 'routes')); }

    public static function routes()
    {
        foreach (array(array('/staff/tasks', 'GET', 'tasks'), array('/staff/tasks/(?P<id>\d+)', 'GET', 'task'), array('/staff/tasks', 'POST', 'task_save'), array('/staff/tasks/act', 'POST', 'act'),
            array('/staff/flows', 'GET', 'flows'), array('/staff/flows', 'POST', 'flow_save'), array('/staff/flows/delete', 'POST', 'flow_delete')) as $x) {
            register_rest_route(Mazesta_Connect::NS, $x[0], array('methods' => $x[1], 'callback' => array(__CLASS__, 'rest_' . $x[2]), 'permission_callback' => '__return_true'));
        }
    }

    private static function db() { return MZC_Crm_Db::db(); }
    private static function t($n) { return MZC_Crm_Db::t($n); }
    private static function now() { return gmdate('Y-m-d H:i:s'); }
    private static function fail($m, $st = 400) { return MZC_Api::fail($m, $st); }
    private static function json($s) { $v = json_decode((string) $s, true); return is_array($v) ? $v : array(); }
    public static function manager($uid) { return MZC_Staff::can($uid, 'manage', 1); }
    private static function name($uid) { return MZC_Rev::who($uid); }

    /** The shop's people who may be given a task: id, name, their roles. */
    public static function team()
    {
        $out = array();
        foreach (MZC_Staff::users() as $u) { $r = MZC_Staff::record($u->ID); if ($r['on']) { $out[] = array('id' => (int) $u->ID, 'name' => $u->display_name !== '' ? $u->display_name : $u->user_login, 'roles' => $r['roles']); } }
        return $out;
    }

    /* ---------- steps ---------- */

    /** Steps as sent, cleaned: unknown roles, people and statuses are left out. An error text when there is nothing to do. */
    private static function clean_steps($in, $kind)
    {
        if (!is_array($in) || !$in) { return 'دست‌کم یک مرحله بنویسید.'; }
        $roles = MZC_Staff::roles(); $out = array();
        $statuses = $kind === 'build' ? MZC_Crm::$build_statuses : ($kind === 'job' ? MZC_Crm::$statuses : array());
        foreach (array_slice(array_values($in), 0, 20) as $s) {
            if (!is_array($s)) { continue; }
            $title = MZC_Api::s($s, 'title', 120); if ($title === '') { return 'نام هر مرحله را بنویسید.'; }
            $users = array(); foreach (isset($s['users']) && is_array($s['users']) ? $s['users'] : array() as $u) { $id = (int) (is_array($u) ? (isset($u['id']) ? $u['id'] : 0) : $u); if ($id && get_userdata($id)) { $users[$id] = $id; } }
            $keys = array(); foreach (isset($s['roles']) && is_array($s['roles']) ? $s['roles'] : array() as $r) { $k = (string) (is_array($r) ? (isset($r['key']) ? $r['key'] : '') : $r); if (isset($roles[$k])) { $keys[$k] = $k; } }
            $st = isset($s['status']) ? (string) $s['status'] : '';
            $out[] = array('title' => $title, 'users' => array_values($users), 'roles' => array_values($keys), 'all' => !empty($s['all']), 'hint' => MZC_Api::s($s, 'hint', 500), 'status' => isset($statuses[$st]) ? $st : '');
        }
        return $out ? $out : 'دست‌کم یک مرحله بنویسید.';
    }

    /** The people a step is for, now (those named, and everyone who holds one of its roles). */
    private static function people($step)
    {
        $ids = array_fill_keys($step['users'], true);
        if ($step['roles']) { foreach (self::team() as $p) { if (array_intersect($p['roles'], $step['roles'])) { $ids[$p['id']] = true; } } }
        return array_keys($ids);
    }

    private static function in_step($step, $uid) { $r = MZC_Staff::record($uid); return in_array($uid, $step['users'], false) || ($r['on'] && array_intersect($r['roles'], $step['roles'])); }
    private static function involved($t, $uid)
    {
        if ((int) $t['created_by'] === $uid || self::manager($uid)) { return true; }
        foreach (self::json($t['steps']) as $s) { if (self::in_step($s, $uid)) { return true; } }
        return false;
    }
    /** Whether $uid may finish the current step: one of its people; a step with nobody named is its maker's. */
    private static function acts($t, $uid)
    {
        $steps = self::json($t['steps']); $i = (int) $t['step']; if (!isset($steps[$i])) { return false; }
        $s = $steps[$i]; return !$s['users'] && !$s['roles'] ? (int) $t['created_by'] === $uid : self::in_step($s, $uid);
    }

    /* ---------- reading ---------- */

    public static function row($id) { $db = self::db(); return $db ? $db->get_row($db->prepare('SELECT * FROM `' . self::t('tasks') . '` WHERE id = %d', (int) $id), ARRAY_A) : null; }

    /** One task as this person sees it, or null when it is not theirs. */
    public static function record($id, $a)
    {
        $t = self::row($id); return $t && self::involved($t, (int) $a['id']) ? self::out($t, (int) $a['id']) : null;
    }

    private static function out($t, $uid)
    {
        $db = self::db(); $steps = self::json($t['steps']); $roles = MZC_Staff::roles(); $n = max(1, count($steps)); $i = (int) $t['step'];
        $named = array_map(function ($s) use ($roles) {
            $s['users'] = array_map(function ($u) { return array('id' => (int) $u, 'name' => self::name($u)); }, $s['users']);
            $s['roles'] = array_map(function ($k) use ($roles) { return array('key' => $k, 'label' => isset($roles[$k]) ? $roles[$k]['label'] : $k); }, $s['roles']);
            return $s;
        }, $steps);
        $percent = $t['status'] === 'done' || $t['status'] === 'review' ? 100 : (int) floor((min($i, $n) + (int) $t['progress'] / 100) / $n * 100);
        $marks = array(); foreach (self::json($t['marks']) as $u => $at) { $marks[] = array('user_id' => (int) $u, 'name' => self::name($u), 'at_j' => MZC_Crm::jtime($at)); }
        $ref = '';
        if ($t['ref_kind'] === 'build' && ($b = $db->get_row($db->prepare('SELECT invoice_no, service_no, title FROM `' . self::t('builds') . '` WHERE id = %d', (int) $t['ref_id']), ARRAY_A))) { $ref = trim('سیستم ' . ($b['invoice_no'] ?: $b['service_no']) . ' — ' . $b['title'], ' —'); }
        if ($t['ref_kind'] === 'job' && ($j = $db->get_row($db->prepare('SELECT service_no, device FROM `' . self::t('jobs') . '` WHERE id = %d', (int) $t['ref_id']), ARRAY_A))) { $ref = trim('سرویس ' . $j['service_no'] . ' — ' . $j['device'], ' —'); }
        // the record's name and the customer's are for those who may see that record or the customers: being given a task does not widen what a person sees
        $area = $t['ref_kind'] === 'build' ? 'builds' : ($t['ref_kind'] === 'job' ? 'jobs' : ''); $sees = $area !== '' && MZC_Staff::can($uid, $area, 1);
        if (!$sees) { $ref = ''; }
        $c = $t['customer_id'] && ($sees || MZC_Staff::can($uid, 'customers', 1)) ? MZC_Crm::customer($t['customer_id']) : null;
        $log = array_map(function ($l) use ($steps) {
            return array('who' => $l['who'], 'at_j' => MZC_Crm::jtime(strtotime($l['at'] . ' UTC')), 'action' => $l['action'], 'actionText' => isset(self::$actions[$l['action']]) ? self::$actions[$l['action']] : $l['action'], 'note' => (string) $l['note'],
                'step' => (int) $l['step'], 'stepTitle' => isset($steps[(int) $l['step']]) ? $steps[(int) $l['step']]['title'] : '', 'progress' => $l['progress'] === null ? null : (int) $l['progress']);
        }, (array) $db->get_results($db->prepare('SELECT * FROM `' . self::t('task_log') . '` WHERE task_id = %d ORDER BY id', (int) $t['id']), ARRAY_A));
        $open = in_array($t['status'], array('open', 'blocked'), true); $boss = (int) $t['created_by'] === $uid || self::manager($uid);
        return array('id' => (int) $t['id'], 'title' => $t['title'], 'body' => (string) $t['body'], 'status' => $t['status'], 'statusText' => self::$statuses[$t['status']], 'progress' => (int) $t['progress'], 'percent' => $percent, 'priority' => (int) $t['priority'],
            'due_at' => (string) $t['due_at'], 'due_at_j' => MZC_Api::jd($t['due_at']), 'flow_id' => (int) $t['flow_id'], 'flow_name' => (string) $t['flow_name'], 'steps' => $named, 'step' => $i, 'marks' => $marks, 'confirm' => (bool) $t['confirm'],
            'ref_kind' => (string) $t['ref_kind'], 'ref_id' => (int) $t['ref_id'], 'ref_label' => $ref, 'customer_id' => $c ? (int) $t['customer_id'] : 0, 'customer_name' => $c ? $c['name'] : '', 'customer_mobile' => $c ? (string) $c['mobile'] : '',
            'created_by' => (int) $t['created_by'], 'who' => $t['who'], 'created_j' => MZC_Crm::jtime(strtotime($t['created'] . ' UTC')), 'done_at_j' => $t['done_at'] ? MZC_Crm::jtime(strtotime($t['done_at'] . ' UTC')) : '', 'updated' => $t['updated'],
            // what this person may do now
            // `mine`: it is this person's turn (their lists); `can_act`: the current step's buttons work for them (a manager may answer for others)
            'mine' => $open && self::acts($t, $uid), 'can_act' => $open && (self::acts($t, $uid) || self::manager($uid)), 'by_me' => (int) $t['created_by'] === $uid, 'boss' => $boss, 'log' => $log);
    }

    /** ?box=mine|review|sent|done|all, or ?ref_kind=&ref_id= (a record's tasks). Urgent first, then the nearest due day. */
    public static function rest_tasks($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $db = self::db(); if (!$db) { return self::fail(MZC_Crm_Db::error(), 503); }
        $box = (string) $req->get_param('box'); $kind = (string) $req->get_param('ref_kind'); $where = array('1=1'); $args = array();
        if ($kind !== '') { $where[] = 'ref_kind = %s AND ref_id = %d'; array_push($args, $kind, (int) $req->get_param('ref_id')); }
        elseif ($box === 'done') { $where[] = "status IN ('done', 'cancelled')"; }
        elseif ($box === 'review') { $where[] = "status = 'review'"; }
        else { $where[] = "status NOT IN ('done', 'cancelled')"; }
        $sql = 'SELECT * FROM `' . self::t('tasks') . '` WHERE ' . implode(' AND ', $where) . " ORDER BY priority DESC, (due_at IS NULL), due_at, id DESC LIMIT 1000";
        $rows = (array) $db->get_results($args ? $db->prepare($sql, $args) : $sql, ARRAY_A);
        $uid = (int) $a['id']; $q = mb_strtolower(trim((string) $req->get_param('q'))); $items = array();
        foreach ($rows as $t) {
            if (!self::involved($t, $uid) || ($q !== '' && mb_strpos(mb_strtolower($t['title'] . ' ' . $t['body'] . ' ' . $t['flow_name']), $q) === false)) { continue; }
            if ($kind === '' && $box === 'mine' && !(in_array($t['status'], array('open', 'blocked'), true) && self::acts($t, $uid))) { continue; }
            if ($kind === '' && ($box === 'review' || $box === 'sent') && (int) $t['created_by'] !== $uid) { continue; }
            $items[] = self::out($t, $uid);
        }
        $pg = MZC_Api::page($req); $per = MZC_Api::per($req);
        return MZC_Api::ok(array('total' => count($items), 'page' => $pg, 'per' => $per, 'items' => array_slice($items, ($pg - 1) * $per, $per)));
    }

    public static function rest_task($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $o = self::record((int) $req['id'], $a); return $o ? MZC_Api::ok($o) : self::fail('کار پیدا نشد.', 404);
    }

    /* ---------- writing ---------- */

    private static function log($a, $id, $action, $note, $step, $progress = null)
    {
        $db = self::db(); $u = $a['id'] ? self::name($a['id']) : 'سیستم';
        $db->insert(self::t('task_log'), array('task_id' => (int) $id, 'user_id' => (int) $a['id'], 'who' => $u, 'at' => self::now(), 'action' => $action, 'note' => $note !== '' ? mb_substr($note, 0, 1000) : null, 'step' => (int) $step, 'progress' => $progress));
    }

    /** The record's status a step asks for, set when the step starts. */
    private static function apply_status($t, $steps, $i, $a)
    {
        $st = isset($steps[$i]) ? $steps[$i]['status'] : ''; if ($st === '' || !$t['ref_id']) { return; }
        $db = self::db(); $kind = $t['ref_kind']; $map = $kind === 'build' ? MZC_Crm::$build_statuses : ($kind === 'job' ? MZC_Crm::$statuses : array()); if (!isset($map[$st])) { return; }
        $tb = self::t($kind === 'build' ? 'builds' : 'jobs'); $was = $db->get_var($db->prepare("SELECT status FROM `$tb` WHERE id = %d", (int) $t['ref_id']));
        if ($was === null || $was === $st) { return; }
        $row = array('status' => $st, 'updated' => self::now()) + ($kind === 'build' ? array('delivered' => $st === 'delivered' ? 1 : 0) : array('service_done' => in_array($st, array('ready', 'delivered'), true) ? 1 : 0));
        $db->update($tb, $row, array('id' => (int) $t['ref_id']));
        if ($a['id']) { MZC_Api::log(array('id' => $a['id'], 'user' => get_userdata($a['id'])), 'status', $kind, (int) $t['ref_id'], $was . ' → ' . $st . ' (گردش کار)'); }
    }

    /** Makes a task (steps already clean). Returns its id, or 0. */
    private static function insert($a, $data, $steps)
    {
        $db = self::db(); $now = self::now();
        $row = $data + array('status' => 'open', 'progress' => 0, 'steps' => wp_json_encode($steps, JSON_UNESCAPED_UNICODE), 'step' => 0, 'marks' => '', 'created_by' => (int) $a['id'], 'who' => $a['id'] ? self::name($a['id']) : 'سیستم', 'created' => $now, 'updated' => $now);
        if (!$db->insert(self::t('tasks'), $row)) { return 0; }
        $id = (int) $db->insert_id; self::log($a, $id, 'create', '', 0);
        self::apply_status(self::row($id), $steps, 0, $a);
        return $id;
    }

    /** A new computer or service: every active workflow of its kind marked `auto` starts for it (once). */
    public static function start_for($kind, $ref, $uid)
    {
        $db = self::db(); if (!$db || !$ref) { return; }
        foreach ((array) $db->get_results($db->prepare('SELECT * FROM `' . self::t('flows') . '` WHERE kind = %s AND auto = 1 AND active = 1 ORDER BY id', $kind), ARRAY_A) as $f) {
            if ($db->get_var($db->prepare('SELECT id FROM `' . self::t('tasks') . '` WHERE flow_id = %d AND ref_kind = %s AND ref_id = %d', (int) $f['id'], $kind, (int) $ref))) { continue; }
            self::from_flow(array('id' => (int) $uid), $f, $kind, (int) $ref);
        }
    }

    private static function from_flow($a, $f, $kind, $ref, $more = array())
    {
        $db = self::db(); $steps = self::json($f['steps']); if (!$steps) { return 0; }
        $title = $f['name']; $cid = 0;
        if ($ref && $kind === 'build' && ($b = $db->get_row($db->prepare('SELECT customer_id, invoice_no, service_no FROM `' . self::t('builds') . '` WHERE id = %d', $ref), ARRAY_A))) { $title .= ' — ' . ($b['invoice_no'] ?: $b['service_no']); $cid = (int) $b['customer_id']; }
        if ($ref && $kind === 'job' && ($j = $db->get_row($db->prepare('SELECT customer_id, service_no FROM `' . self::t('jobs') . '` WHERE id = %d', $ref), ARRAY_A))) { $title .= ' — ' . $j['service_no']; $cid = (int) $j['customer_id']; }
        return self::insert($a, $more + array('title' => mb_substr($title, 0, 190), 'flow_id' => (int) $f['id'], 'flow_name' => $f['name'], 'ref_kind' => $ref ? $kind : null, 'ref_id' => $ref ?: null, 'customer_id' => $cid ?: null, 'confirm' => 0, 'priority' => 0), $steps);
    }

    /**
     * A new task (no id): {title, body, due_at, priority, confirm, steps} or {flow_id}, with ref_kind + ref_id when it is about a computer or a service. With an id:
     * whoever made it (or a manager) changes the title, text, due day, urgency and the steps (who does what); the steps already done stay done.
     */
    public static function rest_task_save($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $db = self::db(); if (!$db) { return self::fail(MZC_Crm_Db::error(), 503); }
        $p = MZC_Api::body($req); $id = MZC_Api::n($p, 'id'); $uid = (int) $a['id'];
        $kind = isset($p['ref_kind']) && in_array($p['ref_kind'], array('build', 'job'), true) ? (string) $p['ref_kind'] : ''; $ref = $kind !== '' ? MZC_Api::n($p, 'ref_id') : 0;
        if ($id) {
            $t = self::row($id); if (!$t || !self::involved($t, $uid)) { return self::fail('کار پیدا نشد.', 404); }
            if ((int) $t['created_by'] !== $uid && !self::manager($uid)) { return self::fail('فقط سازندهٔ کار یا مدیر آن را عوض می‌کند.', 403); }
            if (isset($p['updated']) && (string) $p['updated'] !== (string) $t['updated']) { return self::fail('این کار در همین فاصله عوض شده است؛ دوباره بازش کنید.', 409); }
            $row = array('updated' => self::now());
            if (array_key_exists('title', $p)) { $row['title'] = MZC_Api::s($p, 'title', 190); if ($row['title'] === '') { return self::fail('عنوان کار را بنویسید.', 422); } }
            if (array_key_exists('body', $p)) { $row['body'] = MZC_Api::s($p, 'body', 4000) ?: null; }
            if (array_key_exists('due_at', $p)) { $row['due_at'] = MZC_Api::d($p, 'due_at'); }
            if (array_key_exists('priority', $p)) { $row['priority'] = !empty($p['priority']) ? 1 : 0; }
            if (array_key_exists('confirm', $p)) { $row['confirm'] = !empty($p['confirm']) ? 1 : 0; }
            if (array_key_exists('steps', $p)) {
                $steps = self::clean_steps($p['steps'], (string) $t['ref_kind']); if (is_string($steps)) { return self::fail($steps, 422); }
                if (count($steps) <= (int) $t['step'] && in_array($t['status'], array('open', 'blocked'), true)) { return self::fail('مرحله‌های انجام‌شده را نمی‌شود برداشت.', 422); }
                $row['steps'] = wp_json_encode($steps, JSON_UNESCAPED_UNICODE); $row['marks'] = '';
            }
            if ($db->update(self::t('tasks'), $row, array('id' => $id)) === false) { return self::fail('ذخیره نشد: ' . $db->last_error, 500); }
            self::log($a, $id, 'edit', '', (int) $t['step']);
            return MZC_Api::ok(self::out(self::row($id), $uid));
        }
        $make = function () use ($db, $p, $a, $uid, $kind, $ref) {
            if ($ref && (!MZC_Staff::can($uid, $kind === 'build' ? 'builds' : 'jobs', 1) || !$db->get_var($db->prepare('SELECT id FROM `' . self::t($kind === 'build' ? 'builds' : 'jobs') . '` WHERE id = %d', $ref)))) { return self::fail('پروندهٔ مربوط پیدا نشد.', 404); }
            $fid = MZC_Api::n($p, 'flow_id');
            if ($fid) {
                $f = $db->get_row($db->prepare('SELECT * FROM `' . self::t('flows') . '` WHERE id = %d AND active = 1', $fid), ARRAY_A); if (!$f) { return self::fail('گردش کار پیدا نشد.', 404); }
                if ($f['kind'] !== 'general' && $f['kind'] !== $kind) { return self::fail('این گردش کار برای «' . self::$kinds[$f['kind']] . '» است.', 422); }
                $more = array(); $title = MZC_Api::s($p, 'title', 190); if ($title !== '') { $more['title'] = $title; }
                $more['body'] = MZC_Api::s($p, 'body', 4000) ?: null; $more['due_at'] = MZC_Api::d($p, 'due_at');
                $id = self::from_flow($a, $f, $kind, $ref, $more);
            } else {
                $title = MZC_Api::s($p, 'title', 190); if ($title === '') { return self::fail('عنوان کار را بنویسید.', 422); }
                $steps = self::clean_steps(isset($p['steps']) ? $p['steps'] : null, $kind); if (is_string($steps)) { return self::fail($steps, 422); }
                $cid = MZC_Api::n($p, 'customer_id'); if ($cid && !MZC_Crm::customer($cid)) { $cid = 0; }
                $id = self::insert($a, array('title' => $title, 'body' => MZC_Api::s($p, 'body', 4000) ?: null, 'due_at' => MZC_Api::d($p, 'due_at'), 'priority' => !empty($p['priority']) ? 1 : 0, 'confirm' => !empty($p['confirm']) ? 1 : 0, 'ref_kind' => $ref ? $kind : null, 'ref_id' => $ref ?: null, 'customer_id' => $cid ?: null), $steps);
            }
            return $id ? MZC_Api::ok(self::out(self::row($id), $uid), 201) : self::fail('ذخیره نشد: ' . $db->last_error, 500);
        };
        $find = function ($tid) use ($a) { return self::record($tid, $a); };
        return MZC_Acc_Api::op_run(MZC_Acc_Api::op_key($req), $find, function ($o) { return $o; }, $make);
    }

    /** {task, action, step, note, progress}: what a person does with a task. `step` is the step they saw, so an answer to a step already passed (sent late) changes nothing. */
    public static function rest_act($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $db = self::db(); if (!$db) { return self::fail(MZC_Crm_Db::error(), 503); }
        $p = MZC_Api::body($req); $id = MZC_Api::n($p, 'task'); $uid = (int) $a['id'];
        $find = function ($tid) use ($a) { return self::record($tid, $a); };
        return MZC_Acc_Api::op_run(MZC_Acc_Api::op_key($req), $find, function ($o) { return $o; }, function () use ($db, $p, $a, $id, $uid) {
            // one person at a time on one task: two "done" at once must not skip a step
            $lock = self::t('task') . $id; if (!$db->get_var($db->prepare('SELECT GET_LOCK(%s, 8)', $lock))) { return self::fail('سامانه مشغول است؛ دوباره امتحان کنید.', 503); }
            try { return self::do_act($a, $p, $id, $uid); } finally { $db->query($db->prepare('DO RELEASE_LOCK(%s)', $lock)); }
        });
    }

    private static function do_act($a, $p, $id, $uid)
    {
        $db = self::db(); $t = self::row($id); if (!$t || !self::involved($t, $uid)) { return self::fail('کار پیدا نشد.', 404); }
        $do = isset($p['action']) ? (string) $p['action'] : ''; $note = MZC_Api::s($p, 'note', 1000); $steps = self::json($t['steps']); $i = (int) $t['step']; $st = $t['status'];
        $boss = (int) $t['created_by'] === $uid || self::manager($uid); $actor = self::acts($t, $uid) || self::manager($uid); $open = in_array($st, array('open', 'blocked'), true);
        if (isset($p['step']) && in_array($do, array('done', 'fail', 'back', 'progress'), true) && (int) $p['step'] !== $i) { return self::fail('این کار از این مرحله گذشته است؛ دوباره بازش کنید.', 409); }
        $row = array('updated' => self::now()); $log = $do; $progress = null; $enter = null;
        switch ($do) {
            case 'note': if ($note === '') { return self::fail('یادداشت را بنویسید.', 422); } break;
            case 'progress':
                if (!$open || !$actor) { return self::fail('این کار الان با شما نیست.', 403); }
                $progress = max(0, min(100, MZC_Api::n($p, 'progress'))); $row['progress'] = $progress; $row['status'] = 'open'; break;
            case 'done':
                if (!$open || !$actor) { return self::fail('این کار الان با شما نیست.', 403); }
                $step = $steps[$i]; $marks = self::json($t['marks']); $need = $step['all'] ? self::people($step) : array();
                if (count($need) > 1 && !self::manager($uid)) {
                    $marks[$uid] = time(); if (array_diff($need, array_keys($marks))) { $row['marks'] = wp_json_encode($marks); $log = 'mark'; break; }
                }
                $row['marks'] = ''; $row['progress'] = 0; $row['status'] = 'open';
                if ($i + 1 < count($steps)) { $row['step'] = $i + 1; $enter = $i + 1; }
                elseif ($t['confirm'] && (int) $t['created_by'] !== $uid && $t['created_by']) { $row['status'] = 'review'; }
                else { $row['status'] = 'done'; $row['done_at'] = self::now(); }
                break;
            case 'fail':
                if (!$open || !$actor) { return self::fail('این کار الان با شما نیست.', 403); }
                if ($note === '') { return self::fail('بنویسید چرا انجام نشد.', 422); }
                $row['status'] = 'blocked'; break;
            case 'resume':
                if ($st !== 'blocked' || !($actor || $boss)) { return self::fail('این کار متوقف نیست.', 422); }
                $row['status'] = 'open'; break;
            case 'back':
                if (!$open || !$actor || $i < 1) { return self::fail('مرحلهٔ قبلی برای برگشت نیست.', 422); }
                if ($note === '') { return self::fail('بنویسید چرا برمی‌گردد.', 422); }
                $row += array('step' => $i - 1, 'marks' => '', 'progress' => 0, 'status' => 'open'); $enter = $i - 1; break;
            case 'confirm': case 'reject':
                if ($st !== 'review' || !$boss) { return self::fail('تأیید با سازندهٔ کار است.', 403); }
                if ($do === 'confirm') { $row += array('status' => 'done', 'done_at' => self::now()); break; }
                if ($note === '') { return self::fail('بنویسید چرا تأیید نمی‌کنید.', 422); }
                $row += array('status' => 'open', 'marks' => '', 'progress' => 0); break;
            case 'cancel': case 'reopen':
                if (!$boss) { return self::fail('این کار با سازندهٔ کار یا مدیر است.', 403); }
                if (($do === 'cancel') === in_array($st, array('done', 'cancelled'), true)) { return self::fail($do === 'cancel' ? 'این کار بسته شده است.' : 'این کار باز است.', 422); }
                $row += $do === 'cancel' ? array('status' => 'cancelled', 'done_at' => self::now()) : array('status' => 'open', 'done_at' => null, 'marks' => '', 'progress' => 0); break;
            default: return self::fail('کار خواسته‌شده شناخته نشد.');
        }
        if ($db->update(self::t('tasks'), $row, array('id' => $id)) === false) { return self::fail('ذخیره نشد: ' . $db->last_error, 500); }
        self::log($a, $id, $log, $note, $i, $progress);
        if ($enter !== null) { self::apply_status($t, $steps, $enter, $a); }
        return MZC_Api::ok(self::out(self::row($id), $uid));
    }

    /* ---------- workflows ---------- */

    public static function flow_record($id)
    {
        $db = self::db(); $f = $db ? $db->get_row($db->prepare('SELECT * FROM `' . self::t('flows') . '` WHERE id = %d', (int) $id), ARRAY_A) : null;
        return $f ? self::flow_out($f) : null;
    }

    private static function flow_out($f) { return array('id' => (int) $f['id'], 'name' => $f['name'], 'kind' => $f['kind'], 'kindText' => isset(self::$kinds[$f['kind']]) ? self::$kinds[$f['kind']] : $f['kind'], 'steps' => self::json($f['steps']), 'auto' => (bool) $f['auto'], 'active' => (bool) $f['active'], 'updated' => $f['updated']); }

    public static function rest_flows($req)
    {
        $a = MZC_Staff::auth($req); if ($a instanceof WP_REST_Response) { return $a; }
        $db = self::db(); if (!$db) { return self::fail(MZC_Crm_Db::error(), 503); }
        return MZC_Api::ok(array('items' => array_map(array(__CLASS__, 'flow_out'), (array) $db->get_results('SELECT * FROM `' . self::t('flows') . '` ORDER BY id', ARRAY_A))));
    }

    public static function rest_flow_save($req)
    {
        $a = MZC_Staff::auth($req, 'manage', 2); if ($a instanceof WP_REST_Response) { return $a; }
        $db = self::db(); if (!$db) { return self::fail(MZC_Crm_Db::error(), 503); }
        $p = MZC_Api::body($req); $id = MZC_Api::n($p, 'id'); $name = MZC_Api::s($p, 'name', 120); if ($name === '') { return self::fail('نام گردش کار را بنویسید.', 422); }
        $old = $id ? $db->get_row($db->prepare('SELECT * FROM `' . self::t('flows') . '` WHERE id = %d', $id), ARRAY_A) : null;
        if ($id && !$old) { return self::fail('گردش کار پیدا نشد.', 404); }
        if ($old && isset($p['updated']) && (string) $p['updated'] !== (string) $old['updated']) { return self::fail('این گردش کار را کس دیگری عوض کرده است؛ دوباره بازش کنید.', 409); }
        $kind = isset($p['kind'], self::$kinds[(string) $p['kind']]) ? (string) $p['kind'] : 'general';
        $steps = self::clean_steps(isset($p['steps']) ? $p['steps'] : null, $kind); if (is_string($steps)) { return self::fail($steps, 422); }
        $row = array('name' => $name, 'kind' => $kind, 'steps' => wp_json_encode($steps, JSON_UNESCAPED_UNICODE), 'auto' => !empty($p['auto']) && $kind !== 'general' ? 1 : 0, 'active' => !isset($p['active']) || !empty($p['active']) ? 1 : 0, 'updated' => self::now());
        if ($old) { $ok = $db->update(self::t('flows'), $row, array('id' => $id)) !== false; } else { $row['created'] = $row['updated']; $ok = (bool) $db->insert(self::t('flows'), $row); $id = (int) $db->insert_id; }
        if (!$ok) { return self::fail('ذخیره نشد: ' . $db->last_error, 500); }
        MZC_Api::log($a, $old ? 'update' : 'create', 'flow', $id, $name);
        return MZC_Api::ok(self::flow_record($id), $old ? 200 : 201);
    }

    /** Removes a workflow; the tasks made from it keep their own steps and go on. */
    public static function rest_flow_delete($req)
    {
        $a = MZC_Staff::auth($req, 'manage', 2); if ($a instanceof WP_REST_Response) { return $a; }
        $db = self::db(); $id = MZC_Api::n(MZC_Api::body($req), 'id'); if (!$db || !$id || !$db->delete(self::t('flows'), array('id' => $id))) { return self::fail('گردش کار پیدا نشد.', 404); }
        MZC_Api::log($a, 'delete', 'flow', $id);
        return MZC_Api::ok(array('deleted' => $id));
    }

    /** A record removed on the site: what was running for it stops. */
    public static function cancel_for($kind, $ref)
    {
        $db = self::db(); if (!$db) { return; }
        foreach ((array) $db->get_col($db->prepare('SELECT id FROM `' . self::t('tasks') . "` WHERE ref_kind = %s AND ref_id = %d AND status NOT IN ('done', 'cancelled')", $kind, (int) $ref)) as $id) {
            $db->update(self::t('tasks'), array('status' => 'cancelled', 'done_at' => self::now(), 'updated' => self::now()), array('id' => (int) $id));
            self::log(array('id' => 0), $id, 'cancel', 'پروندهٔ مربوط پاک شد.', 0);
        }
    }

    /** The two workflows a new database starts with (once; a deleted one stays deleted): a new computer's way from the accountant's invoice to the customer, and a service's. */
    public static function seed()
    {
        $db = self::db(); if (!$db) { return; }
        $meta = self::t('meta'); if ($db->get_var("SELECT v FROM `$meta` WHERE k = 'flows_seeded'")) { return; }
        $s = function ($title, $role, $status, $hint) { return array('title' => $title, 'users' => array(), 'roles' => array($role), 'all' => false, 'hint' => $hint, 'status' => $status); };
        $build = array(
            $s('هماهنگی با مشتری', 'reception', 'queue', 'برنامه‌های مورد نیاز مشتری و اطلاعات دیگر را بگیرید و در فرم سیستم ثبت کنید.'),
            $s('اسمبل سیستم', 'assembly', 'assembling', 'قطعات را طبق فرم ببندید و سریال‌ها را ثبت کنید.'),
            $s('تست و نصب برنامه‌ها (QC)', 'test', 'testing', 'تست‌ها را بگیرید، گزارش را به فرم وصل کنید و برنامه‌ها را نصب کنید.'),
            $s('اعلام به مشتری و هماهنگی تحویل', 'reception', 'tested', 'با مشتری تماس بگیرید و نتیجه را در «پیگیری مشتری» ثبت کنید.'));
        $job = array($s('پذیرش و دریافت اطلاعات', 'reception', '', ''), $s('بررسی و سرویس', 'service', 'diagnosing', ''), $s('تست نهایی', 'test', '', ''), $s('تماس با مشتری و تحویل', 'reception', 'ready', ''));
        foreach (array(array('اسمبل سیستم نو', 'build', $build, 1), array('سرویس دستگاه', 'job', $job, 0)) as $f) {
            $db->insert(self::t('flows'), array('name' => $f[0], 'kind' => $f[1], 'steps' => wp_json_encode($f[2], JSON_UNESCAPED_UNICODE), 'auto' => $f[3], 'active' => 1, 'created' => self::now(), 'updated' => self::now()));
        }
        $db->query("REPLACE INTO `$meta` (k, v) VALUES ('flows_seeded', '1')");
    }
}
