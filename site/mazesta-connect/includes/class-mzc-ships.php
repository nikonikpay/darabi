<?php
if (!defined('ABSPATH')) { exit; }

/**
 * Parts and systems the shop sends out for repair (the old CRM's "send to warranty" form): what was sent (model, serial, the fault), to which warranty company or repair shop,
 * when, what they answered, when it came back and what was done, and whose it is (the shop's own, or a customer's service or invoice). Rows live on the CRM database.
 */
final class MZC_Ships
{
    public static function boot()
    {
        foreach (array('ship_save', 'ship_delete', 'vendor_save', 'vendor_delete') as $a) { add_action('admin_post_mzc_' . $a, array(__CLASS__, 'act_' . $a)); }
    }

    private static function db() { return MZC_Crm_Db::db(); }
    private static function t($n) { return MZC_Crm_Db::t($n); }

    public static $kinds = array('warranty' => 'گارانتی', 'repair' => 'تعمیرگاه');

    public static function vendors()
    {
        $db = self::db(); if (!$db) { return array(); }
        return $db->get_results('SELECT v.*, (SELECT COUNT(*) FROM `' . self::t('shipments') . '` s WHERE s.vendor_id = v.id) AS sent FROM `' . self::t('vendors') . '` v ORDER BY v.kind, v.name', ARRAY_A) ?: array();
    }

    /** The vendor with this name, made when it is new ("... (تعمیرکار)" is a repair shop, anything else a warranty company). Returns its id, or null for an empty name. */
    public static function vendor_id($name, $kind = '')
    {
        $db = self::db(); $name = trim(preg_replace('/\s+/u', ' ', (string) $name));
        if (!$db || $name === '') { return null; }
        $name = mb_substr($name, 0, 120);
        $id = (int) $db->get_var($db->prepare('SELECT id FROM `' . self::t('vendors') . '` WHERE name = %s', $name));
        if ($id) { return $id; }
        if (!isset(self::$kinds[$kind])) { $kind = mb_strpos($name, 'تعمیر') !== false ? 'repair' : 'warranty'; }
        $db->insert(self::t('vendors'), array('name' => $name, 'kind' => $kind));
        return (int) $db->insert_id ?: null;
    }

    public static function ship($id)
    {
        $db = self::db(); if (!$db) { return null; }
        return $db->get_row($db->prepare('SELECT s.*, v.name AS vendor_name, c.name AS customer_name, c.mobile AS customer_mobile, j.service_no AS job_no FROM `' . self::t('shipments') . '` s LEFT JOIN `' . self::t('vendors') . '` v ON v.id = s.vendor_id
            LEFT JOIN `' . self::t('customers') . '` c ON c.id = s.customer_id LEFT JOIN `' . self::t('jobs') . '` j ON j.id = s.job_id WHERE s.id = %d', (int) $id), ARRAY_A);
    }

    /** A page of shipments, newest first. $status: 'sent' (still out) or 'back'; $vendor a vendor id. */
    public static function ships($q, $status, $vendor, $page, $per = 30, $customer = 0)
    {
        $db = self::db(); if (!$db) { return array(array(), 0); }
        $where = array('1=1'); $args = array();
        if ($customer) { $where[] = 's.customer_id = %d'; $args[] = (int) $customer; }
        if ($status === 'sent') { $where[] = 's.received_at IS NULL'; } elseif ($status === 'back') { $where[] = 's.received_at IS NOT NULL'; }
        if ($vendor) { $where[] = 's.vendor_id = %d'; $args[] = (int) $vendor; }
        if ($q !== '') {
            $l = '%' . $db->esc_like($q) . '%';
            $where[] = '(s.part_name LIKE %s OR s.serial LIKE %s OR s.problem LIKE %s OR s.tracking LIKE %s OR s.ref LIKE %s OR s.back_serial LIKE %s OR c.name LIKE %s OR v.name LIKE %s)';
            array_push($args, $l, $l, $l, $l, $l, $l, $l, $l);
        }
        $from = 'FROM `' . self::t('shipments') . '` s LEFT JOIN `' . self::t('vendors') . '` v ON v.id = s.vendor_id LEFT JOIN `' . self::t('customers') . '` c ON c.id = s.customer_id WHERE ' . implode(' AND ', $where);
        $total = (int) $db->get_var($args ? $db->prepare("SELECT COUNT(*) $from", $args) : "SELECT COUNT(*) $from");
        $rows = $db->get_results($db->prepare("SELECT s.*, v.name AS vendor_name, c.name AS customer_name $from ORDER BY COALESCE(s.sent_at, DATE(s.created)) DESC, s.id DESC LIMIT %d OFFSET %d", array_merge($args, array((int) $per, (int) (($page - 1) * $per)))), ARRAY_A);
        return array($rows ?: array(), $total);
    }

    /** The customer (and the service) a shipment belongs to, found from what the desk typed: a service number, an invoice number, or the customer chosen on the page. Returns array(customer_id, job_id) or an error text. */
    private static function owner_of($db, $cid, $ref)
    {
        $job = 0;
        if ($ref !== '') {
            $j = $db->get_row($db->prepare('SELECT id, customer_id FROM `' . self::t('jobs') . '` WHERE service_no = %s OR invoice_no = %s ORDER BY id DESC LIMIT 1', $ref, $ref), ARRAY_A);
            if ($j) { $job = (int) $j['id']; if (!$cid) { $cid = (int) $j['customer_id']; } }
            elseif (!$cid && ($b = $db->get_row($db->prepare('SELECT customer_id FROM `' . self::t('builds') . '` WHERE invoice_no = %s OR service_no = %s ORDER BY id DESC LIMIT 1', $ref, $ref), ARRAY_A))) { $cid = (int) $b['customer_id']; }
        }
        if (!$cid || !MZC_Crm::customer($cid)) { return 'مشتری این قطعه پیدا نشد؛ شمارهٔ سرویس یا فاکتور را درست بنویسید یا «مال مازستا» را بزنید.'; }
        return array($cid, $job);
    }

    public static function act_ship_save()
    {
        MZC_Admin::guard('ship_save');
        $db = self::db(); if (!$db) { MZC_Admin::back('mzc-ships', 'err:' . MZC_Crm_Db::error()); }
        $id = isset($_POST['id']) ? (int) $_POST['id'] : 0;
        $back = $id ? array('edit' => $id) : array('add' => 1);
        $part = MZC_Crm::post('part_name', 190);
        if ($part === '') { MZC_Admin::back('mzc-ships', 'err:نام و مدل قطعه را بنویسید.', $back); }
        $owner = isset($_POST['owner']) && $_POST['owner'] === 'customer' ? 'customer' : 'mazesta'; $cid = null; $job = null; $ref = MZC_Crm::post('ref', 60);
        if ($owner === 'customer') {
            $r = self::owner_of($db, isset($_POST['customer_id']) ? (int) $_POST['customer_id'] : 0, $ref);
            if (!is_array($r)) { MZC_Admin::back('mzc-ships', 'err:' . $r, $back); }
            list($cid, $job) = $r; $job = $job ?: null;
        }
        $vid = MZC_Crm::post('vendor_new', 120) !== '' ? self::vendor_id(MZC_Crm::post('vendor_new', 120)) : (isset($_POST['vendor_id']) ? (int) $_POST['vendor_id'] : 0);
        $row = array('vendor_id' => $vid ?: null, 'part_name' => $part, 'serial' => MZC_Crm::post('serial', 120) ?: null, 'problem' => MZC_Crm::post_long('problem') ?: null, 'sent_at' => MZC_Crm::date('sent_at'),
            'tracking' => MZC_Crm::post('tracking', 80) ?: null, 'vendor_reply' => MZC_Crm::post_long('vendor_reply') ?: null, 'received_at' => MZC_Crm::date('received_at'), 'fix_notes' => MZC_Crm::post_long('fix_notes') ?: null,
            'back_part' => MZC_Crm::post('back_part', 190) ?: null, 'back_serial' => MZC_Crm::post('back_serial', 120) ?: null, 'shelf' => MZC_Crm::post('shelf', 60) ?: null, 'picked_up' => empty($_POST['picked_up']) ? 0 : 1,
            'owner' => $owner, 'customer_id' => $cid, 'job_id' => $job, 'ref' => $ref ?: null, 'customer_reply' => MZC_Crm::post_long('customer_reply') ?: null, 'updated' => gmdate('Y-m-d H:i:s'));
        $t = self::t('shipments');
        if ($id) { if ($db->update($t, $row, array('id' => $id)) === false) { MZC_Admin::back('mzc-ships', 'err:ذخیره نشد: ' . $db->last_error, $back); } }
        else { $row['created'] = $row['updated']; if (!$db->insert($t, $row)) { MZC_Admin::back('mzc-ships', 'err:ذخیره نشد: ' . $db->last_error, $back); } }
        MZC_Admin::back('mzc-ships', 'ok:ثبت شد.');
    }

    public static function act_ship_delete()
    {
        MZC_Admin::guard('ship_delete');
        $db = self::db(); $id = isset($_GET['id']) ? (int) $_GET['id'] : 0;
        if ($db && $id) { $db->delete(self::t('shipments'), array('id' => $id)); }
        MZC_Admin::back('mzc-ships', 'ok:پاک شد.');
    }

    public static function act_vendor_save()
    {
        MZC_Admin::guard('vendor_save');
        $db = self::db(); $name = MZC_Crm::post('name', 120);
        if (!$db || $name === '') { MZC_Admin::back('mzc-ships', 'err:نام را بنویسید.'); }
        $kind = isset($_POST['kind'], self::$kinds[$_POST['kind']]) ? (string) $_POST['kind'] : 'warranty';
        $id = self::vendor_id($name, $kind);
        if ($id) { $db->update(self::t('vendors'), array('kind' => $kind, 'phone' => MZC_Crm::post('phone', 60) ?: null), array('id' => $id)); }
        MZC_Admin::back('mzc-ships', 'ok:ثبت شد.');
    }

    public static function act_vendor_delete()
    {
        MZC_Admin::guard('vendor_delete');
        $db = self::db(); $id = isset($_GET['id']) ? (int) $_GET['id'] : 0;
        if ($db && $id) {
            if ((int) $db->get_var($db->prepare('SELECT COUNT(*) FROM `' . self::t('shipments') . '` WHERE vendor_id = %d', $id))) { MZC_Admin::back('mzc-ships', 'err:برای این مورد ارسال ثبت شده؛ پاک نمی‌شود.'); }
            $db->delete(self::t('vendors'), array('id' => $id));
        }
        MZC_Admin::back('mzc-ships', 'ok:پاک شد.');
    }
}
