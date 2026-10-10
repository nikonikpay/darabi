<?php
if (!defined('ABSPATH')) { exit; }

/**
 * What the plugin shares with the rest of the site: the latest posts for the app (GET mazesta/v1/news: only published, public posts, cached, the app's own news box) and the
 * site accounts that belong to a customer (matched by mobile number; shown only to a user who may list users).
 */
final class MZC_Site
{
    public static function boot() { add_action('rest_api_init', array(__CLASS__, 'routes')); }

    public static function routes()
    {
        register_rest_route(Mazesta_Connect::NS, '/news', array('methods' => 'GET', 'callback' => array(__CLASS__, 'rest_news'), 'permission_callback' => '__return_true'));
    }

    /** The newest published posts: id, title, link, date (UTC), a short excerpt and the picture. Public data only; kept ten minutes so the app's calls cost one query an hour at most. */
    public static function rest_news($req)
    {
        if (!Mazesta_Connect::allowed('news', 120)) { return new WP_Error('mazesta_busy', 'Too many calls from this address; try again in an hour.', array('status' => 429)); }
        $after = max(0, (int) $req->get_param('after'));
        $all = get_transient('mzc_news');
        if (!is_array($all)) {
            $all = array();
            $q = new WP_Query(array('post_type' => 'post', 'post_status' => 'publish', 'has_password' => false, 'posts_per_page' => 10, 'no_found_rows' => true, 'ignore_sticky_posts' => true, 'orderby' => 'date', 'order' => 'DESC'));
            foreach ($q->posts as $p) {
                $all[] = array('id' => (int) $p->ID, 'title' => wp_strip_all_tags(get_the_title($p)), 'link' => get_permalink($p), 'date' => get_post_time('c', true, $p),
                    'excerpt' => mb_substr(trim(wp_strip_all_tags(strip_shortcodes(get_the_excerpt($p)))), 0, 220), 'image' => (string) get_the_post_thumbnail_url($p, 'medium'));
            }
            set_transient('mzc_news', $all, 600);
        }
        return Mazesta_Connect::fresh(array('news' => array_values(array_filter($all, function ($n) use ($after) { return $n['id'] > $after; }))));
    }

    /** The user accounts of this site whose phone is the customer's mobile (WooCommerce billing phone, or the login when it is the number). Empty for anyone who may not list users. */
    public static function accounts_for($customer)
    {
        if (!current_user_can('list_users')) { return array(); }
        $nums = array();
        foreach (array('mobile', 'mobile2') as $k) {
            $m = isset($customer[$k]) ? preg_replace('/\D/', '', (string) $customer[$k]) : '';
            if (strlen($m) === 11 && $m[0] === '0') { array_push($nums, $m, substr($m, 1), '98' . substr($m, 1), '+98' . substr($m, 1)); }
        }
        if (!$nums) { return array(); }
        $users = get_users(array('number' => 3, 'fields' => array('ID', 'display_name', 'user_email'), 'meta_query' => array('relation' => 'OR',
            array('key' => 'billing_phone', 'value' => $nums, 'compare' => 'IN'), array('key' => 'digits_phone', 'value' => $nums, 'compare' => 'IN')), 'search' => '', 'orderby' => 'ID'));
        $out = array();
        foreach ($users as $u) { $out[] = array('name' => $u->display_name, 'email' => $u->user_email, 'url' => get_edit_user_link((int) $u->ID)); }
        return $out;
    }
}
