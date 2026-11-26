using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Mazesta.Desk;

/// <summary>The site's own list and record answers, built from the copy: the same filters, order and page sizes as the plugin's routes. Anything this cannot answer as the site would
/// (the money reports, a product's history, a Persian date range) returns null and goes to the site.</summary>
internal sealed partial class Replica
{
    private static string Str(JsonObject o, string k) => o[k] is { } v ? v.ToString() : "";
    private static long Num(JsonObject o, string k) => o[k] is JsonValue v && long.TryParse(v.ToString(), out long n) ? n : 0;

    /// <summary>The fields each list is searched in, as the site's routes search them ("parts.serial": that field of every part).</summary>
    private static readonly Dictionary<string, string[]> Searched = new()
    {
        ["customers"] = ["name", "mobile", "mobile2", "mobiles", "national_id", "phone"], ["vendors"] = ["name"], ["products"] = ["name", "brand", "sku"],
        ["builds"] = ["service_no", "invoice_no", "title", "customer_name", "customer_mobile", "parts.serial", "parts.model"],
        ["jobs"] = ["service_no", "device", "device_serial", "complaint", "customer_name", "customer_mobile"],
        ["invoices"] = ["no", "title", "customer_name", "customer_mobile", "items.name", "items.serials"],
        ["purchases"] = ["invoice_no", "vendor_name", "notes", "items.name", "items.serials"],
        ["payments"] = ["customer_name", "vendor_name", "tx", "notes", "category"], ["cheques"] = ["no", "bank", "party", "customer_name", "vendor_name"],
        ["ships"] = ["part_name", "serial", "problem", "tracking", "ref", "back_serial", "customer_name", "vendor_name"], ["shortages"] = ["name", "ref", "note", "customer_name", "who"],
    };

    private static void Flat(JsonNode? node, StringBuilder sb)
    {
        switch (node)
        {
            case JsonArray a: foreach (var x in a) Flat(x, sb); break;
            case JsonValue v when v.GetValueKind() is JsonValueKind.String or JsonValueKind.Number: sb.Append(v.ToString()).Append(' '); break;
        }
    }

    /// <summary>A record as it is sent out: a copy, with the customer's and supplier's names as they are now.</summary>
    private JsonObject Named(JsonObject doc)
    {
        var o = (JsonObject)doc.DeepClone();
        if (Num(o, "customer_id") is > 0 and var c && Load("customers").TryGetValue(c, out var cu))
        {
            o["customer_name"] = Str(cu.Doc, "name"); o["customer_mobile"] = Str(cu.Doc, "mobile");
            if (o.ContainsKey("customer_address")) { o["customer_address"] = Str(cu.Doc, "address"); o["customer_national_id"] = Str(cu.Doc, "national_id"); }
        }
        if (Num(o, "vendor_id") is > 0 and var v && Load("vendors").TryGetValue(v, out var ve)) o["vendor_name"] = Str(ve.Doc, "name");
        if (o["parts"] is JsonArray parts && o.ContainsKey("title")) o["parts_count"] = parts.Count;
        if (o["items"] is JsonArray lines && (Str(o, "kind") == "template" || Str(o, "kind") == "proforma" && Str(o, "status") == "draft") && Holds("products")) o["short"] = Short(lines);
        return o;
    }

    /// <summary>The parts of an offer the stock does not have enough of now (as the site's MZC_Acc::short_of): a count that changes with the stock, not with the invoice.</summary>
    private JsonArray Short(JsonArray lines)
    {
        var products = Load("products"); var short_ = new JsonArray();
        foreach (var g in lines.OfType<JsonObject>().Where(l => Num(l, "product_id") > 0).GroupBy(l => (Id: Num(l, "product_id"), Name: Str(l, "name"))))
        {
            if (!products.TryGetValue(g.Key.Id, out var p)) continue;
            long qty = g.Sum(l => Num(l, "qty")), stock = long.TryParse(Str(p.Doc, "stock"), out long s) ? s : 0;
            if (stock < qty) short_.Add(new JsonObject { ["product_id"] = g.Key.Id, ["name"] = g.Key.Name, ["qty"] = qty, ["stock"] = stock });
        }
        return short_;
    }

    /// <summary>The cases whose last call asked for another on a day that has come.</summary>
    private JsonObject? Followups()
    {
        if (!Holds("jobs") && !Holds("builds")) return null;
        string today = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var due = new List<(string Next, long Id, JsonObject Item)>();
        foreach (var (kind, held) in new[] { ("job", "jobs"), ("build", "builds") })
        {
            if (!Holds(held)) continue;
            foreach (var r in Load(held).Values)
            {
                if (r.Doc["calls"] is not JsonArray calls || calls.OfType<JsonObject>().OrderBy(c => Str(c, "at"), StringComparer.Ordinal).ThenBy(c => Num(c, "id")).LastOrDefault() is not { } last) continue;
                string next = Str(last, "next_at"); if (next.Length == 0 || string.CompareOrdinal(next, today) > 0) continue;
                var c = Named(r.Doc);
                var item = new JsonObject { ["kind"] = kind, ["record"] = Num(r.Doc, "id"), ["number"] = Str(r.Doc, kind == "job" ? "service_no" : "number"), ["customer_name"] = Str(c, "customer_name"), ["customer_mobile"] = Str(c, "customer_mobile") };
                foreach (var (k, v) in last) item[k] = v?.DeepClone();
                due.Add((next, Num(last, "id"), item));
            }
        }
        return new JsonObject { ["items"] = new JsonArray(due.OrderBy(x => x.Next, StringComparer.Ordinal).ThenBy(x => x.Id).Select(x => (JsonNode)x.Item).ToArray()) };
    }

    private static string Hay(string kind, Row r)
    {
        if (r.Hay is not null) return r.Hay;
        var sb = new StringBuilder();
        foreach (var f in Searched.GetValueOrDefault(kind, []))
        {
            int dot = f.IndexOf('.');
            if (dot < 0) Flat(r.Doc[f], sb); else if (r.Doc[f[..dot]] is JsonArray lines) foreach (var line in lines) Flat(line?[f[(dot + 1)..]], sb);
        }
        return r.Hay = LocalStore.Norm(sb.ToString());
    }

    /// <summary>The records that hold every typed word (in their searched fields, or in their customer's or supplier's name and number as they are now).</summary>
    private IEnumerable<Row> Search(string kind, IEnumerable<Row> rows, string? q)
    {
        var words = LocalStore.Norm(q ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return rows;
        var customers = Load("customers"); var vendors = Load("vendors"); var fields = Searched.GetValueOrDefault(kind, []);
        bool byCustomer = fields.Contains("customer_name"), byVendor = fields.Contains("vendor_name");
        return rows.Where(r => words.All(w => Hay(kind, r).Contains(w)
            || (byCustomer && Num(r.Doc, "customer_id") is > 0 and var c && customers.TryGetValue(c, out var cu) && Hay("customers", cu).Contains(w))
            || (byVendor && Num(r.Doc, "vendor_id") is > 0 and var v && vendors.TryGetValue(v, out var ve) && Hay("vendors", ve).Contains(w))));
    }

    private static JsonObject Page(IReadOnlyList<Row> rows, System.Collections.Specialized.NameValueCollection q, Func<Row, JsonObject> out_)
    {
        int per = int.TryParse(q["per"], out int p) && p > 0 ? Math.Min(p, 50) : 30, page = Math.Max(1, int.TryParse(q["page"], out int g) ? g : 1);
        return new JsonObject { ["total"] = rows.Count, ["page"] = page, ["per"] = per, ["items"] = new JsonArray(rows.Skip((page - 1) * per).Take(per).Select(r => (JsonNode)out_(r)).ToArray()) };
    }

    private static readonly string[] ShortageOrder = ["open", "looking", "bought", "arrived", "cancelled"];

    /// <summary>The answer to a read, or null when it is the site's to give. <paramref name="extras"/>: what a list's answer holds beside its rows (statuses, rights), from the site's last answer;
    /// <paramref name="config"/>: the site's staff/config.</summary>
    public JsonElement? Answer(string path, Func<string, JsonObject?> extras, JsonObject? config)
    {
        int cut = path.IndexOf('?');
        var seg = (cut < 0 ? path : path[..cut]).Split('/');
        var q = System.Web.HttpUtility.ParseQueryString(cut < 0 ? "" : path[(cut + 1)..]);
        if (seg.Length < 2 || seg[0] != "staff") return null;
        lock (_lock)
        {
            JsonObject? o = seg.Length switch { 2 => List(seg[1], q, extras, config), 3 when seg[1] == "shortages" && seg[2] == "count" => ShortCount(extras), 3 when long.TryParse(seg[2], out long id) => One(seg[1], id), _ => null };
            if (o is null) return null;
            using var d = JsonDocument.Parse(o.ToJsonString()); return d.RootElement.Clone();
        }
    }

    private JsonObject? One(string kind, long id)
    {
        if (kind is not ("customers" or "builds" or "jobs" or "invoices" or "purchases" or "ships" or "tasks" or "flows") || !Holds(kind) || !Load(kind).TryGetValue(id, out var r)) return null;
        if (kind != "customers") return Named(r.Doc);
        JsonArray Of(string k) => new(Load(k).Values.Where(x => Num(x.Doc, "customer_id") == id).OrderByDescending(x => Num(x.Doc, "id")).Take(50).Select(x => (JsonNode)Named(x.Doc)).ToArray());
        return new JsonObject { ["customer"] = r.Doc.DeepClone(), ["builds"] = Of("builds"), ["jobs"] = Of("jobs") };
    }

    private JsonObject? ShortCount(Func<string, JsonObject?> extras)
    {
        if (!Holds("shortages") || extras("shortages")?["canAnswer"] is not JsonValue can) return null;
        bool buyer = can.GetValueKind() == JsonValueKind.True;
        return new JsonObject { ["open"] = buyer ? Load("shortages").Values.Count(r => Str(r.Doc, "status") == "open") : 0, ["buyer"] = buyer };
    }

    private JsonObject? List(string what, System.Collections.Specialized.NameValueCollection q, Func<string, JsonObject?> extras, JsonObject? config)
    {
        if (what == "followups") return Followups();
        if (what == "counts") return Counts(config);
        if (what == "find") return Find(q["q"]);
        if (what == "suppliers") return Suppliers();
        if (what is not ("customers" or "products" or "builds" or "jobs" or "invoices" or "purchases" or "payments" or "cheques" or "ships" or "shortages") || !Holds(what)) return null;
        string P(string k) => q[k] ?? "";
        // dates typed the Persian way are the site's to read
        foreach (var k in new[] { "from", "to" }) if (P(k) is { Length: > 0 } d && !(d.Length == 10 && d.StartsWith("20", StringComparison.Ordinal))) return null;
        IEnumerable<Row> rows = Load(what).Values;
        rows = Search(what, rows, q["q"]);
        IEnumerable<Row> Same(IEnumerable<Row> src, string param, string field) => P(param) is { Length: > 0 } want ? src.Where(r => Str(r.Doc, field) == want) : src;
        IEnumerable<Row> Dates(IEnumerable<Row> src, string field)
        {
            if (P("from") is { Length: > 0 } f) src = src.Where(r => string.CompareOrdinal(Str(r.Doc, field), f) >= 0);
            if (P("to") is { Length: > 0 } t) src = src.Where(r => Str(r.Doc, field) is { Length: > 0 } at && string.CompareOrdinal(at, t) <= 0);
            return src;
        }
        static long Id(Row r) => Num(r.Doc, "id");
        List<Row> sorted;
        switch (what)
        {
            case "customers": sorted = Same(rows, "group", "group").OrderByDescending(Id).ToList(); break;
            case "products":
                rows = Same(rows, "category", "category"); if (P("instock") is not ("" or "0")) rows = rows.Where(r => Num(r.Doc, "stock") > 0);
                sorted = rows.OrderBy(r => LocalStore.Norm(Str(r.Doc, "name")), StringComparer.Ordinal).ToList(); break;
            case "builds": case "jobs": sorted = Same(rows, "status", "status").OrderByDescending(Id).ToList(); break;
            case "invoices":
                rows = P("kind") is { Length: > 0 } kind ? rows.Where(r => Str(r.Doc, "kind") == kind) : rows.Where(r => Str(r.Doc, "kind") != "template");
                sorted = Dates(Same(Same(rows, "status", "status"), "customer", "customer_id"), "at").OrderByDescending(r => Str(r.Doc, "at"), StringComparer.Ordinal).ThenByDescending(Id).ToList(); break;
            case "purchases": sorted = Same(rows, "vendor", "vendor_id").OrderByDescending(r => Str(r.Doc, "bought_at"), StringComparer.Ordinal).ThenByDescending(Id).ToList(); break;
            case "payments":
                foreach (var (param, field) in new[] { ("direction", "direction"), ("method", "method"), ("account", "account"), ("category", "category"), ("customer", "customer_id"), ("vendor", "vendor_id"), ("invoice", "invoice_id"), ("via", "via_id") }) rows = Same(rows, param, field);
                if (P("expense") is not ("" or "0")) rows = rows.Where(r => Str(r.Doc, "direction") == "out" && Num(r.Doc, "customer_id") == 0 && Num(r.Doc, "vendor_id") == 0);
                sorted = Dates(rows, "at").OrderByDescending(r => Str(r.Doc, "at"), StringComparer.Ordinal).ThenByDescending(Id).ToList(); break;
            case "cheques":
                if (P("due").Length > 0) return null;
                foreach (var (param, field) in new[] { ("direction", "direction"), ("status", "status"), ("customer", "customer_id"), ("vendor", "vendor_id") }) rows = Same(rows, param, field);
                sorted = Dates(rows, "due_at").OrderByDescending(r => Str(r.Doc, "status") == "pending").ThenBy(r => Str(r.Doc, "due_at"), StringComparer.Ordinal).ThenByDescending(Id).ToList(); break;
            case "ships":
                rows = P("status") switch { "open" => rows.Where(r => Num(r.Doc, "picked_up") == 0), "sent" => rows.Where(r => Str(r.Doc, "received_at") == ""), "back" => rows.Where(r => Str(r.Doc, "received_at") != ""), _ => rows };
                sorted = Same(rows, "vendor", "vendor_id").OrderByDescending(r => Str(r.Doc, "sent_at"), StringComparer.Ordinal).ThenByDescending(Id).ToList(); break;
            default:   // shortages
                if (extras("shortages") is null) return null;
                rows = P("status") switch { "" => rows, "active" => rows.Where(r => Str(r.Doc, "status") is "open" or "looking" or "bought"), var s => rows.Where(r => Str(r.Doc, "status") == s) };
                sorted = rows.OrderBy(r => Array.IndexOf(ShortageOrder, Str(r.Doc, "status"))).ThenByDescending(Id).ToList(); break;
        }
        Func<Row, JsonObject> shape = r => Named(r.Doc);
        if (what == "customers")
        {
            var builds = Load("builds").Values.CountBy(r => Num(r.Doc, "customer_id")).ToDictionary(); var jobs = Load("jobs").Values.CountBy(r => Num(r.Doc, "customer_id")).ToDictionary();
            bool b = Holds("builds"), j = Holds("jobs");
            shape = r => { var c = (JsonObject)r.Doc.DeepClone(); if (b) c["builds"] = builds.GetValueOrDefault(Id(r)); if (j) c["jobs"] = jobs.GetValueOrDefault(Id(r)); return c; };
        }
        var page = Page(sorted, q, shape);
        if (what == "ships") page["vendors"] = new JsonArray(Load("vendors").Values.OrderBy(r => Str(r.Doc, "name"), StringComparer.Ordinal)
            .Select(r => (JsonNode)new JsonObject { ["id"] = Id(r), ["name"] = Str(r.Doc, "name"), ["kind"] = Str(r.Doc, "kind") }).ToArray());
        if (what == "shortages") foreach (var (k, v) in extras("shortages")!) page[k] = v?.DeepClone();
        return page;
    }

    private JsonObject? Counts(JsonObject? config)
    {
        var o = new JsonObject();
        foreach (var (kind, statuses) in new[] { ("builds", "buildStatuses"), ("jobs", "jobStatuses") })
        {
            if (!Holds(kind) || config?[statuses] is not JsonObject all) continue;
            var n = Load(kind).Values.CountBy(r => Str(r.Doc, "status")).ToDictionary(); var per = new JsonObject();
            foreach (var (k, _) in all) per[k] = n.GetValueOrDefault(k);
            o[kind] = per;
        }
        return config is null ? null : o;
    }

    private JsonObject? Find(string? q)
    {
        if ((q ?? "").Trim().Length < 2) return null;
        JsonArray Of(string kind) => new(Holds(kind) ? Search(kind, Load(kind).Values, q).OrderByDescending(r => Num(r.Doc, "id")).Take(8).Select(r => (JsonNode)Named(r.Doc)).ToArray() : []);
        return new JsonObject { ["customers"] = Of("customers"), ["builds"] = Of("builds"), ["jobs"] = Of("jobs") };
    }

    private JsonObject? Suppliers()
    {
        if (!Holds("purchases") || !Holds("vendors")) return null;   // the route is the warehouse's
        var by = Load("purchases").Values.GroupBy(r => Num(r.Doc, "vendor_id")).ToDictionary(g => g.Key, g => (Count: g.Count(), Last: g.OrderByDescending(r => Str(r.Doc, "bought_at"), StringComparer.Ordinal).First().Doc));
        return new JsonObject { ["items"] = new JsonArray(Load("vendors").Values.OrderByDescending(r => Str(r.Doc, "kind") == "supplier").ThenBy(r => Str(r.Doc, "name"), StringComparer.Ordinal).Select(r =>
        {
            var v = (JsonObject)r.Doc.DeepClone(); bool has = by.TryGetValue(Num(v, "id"), out var b);
            v["purchases"] = has ? b.Count : 0; v["last_at"] = has ? Str(b.Last, "bought_at") : ""; v["last_at_j"] = has ? Str(b.Last, "bought_at_j") : "";
            return (JsonNode)v;
        }).ToArray()) };
    }
}
