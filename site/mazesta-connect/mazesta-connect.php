<?php
/**
 * Plugin Name: Mazesta Connect
 * Description: پل ارتباط برنامه Mazesta Test با سایت و سامانه مشتریان: گزارش‌های آزمون، بنچمارک‌ها، آمار و مشخصات سیستم‌ها، پیام به سیستم‌ها، سیستم‌های نو و سرویس‌ها (CRM روی پایگاه داده جداگانه) و پورتال مشتری با ورود پیامکی.
 * Version: 2.1.1
 * Requires at least: 6.0
 * Requires PHP: 7.4
 * Author: Mazesta
 * Text Domain: mazesta-connect
 */

if (!defined('ABSPATH')) { exit; }

define('MZC_VERSION', '2.1.1');
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
 *   includes/class-mzc-import.php       brings the old CRM's data in (file made by site/tools/export_old_crm.py)
 *   includes/class-mzc-sms.php          the SMS sender for the portal's login codes
 *   includes/class-mzc-portal.php       the customer's page ([mazesta_portal]) and its REST routes
 *   includes/class-mzc-admin.php        menus and the shared admin shell; admin/*.php are the pages; assets/ the styles and scripts
 */
foreach (array('mazesta-connect', 'mzc-stats', 'mzc-messages', 'mzc-crm-db', 'mzc-crm', 'mzc-import', 'mzc-sms', 'mzc-portal', 'mzc-admin') as $mzc_file) {
    require_once MZC_DIR . 'includes/class-' . $mzc_file . '.php';
}

Mazesta_Connect::boot();
MZC_Messages::boot();
MZC_Portal::boot();
MZC_Import::boot();
MZC_Admin::boot();
