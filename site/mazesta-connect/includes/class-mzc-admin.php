<?php
if (!defined('ABSPATH')) { exit; }

/** The plugin's menus and the shell every admin page sits in (header, section links, notices), plus the small helpers the pages share. The pages themselves are admin/*.php. */
final class MZC_Admin
{
    private static $pages = array(
        'mzc-overview' => array('نمای کلی', 'overview'), 'mzc-systems' => array('سیستم‌ها', 'systems'), 'mzc-events' => array('رویدادها', 'events'), 'mzc-tuning' => array('آندرولت و اورکلاک', 'tuning'), 'mzc-messages' => array('پیام‌ها', 'messages'),
        'mzc-customers' => array('مشتریان', 'customers'), 'mzc-builds' => array('سیستم‌های نو', 'builds'), 'mzc-jobs' => array('سرویس‌ها', 'jobs'), 'mzc-report' => array('گزارش مالی و آمار', 'report'), 'mzc-stock' => array('انبار و کالاها', 'stock'), 'mzc-purchases' => array('فاکتورهای خرید', 'purchases'), 'mzc-ships' => array('ارسال به گارانتی', 'ships'), 'mzc-staff' => array('کاربران و دسترسی', 'staff'),
        'mazesta-connect' => array('گزارش‌ها و بنچمارک', null), 'mzc-settings' => array('تنظیمات', 'settings'),
    );

    public static function boot()
    {
        add_action('admin_menu', array(__CLASS__, 'menu'));
        add_action('admin_enqueue_scripts', array(__CLASS__, 'assets'));
        add_action('admin_post_mzc_system_note', array(__CLASS__, 'act_system_note'));
        add_action('admin_post_mzc_cols_save', array(__CLASS__, 'act_cols_save'));
        MZC_Crm::boot();
    }

    public static function cap() { return class_exists('WooCommerce') ? 'manage_woocommerce' : 'manage_options'; }

    public static function menu()
    {
        add_menu_page('Mazesta', 'Mazesta', self::cap(), 'mzc-overview', array(__CLASS__, 'render'), 'dashicons-desktop', 58);
        foreach (self::$pages as $slug => $p) {
            add_submenu_page('mzc-overview', $p[0], $p[0], self::cap(), $slug, $p[1] === null ? array('Mazesta_Connect', 'page') : array(__CLASS__, 'render'));
        }
    }

    public static function assets($hook)
    {
        if (strpos((string) $hook, 'mzc-') === false && strpos((string) $hook, 'mazesta-connect') === false) { return; }
        wp_enqueue_style('mzc-admin', MZC_URL . 'assets/admin.css', array(), MZC_VERSION);
        wp_enqueue_script('mzc-admin', MZC_URL . 'assets/admin.js', array(), MZC_VERSION, true);
    }

    public static function render()
    {
        $slug = isset($_GET['page']) ? sanitize_key($_GET['page']) : 'mzc-overview';
        if (!Mazesta_Connect::can()) { wp_die('اجازه دیدن این صفحه را ندارید.', '', array('response' => 403)); }
        if (!isset(self::$pages[$slug]) || self::$pages[$slug][1] === null) { $slug = 'mzc-overview'; }
        self::open($slug, self::$pages[$slug][0]);
        require MZC_DIR . 'admin/' . self::$pages[$slug][1] . '.php';
        self::close();
    }

    /** The page's opening: the header, the section links, and a notice carried in the address (?mzc_msg=ok:… / err:…). */
    public static function open($slug, $title)
    {
        echo '<div class="mzc"><div class="mzc-head"><span class="mark">M</span><div><h1>Mazesta Connect</h1><div class="sub">سیستم‌ها، آمار، مشتریان و سرویس‌ها</div></div><span class="ver lat">v' . esc_html(MZC_VERSION) . '</span></div><nav class="mzc-nav">';
        foreach (self::$pages as $s => $p) { echo '<a class="' . ($s === $slug ? 'on' : '') . '" href="' . esc_url(admin_url('admin.php?page=' . $s)) . '">' . esc_html($p[0]) . '</a>'; }
        echo '</nav>';
        if (!empty($_GET['mzc_msg'])) {
            $m = sanitize_text_field(wp_unslash($_GET['mzc_msg']));
            $kind = strpos($m, 'err:') === 0 ? 'err' : 'ok';
            echo '<div class="mzc-notice ' . $kind . '">' . esc_html(preg_replace('/^(ok|err):/', '', $m)) . '</div>';
        }
        if (!Mazesta_Connect::ensure()) { echo '<div class="mzc-notice err">پوشهٔ داده‌ها ساخته نشد: <code>' . esc_html(Mazesta_Connect::dir()) . '</code>. دسترسی نوشتن wp-content را بررسی کنید.</div>'; }
    }

    public static function close() { echo '</div>'; }

    public static function guard($action, $cap = '')
    {
        if (!Mazesta_Connect::can() || ($cap !== '' && !current_user_can($cap))) { wp_die('اجازه این کار را ندارید.', '', array('response' => 403)); }
        check_admin_referer('mzc_' . $action);
    }

    /** Back to a page with a notice ('ok:…' or 'err:…') and extra address arguments. */
    public static function back($slug, $msg = '', $args = array())
    {
        wp_safe_redirect(add_query_arg(array('page' => $slug, 'mzc_msg' => $msg) + $args, admin_url('admin.php')));
        exit;
    }

    public static function url($slug, $args = array()) { return add_query_arg(array('page' => $slug) + $args, admin_url('admin.php')); }
    public static function post_url($action, $args = array()) { return wp_nonce_url(add_query_arg(array('action' => 'mzc_' . $action) + $args, admin_url('admin-post.php')), 'mzc_' . $action); }

    public static function form_open($action, $extra = '')
    {
        return '<form method="post" action="' . esc_url(admin_url('admin-post.php')) . '" ' . $extra . '><input type="hidden" name="action" value="mzc_' . esc_attr($action) . '">' . wp_nonce_field('mzc_' . $action, '_wpnonce', true, false);
    }

    /** The sections of an imported form that have no column of their own (checklists, benchmarks, software ...) as a folded list. */
    public static function more($more, $title = 'جزئیات فرم قدیمی')
    {
        if (!$more || !is_array($more)) { return ''; }
        $o = '<details class="mzc-more"><summary>' . esc_html($title) . '</summary><dl class="mzc-kv">';
        foreach ($more as $sec) {
            if (empty($sec['r'])) { continue; }
            foreach ($sec['r'] as $k => $v) { $o .= '<dt>' . esc_html($k) . '</dt><dd dir="auto">' . nl2br(esc_html(is_array($v) ? implode('، ', $v) : (string) $v)) . '</dd>'; }
        }
        return $o . '</dl></details>';
    }

    public static function pill($text, $kind = '') { return '<span class="mzc-pill ' . esc_attr($kind) . '">' . esc_html($text) . '</span>'; }

    /** "1405/07/18 14:32" in the site's time zone. */
    public static function when($ts)
    {
        if (!$ts) { return '—'; }
        return '<span class="num">' . esc_html(MZC_Crm::jdate(wp_date('Y-m-d', $ts)) . ' ' . wp_date('H:i', $ts)) . '</span>';
    }

    public static function ago($ts)
    {
        if (!$ts) { return '—'; }
        $d = time() - $ts;
        if ($d < 90) { return 'همین حالا'; } if ($d < 3600) { return floor($d / 60) . ' دقیقه پیش'; } if ($d < 172800) { return floor($d / 3600) . ' ساعت پیش'; }
        return floor($d / 86400) . ' روز پیش';
    }

    public static function num($n, $d = 0) { return $n === null ? '—' : '<span class="num">' . esc_html(number_format_i18n((float) $n, $d)) . '</span>'; }

    public static function pager($total, $per, $paged, $args = array())
    {
        $pages = (int) ceil($total / $per);
        if ($pages < 2) { return ''; }
        $out = '<div class="mzc-pager">';
        $show = array_unique(array_merge(array(1, $pages), range(max(1, $paged - 2), min($pages, $paged + 2))));
        sort($show); $last = 0;
        foreach ($show as $p) {
            if ($last && $p > $last + 1) { $out .= '<span>…</span>'; }
            $out .= $p === $paged ? '<span class="cur">' . $p . '</span>' : '<a href="' . esc_url(add_query_arg($args + array('paged' => $p))) . '">' . $p . '</a>';
            $last = $p;
        }
        return $out . '</div>';
    }

    /** A bar list of (label => count). */
    public static function bars($rows, $empty = 'هنوز داده‌ای نیست.')
    {
        if (!$rows) { return '<p class="muted">' . esc_html($empty) . '</p>'; }
        $max = max($rows); $out = '<div class="mzc-bars">';
        foreach ($rows as $l => $c) { $out .= '<div class="r"><span class="l" title="' . esc_attr((string) $l) . '" dir="auto">' . esc_html((string) $l) . '</span><span class="t"><i style="width:' . (int) round($c / $max * 100) . '%"></i></span><span class="c num">' . esc_html(number_format_i18n($c)) . '</span></div>'; }
        return $out . '</div>';
    }

    public static function act_system_note()
    {
        self::guard('system_note');
        $id = isset($_POST['id']) ? (string) wp_unslash($_POST['id']) : '';
        $note = Mazesta_Connect::clip(isset($_POST['note']) ? wp_unslash($_POST['note']) : '', 300);
        MZC_Stats::set_note($id, $note);
        self::back('mzc-systems', 'ok:یادداشت ذخیره شد.', array('sys' => $id));
    }

    /** The CRM pages need the second database; says what is missing and returns false when it is not ready. */
    /* ---------- list tables with columns the user chooses (which ones, in what order; kept per user) ---------- */

    /** The keys of the columns to show: the user's saved choice (only keys the page still has), else the page's defaults. $defs = key => array(label, default?, cell callback, class). */
    public static function cols($page, $defs)
    {
        $saved = get_user_meta(get_current_user_id(), 'mzc_cols_' . $page, true); $keys = array();
        if (is_array($saved)) { foreach ($saved as $k) { if (is_string($k) && isset($defs[$k]) && !in_array($k, $keys, true)) { $keys[] = $k; } } }
        if (!$keys) { foreach ($defs as $k => $d) { if (!empty($d[1])) { $keys[] = $k; } } }
        return $keys;
    }

    /** The "columns" menu above a list: a tick and a position number for each column. */
    public static function cols_ui($page, $defs)
    {
        $cur = self::cols($page, $defs); $order = array_merge($cur, array_diff(array_keys($defs), $cur));
        $o = '<details class="mzc-cols"><summary class="mzc-btn sm">ستون‌ها</summary>' . self::form_open('cols_save') . '<input type="hidden" name="page_slug" value="' . esc_attr($page) . '"><div class="list">';
        foreach ($order as $i => $k) {
            $o .= '<div class="row"><label><input type="checkbox" name="show[' . esc_attr($k) . ']" value="1"' . checked(in_array($k, $cur, true), true, false) . '> ' . esc_html($defs[$k][0]) . '</label>'
                . '<input type="number" name="ord[' . esc_attr($k) . ']" value="' . (int) ($i + 1) . '" min="1" max="99" aria-label="ترتیب"></div>';
        }
        return $o . '</div><p><button class="mzc-btn primary sm">ذخیره</button> <button class="mzc-btn sm" name="reset" value="1">پیش‌فرض</button></p></form></details>';
    }

    public static function act_cols_save()
    {
        self::guard('cols_save');
        $page = isset($_POST['page_slug']) ? sanitize_key(wp_unslash($_POST['page_slug'])) : '';
        if (!isset(self::$pages[$page])) { wp_die('صفحه نامعتبر است.', '', array('response' => 400)); }
        $meta = 'mzc_cols_' . $page; $uid = get_current_user_id();
        if (!empty($_POST['reset'])) { delete_user_meta($uid, $meta); self::back($page, 'ok:ستون‌ها به حالت پیش‌فرض برگشت.'); }
        $show = isset($_POST['show']) && is_array($_POST['show']) ? array_keys($_POST['show']) : array();
        $ord = isset($_POST['ord']) && is_array($_POST['ord']) ? $_POST['ord'] : array(); $keys = array();
        foreach ($show as $k) { $k = sanitize_key((string) $k); if ($k !== '' && strlen($k) <= 24) { $keys[$k] = isset($ord[$k]) ? (int) $ord[$k] : 50; } }
        asort($keys);
        if ($keys) { update_user_meta($uid, $meta, array_slice(array_keys($keys), 0, 20)); self::back($page, 'ok:ستون‌ها ذخیره شد.'); }
        self::back($page, 'err:دست‌کم یک ستون را نگه دارید.');
    }

    /** The table of a list page. A cell callback returns escaped HTML; $link($row) is where a click on the row goes. */
    public static function table($page, $defs, $rows, $link, $empty)
    {
        $keys = self::cols($page, $defs);
        $o = '<div class="mzc-panel"><div class="body flush mzc-scroll"><table class="mzc-table"><thead><tr>';
        foreach ($keys as $k) { $o .= '<th' . (!empty($defs[$k][3]) ? ' class="' . esc_attr($defs[$k][3]) . '"' : '') . '>' . esc_html($defs[$k][0]) . '</th>'; }
        $o .= '</tr></thead><tbody>';
        if (!$rows) { $o .= '<tr><td class="empty" colspan="' . count($keys) . '">' . esc_html($empty) . '</td></tr>'; }
        foreach ($rows as $r) {
            $o .= '<tr class="click" data-href="' . esc_url($link($r)) . '">';
            foreach ($keys as $k) { $o .= '<td' . (!empty($defs[$k][3]) ? ' class="' . esc_attr($defs[$k][3]) . '"' : '') . '>' . call_user_func($defs[$k][2], $r) . '</td>'; }
            $o .= '</tr>';
        }
        return $o . '</tbody></table></div></div>';
    }

    /** A link to the app's report for a list cell (opens in a tab; the row itself does not follow the click). */
    public static function report_link($id)
    {
        return $id ? '<a href="' . esc_url(admin_url('admin-post.php?action=mzc_report&id=' . rawurlencode((string) $id))) . '" target="_blank" rel="noopener" onclick="event.stopPropagation()">گزارش</a>' : '<span class="muted">—</span>';
    }

    public static function need_crm()
    {
        if (MZC_Crm_Db::ready()) { return true; }
        $e = MZC_Crm_Db::error();
        echo '<div class="mzc-notice warn">' . esc_html($e !== '' ? $e : 'جدول‌های CRM هنوز ساخته نشده‌اند.') . ' <a href="' . esc_url(self::url('mzc-settings')) . '">رفتن به تنظیمات</a></div>';
        return false;
    }

    /** A table of event rows (see MZC_Stats::row). */
    public static function events_table($rows, $withSystem = true, $installs = array())
    {
        $out = '<div class="mzc-scroll"><table class="mzc-table"><thead><tr><th>زمان</th>' . ($withSystem ? '<th>سیستم</th>' : '') . '<th>نوع</th><th>مورد</th><th>نتیجه</th><th>کارت گرافیک</th><th class="n">دما CPU / GPU</th><th class="n">فرکانس CPU / GPU</th><th class="n">FPS</th><th class="n">امتیاز / توان</th><th>توضیح</th></tr></thead><tbody>';
        if (!$rows) { $out .= '<tr><td class="empty" colspan="' . ($withSystem ? 11 : 10) . '">رویدادی پیدا نشد.</td></tr>'; }
        if ($withSystem && !$installs) { $installs = MZC_Stats::names(array_column($rows, 'id')); }
        $t = function ($v, $u) { return $v === null ? '—' : '<span class="num">' . esc_html(round($v) . $u) . '</span>'; };
        foreach ($rows as $r) {
            $pill = $r['result'] === '' ? '' : self::pill($r['result'], $r['ok'] === true ? 'ok' : ($r['ok'] === false ? 'bad' : ''));
            $sys = '';
            if ($withSystem) { $i = isset($installs[$r['id']]) ? $installs[$r['id']] : null; $sys = '<td><a href="' . esc_url(self::url('mzc-systems', array('sys' => $r['id']))) . '" dir="auto">' . esc_html($i ? MZC_Stats::label($i) : substr($r['id'], 0, 8)) . '</a></td>'; }
            $score = $r['score'] !== null ? '<span class="num">' . esc_html(number_format_i18n($r['score'], $r['score'] < 100 ? 1 : 0)) . '</span> <small class="muted">' . esc_html($r['scoreUnit']) . '</small>' : ($r['power'] !== null ? '<span class="num">' . esc_html(round($r['power'])) . ' W</span>' : '—');
            $json = wp_json_encode($r['d'], JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_PRETTY_PRINT);
            $out .= '<tr><td>' . self::when($r['ts']) . '</td>' . $sys . '<td>' . esc_html(MZC_Stats::kind_label($r['kind'])) . '</td><td dir="auto">' . esc_html($r['item']) . '</td><td>' . $pill . '</td><td dir="auto">' . esc_html($r['gpu'] !== '' ? $r['gpu'] : '—') . '</td>'
                . '<td class="n">' . $t($r['cpuTemp'], '°') . ' / ' . $t($r['gpuTemp'], '°') . '</td><td class="n">' . $t($r['cpuClock'], '') . ' / ' . $t($r['gpuClock'], '') . '</td>'
                . '<td class="n">' . ($r['fps'] !== null ? '<span class="num">' . esc_html(number_format_i18n($r['fps'], 1)) . '</span>' : '—') . '</td><td class="n">' . $score . '</td>'
                . '<td>' . ($r['note'] !== '' ? '<span dir="auto">' . esc_html(mb_substr($r['note'], 0, 160)) . '</span> ' : '') . '<details class="mzc-details"><summary>جزئیات</summary><pre>' . esc_html((string) $json) . '</pre></details></td></tr>';
        }
        return $out . '</tbody></table></div>';
    }

    /** The customer fields of a build or job form: the chosen customer, or a mobile (and name) that finds or makes one. */
    public static function customer_picker($customer = null, $prefill = '')
    {
        if ($customer) {
            return '<input type="hidden" name="customer_id" value="' . (int) $customer['id'] . '"><div class="mzc-field wide"><span>مشتری</span><div><strong>' . esc_html($customer['name']) . '</strong> <span class="num muted">' . esc_html($customer['mobile']) . '</span> '
                . '<a href="' . esc_url(self::url('mzc-customers', array('view' => (int) $customer['id']))) . '">پروندهٔ مشتری</a></div></div>';
        }
        // admin.js searches as one types the name or a number (any of the customer's numbers) and offers the closest customers; picking one fills customer_id
        return '<div class="mzc-field wide mzc-cfind" data-mzc-cfind="' . esc_url(admin_url('admin-ajax.php?action=mzc_customer_find&n=' . wp_create_nonce('mzc_customer_find'))) . '"><input type="hidden" name="customer_id" value="">'
            . '<div class="mzc-fields"><label>نام مشتری<input type="text" name="c_name" maxlength="190" autocomplete="off" data-mzc-cq><small>با نوشتن نام یا موبایل، مشتری‌های ثبت‌شده پیشنهاد می‌شوند.</small></label>'
            . '<label>موبایل مشتری<input type="text" name="c_mobile" dir="ltr" placeholder="09121234567" value="' . esc_attr($prefill) . '" autocomplete="off" data-mzc-cq required></label>'
            . '<label>موبایل دوم (اختیاری)<input type="text" name="c_mobile2" dir="ltr" autocomplete="off"></label></div>'
            . '<div class="mzc-cres" hidden></div><div class="mzc-cpicked" hidden></div></div>';
    }

    /** The programs the customer wants installed (rows picked from the shop's list or typed freely) and their own request in words. */
    public static function soft_block($json)
    {
        $s = MZC_Crm::soft_out($json);
        $row = function ($n, $x) {
            return '<div class="mzc-row"><input type="text" name="soft[' . $n . '][name]" value="' . esc_attr($x['name']) . '" placeholder="نام نرم‌افزار (از فهرست یا نام تازه)" maxlength="80" list="mzc-soft" dir="auto">'
                . '<label class="chk"><input type="checkbox" name="soft[' . $n . '][done]" value="1"' . checked(!empty($x['done']), true, false) . '> نصب و اجرا شد</label><button type="button" class="mzc-btn sm danger x" data-mzc-remove>حذف</button></div>';
        };
        $o = '<div class="mzc-field wide"><span>نرم‌افزارهای درخواستی مشتری' . ($s['by_customer'] !== '' ? ' ' . self::pill('مشتری خودش پر کرده: ' . MZC_Crm::jdate($s['by_customer']), 'info') : '') . '</span><div class="mzc-rows" id="mzc-soft-rows" data-next="' . count($s['items']) . '">';
        foreach ($s['items'] as $n => $x) { $o .= $row($n, $x); }
        $o .= '</div><template id="mzc-soft-rows-tpl">' . str_replace('[0]', '[__N__]', $row(0, array('name' => '', 'done' => false))) . '</template><p><button type="button" class="mzc-btn" data-mzc-add="mzc-soft-rows">+ نرم‌افزار</button> <small class="muted">اگر در فهرست نیست، نامش را کامل بنویسید.</small></p>'
            . '<label>درخواست‌های نرم‌افزاری (توضیح)<textarea name="soft_note" rows="2" dir="auto">' . esc_textarea($s['note']) . '</textarea></label><datalist id="mzc-soft">';
        foreach (MZC_Crm::software_catalog() as $n) { $o .= '<option value="' . esc_attr($n) . '">'; }
        return $o . '</datalist></div>';
    }

    public static function soft_view($json)
    {
        $s = MZC_Crm::soft_out($json);
        if (!$s['items'] && $s['note'] === '') { return ''; }
        $o = '<div class="mzc-panel"><header><h3>نرم‌افزارهای درخواستی</h3>' . ($s['by_customer'] !== '' ? self::pill('مشتری خودش پر کرده: ' . MZC_Crm::jdate($s['by_customer']), 'info') : '') . '</header><div class="body"><p>';
        foreach ($s['items'] as $x) { $o .= self::pill($x['name'] . ($x['done'] ? ' ✓' : ''), $x['done'] ? 'ok' : '') . ' '; }
        return $o . '</p>' . ($s['note'] !== '' ? '<p dir="auto">' . nl2br(esc_html($s['note'])) . '</p>' : '') . '</div></div>';
    }

    public static function report_select($selected, $service = '')
    {
        $opts = MZC_Crm::report_options($service);
        $out = '<label class="wide">گزارش تست برنامه<select name="report_id"><option value="">— بدون گزارش —</option>';
        foreach ($opts as $id => $o) { $out .= '<option value="' . esc_attr($id) . '"' . selected($selected, $id, false) . '>' . esc_html(($o['match'] ? '★ ' : '') . $o['label']) . '</option>'; }
        return $out . '</select><small>گزارش‌هایی که با «ارسال به سایت» از برنامه رسیده‌اند. ★ یعنی شمارهٔ سرویسش با شمارهٔ این فرم یکی است.</small></label>';
    }

    /** The stock products a part row can be picked from (an <input list="mzc-products">); admin.js fills the row's product id, category and prices from the chosen one. */
    public static function product_list()
    {
        $o = '<datalist id="mzc-products">';
        foreach (MZC_Stock::options() as $p) {
            $o .= '<option value="' . esc_attr($p['name']) . '" data-id="' . (int) $p['id'] . '" data-cat="' . esc_attr($p['category']) . '" data-sell="' . esc_attr($p['sell_price']) . '" data-buy="' . esc_attr($p['buy_price']) . '" data-w="' . esc_attr($p['warranty_months']) . '">'
                . esc_html('موجودی ' . (int) $p['stock'] . ($p['sell_price'] !== null ? ' — ' . number_format((int) $p['sell_price']) . ' تومان' : '')) . '</option>';
        }
        return $o . '</datalist>';
    }

    /** The device-type choice and the Telegram links of a build or job form. */
    public static function device_select($cur, $none = false)
    {
        $o = '<label>نوع دستگاه<select name="device_type">' . ($none ? '<option value="">—</option>' : '');
        foreach (MZC_Crm::$device_types as $k => $l) { $o .= '<option value="' . esc_attr($k) . '"' . selected((string) $cur, $k, false) . '>' . esc_html($l) . '</option>'; }
        return $o . '</select></label>';
    }

    public static function media_field($media)
    {
        return '<label class="wide">لینک عکس‌ها و فیلم‌ها در تلگرام<textarea name="media" rows="2" dir="ltr" placeholder="https://t.me/…  (هر لینک در یک خط)">' . esc_textarea((string) $media) . '</textarea><small>پست‌های کانال تلگرام همین سیستم یا سرویس.</small></label>';
    }

    public static function media_links($media)
    {
        $l = MZC_Crm::media_list((string) $media);
        return $l ? implode(' · ', array_map(function ($u, $i) { return '<a href="' . esc_url($u) . '" target="_blank" rel="noopener noreferrer">لینک ' . ($i + 1) . '</a>'; }, $l, array_keys($l))) : '—';
    }

    public static function photos_block($photos)
    {
        $out = '<div class="mzc-field wide"><span>عکس‌ها (تا ' . MZC_Crm::PHOTO_MAX . ' عکس؛ jpg / png / webp)</span>';
        if ($photos) {
            $out .= '<div class="mzc-photos">';
            foreach ($photos as $p) {
                $src = admin_url('admin-post.php?action=mzc_photo&id=' . (int) $p['id']);
                $out .= '<figure><a href="' . esc_url($src) . '" target="_blank"><img loading="lazy" src="' . esc_url($src) . '" alt=""></a>'
                    . '<a class="mzc-btn sm danger" href="' . esc_url(self::post_url('photo_delete', array('id' => (int) $p['id']))) . '" onclick="return confirm(\'این عکس پاک شود؟\')">پاک</a></figure>';
            }
            $out .= '</div>';
        }
        return $out . '<input type="file" name="photos[]" accept="image/jpeg,image/png,image/webp" multiple><input type="text" name="photo_caption" placeholder="توضیح کوتاه برای عکس‌های تازه (اختیاری)" maxlength="190"></div>';
    }
}
