<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The CRM's own database. The customers, their systems and service jobs are never put in WordPress' database: this connects to a second MySQL database whose host, name,
 * user and password are entered on the plugin's settings page. The password is kept in the plugin's data folder (a file that PHP refuses to show), encrypted with the
 * site's own salts. Until a working connection is saved, the CRM pages say so and nothing is written anywhere.
 */
final class MZC_Crm_Db
{
    const SCHEMA = 3;
    private static $db = false;     // false: not tried yet; null: no connection
    private static $error = '';

    public static function settings()
    {
        $c = Mazesta_Connect::config();
        $x = isset($c['crm']) && is_array($c['crm']) ? $c['crm'] : array();
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
        foreach ($sql as $q) { if ($db->query($q) === false) { return 'ساخت جدول ناموفق بود: ' . $db->last_error; } }
        $db->query($db->prepare("REPLACE INTO `{$t('meta')}` (k, v) VALUES ('schema', %s)", (string) self::SCHEMA));
        return '';
    }

    public static function ready()
    {
        $db = self::db(); if (!$db) { return false; }
        return (bool) $db->get_var($db->prepare('SHOW TABLES LIKE %s', $db->esc_like(self::t('customers'))));
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
        self::$db = false;
        return '';
    }
}
