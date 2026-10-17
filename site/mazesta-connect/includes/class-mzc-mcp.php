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
        if ($method === 'tools/list') { return self::result($id, array('tools' => self::tools())); }
        if ($method === 'tools/call') {
            $name = isset($params['name']) ? (string) $params['name'] : ''; $args = isset($params['arguments']) && is_array($params['arguments']) ? $params['arguments'] : array();
            $all = self::defs(); if (!isset($all[$name])) { return self::error($id, -32602, 'Unknown tool: ' . $name); }
            try { list($status, $data) = call_user_func($all[$name][3], $args, $token); }
            catch (Throwable $e) { $status = 500; $data = array('error' => 'Internal error: ' . $e->getMessage()); }
            $isErr = $status >= 400;
            $rest = is_array($data) ? array_diff_key($data, array('error' => 1)) : array();   // e.g. the duplicate record, the candidates, the current version
            $text = $isErr ? (is_array($data) && isset($data['error']) ? (string) $data['error'] : 'Error ' . $status) . ($rest ? ' ' . wp_json_encode($rest, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES) : '')
                : wp_json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
            $res = array('content' => array(array('type' => 'text', 'text' => $text)), 'isError' => $isErr);
            if (!$isErr && is_array($data) && $data && array_keys($data) !== range(0, count($data) - 1)) { $res['structuredContent'] = $data; }
            return self::result($id, $res);
        }
        if ($method === 'resources/list') { return self::result($id, array('resources' => array())); }
        if ($method === 'prompts/list') { return self::result($id, array('prompts' => array())); }
        return self::error($id, -32601, 'Method not found: ' . $method);
    }

    const INSTRUCTIONS = "This is the CRM of Mazesta, a computer shop in Iran (Persian data; prices in toman). You act for the signed-in staff member and can do only what their role allows.\n"
        . "Records: customers; 'systems' = new computers the shop built and sold (known by the accounting INVOICE number, plus the buyer's name; older ones also have a service number R-n); "
        . "'services' = repair/service jobs (known by the service number R-n, plus the owner's name); products = the stock; purchases = suppliers' invoices that brought products in.\n"
        . "When someone names a system or service by a number (e.g. \"1289\"), pass it as `number`. Before changing a record, read it (get_system / get_service) so you keep what is there. "
        . "To add parts to a system use add_parts (keeps the existing ones). Pick parts from the stock: search find_products and pass product_id, so prices and stock follow. "
        . "If the user sends photos of part labels or boxes, read the model and serial number from them and fill the fields; ask when a value is unreadable - never guess serials. "
        . "Dates: YYYY-MM-DD, Gregorian or Persian (1405/07/25). Photos/videos live in the shop's Telegram channel: store their post links in `media`. "
        . "Confirm with the user before saving anything you inferred rather than were told.";

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
        $res = call_user_func(array('MZC_Api', 'rest_' . $fn), self::request($method, $query, $body, $token, $url));
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
        $cats = array('type' => 'string', 'enum' => array_keys(MZC_Crm::$categories));
        $bpart = self::obj(array('product_id' => array('type' => 'integer', 'description' => 'From find_products; sets the price and takes it out of stock.'), 'category' => $cats, 'model' => $str, 'serial' => $str, 'warranty_months' => $int,
            'sell_price' => $money, 'buy_price' => $money, 'vendor' => array('type' => 'string', 'description' => 'Warranty company.'), 'note' => $str, 'has_box' => $bool, 'qc' => $bool), array('model'));
        $jpart = self::obj(array('product_id' => $int, 'name' => $str, 'qty' => $int, 'unit_price' => $money, 'buy_price' => $money, 'serial' => $str, 'warranty_months' => $int, 'note' => $str), array('name'));
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
                function ($a, $t) { return self::api('customers', $t, 'GET', self::only($a, array('query' => 'q', 'group' => 'group', 'page' => 'page'))); }),
            'get_customer' => array('One customer with their systems and services.', self::obj(array('id' => $int), array('id')), true,
                function ($a, $t) { return self::api('customer', $t, 'GET', array(), array(), array('id' => (int) $a['id'])); }),
            'save_customer' => array('Create (no id) or update a customer (fields left out keep their value).', self::obj(array('id' => $int, 'name' => $str, 'mobile' => $str, 'mobile2' => $str, 'phone' => $str, 'national_id' => $str, 'email' => $str, 'address' => $str, 'notes' => $str, 'group' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$groups)))), false,
                function ($a, $t) {
                    if (!empty($a['id'])) { list($st, $d) = self::api('customer', $t, 'GET', array(), array(), array('id' => (int) $a['id'])); if ($st >= 400) { return array($st, $d); } $a += $d['customer']; unset($a['updated']); }
                    return self::api('customer_save', $t, 'POST', array(), $a);
                }),
            'list_systems' => array('List new computers (sales). Statuses: ' . implode(', ', array_keys(MZC_Crm::$build_statuses)) . '.', self::obj(array('query' => $str, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$build_statuses)), 'page' => $page)), true,
                function ($a, $t) { return self::api('builds', $t, 'GET', self::only($a, array('query' => 'q', 'status' => 'status', 'page' => 'page'))); }),
            'get_system' => array('One new computer with its parts, by id or by its invoice / service number.', self::obj(array('id' => $int, 'number' => $num)), true,
                function ($a, $t) { $id = self::rid('build', $a); return $id ? self::api('build', $t, 'GET', array(), array(), array('id' => $id)) : self::not_found('System', $a); }),
            'save_system' => array('Register a new computer (needs the invoice number and the customer) or update one (id or number). `parts` replaces the part list; `add_parts` appends to it. A serial already used elsewhere comes back as a warning.',
                self::obj($cust + array('id' => $int, 'number' => $num, 'invoice_no' => $str, 'title' => $str, 'device_type' => $dtype, 'sold_at' => $date, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$build_statuses)), 'notes' => $str, 'media' => $media,
                    'parts' => array('type' => 'array', 'items' => $bpart), 'add_parts' => array('type' => 'array', 'items' => $bpart), 'force' => array('type' => 'boolean', 'description' => 'Save although the invoice number is already used (only when the user confirms).'))), false,
                function ($a, $t) { return self::save_record('build', $a, $t); }),
            'set_system_status' => array('Move a new computer to another step.', self::obj(array('id' => $int, 'number' => $num, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$build_statuses))), array('status')), false,
                function ($a, $t) { $id = self::rid('build', $a); return $id ? self::api('build_status', $t, 'POST', array(), array('id' => $id, 'status' => $a['status'])) : self::not_found('System', $a); }),
            'list_services' => array('List service jobs. Statuses: ' . implode(', ', array_keys(MZC_Crm::$statuses)) . '.', self::obj(array('query' => $str, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$statuses)), 'page' => $page)), true,
                function ($a, $t) { return self::api('jobs', $t, 'GET', self::only($a, array('query' => 'q', 'status' => 'status', 'page' => 'page'))); }),
            'get_service' => array('One service job with parts and payments, by id or service number.', self::obj(array('id' => $int, 'number' => $num)), true,
                function ($a, $t) { $id = self::rid('job', $a); return $id ? self::api('job', $t, 'GET', array(), array(), array('id' => $id)) : self::not_found('Service', $a); }),
            'save_service' => array('Receive a new service job (gets the next service number) or update one. `parts` = parts the shop added (replaces), `add_parts` appends, `recv` = what the customer handed in.',
                self::obj($cust + array('id' => $int, 'number' => $num, 'device' => array('type' => 'string', 'description' => 'What it is, e.g. "ASUS laptop".'), 'device_type' => $dtype, 'complaint' => $str, 'work_done' => $str, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$statuses)),
                    'received_at' => $date, 'due_at' => $date, 'invoice_no' => $str, 'is_mazesta' => $bool, 'has_warranty' => $bool, 'labor_price' => $money, 'discount' => $money, 'paid' => $money, 'notes' => $str, 'media' => $media,
                    'parts' => array('type' => 'array', 'items' => $jpart), 'add_parts' => array('type' => 'array', 'items' => $jpart), 'recv' => array('type' => 'array', 'items' => self::obj(array('name' => $str, 'serial' => $str, 'note' => $str, 'has_box' => $bool), array('name'))))), false,
                function ($a, $t) { return self::save_record('job', $a, $t); }),
            'set_service_status' => array('Move a service job to another status.', self::obj(array('id' => $int, 'number' => $num, 'status' => array('type' => 'string', 'enum' => array_keys(MZC_Crm::$statuses))), array('status')), false,
                function ($a, $t) { $id = self::rid('job', $a); return $id ? self::api('job_status', $t, 'POST', array(), array('id' => $id, 'status' => $a['status'])) : self::not_found('Service', $a); }),
            'find_products' => array('Search the stock (name, brand, part number) with stock count and prices.', self::obj(array('query' => $str, 'category' => $cats, 'in_stock' => $bool, 'page' => $page)), true,
                function ($a, $t) { return self::api('products', $t, 'GET', self::only($a, array('query' => 'q', 'category' => 'category', 'in_stock' => 'instock', 'page' => 'page'))); }),
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
                function ($a, $t) { return self::api('purchases', $t, 'GET', self::only($a, array('query' => 'q', 'supplier_id' => 'vendor', 'page' => 'page'))); }),
            'get_purchase' => array('One purchase invoice with its lines.', self::obj(array('id' => $int), array('id')), true,
                function ($a, $t) { return self::api('purchase', $t, 'GET', array(), array(), array('id' => (int) $a['id'])); }),
            'save_purchase' => array('Enter (no id) or replace a supplier\'s purchase invoice. Each line names a product by product_id, or by name (+category) - an unknown name becomes a new product. Serial numbers: one per unit.',
                self::obj(array('id' => $int, 'supplier_id' => $int, 'supplier_name' => array('type' => 'string', 'description' => 'When the supplier is not in list_suppliers (becomes a new one).'), 'invoice_no' => $str, 'bought_at' => $date, 'notes' => $str, 'force' => $bool,
                    'items' => array('type' => 'array', 'items' => self::obj(array('product_id' => $int, 'name' => $str, 'category' => $cats, 'qty' => $int, 'unit_cost' => $money, 'sell_price' => array('type' => 'integer', 'description' => 'Optional new selling price of the product.'), 'serials' => array('type' => 'array', 'items' => $str)), array('qty')))), array('items')), false,
                function ($a, $t) { $b = $a; if (isset($a['supplier_id'])) { $b['vendor_id'] = $a['supplier_id']; } if (isset($a['supplier_name'])) { $b['vendor_name'] = $a['supplier_name']; } return self::api('purchase_save', $t, 'POST', array(), $b); }),
            'trace_serial' => array('Where a serial number has been: bought on which invoice from whom, in which system or service, sent to which warranty company.', self::obj(array('serial' => $str), array('serial')), true,
                function ($a, $t) { return self::api('serial', $t, 'GET', array('q' => (string) $a['serial'])); }),
            'list_test_reports' => array('The Mazesta Test app\'s reports sent to the site (newest first); those of the given service number first.', self::obj(array('query' => $str, 'number' => $str)), true,
                function ($a, $t) { return self::api('reports', $t, 'GET', self::only($a, array('query' => 'q', 'number' => 'service'))); }),
            'attach_test_report' => array('Attach a test report to a system or a service (empty report_id removes it).', self::obj(array('kind' => array('type' => 'string', 'enum' => array('build', 'job')), 'id' => $int, 'number' => $num, 'report_id' => $str), array('kind', 'report_id')), false,
                function ($a, $t) { $id = self::rid($a['kind'] === 'build' ? 'build' : 'job', $a); return $id ? self::api('attach', $t, 'POST', array(), array('kind' => $a['kind'], 'id' => $id, 'report_id' => (string) $a['report_id'])) : self::not_found('Record', $a); }),
        );
    }

    private static function tools()
    {
        $out = array();
        foreach (self::defs() as $name => $d) { $out[] = array('name' => $name, 'description' => $d[0], 'inputSchema' => $d[1], 'annotations' => array('readOnlyHint' => $d[2], 'destructiveHint' => false, 'openWorldHint' => false)); }
        return $out;
    }

    /** The tool arguments renamed to the API's query names, empty ones left out. */
    private static function only($a, $map) { $o = array(); foreach ($map as $k => $q) { if (isset($a[$k]) && $a[$k] !== '' && is_scalar($a[$k])) { $o[$q] = is_bool($a[$k]) ? (int) $a[$k] : $a[$k]; } } return $o; }
}
