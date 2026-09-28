using System.Text; using System.Text.Encodings.Web; using System.Text.Json; using System.Text.Json.Serialization; using Mazesta.Core.Inventory;
namespace Mazesta.Reporting;

public sealed record SpecRow(string Label, string Value);
public sealed record SpecSection(string Title, IReadOnlyList<SpecRow> Rows);

/// <summary>
/// The machine's specifications, exported by hand from the System page: the same sections and rows the page shows (already in the app's language,
/// "not available" where Windows reported nothing) as one self-contained offline HTML page laid out for A4, and the whole inventory as JSON.
/// </summary>
public static class SpecSheet
{
    private static string E(string? s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>The raw inventory, every field as Windows reported it (null where it reported nothing), with when and by what it was read.</summary>
    public static string WriteJson(HardwareInventory inv, DateTimeOffset createdAt, string shopName, string appVersion)
        => JsonSerializer.Serialize(new { createdAt, shopName, app = $"Mazesta Test {appVersion}", machine = inv }, Json);

    public static string WriteHtml(IReadOnlyList<SpecSection> sections, IReadOnlyList<string> errors, DateTimeOffset createdAt, string shopName, string appVersion,
        string title, string footer, bool rtl, ReportFont? font = null)
    {
        var b = new StringBuilder(16 * 1024);
        b.Append("<!DOCTYPE html><html lang=\"").Append(rtl ? "fa" : "en").Append("\" dir=\"").Append(rtl ? "rtl" : "ltr").Append("\"><head><meta charset=\"utf-8\">")
         .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; font-src data:; img-src data:\">")
         .Append("<title>").Append(E(title)).Append(" — ").Append(E(shopName)).Append("</title><style>").Append(Css(font)).Append("</style></head><body><main>");
        b.Append("<header><div class=\"brand\">").Append(MazestaLogo.Svg("#0B0B0D", "logo")).Append("<div><h1>").Append(E(title)).Append("</h1><div class=\"shop\">").Append(E(shopName))
         .Append("</div></div></div><div class=\"meta\"><bdi class=\"lt\">").Append(E(ReportFormat.Stamp(createdAt))).Append("</bdi></div></header><div class=\"grid\">");
        foreach (var s in sections)
        {
            b.Append("<section><h2>").Append(E(s.Title)).Append("</h2><dl>");
            foreach (var r in s.Rows) b.Append("<dt>").Append(E(r.Label)).Append("</dt><dd><bdi class=\"lt\">").Append(E(r.Value)).Append("</bdi></dd>");
            b.Append("</dl></section>");
        }
        b.Append("</div>");
        if (errors.Count > 0) b.Append("<p class=\"err lt\">").Append(E(string.Join(" · ", errors))).Append("</p>");
        b.Append("<footer>").Append(E(footer)).Append(" <bdi class=\"lt\">").Append(E($"Mazesta Test {appVersion}")).Append("</bdi></footer></main></body></html>");
        return b.ToString();
    }

    private static string Css(ReportFont? f)
    {
        string face = f is null ? "" :
            $"@font-face{{font-family:'{f.Family}';font-weight:400;src:url(data:font/ttf;base64,{Convert.ToBase64String(f.Regular)}) format('truetype')}}" +
            $"@font-face{{font-family:'{f.Family}';font-weight:700;src:url(data:font/ttf;base64,{Convert.ToBase64String(f.Bold)}) format('truetype')}}";
        string family = f is null ? "Tahoma,'Segoe UI',sans-serif" : $"'{f.Family}',Tahoma,'Segoe UI',sans-serif";
        return face + $@"
@page{{size:A4;margin:10mm}}
*{{box-sizing:border-box}}
body{{margin:0;background:#eceae3;color:#16161a;font:11px/1.55 {family}}}
main{{max-width:820px;margin:0 auto;background:#fff;padding:18px 22px}}
.lt{{font-family:'Segoe UI',Tahoma,sans-serif;direction:ltr;unicode-bidi:isolate}}
header{{display:flex;justify-content:space-between;align-items:center;gap:10px;border-bottom:3px solid #FDD400;padding-bottom:8px;margin-bottom:14px}}
.brand{{display:flex;align-items:center;gap:10px}} .logo{{height:24px;width:auto}}
h1{{margin:0;font-size:17px;line-height:1.3}} .shop{{color:#55555f;font-size:10.5px}} .meta{{font-size:10.5px;color:#44444c}}
.grid{{columns:2;column-gap:12px}}
section{{break-inside:avoid;border:1.3px solid #16161a;border-radius:7px;padding:10px 10px 6px;margin:8px 0 12px}}
h2{{margin:-19px 0 4px;font-size:11.5px;display:inline-block;background:#fff;padding:0 5px;border-inline-start:3px solid #FDD400;line-height:1.4}}
dl{{display:grid;grid-template-columns:auto 1fr;gap:0 10px;margin:0}}
dt{{color:#55555f;padding:2px 0;border-bottom:1px dotted #d0cfc8}} dd{{margin:0;font-weight:700;padding:2px 0;border-bottom:1px dotted #d0cfc8;overflow-wrap:anywhere}}
.err{{color:#8a5a00;font-size:9.5px}}
footer{{color:#77777f;font-size:9px;line-height:1.45;border-top:1px solid #d8d7d0;padding-top:5px;margin-top:6px}}
@media print{{body{{background:#fff}} main{{padding:0;max-width:none}}}}
@media (max-width:640px){{.grid{{columns:1}}}}";
    }
}
