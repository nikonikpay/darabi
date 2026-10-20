<?php
if (!defined('ABSPATH')) { exit; }

/**
 * Messages from the shop to the systems that run the app. The app asks (no key, only its random installation id) every eight hours (three times a day at most): GET mazesta/v1/messages?install=ID&after=N
 * answers what was sent to every system or to that one with an id above N, and the app shows each as a Windows notification and keeps it on its "System messages" page.
 * Kept in the CRM's database: 'messages' (target 'all', 'group' with its hardware search in grp, or 'some') and 'message_systems' (the chosen systems of a 'some');
 * a message to every system or to a group reaches the ones that ask later too, for ninety days.
 * What the app has already fetched is its 'after' of the next call: that is how the page here knows which systems have it.
 */
final class MZC_Messages
{
    const KEEP_DAYS = 90;
    const MAX = 500;

    public static function boot()
    {
        add_action('rest_api_init', array(__CLASS__, 'routes'));
        add_action('admin_post_mzc_message_send', array(__CLASS__, 'act_send'));
        add_action('admin_post_mzc_message_delete', array(__CLASS__, 'act_delete'));
    }

    public static function routes()
    {
        register_rest_route(Mazesta_Connect::NS, '/messages', array('methods' => 'GET', 'callback' => array(__CLASS__, 'rest_get'), 'permission_callback' => '__return_true'));
    }

    /** The messages of the last ninety days, newest first, each with 'to': 'all', array('q' => search) or the chosen ids. */
    public static function all($after = 0)
    {
        $db = Mazesta_Connect::db(); if (!$db) { return array(); }
        $rows = (array) $db->get_results($db->prepare('SELECT * FROM `' . Mazesta_Connect::t('messages') . '` WHERE id > %d AND created >= %s ORDER BY id DESC LIMIT %d', $after, gmdate('Y-m-d H:i:s', strtotime('-' . self::KEEP_DAYS . ' days')), self::MAX), ARRAY_A);
        $some = array(); foreach ($rows as $r) { if ($r['target'] === 'some') { $some[] = (int) $r['id']; } }
        $chosen = array();
        if ($some) { foreach ((array) $db->get_results('SELECT message_id, sys FROM `' . Mazesta_Connect::t('message_systems') . '` WHERE message_id IN (' . implode(',', $some) . ')', ARRAY_A) as $x) { $chosen[$x['message_id']][] = $x['sys']; } }
        $out = array();
        foreach ($rows as $r) {
            $to = $r['target'] === 'group' ? array('q' => $r['grp']) : ($r['target'] === 'some' ? (isset($chosen[$r['id']]) ? $chosen[$r['id']] : array()) : 'all');
            $out[(int) $r['id']] = array('id' => (int) $r['id'], 'title' => $r['title'], 'body' => (string) $r['body'], 'link' => $r['link'], 'to' => $to, 'created' => Mazesta_Connect::iso($r['created']), 'by' => (int) $r['by_user']);
        }
        return $out;
    }

    public static function rest_get($req)
    {
        $id = (string) $req->get_param('install'); $after = (int) $req->get_param('after');
        if (!preg_match('/^[a-f0-9]{32}$/', $id)) { return new WP_Error('mazesta_msg', 'Not understood.', array('status' => 400)); }
        if (!Mazesta_Connect::allowed('msg', 240)) { return new WP_Error('mazesta_busy', 'Too many calls from this address; try again in an hour.', array('status' => 429)); }
        if (!Mazesta_Connect::db()) { return Mazesta_Connect::no_db(); }
        $out = array();
        foreach (self::all($after) as $m) {
            if (!self::for_system($m, $id)) { continue; }
            $out[] = array('id' => (int) $m['id'], 'title' => $m['title'], 'body' => $m['body'], 'link' => $m['link'], 'created' => $m['created']);
        }
        usort($out, function ($a, $b) { return $a['id'] <=> $b['id']; });
        // The 'after' a system sends is the newest message it holds: remembered only when it moved, so the usual call writes nothing.
        if ($after > 0) { MZC_Stats::ack($id, $after); }
        return Mazesta_Connect::fresh(array('messages' => array_slice($out, 0, 20)));
    }

    /** Whether a message is for a system: every system, a group (a hardware search, matched when the system asks, so a system that joins later gets it too) or chosen ids. */
    public static function for_system($m, $id)
    {
        if ($m['to'] === 'all') { return true; }
        if (is_array($m['to']) && isset($m['to']['q'])) { return MZC_Stats::matches($id, $m['to']['q']); }
        return is_array($m['to']) && in_array($id, $m['to'], true);
    }

    /** How many of the targeted systems have fetched this message: array(delivered, targeted). */
    public static function delivery($m)
    {
        if ($m['to'] === 'all') { return MZC_Stats::delivered((int) $m['id']); }
        if (isset($m['to']['q'])) { return MZC_Stats::delivered((int) $m['id'], $m['to']['q']); }
        $got = 0; foreach (MZC_Stats::names((array) $m['to']) as $s) { if ($s['ack'] >= (int) $m['id']) { $got++; } }
        return array($got, count((array) $m['to']));
    }

    public static function act_send()
    {
        MZC_Admin::guard('message_send');
        $title = Mazesta_Connect::clip(isset($_POST['title']) ? wp_unslash($_POST['title']) : '', 120);
        $body = Mazesta_Connect::clip(isset($_POST['body']) ? wp_unslash($_POST['body']) : '', 2000);
        $link = isset($_POST['link']) ? esc_url_raw(trim((string) wp_unslash($_POST['link']))) : '';
        if ($link !== '' && !preg_match('#^https://(www\.)?dfmrendering\.com(/|$)#i', $link)) { $link = ''; }   // the app only opens the shop's own pages
        $to = 'all'; $target = isset($_POST['target']) ? (string) $_POST['target'] : 'all';
        if ($target === 'group') {
            $q = Mazesta_Connect::clip(isset($_POST['group']) ? wp_unslash($_POST['group']) : '', 120);
            if ($q === '') { MZC_Admin::back('mzc-messages', 'err:برای گروه، عبارت سخت‌افزار (مثلاً 3060) را بنویسید.'); }
            list(, $n) = MZC_Stats::systems($q, 'last', 1);
            if (!$n) { MZC_Admin::back('mzc-messages', 'err:هیچ سیستمی با «' . $q . '» پیدا نشد.'); }
            $to = array('q' => $q);
        } elseif ($target === 'some') {
            $to = array(); $ids = array_filter((array) (isset($_POST['systems']) ? wp_unslash($_POST['systems']) : array()), 'is_string');
            foreach (array_keys(MZC_Stats::names($ids)) as $sid) { $to[] = $sid; }
            if (!$to) { MZC_Admin::back('mzc-messages', 'err:هیچ سیستمی انتخاب نشده است.'); }
        }
        if ($title === '' && $body === '') { MZC_Admin::back('mzc-messages', 'err:عنوان یا متن پیام را بنویسید.'); }
        $db = Mazesta_Connect::db(); if (!$db) { MZC_Admin::back('mzc-messages', 'err:' . (MZC_Crm_Db::error() ?: 'پایگاه دادهٔ CRM آماده نیست.')); }
        // the id is AUTO_INCREMENT: never reused, not even after the newest message was deleted (the app keeps the highest id it holds)
        $ok = $db->insert(Mazesta_Connect::t('messages'), array('title' => $title, 'body' => $body, 'link' => $link !== '' ? $link : null, 'target' => $to === 'all' ? 'all' : (isset($to['q']) ? 'group' : 'some'),
            'grp' => isset($to['q']) ? $to['q'] : null, 'created' => gmdate('Y-m-d H:i:s'), 'by_user' => get_current_user_id()));
        if (!$ok) { MZC_Admin::back('mzc-messages', 'err:پیام ذخیره نشد: ' . $db->last_error); }
        if (is_array($to) && !isset($to['q'])) { $mid = (int) $db->insert_id; foreach ($to as $sid) { $db->insert(Mazesta_Connect::t('message_systems'), array('message_id' => $mid, 'sys' => $sid)); } }
        MZC_Admin::back('mzc-messages', 'ok:پیام ثبت شد؛ هر سیستم در نوبت بعدی پرسش خود (روزی حداکثر سه بار) آن را می‌گیرد.');
    }

    public static function act_delete()
    {
        MZC_Admin::guard('message_delete');
        $id = isset($_GET['id']) ? (int) $_GET['id'] : 0;
        if ($db = Mazesta_Connect::db()) { $db->delete(Mazesta_Connect::t('message_systems'), array('message_id' => $id)); $db->delete(Mazesta_Connect::t('messages'), array('id' => $id)); }
        MZC_Admin::back('mzc-messages', 'ok:پیام پاک شد (سیستم‌هایی که قبلاً گرفته‌اند نسخهٔ خودشان را دارند).');
    }
}
