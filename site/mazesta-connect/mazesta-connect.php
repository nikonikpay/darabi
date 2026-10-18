<?php
/**
 * Plugin Name: Mazesta Connect
 * Description: پل ارتباط برنامه Mazesta Test با سایت و سامانه مشتریان: گزارش‌های آزمون، بنچمارک‌ها، آمار و مشخصات سیستم‌ها، پیام به سیستم‌ها، سیستم‌های نو و سرویس‌ها (CRM روی پایگاه داده جداگانه) و پورتال مشتری با ورود پیامکی.
 * Version: 2.6.1
 * Requires at least: 6.0
 * Requires PHP: 7.4
 * Author: Mazesta
 * Text Domain: mazesta-connect
 */

if (!defined('ABSPATH')) { exit; }

define('MZC_VERSION', '2.6.1');
define('MZC_FILE', __FILE__);
define('MZC_DIR', plugin_dir_path(__FILE__));
define('MZC_URL', plugin_dir_url(__FILE__));

/*
 * Layout:
 *   includes/class-mazesta-connect.php  the bridge to the desktop app (REST routes, reports, benchmark lists, shares, releases; data in files)
 *   includes/class-mzc-stats.php        installs and usage events: what the dashboard and the systems pages read
 *   includes/class-mzc-messages.php     messages to the systems (REST for the app, files for the store)
 *   includes/class-mzc-crm-db.php       the CRM's own database (not WordPress'): connection, schema
 *   includes/class-mzc-crm.php          customers, new builds, service jobs, parts, photos
 *   includes/class-mzc-stock.php        the stock: products, suppliers' purchase invoices, where each part went, serial numbers
 *   includes/class-mzc-report.php       the money and counts of a period (services, new computers, profit, the best-selling parts)
 *   includes/class-mzc-ships.php        parts sent to warranty companies and repair shops, and those companies
 *   includes/class-mzc-site.php         what is shared with the rest of the site: the news for the app, the site accounts of a customer
 *   includes/class-mzc-import.php       brings the old CRM's data in (file made by site/tools/export_old_crm.py)
 *   includes/class-mzc-sms.php          the SMS sender for the portal's login codes
 *   includes/class-mzc-staff.php        the shop's people: roles, per-area levels, the apps' login (user name + password -> token)
 *   includes/class-mzc-api.php          what the staff apps read and write (customers, builds, jobs, stock, purchases, reports, receipt SMS, labels), checked per area
 *   includes/class-mzc-oauth.php        the AI apps' sign-in (OAuth 2.1 with PKCE and client registration) for the MCP endpoint
 *   includes/class-mzc-mcp.php          the MCP endpoint: ChatGPT / Claude read and write the CRM through the staff API, with the signed-in person's own rights
 *   includes/class-mzc-portal.php       the customer's page ([mazesta_portal]) and its REST routes
 *   includes/class-mzc-admin.php        menus and the shared admin shell; admin/*.php are the pages; assets/ the styles and scripts
 */
foreach (array('mazesta-connect', 'mzc-stats', 'mzc-messages', 'mzc-crm-db', 'mzc-crm', 'mzc-stock', 'mzc-report', 'mzc-ships', 'mzc-site', 'mzc-import', 'mzc-sms', 'mzc-portal', 'mzc-staff', 'mzc-api', 'mzc-oauth', 'mzc-mcp', 'mzc-admin') as $mzc_file) {
    require_once MZC_DIR . 'includes/class-' . $mzc_file . '.php';
}

Mazesta_Connect::boot();
MZC_Messages::boot();
MZC_Portal::boot();
MZC_Staff::boot();
MZC_Api::boot();
MZC_Stock::boot();
MZC_Oauth::boot();
MZC_Mcp::boot();
MZC_Import::boot();
MZC_Ships::boot();
MZC_Site::boot();
MZC_Admin::boot();
