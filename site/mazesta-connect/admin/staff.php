<?php
if (!defined('ABSPATH')) { exit; }
/** Who may use the CRM apps and what each role may do. Roles are a level (none / view / view and edit) for each area; a person can have several roles and gets the highest level of each. */

echo '<div class="mzc-title"><h2>کاربران و دسترسی</h2></div>';
if (!current_user_can('manage_options')) { echo '<div class="mzc-notice warn">فقط مدیر سایت می‌تواند کاربران و دسترسی‌ها را عوض کند.</div>'; return; }

$roles = MZC_Staff::roles();
$lv = function ($name, $cur) {
    $o = '<select name="' . esc_attr($name) . '">';
    foreach (MZC_Staff::$levels as $v => $l) { $o .= '<option value="' . (int) $v . '"' . selected((int) $cur, $v, false) . '>' . esc_html($l) . '</option>'; }
    return $o . '</select>';
};

/* ---- roles ---- */
echo '<div class="mzc-panel"><header><h3>نقش‌ها</h3></header><div class="body">'
    . '<p class="muted" style="max-width:90ch">برای هر نقش مشخص کنید در هر بخش چه کاری می‌تواند بکند. به هر کاربر می‌توانید چند نقش بدهید (مثلاً منشی و اسمبل)؛ در هر بخش بالاترین سطحِ نقش‌هایش به او می‌رسد. اگر نقش یا بخشی را «ندارد» بگذارید، آن بخش در برنامه برای او دیده نمی‌شود.</p>'
    . MZC_Admin::form_open('staff_roles_save', 'class="mzc-form"') . '<div class="mzc-scroll"><table class="mzc-table"><thead><tr><th>نقش</th>';
foreach (MZC_Staff::$areas as $a => $l) { echo '<th>' . esc_html($l) . '</th>'; }
echo '<th>حذف</th></tr></thead><tbody>';
foreach ($roles as $k => $r) {
    echo '<tr><td><input type="text" name="roles[' . esc_attr($k) . '][label]" value="' . esc_attr($r['label']) . '" maxlength="40"></td>';
    foreach (MZC_Staff::$areas as $a => $l) { echo '<td>' . $lv('roles[' . $k . '][p][' . $a . ']', isset($r['p'][$a]) ? $r['p'][$a] : 0) . '</td>'; }
    echo '<td><input type="checkbox" name="roles[' . esc_attr($k) . '][del]" value="1" aria-label="حذف"></td></tr>';
}
echo '<tr><td><input type="text" name="new_label" maxlength="40" placeholder="نقش تازه (نام)"></td>';
foreach (MZC_Staff::$areas as $a => $l) { echo '<td>' . $lv('new_p[' . $a . ']', 0) . '</td>'; }
echo '<td></td></tr></tbody></table></div><p><button class="mzc-btn primary">ذخیرهٔ نقش‌ها</button></p></form></div></div>';

/* ---- people ---- */
$roleBoxes = function ($have) use ($roles) {
    $o = '';
    foreach ($roles as $k => $r) { $o .= '<label style="display:inline-block;margin-inline-end:.8em"><input type="checkbox" name="roles[' . esc_attr($k) . ']" value="1"' . checked(in_array($k, $have, true), true, false) . '> ' . esc_html($r['label']) . '</label>'; }
    return $o !== '' ? $o : '<span class="muted">اول یک نقش بسازید.</span>';
};

echo '<div class="mzc-panel"><header><h3>کاربر تازه</h3></header><div class="body">'
    . '<p class="muted" style="max-width:90ch">کاربر یک حساب وردپرس است که فقط برای برنامه‌ها کار می‌کند و به پیشخوان سایت راه ندارد. اگر نام کاربریِ یک حساب موجود را بنویسید، همان حساب نقش می‌گیرد و گذرواژه‌اش عوض نمی‌شود (مگر گذرواژهٔ تازه بنویسید).</p>'
    . MZC_Admin::form_open('staff_save', 'class="mzc-form"') . '<div class="mzc-fields">'
    . '<label>نام کاربری (لاتین)<input type="text" name="username" dir="ltr" required autocomplete="off"></label>'
    . '<label>نام نمایشی<input type="text" name="display_name" maxlength="80"></label>'
    . '<label>گذرواژه (دست‌کم ۸ نویسه)<input type="password" name="password" dir="ltr" autocomplete="new-password"></label>'
    . '<div class="mzc-field wide"><span>نقش‌ها</span><div>' . $roleBoxes(array()) . '</div></div></div>'
    . '<p><button class="mzc-btn primary">افزودن</button></p></form></div></div>';

$users = MZC_Staff::users();
echo '<div class="mzc-panel"><header><h3>کاربران</h3></header><div class="body">';
if (!$users) { echo '<p class="muted">هنوز کاربری تعریف نشده است.</p>'; }
foreach ($users as $u) {
    $rec = MZC_Staff::record($u->ID); $p = MZC_Staff::perms_of($u->ID); $adm = MZC_Staff::is_admin($u->ID);
    $chips = '';
    foreach ($p as $a => $v) { if ($v > 0) { $chips .= MZC_Admin::pill(MZC_Staff::$areas[$a] . ': ' . MZC_Staff::$levels[$v], $v > 1 ? 'ok' : '') . ' '; } }
    echo '<details class="mzc-more" style="margin-bottom:.6em"><summary><strong dir="auto">' . esc_html($u->display_name !== '' ? $u->display_name : $u->user_login) . '</strong> <span class="lat muted">' . esc_html($u->user_login) . '</span> '
        . ($rec['on'] || $adm ? MZC_Admin::pill($adm ? 'مدیر سایت' : 'فعال', 'ok') : MZC_Admin::pill('خاموش', 'bad')) . '</summary>'
        . '<p>' . ($chips !== '' ? $chips : '<span class="muted">هیچ دسترسی‌ای ندارد.</span>') . '</p>'
        . MZC_Admin::form_open('staff_save', 'class="mzc-form"') . '<input type="hidden" name="uid" value="' . (int) $u->ID . '"><div class="mzc-fields">'
        . '<label>نام نمایشی<input type="text" name="display_name" maxlength="80" value="' . esc_attr($u->display_name) . '"></label>'
        . '<label>گذرواژهٔ تازه (خالی = بدون تغییر)<input type="password" name="password" dir="ltr" autocomplete="new-password"></label>'
        . '<div class="mzc-field wide"><span>نقش‌ها</span><div>' . $roleBoxes($rec['roles']) . '</div></div>'
        . '<label><input type="checkbox" name="on" value="1"' . checked($rec['on'], true, false) . '> فعال (خاموش = نمی‌تواند وارد شود)</label></div>'
        . '<p><button class="mzc-btn primary">ذخیره</button> <button class="mzc-btn danger" name="remove" value="1" onclick="return confirm(\'دسترسی این کاربر به CRM برداشته شود؟ حساب وردپرسی‌اش پاک نمی‌شود.\')">برداشتن دسترسی</button></p></form></details>';
}
echo '</div></div>';

echo '<div class="mzc-panel"><header><h3>نشانی برای برنامه‌ها</h3></header><div class="body"><p>برنامه‌ها با نام کاربری و گذرواژه به این نشانی وارد می‌شوند: <code class="lat">' . esc_html(rest_url(Mazesta_Connect::NS . '/staff/login')) . '</code></p>'
    . '<p class="muted">این مسیر نباید کش شود و سایت باید روی https باشد، وگرنه گذرواژه بی‌رمز می‌رود. هر ورود اشتباه شمرده می‌شود: شش بار اشتباه، آن نام کاربری را ۱۵ دقیقه قفل می‌کند.</p></div></div>';
