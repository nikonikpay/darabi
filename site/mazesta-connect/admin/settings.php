<?php
if (!defined('ABSPATH')) { exit; }
/** Settings of the CRM's own database and of the SMS sender. (The keys of the app, sharing and the release are on the Reports and benchmarks page, tab "تنظیمات و انتشار".) */

$admin = current_user_can('manage_options');
$db = MZC_Crm_Db::settings(); $ready = MZC_Crm_Db::ready();
$sms = MZC_Sms::settings();

echo '<div class="mzc-title"><h2>تنظیمات</h2></div>';
if (!$admin) { echo '<div class="mzc-notice warn">فقط مدیر سایت می‌تواند این تنظیمات را ببیند و عوض کند.</div>'; return; }

echo '<div class="mzc-panel"><header><h3>پایگاه دادهٔ CRM</h3>' . ($ready ? MZC_Admin::pill('وصل و آماده', 'ok') : MZC_Admin::pill('وصل نیست', 'bad')) . '</header><div class="body">'
    . '<p class="muted mzc-prose">اطلاعات مشتریان، سیستم‌ها و سرویس‌ها در یک پایگاه دادهٔ جدا از پایگاه دادهٔ وردپرس نگه داشته می‌شود. یک پایگاه دادهٔ MySQL خالی (و کاربری که به آن دسترسی کامل دارد) در هاست بسازید و مشخصاتش را اینجا وارد کنید؛ جدول‌ها خودکار ساخته می‌شوند. هیچ داده‌ای از CRM در پایگاه دادهٔ سایت نمی‌نشیند.</p>';
if (!$ready && MZC_Crm_Db::configured() && MZC_Crm_Db::error() !== '') { echo '<div class="mzc-notice err">' . esc_html(MZC_Crm_Db::error()) . '</div>'; }
echo MZC_Admin::form_open('db_save', 'class="mzc-form"') . '<div class="mzc-fields">'
    . '<label>میزبان (Host)<input type="text" name="host" dir="ltr" value="' . esc_attr($db['host']) . '"><small>معمولاً localhost</small></label>'
    . '<label>نام پایگاه داده<input type="text" name="name" dir="ltr" value="' . esc_attr($db['name']) . '" required></label>'
    . '<label>نام کاربری<input type="text" name="user" dir="ltr" value="' . esc_attr($db['user']) . '" required autocomplete="off"></label>'
    . '<label>گذرواژه<input type="password" name="pass" dir="ltr" autocomplete="new-password" placeholder="' . ($db['pass'] !== '' ? '•••••••• (بدون تغییر)' : '') . '"><small>رمزشده در پوشهٔ داده‌های افزونه نگه داشته می‌شود.</small></label>'
    . '<label>پیشوند جدول‌ها<input type="text" name="prefix" dir="ltr" value="' . esc_attr($db['prefix']) . '" maxlength="20"></label></div>'
    . '<div class="mzc-actions"><button class="mzc-btn primary">آزمایش اتصال و ذخیره</button>' . ($ready ? '<a class="mzc-btn" href="' . esc_url(MZC_Admin::post_url('db_install')) . '">بررسی دوبارهٔ جدول‌ها</a>' : '') . '</div></form></div></div>';

if ($ready) {
    $imp = MZC_Import::state();
    echo '<div class="mzc-panel"><header><h3>وارد کردن اطلاعات CRM قدیمی</h3>' . ($imp && !empty($imp['done']) ? MZC_Admin::pill('انجام شد', 'ok') : ($imp ? MZC_Admin::pill('نیمه‌کاره', 'warn') : '')) . '</header><div class="body">'
        . '<p class="muted mzc-prose">فایل <code>old-crm.jsonl.gz</code> را که با <code>site/tools/export_old_crm.py</code> از پشتیبان CRM قدیمی ساخته شده بارگذاری کنید و «شروع واردسازی» را بزنید. مشتریان با موبایل یکسان یکی می‌شوند و اجرای دوباره چیزی را دوبار نمی‌سازد.</p>'
        . MZC_Admin::form_open('import_upload', 'class="mzc-filter" enctype="multipart/form-data"') . '<input type="file" name="old" accept=".gz" required><button class="mzc-btn">بارگذاری فایل</button></form>';
    if ($imp) {
        echo '<div id="mzc-imp" data-url="' . esc_url(admin_url('admin-ajax.php')) . '" data-nonce="' . esc_attr(wp_create_nonce('mzc_import_step')) . '" data-done="' . (!empty($imp['done']) ? 1 : 0) . '">'
            . '<div class="mzc-bar"><i style="width:' . ($imp['size'] ? (int) floor(100 * $imp['offset'] / $imp['size']) : 0) . '%"></i></div><p class="muted" id="mzc-imp-text">' . esc_html(number_format_i18n($imp['lines'])) . ' رکورد خوانده شده</p>'
            . (empty($imp['done']) ? '<p><button type="button" class="mzc-btn primary" id="mzc-imp-go">شروع واردسازی</button></p>' : '') . '</div>';
    }
    echo '</div></div>';
    echo '<div class="mzc-panel"><header><h3>پاک کردن اطلاعات CRM</h3></header><div class="body"><p class="muted mzc-prose">در دورهٔ آزمایش: همهٔ مشتریان، سیستم‌ها، سرویس‌ها، فرم‌ها، عکس‌ها و ارسال‌های گارانتی پاک می‌شود تا واردسازی از نو انجام شود. تنظیمات و اتصال پایگاه داده می‌ماند. این کار برگشت‌پذیر نیست.</p>'
        . MZC_Admin::form_open('db_wipe', 'class="mzc-filter" onsubmit="return confirm(\'همهٔ اطلاعات CRM پاک شود؟\')"') . '<input type="text" name="confirm" placeholder="کلمهٔ «پاک‌کن» را بنویسید" autocomplete="off"><button class="mzc-btn danger">پاک کردن همه</button></form></div></div>';
}

echo '<div class="mzc-panel"><header><h3>پیامک (ورود مشتری به پورتال)</h3>' . (MZC_Sms::configured() ? MZC_Admin::pill('فعال', 'ok') : MZC_Admin::pill('غیرفعال', 'warn')) . '</header><div class="body">'
    . '<p class="muted mzc-prose">مشتری در صفحه‌ای که کد کوتاه <code>[mazesta_portal]</code> را دارد شمارهٔ موبایلش را می‌نویسد و کد شش‌رقمی را با پیامک می‌گیرد. تا درگاه پیامک تنظیم نشده پورتال کار نمی‌کند.</p>'
    . MZC_Admin::form_open('sms_save', 'class="mzc-form"') . '<div class="mzc-fields"><label>درگاه<select name="driver"><option value="">— غیرفعال —</option><option value="melipayamak"' . selected($sms['driver'], 'melipayamak', false) . '>ملی‌پیامک (melipayamak.com)</option><option value="kavenegar"' . selected($sms['driver'], 'kavenegar', false) . '>کاوه‌نگار</option>'
    . '<option value="url"' . selected($sms['driver'], 'url', false) . '>نشانی وب دلخواه (هر درگاهی که با یک درخواست HTTP کار کند)</option></select></label>'
    . '<label>نام کاربری (ملی‌پیامک)<input type="text" name="user" dir="ltr" value="' . esc_attr(isset($sms['user']) ? $sms['user'] : '') . '" autocomplete="off"></label>'
    . '<label>کلید API کاوه‌نگار / گذرواژهٔ ملی‌پیامک<input type="password" name="key" dir="ltr" autocomplete="new-password" placeholder="' . ($sms['key'] !== '' ? '•••••••• (بدون تغییر)' : '') . '"></label>'
    . '<label>الگو: کاوه‌نگار template / ملی‌پیامک bodyId (اختیاری)<input type="text" name="template" dir="ltr" value="' . esc_attr($sms['template']) . '"><small>اگر پر شود کد از راه الگو (سرویس اشتراکی) فرستاده می‌شود؛ وگرنه پیامک ساده با متن زیر.</small></label>'
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

if (MZC_Crm_Db::ready()) {
    echo '<div class="mzc-panel"><header><h3>فهرست نرم‌افزارها</h3></header><div class="body"><p class="muted mzc-prose">فرم‌های سیستم نو و سرویس، و صفحهٔ مشتری، هنگام نوشتن نام نرم‌افزار از این فهرست پیشنهاد می‌دهند. نامی که در فهرست نیست هم پذیرفته می‌شود. هر نرم‌افزار در یک خط.</p>'
        . MZC_Admin::form_open('soft_save', 'class="mzc-form"') . '<textarea name="software" rows="10" dir="ltr" class="mzc-w520">' . esc_textarea(implode("\n", MZC_Crm::software_catalog())) . '</textarea><p><button class="mzc-btn primary">ذخیرهٔ فهرست</button></p></form></div></div>';
}
if (MZC_Crm_Db::ready()) {
    echo '<div class="mzc-panel"><header><h3>حساب‌های واریز</h3></header><div class="body"><p class="muted mzc-prose">در «پرداخت‌های مشتری» خانهٔ «واریز به حساب» این‌ها را پیشنهاد می‌دهد (پوز، کارت، شماره حساب ...)؛ هر نوشتهٔ دیگری هم پذیرفته می‌شود. هر حساب در یک خط.</p>'
        . MZC_Admin::form_open('soft_save', 'class="mzc-form"') . '<textarea name="accounts" rows="6" dir="auto" class="mzc-w520">' . esc_textarea(implode("\n", MZC_Crm::software_catalog('accounts'))) . '</textarea><p><button class="mzc-btn primary">ذخیرهٔ حساب‌ها</button></p></form></div></div>';
}
if (MZC_Crm_Db::ready()) {
    $as = MZC_Acc::settings(); $ws = MZC_Woo::settings(); $woo = class_exists('WooCommerce');
    echo '<div class="mzc-panel"><header><h3>حسابداری و فروشگاه سایت</h3>' . ($woo ? ($ws['on'] ? MZC_Admin::pill('همگام با ووکامرس', 'ok') : MZC_Admin::pill('همگام‌سازی خاموش', 'warn')) : MZC_Admin::pill('ووکامرس فعال نیست')) . '</header><div class="body">'
        . MZC_Admin::form_open('acc_save', 'class="mzc-form"') . '<div class="mzc-fields">'
        . '<label>اولین شمارهٔ فاکتور فروش<input type="text" name="start" dir="ltr" value="' . esc_attr($as['start']) . '"><small>شماره از بزرگ‌ترینِ همین عدد، فاکتورهای قبلی و شمارهٔ فاکتور سیستم‌های نو ادامه پیدا می‌کند (برای ادامهٔ شماره‌های برنامهٔ حسابداری قبلی).</small></label>'
        . '<label>مالیات بر ارزش افزوده (٪)<input type="text" name="tax" dir="ltr" value="' . esc_attr($as['tax']) . '"><small>پیش‌فرض فاکتورهای تازه؛ در هر فاکتور عوض می‌شود. ۰ یعنی بدون مالیات.</small></label>'
        . '<label>یادآوری چک (روز پیش از سررسید)<input type="number" name="remind" min="0" max="60" value="' . (int) $as['remind'] . '"><small>برنامه و این صفحه‌ها نشان می‌دهند و روزی یک ایمیل به مدیر سایت می‌رود.</small></label>'
        . '<label>نام فروشگاه روی فاکتور<input type="text" name="shop" value="' . esc_attr($as['shop']) . '" maxlength="120"></label><label>تلفن روی فاکتور<input type="text" name="phone" dir="ltr" value="' . esc_attr($as['phone']) . '" maxlength="60"></label>'
        . '<label class="wide">نشانی روی فاکتور<input type="text" name="address" value="' . esc_attr($as['address']) . '" maxlength="255"></label><label class="wide">متن پایین فاکتور<input type="text" name="foot" value="' . esc_attr($as['foot']) . '" maxlength="300"></label>'
        . '<label class="wide">سرفصل‌های هزینه (هر خط یکی)<textarea name="expenses" rows="5">' . esc_textarea($as['expenses']) . '</textarea></label>'
        . '<label class="chk wide"><input type="checkbox" name="woo_on" value="1"' . checked(!empty($ws['on']), true, false) . ($woo ? '' : ' disabled') . '> قیمت فروش و موجودی کالاهای وصل‌شده با محصولات سایت همگام شود (قیمت از هر دو طرف؛ موجودی از انبار)</label>'
        . '<label class="chk wide"><input type="checkbox" name="woo_orders" value="1"' . checked(!empty($ws['orders']), true, false) . ($woo ? '' : ' disabled') . '> سفارش‌های پرداخت‌شدهٔ سایت فاکتور فروش مشتری شوند (مشتری با موبایل پیدا یا ساخته می‌شود)</label>'
        . '<label>ضریب قیمت سایت<input type="text" name="woo_factor" dir="ltr" value="' . esc_attr($ws['factor']) . '" placeholder="' . esc_attr($woo ? (string) MZC_Woo::factor() : '1') . '"><small>قیمت سایت = قیمت انبار (تومان) × این عدد. خالی: از واحد پول فروشگاه (ریال ۱۰، تومان ۱).</small></label>'
        . '</div><p><button class="mzc-btn primary">ذخیره</button></p></form>';
    if ($woo) {
        echo '<div class="mzc-actions"><a class="mzc-btn" href="' . esc_url(MZC_Admin::post_url('woo_link')) . '">وصل کردن با پارت‌نامبر (SKU)</a><a class="mzc-btn" href="' . esc_url(MZC_Admin::post_url('woo_import')) . '" onclick="return confirm(\'برای هر محصول سادهٔ سایت که وصل نیست کالای انبار ساخته شود؟\')">آوردن محصولات سایت به انبار</a>'
            . '<a class="mzc-btn" href="' . esc_url(MZC_Admin::post_url('woo_push')) . '">فرستادن قیمت و موجودی همه به سایت</a><a class="mzc-btn" href="' . esc_url(MZC_Admin::post_url('woo_orders')) . '">آوردن ۲۰۰ سفارش پرداخت‌شدهٔ اخیر</a></div>'
            . '<p class="muted mzc-prose">موجودی را انبار حساب می‌کند (خرید منهای فروش)؛ عددی که در سایت دستی عوض شود در همگام‌سازی بعدی (هر ۱۵ دقیقه یا با هر تغییر) برمی‌گردد. قیمت فروش از هر طرف عوض شود طرف دیگر هم عوض می‌شود. هر کالا را در صفحهٔ کالا هم می‌شود دستی به محصول سایت وصل کرد.</p>';
    }
    echo '</div></div>';
}
