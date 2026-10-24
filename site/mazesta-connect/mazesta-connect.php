<?php
/**
 * Plugin Name: Mazesta Connect
 * Description: پل ارتباط برنامه Mazesta Test با سایت و سامانه مشتریان: گزارش‌های آزمون، بنچمارک‌ها، آمار و مشخصات سیستم‌ها، پیام به سیستم‌ها، سیستم‌های نو و سرویس‌ها (CRM روی پایگاه داده جداگانه) و پورتال مشتری با ورود پیامکی.
 * Version: 2.10.2
 * Requires at least: 6.0
 * Requires PHP: 7.4
 * Author: Mazesta
 * Text Domain: mazesta-connect
 */

if (!defined('ABSPATH')) { exit; }

define('MZC_VERSION', '2.10.2');
define('MZC_FILE', __FILE__);
define('MZC_DIR', plugin_dir_path(__FILE__));
define('MZC_URL', plugin_dir_url(__FILE__));

/*
 * Layout:
 *   includes/class-mazesta-connect.php  the bridge to the desktop app (REST routes, reports, benchmark lists, shares, releases; data in the CRM's database, the test reports as files)
 *   includes/class-mzc-log.php          the plugin's own log (failed and slow REST answers, its PHP fatals, what the code reports), a file a day in the data folder
 *   includes/class-mzc-stats.php        systems, usage events and the apps' logs: tables of their own in the CRM's database, what the dashboard, systems, events, tuning and messages pages read
 *   includes/class-mzc-messages.php     messages to the systems (REST for the app; tables in the CRM's database)
 *   includes/class-mzc-crm-db.php       the CRM's own database (not WordPress'): connection, the schema of every table of the plugin, the removal of the data of before 2.8
 *   includes/class-mzc-crm.php          customers, new builds, service jobs, parts, photos
 *   includes/class-mzc-rev.php          the saved versions of services, new computers and invoices (who changed what, going back) and who has one open for editing
 *   includes/class-mzc-stock.php        the stock: products, suppliers' purchase invoices, where each part went, serial numbers
 *   includes/class-mzc-acc.php          the accounting: sales and return invoices, proformas, ready-made invoices, payments, cheques, the accounts of customers and suppliers
 *   includes/class-mzc-acc-api.php      the accounting's staff API routes
 *   includes/class-mzc-woo.php          the stock and the site's WooCommerce shop in step: prices and counts both ways, paid orders as invoices
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
foreach (array('mazesta-connect', 'mzc-log', 'mzc-stats', 'mzc-messages', 'mzc-crm-db', 'mzc-crm', 'mzc-rev', 'mzc-stock', 'mzc-acc', 'mzc-woo', 'mzc-report', 'mzc-ships', 'mzc-site', 'mzc-import', 'mzc-sms', 'mzc-portal', 'mzc-staff', 'mzc-api', 'mzc-acc-api', 'mzc-oauth', 'mzc-mcp', 'mzc-admin') as $mzc_file) {
    require_once MZC_DIR . 'includes/class-' . $mzc_file . '.php';
}

MZC_Log::boot();
Mazesta_Connect::boot();
MZC_Messages::boot();
MZC_Portal::boot();
MZC_Staff::boot();
MZC_Api::boot();
MZC_Rev::boot();
MZC_Stock::boot();
MZC_Acc::boot();
MZC_Acc_Api::boot();
MZC_Woo::boot();
MZC_Oauth::boot();
MZC_Mcp::boot();
MZC_Import::boot();
MZC_Ships::boot();
MZC_Site::boot();
MZC_Admin::boot();
