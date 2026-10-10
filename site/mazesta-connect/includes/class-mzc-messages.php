<?php
if (!defined('ABSPATH')) { exit; }

/**
 * Messages from the shop to the systems that run the app. The app asks (no key, only its random installation id) every eight hours (three times a day at most): GET mazesta/v1/messages?install=ID&after=N
 * answers what was sent to every system or to that one with an id above N, and the app shows each as a Windows notification and keeps it on its "System messages" page.
 * The store is a file like the plugin's other data; a message to every system reaches the ones that ask later too, for ninety days.
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

    public static function all() { $m = Mazesta_Connect::read('messages'); krsort($m); return $m; }

    public static function rest_get($req)
    {
        $id = (string) $req->get_param('install'); $after = (int) $req->get_param('after');
        if (!preg_match('/^[a-f0-9]{32}$/', $id)) { return new WP_Error('mazesta_msg', 'Not understood.', array('status' => 400)); }
        if (!Mazesta_Connect::allowed('msg', 240)) { return new WP_Error('mazesta_busy', 'Too many calls from this address; try again in an hour.', array('status' => 429)); }
        $out = array();
        foreach (Mazesta_Connect::read('messages') as $m) {
            if ((int) $m['id'] <= $after) { continue; }
            if ($m['to'] !== 'all' && !(is_array($m['to']) && in_array($id, $m['to'], true))) { continue; }
            $out[] = array('id' => (int) $m['id'], 'title' => $m['title'], 'body' => $m['body'], 'link' => $m['link'], 'created' => $m['created']);
        }
        usort($out, function ($a, $b) { return $a['id'] <=> $b['id']; });
        // The 'after' a system sends is the newest message it holds: remembered only when it moved, so the usual call writes nothing.
        $acks = Mazesta_Connect::read('acks');
        if ($after > 0 && (!isset($acks[$id]) || (int) $acks[$id] < $after) && isset(MZC_Stats::installs()[$id])) {
            Mazesta_Connect::locked(function () use ($id, $after) { $a = Mazesta_Connect::read('acks'); if (!isset($a[$id]) || (int) $a[$id] < $after) { $a[$id] = $after; Mazesta_Connect::write('acks', $a); } });
        }
        return Mazesta_Connect::fresh(array('messages' => array_slice($out, 0, 20)));
    }

    /** How many of the targeted systems have fetched this message: array(delivered, targeted). */
    public static function delivery($m, $installs, $acks)
    {
        $targets = $m['to'] === 'all' ? array_keys($installs) : (array) $m['to'];
        $got = 0;
        foreach ($targets as $t) { if (isset($acks[$t]) && (int) $acks[$t] >= (int) $m['id']) { $got++; } }
        return array($got, count($targets));
    }

    public static function act_send()
    {
        MZC_Admin::guard('message_send');
        $title = Mazesta_Connect::clip(isset($_POST['title']) ? wp_unslash($_POST['title']) : '', 120);
        $body = Mazesta_Connect::clip(isset($_POST['body']) ? wp_unslash($_POST['body']) : '', 2000);
        $link = isset($_POST['link']) ? esc_url_raw(trim((string) wp_unslash($_POST['link']))) : '';
        if ($link !== '' && !preg_match('#^https://(www\.)?dfmrendering\.com(/|$)#i', $link)) { $link = ''; }   // the app only opens the shop's own pages
        $installs = MZC_Stats::installs();
        $to = 'all';
        if (isset($_POST['target']) && $_POST['target'] === 'some') {
            $to = array();
            foreach ((array) (isset($_POST['systems']) ? wp_unslash($_POST['systems']) : array()) as $sid) { if (is_string($sid) && isset($installs[$sid])) { $to[] = $sid; } }
            if (!$to) { MZC_Admin::back('mzc-messages', 'err:هیچ سیستمی انتخاب نشده است.'); }
        }
        if ($title === '' && $body === '') { MZC_Admin::back('mzc-messages', 'err:عنوان یا متن پیام را بنویسید.'); }
        Mazesta_Connect::locked(function () use ($title, $body, $link, $to) {
            $all = Mazesta_Connect::read('messages');
            $cut = gmdate('c', strtotime('-' . self::KEEP_DAYS . ' days'));
            foreach ($all as $k => $m) { if (strcmp((string) $m['created'], $cut) < 0) { unset($all[$k]); } }
            $seq = Mazesta_Connect::read('msgseq'); $next = (isset($seq['n']) ? (int) $seq['n'] : 0) + 1;   // an id is never reused, not even after the newest message was deleted
            foreach ($all as $m) { $next = max($next, (int) $m['id'] + 1); }
            Mazesta_Connect::write('msgseq', array('n' => $next));
            if (count($all) >= self::MAX) { ksort($all); array_shift($all); }
            $all[$next] = array('id' => $next, 'title' => $title, 'body' => $body, 'link' => $link !== '' ? $link : null, 'to' => $to, 'created' => gmdate('c'), 'by' => get_current_user_id());
            Mazesta_Connect::write('messages', $all);
        });
        MZC_Admin::back('mzc-messages', 'ok:پیام ثبت شد؛ هر سیستم در نوبت بعدی پرسش خود (روزی حداکثر سه بار) آن را می‌گیرد.');
    }

    public static function act_delete()
    {
        MZC_Admin::guard('message_delete');
        $id = isset($_GET['id']) ? (int) $_GET['id'] : 0;
        Mazesta_Connect::locked(function () use ($id) { $all = Mazesta_Connect::read('messages'); unset($all[$id]); Mazesta_Connect::write('messages', $all); });
        MZC_Admin::back('mzc-messages', 'ok:پیام پاک شد (سیستم‌هایی که قبلاً گرفته‌اند نسخهٔ خودشان را دارند).');
    }
}
