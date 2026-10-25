<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The CRM's own database. The customers, their systems and service jobs are never put in WordPress' database: this connects to a second MySQL database whose host, name,
 * user and password are entered on the plugin's settings page. The password is kept in the plugin's data folder (a file that PHP refuses to show), encrypted with the
 * site's own salts. Until a working connection is saved, the CRM pages say so and nothing is written anywhere.
 */
final class MZC_Crm_Db
{
    const SCHEMA = 16;
    private static $db = false;     // false: not tried yet; null: no connection
    private static $error = '';
    private static $cfg = null;

    public static function settings()
    {
        // read from the file (not the request-wide cache of config()): save() writes it and install() runs in the same request
        if (self::$cfg === null) { $c = Mazesta_Connect::read('config'); self::$cfg = isset($c['crm']) && is_array($c['crm']) ? $c['crm'] : array(); }
        $x = self::$cfg;
        return $x + array('host' => 'localhost', 'name' => '', 'user' => '', 'pass' => '', 'prefix' => 'mz_');
    }

    private static function key() { return hash('sha256', 'mzc|' . wp_salt('auth'), true); }

    public static function seal($plain)
    {
        if ($plain === '') { return ''; }
        $iv = random_bytes(16);
        $c = openssl_encrypt($plain, 'aes-256-cbc', self::key(), OPENSSL_RAW_DATA, $iv);
        return $c === false ? '' : base64_encode($iv . $c);
    }

    private static function unseal($sealed)
    {
        $raw = base64_decode((string) $sealed, true);
        if ($raw === false || strlen($raw) < 17) { return ''; }
        $p = openssl_decrypt(substr($raw, 16), 'aes-256-cbc', self::key(), OPENSSL_RAW_DATA, substr($raw, 0, 16));
        return $p === false ? '' : $p;
    }

    public static function configured() { $s = self::settings(); return $s['name'] !== '' && $s['user'] !== ''; }
    public static function error() { return self::$error; }
    public static function prefix() { $s = self::settings(); return preg_match('/^[A-Za-z0-9_]{1,20}$/', (string) $s['prefix']) ? $s['prefix'] : 'mz_'; }
    public static function t($table) { return self::prefix() . $table; }

    /** Tries a connection with plain mysqli first (wpdb would stop the request with an error page on a wrong password). Returns an error text or ''. */
    public static function test($host, $name, $user, $pass)
    {
        if (!function_exists('mysqli_init')) { return 'افزونهٔ mysqli در PHP نیست.'; }
        mysqli_report(MYSQLI_REPORT_OFF);
        $port = null; $h = $host;
        if (strpos($host, ':') !== false && substr_count($host, ':') === 1) { list($h, $p) = explode(':', $host, 2); if (ctype_digit($p)) { $port = (int) $p; } }
        $m = mysqli_init(); $m->options(MYSQLI_OPT_CONNECT_TIMEOUT, 6);
        if (!@$m->real_connect($h, $user, $pass, $name, $port)) { return 'اتصال برقرار نشد: ' . $m->connect_error; }
        $m->close();
        return '';
    }

    /** The wpdb of the CRM database, or null (see error()). */
    public static function db()
    {
        if (self::$db !== false) { return self::$db; }
        self::$db = null;
        if (!self::configured()) { self::$error = 'پایگاه داده CRM هنوز در تنظیمات وارد نشده است.'; return null; }
        $s = self::settings(); $pass = self::unseal($s['pass']);
        $e = self::test($s['host'], $s['name'], $s['user'], $pass);
        if ($e !== '') { self::$error = $e; return null; }
        $db = new wpdb($s['user'], $pass, $s['name'], $s['host']);
        $db->suppress_errors(true);
        $db->set_charset($db->dbh, 'utf8mb4', 'utf8mb4_unicode_ci');
        self::$db = $db;
        return $db;
    }

    /** Makes the tables when they are missing (and brings an older layout up to date). Returns an error text or ''. */
    public static function install()
    {
        $db = self::db(); if (!$db) { return self::$error; }
        $t = function ($n) { return self::t($n); };
        $engine = 'ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci';
        $sql = array(
            "CREATE TABLE IF NOT EXISTS `{$t('customers')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, name VARCHAR(190) NOT NULL, mobile VARCHAR(20) NOT NULL, phone2 VARCHAR(30) NULL,
                national_id VARCHAR(20) NULL, email VARCHAR(190) NULL, address TEXT NULL, notes TEXT NULL, created DATETIME NOT NULL, updated DATETIME NOT NULL,
                UNIQUE KEY mobile (mobile), KEY name (name)) $engine",
            "CREATE TABLE IF NOT EXISTS `{$t('builds')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, customer_id BIGINT UNSIGNED NOT NULL, service_no VARCHAR(40) NULL, invoice_no VARCHAR(60) NULL,
                title VARCHAR(190) NOT NULL, sold_at DATE NULL, report_id VARCHAR(64) NULL, notes TEXT NULL, created DATETIME NOT NULL, updated DATETIME NOT NULL,
                KEY customer_id (customer_id), KEY service_no (service_no), KEY invoice_no (invoice_no)) $engine",
            "CREATE TABLE IF NOT EXISTS `{$t('build_parts')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, build_id BIGINT UNSIGNED NOT NULL, category VARCHAR(40) NOT NULL, model VARCHAR(190) NOT NULL,
                serial VARCHAR(120) NULL, warranty_months SMALLINT NULL, vendor VARCHAR(120) NULL, note VARCHAR(255) NULL, KEY build_id (build_id), KEY serial (serial)) $engine",
            "CREATE TABLE IF NOT EXISTS `{$t('jobs')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, customer_id BIGINT UNSIGNED NOT NULL, service_no VARCHAR(40) NULL, device VARCHAR(190) NULL,
                received_at DATE NULL, closed_at DATE NULL, status VARCHAR(20) NOT NULL DEFAULT 'received', complaint TEXT NULL, work_done TEXT NULL, labor_price BIGINT NOT NULL DEFAULT 0,
                report_id VARCHAR(64) NULL, notes TEXT NULL, created DATETIME NOT NULL, updated DATETIME NOT NULL, KEY customer_id (customer_id), KEY service_no (service_no), KEY status (status)) $engine",
            "CREATE TABLE IF NOT EXISTS `{$t('job_parts')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, job_id BIGINT UNSIGNED NOT NULL, name VARCHAR(190) NOT NULL, qty INT NOT NULL DEFAULT 1,
                unit_price BIGINT NOT NULL DEFAULT 0, serial VARCHAR(120) NULL, warranty_months SMALLINT NULL, note VARCHAR(255) NULL, KEY job_id (job_id)) $engine",
            "CREATE TABLE IF NOT EXISTS `{$t('photos')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, entity VARCHAR(10) NOT NULL, entity_id BIGINT UNSIGNED NOT NULL, file VARCHAR(80) NOT NULL,
                caption VARCHAR(190) NULL, created DATETIME NOT NULL, KEY entity (entity, entity_id)) $engine",
            "CREATE TABLE IF NOT EXISTS `{$t('otps')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, mobile VARCHAR(20) NOT NULL, code_hash CHAR(64) NOT NULL, expires DATETIME NOT NULL,
                attempts TINYINT NOT NULL DEFAULT 0, created DATETIME NOT NULL, KEY mobile (mobile, created)) $engine",
            "CREATE TABLE IF NOT EXISTS `{$t('sessions')}` (token_hash CHAR(64) NOT NULL PRIMARY KEY, customer_id BIGINT UNSIGNED NOT NULL, expires DATETIME NOT NULL, created DATETIME NOT NULL, KEY customer_id (customer_id)) $engine",
            "CREATE TABLE IF NOT EXISTS `{$t('meta')}` (k VARCHAR(40) NOT NULL PRIMARY KEY, v TEXT NULL) $engine",
        );
        // schema 4 (the shop's real forms): more columns, payments, the other forms, and the map the old-database import keeps
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('job_pays')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, job_id BIGINT UNSIGNED NOT NULL, amount BIGINT NOT NULL DEFAULT 0, account VARCHAR(120) NULL,
                tx VARCHAR(80) NULL, holder VARCHAR(120) NULL, paid_at DATE NULL, KEY job_id (job_id)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('forms')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, customer_id BIGINT UNSIGNED NULL, kind VARCHAR(12) NOT NULL, ref VARCHAR(60) NULL, no VARCHAR(40) NULL,
                at DATE NULL, title VARCHAR(255) NULL, data LONGTEXT NULL, old_id INT NULL, created DATETIME NOT NULL, KEY customer_id (customer_id), KEY kind (kind, ref), KEY old_id (old_id)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('oldmap')}` (kind VARCHAR(10) NOT NULL, old_id INT NOT NULL, new_id BIGINT UNSIGNED NOT NULL, PRIMARY KEY (kind, old_id)) $engine";
        // schema 6: where the shop sends faulty parts (warranty companies and repair shops) and what came back
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('vendors')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, name VARCHAR(120) NOT NULL, kind VARCHAR(10) NOT NULL DEFAULT 'warranty', phone VARCHAR(60) NULL, notes VARCHAR(255) NULL, UNIQUE KEY name (name)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('shipments')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, vendor_id BIGINT UNSIGNED NULL, part_name VARCHAR(190) NOT NULL, serial VARCHAR(120) NULL, problem TEXT NULL,
                sent_at DATE NULL, tracking VARCHAR(80) NULL, vendor_reply TEXT NULL, received_at DATE NULL, fix_notes TEXT NULL, back_part VARCHAR(190) NULL, back_serial VARCHAR(120) NULL, shelf VARCHAR(60) NULL, picked_up TINYINT NOT NULL DEFAULT 0,
                owner VARCHAR(10) NOT NULL DEFAULT 'mazesta', customer_id BIGINT UNSIGNED NULL, job_id BIGINT UNSIGNED NULL, ref VARCHAR(60) NULL, customer_reply TEXT NULL, old_id INT NULL, created DATETIME NOT NULL, updated DATETIME NOT NULL,
                KEY vendor_id (vendor_id), KEY customer_id (customer_id), KEY job_id (job_id), KEY sent_at (sent_at), KEY old_id (old_id), KEY serial (serial)) $engine";
        // schema 8: who changed what (the staff apps' writes)
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('audit')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, user_id BIGINT UNSIGNED NOT NULL, who VARCHAR(120) NOT NULL, action VARCHAR(12) NOT NULL, entity VARCHAR(12) NOT NULL,
                entity_id BIGINT UNSIGNED NOT NULL DEFAULT 0, note VARCHAR(255) NULL, at DATETIME NOT NULL, KEY entity (entity, entity_id, action, at), KEY at (at)) $engine";
        // schema 9: the stock (products), purchase invoices from suppliers (vendors of kind 'supplier') and their lines, and the AI apps' OAuth sign-in
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('products')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, name VARCHAR(190) NOT NULL, category VARCHAR(40) NOT NULL DEFAULT 'other', brand VARCHAR(80) NULL, sku VARCHAR(120) NULL,
                buy_price BIGINT NULL, sell_price BIGINT NULL, warranty_months SMALLINT NULL, notes VARCHAR(255) NULL, created DATETIME NOT NULL, updated DATETIME NOT NULL, KEY name (name), KEY sku (sku), KEY category (category)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('purchases')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, vendor_id BIGINT UNSIGNED NULL, invoice_no VARCHAR(60) NULL, bought_at DATE NULL, notes TEXT NULL,
                created DATETIME NOT NULL, updated DATETIME NOT NULL, KEY vendor_id (vendor_id), KEY invoice_no (invoice_no), KEY bought_at (bought_at)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('purchase_items')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, purchase_id BIGINT UNSIGNED NOT NULL, product_id BIGINT UNSIGNED NOT NULL, qty INT NOT NULL DEFAULT 1,
                unit_cost BIGINT NOT NULL DEFAULT 0, serials TEXT NULL, KEY purchase_id (purchase_id), KEY product_id (product_id)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('oauth_clients')}` (client_id CHAR(32) NOT NULL PRIMARY KEY, secret_hash CHAR(64) NULL, name VARCHAR(120) NOT NULL, redirect_uris TEXT NOT NULL, created DATETIME NOT NULL) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('oauth_codes')}` (code_hash CHAR(64) NOT NULL PRIMARY KEY, client_id CHAR(32) NOT NULL, user_id BIGINT UNSIGNED NOT NULL, redirect_uri TEXT NOT NULL, challenge VARCHAR(128) NOT NULL, expires DATETIME NOT NULL) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('oauth_refresh')}` (token_hash CHAR(64) NOT NULL PRIMARY KEY, client_id CHAR(32) NOT NULL, user_id BIGINT UNSIGNED NOT NULL, expires DATETIME NOT NULL, created DATETIME NOT NULL, KEY user_id (user_id)) $engine";
        // schema 12: the rest of the plugin's data, here too (until 2.7 it was files in wp-content and three tables in WordPress' database), a table per kind:
        // the systems that send statistics, their events and logs; messages to them and the chosen systems of each; benchmark runs and the
        // benchmarks' names; users' shared results and their rows; the app's pairing requests; the per-address and per-login counters
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('systems')}` (id CHAR(32) NOT NULL PRIMARY KEY, first_at DATETIME NOT NULL, last_at DATETIME NOT NULL, n INT UNSIGNED NOT NULL DEFAULT 0,
                app VARCHAR(20) NULL, edition VARCHAR(12) NULL, lang VARCHAR(8) NULL, note VARCHAR(300) NULL, cpu VARCHAR(120) NULL, gpus VARCHAR(500) NULL, ram_gb SMALLINT NULL,
                os VARCHAR(120) NULL, hw TEXT NULL, machine TEXT NULL, ack INT UNSIGNED NOT NULL DEFAULT 0, KEY last_at (last_at), KEY first_at (first_at), KEY n (n)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('system_events')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, sys CHAR(32) NOT NULL, at DATETIME NOT NULL, kind VARCHAR(16) NOT NULL,
                item VARCHAR(64) NULL, gpu VARCHAR(120) NULL, d MEDIUMTEXT NULL, KEY at (at), KEY sys (sys, at), KEY kind (kind, at), KEY gpu (gpu(60), kind)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('system_logs')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, sys CHAR(32) NOT NULL, at DATETIME NOT NULL, app VARCHAR(20) NULL,
                file VARCHAR(40) NULL, body MEDIUMTEXT NULL, crashes TEXT NULL, KEY sys (sys, at), KEY at (at)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('messages')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, title VARCHAR(120) NOT NULL, body TEXT NULL, link VARCHAR(255) NULL,
                target VARCHAR(5) NOT NULL DEFAULT 'all', grp VARCHAR(120) NULL, created DATETIME NOT NULL, by_user BIGINT UNSIGNED NOT NULL DEFAULT 0, KEY created (created)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('message_systems')}` (message_id BIGINT UNSIGNED NOT NULL, sys CHAR(32) NOT NULL, PRIMARY KEY (message_id, sys), KEY sys (sys)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('bench_runs')}` (id VARCHAR(64) NOT NULL PRIMARY KEY, list VARCHAR(255) NOT NULL, part VARCHAR(120) NOT NULL, value DOUBLE NOT NULL, unit VARCHAR(20) NOT NULL,
                higher TINYINT NOT NULL DEFAULT 1, approved TINYINT NOT NULL DEFAULT 0, featured TINYINT NOT NULL DEFAULT 0, oc TINYINT NULL, note VARCHAR(190) NOT NULL DEFAULT '', mark_at DATETIME NULL,
                run_at DATETIME NOT NULL, received DATETIME NOT NULL, run MEDIUMTEXT NOT NULL, KEY list (approved, list(190)), KEY received (received)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('bench_names')}` (id VARCHAR(80) NOT NULL PRIMARY KEY, name VARCHAR(80) NOT NULL) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('shares')}` (token CHAR(24) NOT NULL PRIMARY KEY, who CHAR(24) NOT NULL, created DATETIME NOT NULL, app VARCHAR(40) NOT NULL DEFAULT '', owner VARCHAR(40) NOT NULL DEFAULT '',
                cpu VARCHAR(120) NOT NULL DEFAULT '', gpu VARCHAR(120) NOT NULL DEFAULT '', os VARCHAR(120) NOT NULL DEFAULT '', ram_gb DOUBLE NULL, UNIQUE KEY who (who), KEY created (created)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('share_rows')}` (token CHAR(24) NOT NULL, pos SMALLINT NOT NULL, benchmark VARCHAR(80) NOT NULL, name VARCHAR(80) NOT NULL, settings VARCHAR(160) NOT NULL DEFAULT '',
                value DOUBLE NOT NULL, unit VARCHAR(20) NOT NULL, part VARCHAR(120) NOT NULL, at DATETIME NOT NULL, version INT NOT NULL DEFAULT 1, PRIMARY KEY (token, pos)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('pairs')}` (id CHAR(64) NOT NULL PRIMARY KEY, name VARCHAR(60) NOT NULL DEFAULT '', approved TINYINT NOT NULL DEFAULT 0, created INT UNSIGNED NOT NULL, KEY created (created)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('limits')}` (slot VARCHAR(80) NOT NULL, win INT UNSIGNED NOT NULL, n INT UNSIGNED NOT NULL DEFAULT 0, PRIMARY KEY (slot, win), KEY win (win)) $engine";
        // schema 13: the accounting. One table of documents (sales and return invoices, proformas, and the ready-made templates), their lines, the money that came in or went out
        // (from a customer, to a supplier, or an expense) and the cheques received and written. A line's buy_price is the cost when sold, so a later purchase does not change old profit.
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('invoices')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, kind VARCHAR(10) NOT NULL, no BIGINT UNSIGNED NULL, status VARCHAR(8) NOT NULL DEFAULT 'draft',
                customer_id BIGINT UNSIGNED NULL, title VARCHAR(190) NULL, at DATE NULL, valid_until DATE NULL, build_id BIGINT UNSIGNED NULL, ref_id BIGINT UNSIGNED NULL, wc_order_id BIGINT UNSIGNED NULL,
                subtotal BIGINT NOT NULL DEFAULT 0, discount BIGINT NOT NULL DEFAULT 0, tax_pct DECIMAL(5,2) NOT NULL DEFAULT 0, tax BIGINT NOT NULL DEFAULT 0, total BIGINT NOT NULL DEFAULT 0,
                notes TEXT NULL, void_reason VARCHAR(255) NULL, created_by BIGINT UNSIGNED NOT NULL DEFAULT 0, created DATETIME NOT NULL, updated DATETIME NOT NULL,
                UNIQUE KEY kind_no (kind, no), UNIQUE KEY wc_order_id (wc_order_id), KEY customer_id (customer_id), KEY kind_status (kind, status, at), KEY build_id (build_id), KEY ref_id (ref_id)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('invoice_items')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, invoice_id BIGINT UNSIGNED NOT NULL, product_id BIGINT UNSIGNED NULL, category VARCHAR(40) NOT NULL DEFAULT 'other',
                name VARCHAR(190) NOT NULL, qty INT NOT NULL DEFAULT 1, unit_price BIGINT NOT NULL DEFAULT 0, buy_price BIGINT NULL, serials TEXT NULL, warranty_months SMALLINT NULL,
                KEY invoice_id (invoice_id), KEY product_id (product_id)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('cheques')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, direction CHAR(3) NOT NULL, no VARCHAR(40) NOT NULL, bank VARCHAR(80) NULL, account VARCHAR(120) NULL,
                amount BIGINT NOT NULL, issued_at DATE NULL, due_at DATE NOT NULL, customer_id BIGINT UNSIGNED NULL, vendor_id BIGINT UNSIGNED NULL, party VARCHAR(190) NULL, status VARCHAR(10) NOT NULL DEFAULT 'pending',
                notes VARCHAR(255) NULL, created_by BIGINT UNSIGNED NOT NULL DEFAULT 0, created DATETIME NOT NULL, updated DATETIME NOT NULL,
                KEY due (status, due_at), KEY customer_id (customer_id), KEY vendor_id (vendor_id)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('payments')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, direction CHAR(3) NOT NULL, amount BIGINT NOT NULL, method VARCHAR(10) NOT NULL DEFAULT 'cash',
                account VARCHAR(120) NULL, at DATE NOT NULL, customer_id BIGINT UNSIGNED NULL, vendor_id BIGINT UNSIGNED NULL, invoice_id BIGINT UNSIGNED NULL, purchase_id BIGINT UNSIGNED NULL, cheque_id BIGINT UNSIGNED NULL,
                category VARCHAR(60) NULL, tx VARCHAR(80) NULL, notes VARCHAR(255) NULL, created_by BIGINT UNSIGNED NOT NULL DEFAULT 0, created DATETIME NOT NULL, updated DATETIME NOT NULL,
                KEY customer_id (customer_id), KEY vendor_id (vendor_id), KEY invoice_id (invoice_id), KEY cheque_id (cheque_id), KEY at (at)) $engine";
        // schema 15: every saved version of a service, a new computer or an invoice (who, when, what changed, the whole record) and who has one open for editing (MZC_Rev)
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('revisions')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, entity VARCHAR(10) NOT NULL, entity_id BIGINT UNSIGNED NOT NULL, user_id BIGINT UNSIGNED NOT NULL DEFAULT 0,
                who VARCHAR(120) NOT NULL DEFAULT '', action VARCHAR(12) NOT NULL DEFAULT '', at DATETIME NOT NULL, changes MEDIUMTEXT NULL, snap MEDIUMTEXT NOT NULL, KEY entity (entity, entity_id, id)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('edit_locks')}` (entity VARCHAR(10) NOT NULL, entity_id BIGINT UNSIGNED NOT NULL, user_id BIGINT UNSIGNED NOT NULL, who VARCHAR(120) NOT NULL, since INT UNSIGNED NOT NULL,
                until INT UNSIGNED NOT NULL, PRIMARY KEY (entity, entity_id)) $engine";
        // schema 16: the app's accounts (mobile number + SMS code), their sign-in codes and sessions, and the installations linked to each account (MZC_Access)
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('accounts')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, mobile VARCHAR(11) NOT NULL, plan VARCHAR(8) NOT NULL DEFAULT 'free', pro_until DATETIME NULL, disabled TINYINT NOT NULL DEFAULT 0,
                note VARCHAR(255) NULL, created DATETIME NOT NULL, last_seen DATETIME NULL, UNIQUE KEY mobile (mobile)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('app_codes')}` (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, mobile VARCHAR(11) NOT NULL, code_hash CHAR(64) NOT NULL, expires DATETIME NOT NULL, attempts TINYINT NOT NULL DEFAULT 0, created DATETIME NOT NULL, KEY mobile (mobile)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('app_sessions')}` (token_hash CHAR(64) NOT NULL PRIMARY KEY, account_id BIGINT UNSIGNED NOT NULL, install CHAR(32) NOT NULL DEFAULT '', expires DATETIME NOT NULL, created DATETIME NOT NULL, KEY account_id (account_id)) $engine";
        $sql[] = "CREATE TABLE IF NOT EXISTS `{$t('account_installs')}` (install CHAR(32) NOT NULL PRIMARY KEY, account_id BIGINT UNSIGNED NOT NULL, linked DATETIME NOT NULL, KEY account_id (account_id)) $engine";
        $before = (int) $db->get_var('SELECT v FROM `' . $t('meta') . "` WHERE k = 'schema'");
        foreach ($sql as $q) { if ($db->query($q) === false) { return 'ساخت جدول ناموفق بود: ' . $db->last_error; } }
        if ($before < 12) { self::forget_old(); }
        // columns added after schema 3 (and the customer's mobile may now be empty: the old database has customers without one)
        $cols = array(
            'customers' => array('mobile2' => 'VARCHAR(20) NULL', 'old_id' => 'INT NULL', 'phone' => 'VARCHAR(80) NULL', 'grp' => 'VARCHAR(12) NULL'),
            'builds' => array('status' => "VARCHAR(12) NOT NULL DEFAULT 'delivered'", 'delivered' => 'TINYINT NOT NULL DEFAULT 0', 'data' => 'LONGTEXT NULL', 'old_id' => 'INT NULL'),
            'build_parts' => array('has_warranty' => 'TINYINT NOT NULL DEFAULT 0', 'has_box' => 'TINYINT NOT NULL DEFAULT 0', 'qc' => 'TINYINT NOT NULL DEFAULT 0'),
            'jobs' => array('due_at' => 'DATE NULL', 'is_mazesta' => 'TINYINT NOT NULL DEFAULT 0', 'has_warranty' => 'TINYINT NOT NULL DEFAULT 0', 'discount' => 'BIGINT NOT NULL DEFAULT 0', 'paid' => 'BIGINT NOT NULL DEFAULT 0',
                'invoice_no' => 'VARCHAR(60) NULL', 'service_done' => 'TINYINT NOT NULL DEFAULT 0', 'ship' => 'TEXT NULL', 'data' => 'LONGTEXT NULL', 'old_id' => 'INT NULL'),
            'sessions' => array('user_id' => 'BIGINT UNSIGNED NULL', 'device' => 'VARCHAR(80) NULL'),   // schema 7: the staff apps' sessions (user_id) share the customers' table
            'job_parts' => array('kind' => "VARCHAR(4) NOT NULL DEFAULT 'add'", 'has_box' => 'TINYINT NOT NULL DEFAULT 0', 'has_warranty' => 'TINYINT NOT NULL DEFAULT 0', 'product_id' => 'BIGINT UNSIGNED NULL', 'buy_price' => 'BIGINT NULL'),
        );
        // schema 9: what kind of device a record is, its photos and videos in the shop's Telegram channel (links), and the stock product a part came from with its prices
        $cols['builds'] += array('device_type' => "VARCHAR(10) NOT NULL DEFAULT 'system'", 'media' => 'TEXT NULL');
        $cols['jobs'] += array('device_type' => 'VARCHAR(10) NULL', 'media' => 'TEXT NULL');
        $cols['build_parts'] += array('product_id' => 'BIGINT UNSIGNED NULL', 'buy_price' => 'BIGINT NULL', 'sell_price' => 'BIGINT NULL');
        // schema 10: the programs the customer wants installed (JSON, see MZC_Crm::soft_out), a laptop's serial number, and what the shop's own paid-out costs of a service were for
        $cols['builds'] += array('soft' => 'TEXT NULL');
        $cols['jobs'] += array('soft' => 'TEXT NULL', 'device_serial' => 'VARCHAR(120) NULL', 'paid_note' => 'VARCHAR(255) NULL');
        // schema 11: a customer's third mobile number onward, one per line (the first two stay in mobile and mobile2)
        $cols['customers'] += array('mobiles' => 'TEXT NULL');
        // schema 13: the customer's account on the site (WooCommerce) and the site product a stock product is kept in step with
        $cols['customers'] += array('wp_user_id' => 'BIGINT UNSIGNED NULL');
        $cols['products'] = array('wc_id' => 'BIGINT UNSIGNED NULL');
        // schema 14: the tick lists of what came in with a device and of the checks before it leaves (MZC_Crm::$checklists)
        $cols['builds'] += array('checks' => 'TEXT NULL'); $cols['jobs'] += array('checks' => 'TEXT NULL');
        foreach ($cols as $tb => $list) {
            $have = (array) $db->get_col('SHOW COLUMNS FROM `' . $t($tb) . '`', 0);
            foreach ($list as $c => $def) {
                if (in_array($c, $have, true)) { continue; }
                if ($db->query('ALTER TABLE `' . $t($tb) . "` ADD COLUMN `$c` $def") === false) { return 'به‌روزرسانی جدول ناموفق بود: ' . $db->last_error; }
                if (in_array($c, array('product_id', 'wp_user_id', 'wc_id'), true)) { $db->query('ALTER TABLE `' . $t($tb) . "` ADD KEY `$c` (`$c`)"); }
            }
        }
        $db->query('ALTER TABLE `' . $t('customers') . '` MODIFY mobile VARCHAR(20) NULL');
        $db->query($db->prepare("REPLACE INTO `{$t('meta')}` (k, v) VALUES ('schema', %s)", (string) self::SCHEMA));
        return '';
    }

    /**
     * Until 2.7 the plugin's own data was files in wp-content/mazesta-connect-data and three tables in WordPress' database. The shop's word was that all of it was
     * test data: it is deleted, not moved, once the tables above exist. config.php (the keys, and this database's own connection), the test reports (kept as
     * files on purpose) and an old-CRM import waiting to run stay; the comparison lists are built again from the (now empty) runs.
     */
    private static function forget_old()
    {
        global $wpdb;
        foreach (array('systems', 'events', 'logs') as $n) { $wpdb->query('DROP TABLE IF EXISTS `' . $wpdb->prefix . 'mzc_' . $n . '`'); }
        delete_option('mzc_stats_schema');
        $dir = Mazesta_Connect::dir();
        foreach (array_merge(glob($dir . '/*.php') ?: array(), glob($dir . '/*.tmp') ?: array(), glob($dir . '/benchdb/*.json') ?: array()) as $f) {
            $b = basename($f);   // the reports stay: they are files on purpose
            if (!in_array($b, array('config.php', 'index.php', 'reports.php'), true) && strncmp($b, 'report-', 7) !== 0) { @unlink($f); }
        }
    }

    /** The database is reachable and its tables are current (an older layout is brought up to date the first time). Asked once a request. */
    public static function ready()
    {
        static $ok = null;
        return $ok !== null ? $ok : ($ok = self::check());
    }

    private static function check()
    {
        $db = self::db(); if (!$db) { return false; }
        if (!$db->get_var($db->prepare('SHOW TABLES LIKE %s', $db->esc_like(self::t('customers'))))) { return false; }
        // a database made by an older plugin is brought up to date the first time a page asks
        if ((int) $db->get_var('SELECT v FROM `' . self::t('meta') . "` WHERE k = 'schema'") < self::SCHEMA) { return self::install() === ''; }
        return true;
    }

    /** Saves the connection (the password is left as it was when the field is empty); returns an error text or ''. */
    public static function save($host, $name, $user, $pass, $prefix)
    {
        $s = self::settings();
        $pass = $pass !== '' ? $pass : self::unseal($s['pass']);
        $e = self::test($host, $name, $user, $pass);
        if ($e !== '') { return $e; }
        Mazesta_Connect::config();
        Mazesta_Connect::locked(function () use ($host, $name, $user, $pass, $prefix) {
            $c = Mazesta_Connect::read('config');
            $c['crm'] = array('host' => $host, 'name' => $name, 'user' => $user, 'pass' => self::seal($pass), 'prefix' => $prefix);
            Mazesta_Connect::write('config', $c);
        });
        self::$db = false; self::$cfg = null;
        return '';
    }
}
