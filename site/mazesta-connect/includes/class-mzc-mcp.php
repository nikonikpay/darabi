<?php
if (!defined('ABSPATH')) { exit; }

/**
 * The MCP endpoint (/wp-json/mazesta/v1/mcp, Streamable HTTP, JSON answers only): ChatGPT or Claude, signed in as one of the shop's people (see MZC_Oauth), search the CRM and
 * register computers, services, purchases and products for them. Every tool is a call to the staff API (MZC_Api) with the person's own session, so it can do exactly what
 * that person could do in the staff program - the same role checks, the same audit log - and nothing else. Reading photos of part labels is the AI app's job: it reads the
 * model and serial off the picture and passes them to save_system / save_purchase like any typed value.
 */
final class MZC_Mcp
{
    const VERSIONS = array('2025-06-18', '2025-03-26', '2024-11-05');

    public static function boot() { add_action('rest_api_init', array(__CLASS__, 'routes')); }

    public static function routes()
    {
        register_rest_route(Mazesta_Connect::NS, '/mcp', array('methods' => array('GET', 'POST', 'DELETE'), 'callback' => array(__CLASS__, 'rest'), 'permission_callback' => '__return_true'));
    }

    /** The bearer token of the request (some hosts hand the header over only as REDIRECT_HTTP_AUTHORIZATION). */
    private static function token($req)
    {
        $h = (string) $req->get_header('authorization');
        if ($h === '') { foreach (array('HTTP_AUTHORIZATION', 'REDIRECT_HTTP_AUTHORIZATION') as $k) { if (!empty($_SERVER[$k])) { $h = (string) $_SERVER[$k]; break; } } }
        return preg_match('/^Bearer\s+([a-f0-9]{64})$/i', trim($h), $m) ? strtolower($m[1]) : '';
    }

    private static function unauthorized()
    {
        $r = Mazesta_Connect::fresh(array('error' => 'invalid_token', 'error_description' => 'Sign in first.'), 401);
        $r->header('WWW-Authenticate', 'Bearer realm="mazesta", resource_metadata="' . MZC_Oauth::resource_metadata() . '"');
        return $r;
    }

    public static function rest($req)
    {
        if ($req->get_method() !== 'POST') { $r = Mazesta_Connect::fresh(array('error' => 'Use POST.'), 405); $r->header('Allow', 'POST'); return $r; }
        $token = self::token($req);
        $who = $token !== '' ? MZC_Staff::auth(self::request('GET', array(), array(), $token)) : null;
        if (!is_array($who)) { return self::unauthorized(); }
        $body = json_decode((string) $req->get_body(), true);
        if (!is_array($body)) { return Mazesta_Connect::fresh(self::error(null, -32700, 'Parse error'), 400); }
        $batch = array_keys($body) === range(0, count($body) - 1) && $body;
        $out = array();
        foreach ($batch ? $body : array($body) as $msg) { $r = self::handle(is_array($msg) ? $msg : array(), $token); if ($r !== null) { $out[] = $r; } }
        if (!$out) { status_header(202); exit; }   // notifications only: nothing to answer
        return Mazesta_Connect::fresh($batch ? $out : $out[0]);
    }

    private static function error($id, $code, $msg) { return array('jsonrpc' => '2.0', 'id' => $id, 'error' => array('code' => $code, 'message' => $msg)); }
    private static function result($id, $res) { return array('jsonrpc' => '2.0', 'id' => $id, 'result' => $res); }

    private static function handle($m, $token)
    {
        $id = isset($m['id']) && (is_int($m['id']) || is_string($m['id'])) ? $m['id'] : null; $method = isset($m['method']) && is_string($m['method']) ? $m['method'] : '';
        $params = isset($m['params']) && is_array($m['params']) ? $m['params'] : array();
        if (!array_key_exists('id', $m)) { return null; }   // a notification (initialized, cancelled ...)
        if ($method === 'initialize') {
            $want = isset($params['protocolVersion']) ? (string) $params['protocolVersion'] : '';
            return self::result($id, array('protocolVersion' => in_array($want, self::VERSIONS, true) ? $want : self::VERSIONS[0], 'capabilities' => array('tools' => array('listChanged' => false)),
                'serverInfo' => array('name' => 'mazesta-crm', 'title' => 'Mazesta CRM', 'version' => MZC_VERSION), 'instructions' => self::INSTRUCTIONS));
        }
        if ($method === 'ping') { return self::result($id, new stdClass()); }
        if ($method === 'tools/list') { return self::result($id, array('tools' => self::tools($token))); }
        if ($method === 'tools/call') {
            $name = isset($params['name']) ? (string) $params['name'] : ''; $args = isset($params['arguments']) && is_array($params['arguments']) ? $params['arguments'] : array();
            $all = self::defs(); if (!isset($all[$name])) { return self::error($id, -32602, 'Unknown tool: ' . $name); }
            if (!self::allowed($name, self::perms($token))) { return self::result($id, array('content' => array(array('type' => 'text', 'text' => 'دسترسی این کار را ندارید. (Not allowed for this person\'s role; the shop\'s manager can give it on the site.)')), 'isError' => true)); }
            try { list($status, $data) = call_user_func($all[$name][3], $args, $token); }
            catch (Throwable $e) { $status = 500; $data = array('error' => 'Internal error: ' . $e->getMessage()); }
            $isErr = $status >= 400; if (!$isErr) { $data = self::lean($data); }
            $rest = is_array($data) ? array_diff_key($data, array('error' => 1)) : array();   // e.g. the duplicate record, the candidates, the current version
            $text = $isErr ? (is_array($data) && isset($data['error']) ? (string) $data['error'] : 'Error ' . $status) . ($rest ? ' ' . wp_json_encode($rest, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES) : '')
                : wp_json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
            // no structuredContent: it repeated the whole answer next to the text and doubled what the model reads
            return self::result($id, array('content' => array(array('type' => 'text', 'text' => $text)), 'isError' => $isErr));
        }
        if ($method === 'resources/list') { return self::result($id, array('resources' => array())); }
        if ($method === 'prompts/list') { return self::result($id, array('prompts' => array())); }
        return self::error($id, -32601, 'Method not found: ' . $method);
    }

    /** Rows per page of the list tools: the model asks for the next page when it needs it. */
    const PER = 10;

    const INSTRUCTIONS = "This is the CRM of Mazesta, a computer shop in Iran (Persian data; prices in toman). You act for the signed-in staff member and can do only what their role allows.\n"
        . "Records: customers; 'systems' = new computers the shop built and sold (known by the accounting INVOICE number, plus the buyer's name; older ones also have a service number R-n); "
        . "'services' = repair/service jobs (known by the service number R-n, plus the owner's name); products = the stock; purchases = suppliers' invoices that brought products in.\n"
        . "When someone names a system or service by a number (e.g. \"1289\"), pass it as `number`. Before changing a record, read it (get_system / get_service) so you keep what is there. "
        . "To add parts to a system use add_parts (keeps the existing ones). Pick parts from the stock: search find_products and pass product_id, so prices and stock follow. "
        . "If the user sends photos of part labels or boxes, read the model and serial number from them and fill the fields; ask when a value is unreadable - never guess serials. "
        . "Dates: YYYY-MM-DD, Gregorian or Persian (1405/07/25); answers give Persian dates (*_j). Lists come 10 per page with `total`: ask for the next page only when needed. "
        . "A service's money: labor_price = what we charge for the work, discount, parts (sell and buy prices); total (what the customer pays) = parts + labor - discount. paid = what the shop itself paid out for the job (courier, outside repair; paid_note says for what): the customer never sees it, it only lowers the service profit (labor - discount - paid); parts profit = sell - buy. "
        . "For money and counts over a period (profit, how many services/systems, best sellers) call `stats` instead of paging through lists. "
        . "`software` = the programs the customer wants installed (name, done); `software_note` = their request in words. "
        . "Accounting: an invoice is made as a DRAFT (save_invoice), then invoice_action finalize gives it its number - a final invoice never changes (void it, or copy it to a return). "
        . "Ready-made invoices are kind=template: list_invoices kind=template, then invoice_action copy to_kind=sale (today's prices) and edit the draft. A proforma (پیش‌فاکتور) copies to a sale the same way. "
        . "Money received or paid is save_payment; cheques are save_cheque / list_cheques (due=soon for the reminder); customer_account shows a customer's balance. "
        . "Confirm with the user before saving anything you inferred rather than were told, and always before finalize or void.";

    /** An answer as the model reads it: no empty values, no edit stamps, the Persian date only where both are sent, and a list's number only once (fewer tokens, same facts). */
    private static function lean($v)
    {
        if (!is_array($v)) { return $v; }
        $assoc = $v && array_keys($v) !== range(0, count($v) - 1); $out = array();
        foreach ($v as $k => $x) {
            if ($assoc && ($k === 'updated' || $k === 'deviceText' || (is_string($k) && isset($v[$k . '_j'])))) { continue; }
            if ($assoc && ($k === 'service_no' || $k === 'invoice_no') && isset($v['number']) && $x === $v['number']) { continue; }
            $x = self::lean($x);
            if ($x === null || $x === '' || $x === array()) { continue; }
            if ($assoc) { $out[$k] = $x; } else { $out[] = $x; }
        }
        return $out;
    }

    /* ---------- tools ---------- */

    /** A request as the staff API reads it, carrying the person's session. */
    private static function request($method, $query, $body, $token, $url = array())
    {
        $r = new WP_REST_Request($method);
        $r->set_header('x_mzc_token', $token); $r->set_query_params($query); $r->set_url_params($url);
        if ($method === 'POST') { $r->set_header('content-type', 'application/json'); $r->set_body(wp_json_encode($body)); }
        return $r;
    }

    /** Runs one staff API handler; array(status, data). */
    private static function api($fn, $token, $method = 'GET', $query = array(), $body = array(), $url = array())
    {
        $res = call_user_func(array(method_exists('MZC_Api', 'rest_' . $fn) ? 'MZC_Api' : 'MZC_Acc_Api', 'rest_' . $fn), self::request($method, $query, $body, $token, $url));
        return array($res->status, $res->data);
    }

    private static function pick($a, $keys) { $o = array(); foreach ($keys as $k) { if (array_key_exists($k, $a)) { $o[$k] = $a[$k]; } } return $o; }

    /** The id a tool's arguments name: `id`, or `number` (invoice or service number) looked up. 0 when neither finds one record. */
    private static function rid($kind, $a)
    {
        if (!empty($a['id'])) { return (int) $a['id']; }
        return isset($a['number']) && is_scalar($a['number']) ? MZC_Crm::find_number($kind, (string) $a['number']) : 0;
    }

    private static function not_found($what, $a) { return array(404, array('error' => $what . (isset($a['number']) ? ' ' . $a['number'] : '') . ' not found (or the number matches several records; search first).')); }

    /** save_system / save_service: the customer by id or mobile + name, `add_parts` appended to the record's current parts, `id`/`number` for an existing record. */
    private static function save_record($kind, $a, $token)
    {
        $id = self::rid($kind, $a);
        if (isset($a['number']) && empty($a['id']) && !$id && $kind === 'job') { return self::not_found('Service', $a); }
        $body = $a; unset($body['number'], $body['add_parts']); $body['id'] = $id;
        if (isset($a['customer_mobile'])) { $body['c_mobile'] = $a['customer_mobile']; } if (isset($a['customer_name'])) { $body['c_name'] = $a['customer_name']; }
        if ($kind === 'build' && !$id && isset($a['number']) && !isset($body['invoice_no'])) { $body['invoice_no'] = (string) $a['number']; }   // "register system 1289" = a new one with that invoice number
        if (!empty($a['add_parts']) && is_array($a['add_parts'])) {
            $cur = array();
            if ($id) { list($st, $d) = self::api($kind, $token, 'GET', array(), array(), array('id' => $id)); if ($st >= 400) { return array($st, $d); } $cur = isset($d['parts']) ? $d['parts'] : array(); }
            $body['parts'] = array_merge(isset($body['parts']) && is_array($body['parts']) ? $body['parts'] : $cur, $a['add_parts']);
        }
        return self::api($kind . '_save', $token, 'POST', array(), $body);
    }

    private static function obj($props, $req = array())
    {
        $o = array('type' => 'object', 'properties' => $props ? $props : new stdClass());
        if ($req) { $o['required'] = $req; }
        return $o;
    }

    /** name => description, input schema, read-only?, run(args, token) => array(status, data). */
    private static function defs()
    {
        $str = array('type' => 'string'); $int = array('type' => 'integer'); $bool = array('type' => 'boolean');
        $page = array('type' => 'integer', 'description' => 'Page number, from 1.');
        $money = array('type' => 'integer', 'description' => 'Toman.');
        $date = array('type' => 'string', 'description' => 'YYYY-MM-DD, Gregorian or Persian (e.g. 1405/07/25).');
        $num = array('type' => 'string', 'description' => 'Invoice number or service number as people say it (e.g. "1289" or "R-3579").');
        $dtype = array('type' => 'string', 'enum' => array_keys(MZC_Crm::$device_types), 'description' => 'system / laptop / part / other.');
        $media = array('type' => 'array', 'items' => $str, 'description' => 'https links to the photos/videos (Telegram channel posts). Replaces the list.');
        $soft = array('software' => array('type' => 'array', 'description' => 'Programs to install; replaces the list.', 'items' => self::obj(array('name' => $str, 'done' => array('type' => 'boolean', 'description' => 'Installed and runs.')), array('name'))),
            'software_note' => array('type' => 'string', 'description' => 'The customer\'s software requests in words.'));
        $cats = array('type' => 'string', 'enum' => array_keys(MZC_Crm::$categories));
        // the tick lists: the keys of each device type, so the AI app can tick what the person confirms (all of it, after reading a photo, or item by item)
        $keys = function ($l) { $o = array(); foreach (MZC_Crm::$checklists[$l] as $t => $items) { $o[] = $t . ': ' . implode(', ', array_map(function ($k, $v) { return $k . ' (' . $v . ')'; }, array_keys($items), $items)); } return implode('; ', $o); };
        $checks = array('checks' => self::obj(array('recv' => array('type' => 'array', 'items' => $str, 'description' => 'Ticked keys of what came in with the device (services only; a new computer has none). ' . $keys('recv')),
            'check' => array('type' => 'array', 'items' => $str, 'description' => 'Ticked keys of the checks before it leaves. ' . $keys('check')), 'note' => array('type' => 'string', 'description' => 'Notes on what came in.'))));
        $checks['checks']['description'] = 'The checklist; replaces the ticks, and keys outside the record\'s device type are dropped. Tick only what the person confirmed; get the record first to keep its current ticks.';
        $bpart = self::obj(array('product_id' => array('type' => 'integer', 'description' => 'From find_products; sets the price and takes it out of stock.'), 'category' => $cats, 'model' => $str, 'serial' => $str, 'warranty_months' => $int,
            'sell_price' => $money, 'buy_price' => $money, 'vendor' => array('type' => 'string', 'description' => 'Warranty company.'), 'note' => $str, 'has_box' => $bool, 'qc' => $bool), array('model'));
        $jpart = self::obj(array('product_id' => $int, 'name' => $str, 'qty' => $int, 'unit_price' => $money, 'buy_price' => $money, 'serial' => $str, 'warranty_months' => $int, 'note' => $str), array('name'));
        $ikind = array('type' => 'string', 'enum' => array_keys(MZC_Acc::$kinds), 'description' => 'sale, return, proforma, template (ready-made invoice).');
        $cust = array('customer_id' => $int, 'customer_mobile' => array('type' => 'string', 'description' => 'Finds the customer, or makes a new one with customer_name.'), 'customer_name' => $str);
        return array(
            'whoami' => array('Who is signed in and what their role lets them do (levels per area: 0 none, 1 view, 2 edit).', self::obj(array()), true,
                function ($a, $t) { $r = MZC_Staff::rest_me(self::request('GET', array(), array(), $t)); return array($r->status, $r->data); }),
            'search' => array('Search everything at once: customers (name, mobile), systems and services (number, name, serial), products, and a serial number\'s history.', self::obj(array('query' => $str), array('query')), true,
                function ($a, $t) {
                    $q = isset($a['query']) ? (string) $a['query'] : ''; list($st, $d) = self::api('find', $t, 'GET', array('q' => $q)); if ($st >= 400) { return array($st, $d); }
                    list($s2, $p) = self::api('products', $t, 'GET', array('q' => $q, 'per' => 10)); if ($s2 < 400) { $d['products'] = $p['items']; }
                    if (mb_strlen($q) >= 3) { list($s3, $sr) = self::api('serial', $t, 'GET', array('q' => $q)); if ($s3 < 400 && array_filter($sr)) { $d['serial'] = $sr; } }
                    return array(200, $d);
                }),
            'list_customers' => array('List or search customers.', self::obj(array('query' => $str, 'group' => $str, 'page' => $page)), true,
                function ($a, $t) { return self::api('customers', $t, 'GET', self::only($a, array('query' => 'q', 'group' => 'group', 'page' => 'page')) + array('per' => self::PER)); }),
            'get_customer' => array('One customer with their systems and services.', self::obj(array('id' => $int), array('id')), true,
                function ($a, $t) { return self::api('customer', $t, 'GET', array(), array(), array('id' => (int) $a['id'])); }),
            'save_customer' => array('Create (no id) or update a customer (fields left out keep their value).', self::obj(array('id' => $int, 'name' => $str, 'mobiles' => array('type' => 'array', 'items' => $str, 'description' => 'Every mobile number, the main one first; replaces the list.'), 'mobile' => $str, 'mobile2' => $str, 'phone' => $str, 'national_id' => $str, 'email' => $str, 'address' => $str, 'notes' => $str, 'group' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$groups)))), false,
                function ($a, $t) {
                    if (!empty($a['id'])) { list($st, $d) = self::api('customer', $t, 'GET', array(), array(), array('id' => (int) $a['id'])); if ($st >= 400) { return array($st, $d); } $mine = isset($a['mobiles']); $a += $d['customer']; unset($a['updated']); if (!$mine) { unset($a['mobiles']); } }   // the stored list would override a mobile the model changed
                    return self::api('customer_save', $t, 'POST', array(), $a);
                }),
            'list_systems' => array('List new computers (sales). Statuses: ' . implode(', ', array_keys(MZC_Crm::$build_statuses)) . '.', self::obj(array('query' => $str, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$build_statuses)), 'page' => $page)), true,
                function ($a, $t) { return self::api('builds', $t, 'GET', self::only($a, array('query' => 'q', 'status' => 'status', 'page' => 'page')) + array('per' => self::PER)); }),
            'get_system' => array('One new computer with its parts, by id or by its invoice / service number.', self::obj(array('id' => $int, 'number' => $num)), true,
                function ($a, $t) { $id = self::rid('build', $a); return $id ? self::api('build', $t, 'GET', array(), array(), array('id' => $id)) : self::not_found('System', $a); }),
            'save_system' => array('Register a new computer (needs the invoice number and the customer) or update one (id or number). `parts` replaces the part list; `add_parts` appends to it. A serial already used elsewhere comes back as a warning.',
                self::obj($cust + $soft + $checks + array('id' => $int, 'number' => $num, 'invoice_no' => $str, 'sold_at' => $date, 'device_type' => $dtype, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$build_statuses)), 'notes' => $str,
                    'parts' => array('type' => 'array', 'items' => $bpart), 'add_parts' => array('type' => 'array', 'items' => $bpart), 'force' => array('type' => 'boolean', 'description' => 'Save although the invoice number is already used (only when the user confirms).'))), false,
                function ($a, $t) { return self::save_record('build', $a, $t); }),
            'set_system_status' => array('Move a new computer to another step.', self::obj(array('id' => $int, 'number' => $num, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$build_statuses))), array('status')), false,
                function ($a, $t) { $id = self::rid('build', $a); return $id ? self::api('build_status', $t, 'POST', array(), array('id' => $id, 'status' => $a['status'])) : self::not_found('System', $a); }),
            'list_services' => array('List service jobs. Statuses: ' . implode(', ', array_keys(MZC_Crm::$statuses)) . '.', self::obj(array('query' => $str, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$statuses)), 'page' => $page)), true,
                function ($a, $t) { return self::api('jobs', $t, 'GET', self::only($a, array('query' => 'q', 'status' => 'status', 'page' => 'page')) + array('per' => self::PER)); }),
            'get_service' => array('One service job with parts and payments, by id or service number.', self::obj(array('id' => $int, 'number' => $num)), true,
                function ($a, $t) { $id = self::rid('job', $a); return $id ? self::api('job', $t, 'GET', array(), array(), array('id' => $id)) : self::not_found('Service', $a); }),
            'save_service' => array('Receive a new service job (gets the next service number) or update one. `parts` = parts the shop added (replaces), `add_parts` appends, `recv` = what the customer handed in.',
                self::obj($cust + $soft + $checks + array('id' => $int, 'number' => $num, 'device' => array('type' => 'string', 'description' => 'A laptop\'s or part\'s model, e.g. "ASUS TUF F15".'), 'device_serial' => $str, 'device_type' => $dtype, 'complaint' => $str, 'work_done' => $str, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$statuses)),
                    'received_at' => $date, 'due_at' => $date, 'invoice_no' => $str, 'is_mazesta' => $bool, 'has_warranty' => $bool, 'labor_price' => $money, 'discount' => $money, 'paid' => array('type' => 'integer', 'description' => 'Toman the shop itself paid out for this job (courier, outside repair ...); internal, not on the customer\'s bill.'), 'paid_note' => $str, 'notes' => $str, 'media' => $media,
                    'parts' => array('type' => 'array', 'items' => $jpart), 'add_parts' => array('type' => 'array', 'items' => $jpart), 'recv' => array('type' => 'array', 'items' => self::obj(array('name' => $str, 'serial' => $str, 'note' => $str, 'has_box' => $bool), array('name'))))), false,
                function ($a, $t) { return self::save_record('job', $a, $t); }),
            'set_service_status' => array('Move a service job to another status.', self::obj(array('id' => $int, 'number' => $num, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$statuses))), array('status')), false,
                function ($a, $t) { $id = self::rid('job', $a); return $id ? self::api('job_status', $t, 'POST', array(), array('id' => $id, 'status' => $a['status'])) : self::not_found('Service', $a); }),
            'find_products' => array('Search the stock (name, brand, part number) with stock count and prices.', self::obj(array('query' => $str, 'category' => $cats, 'in_stock' => $bool, 'page' => $page)), true,
                function ($a, $t) { return self::api('products', $t, 'GET', self::only($a, array('query' => 'q', 'category' => 'category', 'in_stock' => 'instock', 'page' => 'page')) + array('per' => self::PER)); }),
            'product_report' => array('A product\'s history: which suppliers it was bought from, when, how many and at what cost; which customers it went to (system or service), when and at what price; the stock now.', self::obj(array('id' => $int, 'query' => array('type' => 'string', 'description' => 'A name to find the product when the id is not known.'))), true,
                function ($a, $t) {
                    $id = isset($a['id']) ? (int) $a['id'] : 0;
                    if (!$id && !empty($a['query'])) { list($st, $d) = self::api('products', $t, 'GET', array('q' => (string) $a['query'], 'per' => 5)); if ($st >= 400) { return array($st, $d); } if (count($d['items']) !== 1) { return array(count($d['items']) ? 422 : 404, array('error' => count($d['items']) ? 'Several products match; pick an id.' : 'No product matches.', 'candidates' => $d['items'])); } $id = $d['items'][0]['id']; }
                    return self::api('product', $t, 'GET', array(), array(), array('id' => $id));
                }),
            'save_product' => array('Create (no id) or update a stock product. Stock is never set by hand: it comes from purchases and from the parts used.', self::obj(array('id' => $int, 'name' => $str, 'category' => $cats, 'brand' => $str, 'sku' => array('type' => 'string', 'description' => 'Part number / barcode.'), 'sell_price' => $money, 'buy_price' => $money, 'warranty_months' => $int, 'notes' => $str)), false,
                function ($a, $t) { return self::api('product_save', $t, 'POST', array(), $a); }),
            'list_suppliers' => array('The suppliers (and warranty companies) a purchase can name, with their invoice counts.', self::obj(array()), true,
                function ($a, $t) { return self::api('suppliers', $t); }),
            'save_supplier' => array('Add or update a supplier.', self::obj(array('id' => $int, 'name' => $str, 'phone' => $str, 'notes' => $str), array('name')), false,
                function ($a, $t) {
                    if (!empty($a['id'])) { list($st, $d) = self::api('suppliers', $t); foreach ($st < 400 ? $d['items'] : array() as $v) { if ($v['id'] === (int) $a['id']) { $a += $v; } } }
                    return self::api('supplier_save', $t, 'POST', array(), $a);
                }),
            'list_purchases' => array('List purchase invoices (search by invoice number, supplier, product or serial).', self::obj(array('query' => $str, 'supplier_id' => $int, 'page' => $page)), true,
                function ($a, $t) { return self::api('purchases', $t, 'GET', self::only($a, array('query' => 'q', 'supplier_id' => 'vendor', 'page' => 'page')) + array('per' => self::PER)); }),
            'get_purchase' => array('One purchase invoice with its lines.', self::obj(array('id' => $int), array('id')), true,
                function ($a, $t) { return self::api('purchase', $t, 'GET', array(), array(), array('id' => (int) $a['id'])); }),
            'save_purchase' => array('Enter (no id) or replace a supplier\'s purchase invoice. Each line names a product by product_id, or by name (+category) - an unknown name becomes a new product. Serial numbers: one per unit.',
                self::obj(array('id' => $int, 'supplier_id' => $int, 'supplier_name' => array('type' => 'string', 'description' => 'When the supplier is not in list_suppliers (becomes a new one).'), 'invoice_no' => $str, 'bought_at' => $date, 'notes' => $str, 'force' => $bool,
                    'items' => array('type' => 'array', 'items' => self::obj(array('product_id' => $int, 'name' => $str, 'category' => $cats, 'qty' => $int, 'unit_cost' => $money, 'sell_price' => array('type' => 'integer', 'description' => 'Optional new selling price of the product.'), 'serials' => array('type' => 'array', 'items' => $str)), array('qty')))), array('items')), false,
                function ($a, $t) { $b = $a; if (isset($a['supplier_id'])) { $b['vendor_id'] = $a['supplier_id']; } if (isset($a['supplier_name'])) { $b['vendor_name'] = $a['supplier_name']; } return self::api('purchase_save', $t, 'POST', array(), $b); }),
            'stats' => array('Money and counts of a period: services (by day received: count per status, labor, parts, paid-out costs, discount, total, received, profit) and new computers (by day sold: count, sales, cost, profit), month by month, and the best-selling parts. Default: this Persian month.',
                self::obj(array('period' => array('type' => 'string', 'enum' => array('month', 'last', 'd30', 'year'), 'description' => 'this month, last month, last 30 days, this year (Persian calendar).'), 'from' => $date, 'to' => $date)), true,
                function ($a, $t) { $r = self::api('stats', $t, 'GET', self::only($a, array('period' => 'period', 'from' => 'from', 'to' => 'to'))); if (isset($r[1]['periods'])) { unset($r[1]['periods']); } return $r; }),
            'trace_serial' => array('Where a serial number has been: bought on which invoice from whom, in which system or service, sent to which warranty company.', self::obj(array('serial' => $str), array('serial')), true,
                function ($a, $t) { return self::api('serial', $t, 'GET', array('q' => (string) $a['serial'])); }),
            'list_test_reports' => array('The Mazesta Test app\'s reports sent to the site (newest first); those of the given service number first.', self::obj(array('query' => $str, 'number' => $str)), true,
                function ($a, $t) { return self::api('reports', $t, 'GET', self::only($a, array('query' => 'q', 'number' => 'service'))); }),
            'attach_test_report' => array('Attach a test report to a system or a service (empty report_id removes it).', self::obj(array('kind' => array('type' => 'string', 'enum' => array('build', 'job')), 'id' => $int, 'number' => $num, 'report_id' => $str), array('kind', 'report_id')), false,
                function ($a, $t) { $id = self::rid($a['kind'] === 'build' ? 'build' : 'job', $a); return $id ? self::api('attach', $t, 'POST', array(), array('kind' => $a['kind'], 'id' => $id, 'report_id' => (string) $a['report_id'])) : self::not_found('Record', $a); }),
            // ---- accounting ----
            'list_invoices' => array('List sales invoices, returns and proformas (kind), or the ready-made invoice templates (kind=template). Search by number, customer, product or serial.', self::obj(array('kind' => $ikind, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Acc::$statuses)), 'query' => $str, 'customer_id' => $int, 'from' => $date, 'to' => $date, 'page' => $page)), true,
                function ($a, $t) { return self::api('invoices', $t, 'GET', self::only($a, array('kind' => 'kind', 'status' => 'status', 'query' => 'q', 'customer_id' => 'customer', 'from' => 'from', 'to' => 'to', 'page' => 'page')) + array('per' => self::PER)); }),
            'get_invoice' => array('One invoice with its lines and payments, by id, or by its number and kind (default sale).', self::obj(array('id' => $int, 'number' => $int, 'kind' => $ikind)), true,
                function ($a, $t) { $id = self::iid($a); return $id ? self::api('invoice', $t, 'GET', array(), array(), array('id' => $id)) : self::not_found('Invoice', $a); }),
            'save_invoice' => array('Make (no id) or change a DRAFT invoice, proforma or template. A final invoice cannot change (void it or make a return). Lines name stock products by product_id (find_products) so price, cost and stock follow; free lines (assembly, Windows install) need name and unit_price. `items` replaces all lines. Then call invoice_action finalize to give it a number.',
                self::obj($cust + array('id' => $int, 'kind' => $ikind, 'title' => array('type' => 'string', 'description' => 'Template name, or a short description.'), 'at' => $date, 'valid_until' => $date, 'discount' => $money, 'tax_pct' => array('type' => 'number', 'description' => 'VAT percent; default from settings.'), 'notes' => $str,
                    'items' => array('type' => 'array', 'items' => self::obj(array('product_id' => $int, 'name' => $str, 'qty' => $int, 'unit_price' => $money, 'serials' => array('type' => 'array', 'items' => $str), 'warranty_months' => $int)))), array()), false,
                function ($a, $t) { $b = $a; if (isset($a['customer_mobile'])) { $b['c_mobile'] = $a['customer_mobile']; } if (isset($a['customer_name'])) { $b['c_name'] = $a['customer_name']; } if (!empty($a['id'])) { list($st, $d) = self::api('invoice', $t, 'GET', array(), array(), array('id' => (int) $a['id'])); if ($st >= 400) { return array($st, $d); } } return self::api('invoice_save', $t, 'POST', array(), $b); }),
            'invoice_action' => array('finalize: a draft sale/return gets its number and moves the stock (make_build: also queue a new computer for assembly from its parts). void: cancel a final invoice or a proforma (reason needed). copy: a new draft of `to_kind` from this one (template or proforma -> sale, sale -> return or template).',
                self::obj(array('id' => $int, 'number' => $int, 'kind' => $ikind, 'action' => array('type' => 'string', 'enum' => array('finalize', 'void', 'copy')), 'make_build' => $bool, 'reason' => $str, 'to_kind' => $ikind, 'customer_id' => $int), array('action')), false,
                function ($a, $t) {
                    $id = self::iid($a); if (!$id) { return self::not_found('Invoice', $a); }
                    if ($a['action'] === 'copy') { return self::api('copy', $t, 'POST', array(), array('id' => $id, 'kind' => isset($a['to_kind']) ? $a['to_kind'] : 'sale', 'customer_id' => isset($a['customer_id']) ? (int) $a['customer_id'] : 0)); }
                    return self::api($a['action'] === 'void' ? 'void' : 'finalize', $t, 'POST', array(), array('id' => $id, 'make_build' => !empty($a['make_build']), 'reason' => isset($a['reason']) ? (string) $a['reason'] : ''));
                }),
            'save_payment' => array('Record money: direction in (received: a customer\'s deposit, set customer_id and invoice_id if it pays one) or out (paid: to a supplier vendor_id, back to a customer, or an expense with category and no party). method cheque needs cheque {no, bank, due_at}.',
                self::obj(array('id' => $int, 'direction' => array('type' => 'string', 'enum' => array('in', 'out')), 'amount' => $money, 'method' => array('type' => 'string', 'enum' => array_keys(MZC_Acc::$methods)), 'account' => array('type' => 'string', 'description' => 'Which cash box / bank account / POS.'), 'at' => $date,
                    'customer_id' => $int, 'vendor_id' => $int, 'invoice_id' => $int, 'purchase_id' => $int, 'category' => array('type' => 'string', 'description' => 'Expense heading: ' . implode('، ', MZC_Acc::expense_heads())), 'tx' => $str, 'notes' => $str,
                    'cheque' => self::obj(array('no' => $str, 'bank' => $str, 'due_at' => $date))), array('amount')), false,
                function ($a, $t) { return self::api('payment_save', $t, 'POST', array(), $a); }),
            'list_payments' => array('Payments received and paid (filter by customer, supplier, invoice, direction, method, account, expense=true for expenses only, dates).', self::obj(array('customer_id' => $int, 'vendor_id' => $int, 'invoice_id' => $int, 'direction' => array('type' => 'string', 'enum' => array('in', 'out')), 'method' => $str, 'account' => $str, 'expense' => $bool, 'from' => $date, 'to' => $date, 'query' => $str, 'page' => $page)), true,
                function ($a, $t) { return self::api('payments', $t, 'GET', self::only($a, array('customer_id' => 'customer', 'vendor_id' => 'vendor', 'invoice_id' => 'invoice', 'direction' => 'direction', 'method' => 'method', 'account' => 'account', 'expense' => 'expense', 'from' => 'from', 'to' => 'to', 'query' => 'q', 'page' => 'page')) + array('per' => self::PER)); }),
            'list_cheques' => array('Cheques received (direction in) and written by the shop (out). due=soon: pending ones due within the reminder days; due=late: pending and past due.', self::obj(array('due' => array('type' => 'string', 'enum' => array('soon', 'late')), 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Acc::$cheque_statuses)), 'direction' => array('type' => 'string', 'enum' => array('in', 'out')), 'query' => $str, 'page' => $page)), true,
                function ($a, $t) { return self::api('cheques', $t, 'GET', self::only($a, array('due' => 'due', 'status' => 'status', 'direction' => 'direction', 'query' => 'q', 'page' => 'page')) + array('per' => self::PER)); }),
            'save_cheque' => array('Enter a cheque or change one (e.g. status cleared / bounced). pay=true also records it as a payment of that customer or supplier.', self::obj(array('id' => $int, 'direction' => array('type' => 'string', 'enum' => array('in', 'out')), 'no' => $str, 'bank' => $str, 'amount' => $money, 'issued_at' => $date, 'due_at' => $date, 'customer_id' => $int, 'vendor_id' => $int, 'party' => $str, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Acc::$cheque_statuses)), 'notes' => $str, 'pay' => $bool)), false,
                function ($a, $t) { return self::api('cheque_save', $t, 'POST', array(), $a); }),
            'customer_account' => array('A customer\'s account: running balance (positive = they owe), every invoice and proforma, payments and cheques.', self::obj(array('customer_id' => $int), array('customer_id')), true,
                function ($a, $t) { return self::api('customer_account', $t, 'GET', array(), array(), array('id' => (int) $a['customer_id'])); }),
            'accounting_summary' => array('The money of a period: sales, returns, profit, money in/out by method, expenses by heading; and now: account balances, cheques due soon, the customers who owe most.', self::obj(array('period' => array('type' => 'string', 'enum' => array('month', 'last', 'd30', 'year')), 'from' => $date, 'to' => $date)), true,
                function ($a, $t) { $r = self::api('summary', $t, 'GET', self::only($a, array('period' => 'period', 'from' => 'from', 'to' => 'to'))); if (isset($r[1]['periods'])) { unset($r[1]['periods']); } return $r; }),
        );
    }

    /** The invoice a tool names: `id`, or `number` of `kind` (default sale). 0 when not found. */
    private static function iid($a)
    {
        if (!empty($a['id'])) { return (int) $a['id']; }
        if (empty($a['number'])) { return 0; }
        $db = MZC_Crm_Db::db(); return $db ? (int) $db->get_var($db->prepare('SELECT id FROM `' . MZC_Crm_Db::t('invoices') . '` WHERE kind = %s AND no = %d', isset($a['kind'], MZC_Acc::$kinds[$a['kind']]) ? $a['kind'] : 'sale', (int) MZC_Crm::digits((string) $a['number']))) : 0;
    }

    /** The area and level each tool needs (the API checks again; this keeps tools the person may not use out of the model's sight). Tools not listed need only a sign-in. */
    private static $need = array(
        'list_customers' => array('customers', 1), 'get_customer' => array('customers', 1), 'save_customer' => array('customers', 2),
        'list_systems' => array('builds', 1), 'get_system' => array('builds', 1), 'save_system' => array('builds', 2), 'set_system_status' => array('builds', 1),
        'list_services' => array('jobs', 1), 'get_service' => array('jobs', 1), 'save_service' => array('jobs', 2), 'set_service_status' => array('jobs', 2),
        'save_product' => array('stock', 2), 'list_suppliers' => array('stock', 1), 'save_supplier' => array('stock', 2),
        'list_purchases' => array('stock', 1), 'get_purchase' => array('stock', 1), 'save_purchase' => array('stock', 2),
        'stats' => array('reports', 1), 'list_test_reports' => array('tests', 1), 'attach_test_report' => array('tests', 2),
        'list_invoices' => array('accounting', 1), 'get_invoice' => array('accounting', 1), 'save_invoice' => array('accounting', 2), 'invoice_action' => array('accounting', 2), 'list_payments' => array('accounting', 1),
        'save_payment' => array('payments', 2), 'list_cheques' => array('accounting', 1), 'save_cheque' => array('accounting', 2), 'customer_account' => array('payments', 1), 'accounting_summary' => array('accounting', 1),
    );

    /** The signed-in person's level per area (empty when the token is no longer good). */
    private static function perms($token)
    {
        $a = MZC_Staff::auth(self::request('GET', array(), array(), $token));
        return $a instanceof WP_REST_Response ? array() : MZC_Staff::perms_of($a['id']);
    }

    private static function allowed($name, $perms)
    {
        if (!isset(self::$need[$name])) { return (bool) array_filter($perms); }
        list($area, $lvl) = self::$need[$name];
        return isset($perms[$area]) && $perms[$area] >= $lvl;
    }

    private static function tools($token)
    {
        $out = array(); $perms = self::perms($token);
        foreach (self::defs() as $name => $d) { if (!self::allowed($name, $perms)) { continue; } $out[] = array('name' => $name, 'description' => $d[0], 'inputSchema' => $d[1], 'annotations' => array('readOnlyHint' => $d[2], 'destructiveHint' => false, 'openWorldHint' => false)); }
        return $out;
    }

    /** The tool arguments renamed to the API's query names, empty ones left out. */
    private static function only($a, $map) { $o = array(); foreach ($map as $k => $q) { if (isset($a[$k]) && $a[$k] !== '' && is_scalar($a[$k])) { $o[$q] = is_bool($a[$k]) ? (int) $a[$k] : $a[$k]; } } return $o; }
}
