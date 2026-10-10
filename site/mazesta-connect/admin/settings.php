<?php
if (!defined('ABSPATH')) { exit; }
/** Settings of the CRM's own database and of the SMS sender. (The keys of the app, sharing and the release are on the Reports and benchmarks page, tab "تنظیمات و انتشار".) */

$admin = current_user_can('manage_options');
$db = MZC_Crm_Db::settings(); $ready = MZC_Crm_Db::ready();
$sms = MZC_Sms::settings();

echo '<div class="mzc-title"><h2>تنظیمات</h2></div>';
if (!$admin) { echo '<div class="mzc-notice warn">فقط مدیر سایت می‌تواند این تنظیمات را ببیند و عوض کند.</div>'; return; }

echo '<div class="mzc-panel"><header><h3>پایگاه دادهٔ CRM</h3>' . ($ready ? MZC_Admin::pill('وصل و آماده', 'ok') : MZC_Admin::pill('وصل نیست', 'bad')) . '</header><div class="body">'
    . '<p class="muted" style="max-width:90ch">اطلاعات مشتریان، سیستم‌ها و سرویس‌ها در یک پایگاه دادهٔ جدا از پایگاه دادهٔ وردپرس نگه داشته می‌شود. یک پایگاه دادهٔ MySQL خالی (و کاربری که به آن دسترسی کامل دارد) در هاست بسازید و مشخصاتش را اینجا وارد کنید؛ جدول‌ها خودکار ساخته می‌شوند. هیچ داده‌ای از CRM در پایگاه دادهٔ سایت نمی‌نشیند.</p>';
if (!$ready && MZC_Crm_Db::configured() && MZC_Crm_Db::error() !== '') { echo '<div class="mzc-notice err">' . esc_html(MZC_Crm_Db::error()) . '</div>'; }
echo MZC_Admin::form_open('db_save', 'class="mzc-form"') . '<div class="mzc-fields">'
    . '<label>میزبان (Host)<input type="text" name="host" dir="ltr" value="' . esc_attr($db['host']) . '"><small>معمولاً localhost</small></label>'
    . '<label>نام پایگاه داده<input type="text" name="name" dir="ltr" value="' . esc_attr($db['name']) . '" required></label>'
    . '<label>نام کاربری<input type="text" name="user" dir="ltr" value="' . esc_attr($db['user']) . '" required autocomplete="off"></label>'
    . '<label>گذرواژه<input type="password" name="pass" dir="ltr" autocomplete="new-password" placeholder="' . ($db['pass'] !== '' ? '•••••••• (بدون تغییر)' : '') . '"><small>رمزشده در پوشهٔ داده‌های افزونه نگه داشته می‌شود.</small></label>'
    . '<label>پیشوند جدول‌ها<input type="text" name="prefix" dir="ltr" value="' . esc_attr($db['prefix']) . '" maxlength="20"></label></div>'
    . '<div class="mzc-actions"><button class="mzc-btn primary">آزمایش اتصال و ذخیره</button>' . ($ready ? '<a class="mzc-btn" href="' . esc_url(MZC_Admin::post_url('db_install')) . '">بررسی دوبارهٔ جدول‌ها</a>' : '') . '</div></form></div></div>';

echo '<div class="mzc-panel"><header><h3>پیامک (ورود مشتری به پورتال)</h3>' . (MZC_Sms::configured() ? MZC_Admin::pill('فعال', 'ok') : MZC_Admin::pill('غیرفعال', 'warn')) . '</header><div class="body">'
    . '<p class="muted" style="max-width:90ch">مشتری در صفحه‌ای که کد کوتاه <code>[mazesta_portal]</code> را دارد شمارهٔ موبایلش را می‌نویسد و کد شش‌رقمی را با پیامک می‌گیرد. تا درگاه پیامک تنظیم نشده پورتال کار نمی‌کند.</p>'
    . MZC_Admin::form_open('sms_save', 'class="mzc-form"') . '<div class="mzc-fields"><label>درگاه<select name="driver"><option value="">— غیرفعال —</option><option value="kavenegar"' . selected($sms['driver'], 'kavenegar', false) . '>کاوه‌نگار</option>'
    . '<option value="url"' . selected($sms['driver'], 'url', false) . '>نشانی وب دلخواه (هر درگاهی که با یک درخواست HTTP کار کند)</option></select></label>'
    . '<label>کلید API (کاوه‌نگار)<input type="password" name="key" dir="ltr" autocomplete="new-password" placeholder="' . ($sms['key'] !== '' ? '•••••••• (بدون تغییر)' : '') . '"></label>'
    . '<label>الگوی تأیید کاوه‌نگار (اختیاری)<input type="text" name="template" dir="ltr" value="' . esc_attr($sms['template']) . '"><small>اگر پر شود از verify/lookup با token=کد استفاده می‌شود؛ وگرنه پیامک ساده.</small></label>'
    . '<label>شمارهٔ خط ارسال‌کننده (اختیاری)<input type="text" name="sender" dir="ltr" value="' . esc_attr($sms['sender']) . '"></label>'
    . '<label class="wide">متن پیامک<input type="text" name="text" value="' . esc_attr($sms['text']) . '" maxlength="300"><small>{code} با کد جایگزین می‌شود.</small></label>'
    . '<label class="wide">نشانی درگاه (حالت «نشانی وب»)<input type="url" name="url" dir="ltr" value="' . esc_attr($sms['url']) . '" placeholder="https://…?to={mobile}&text={message}"><small>جانگهدارها: {mobile} {message} {code}. باید https باشد.</small></label>'
    . '<label>روش<select name="method"><option value="GET"' . selected($sms['method'], 'GET', false) . '>GET</option><option value="POST"' . selected($sms['method'], 'POST', false) . '>POST</option></select></label>'
    . '<label class="wide">بدنهٔ درخواست (فقط POST؛ اگر با { شروع شود JSON است)<textarea name="body" dir="ltr" rows="2">' . esc_textarea($sms['body']) . '</textarea></label></div>'
    . '<p><button class="mzc-btn primary">ذخیره</button></p></form>';
if (MZC_Sms::configured()) {
    echo MZC_Admin::form_open('sms_test', 'class="mzc-filter"') . '<input type="text" name="test_mobile" dir="ltr" placeholder="موبایل شما: 09121234567"><button class="mzc-btn">ارسال پیامک آزمایشی</button></form>';
}
echo '</div></div>';

echo '<div class="mzc-panel"><header><h3>پورتال مشتری</h3></header><div class="body"><p>یک برگهٔ تازه در وردپرس بسازید و فقط این کد کوتاه را در آن بگذارید: <code>[mazesta_portal]</code></p>'
    . '<p class="muted">برگه را از کش مستثنا نکنید لازم نیست (خودش ثابت است و داده‌ها با درخواست جداگانه می‌آیند)، ولی مسیر <code>/wp-json/mazesta/v1/portal/</code> نباید کش شود.</p></div></div>';
