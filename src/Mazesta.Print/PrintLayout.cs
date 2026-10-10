using System.Net;
namespace Mazesta.Print;

/// <summary>How the summary sheet sits on the paper. The sheet itself is one A5 page; a printer fed A4 paper the long way (landscape) has to be told
/// the page is A4 landscape, or a portrait A5 page is cut off against it. So the page that is printed always declares the paper it is meant for.
/// Some drivers print portrait whatever the dialog says: the Turned layouts keep the page A4 portrait and turn the sheet(s) a quarter turn on it, so the print runs across the paper's width (Back = the other way round).</summary>
internal enum SheetLayout { A5, A4Landscape, A4LandscapeTwice, A4Turned, A4TurnedTwice, A4TurnedBack, A4TurnedBackTwice }

internal static class PrintLayout
{
    public const double SheetWidthMm = 148, SheetHeightMm = 210;

    /// <summary>The paper in inches as a portrait page (width, height) and whether it is printed turned: the numbers WebView2's PDF settings take.</summary>
    public static (double Width, double Height, bool Landscape) Paper(SheetLayout layout) => layout == SheetLayout.A5 ? (5.8268, 8.2677, false) : layout >= SheetLayout.A4Turned ? (8.2677, 11.6929, false) : (8.2677, 11.6929, true);

    /// <summary>The page to show and print: the summary as it is for A5; for A4 landscape the same sheet(s) at their own size, centred on the wide page
    /// (twice: side by side, one to keep and one to hand over, with a line to cut along). Each sheet is a frame of its own, so its layout is untouched.</summary>
    public static string Apply(string summaryHtml, SheetLayout layout)
    {
        if (layout == SheetLayout.A5) return summaryHtml;
        if (layout >= SheetLayout.A4Turned) return Turned(summaryHtml, layout is SheetLayout.A4TurnedBack or SheetLayout.A4TurnedBackTwice, layout is SheetLayout.A4TurnedTwice or SheetLayout.A4TurnedBackTwice);
        string sheet = "<iframe class=\"sheet\" scrolling=\"no\" srcdoc=\"" + WebUtility.HtmlEncode(Plain(summaryHtml)) + "\"></iframe>";
        string sheets = layout == SheetLayout.A4LandscapeTwice ? sheet + sheet : sheet;
        return "<!doctype html><html><head><meta charset=\"utf-8\"><style>"
            + "@page{size:A4 landscape;margin:0}html,body{margin:0;background:#fff}"
            + $".sheets{{display:flex;justify-content:center;width:297mm;height:210mm;overflow:hidden}}"
            + $".sheet{{flex:0 0 {SheetWidthMm}mm;width:{SheetWidthMm}mm;height:{SheetHeightMm}mm;border:0;background:#fff}}"
            + $".sheet+.sheet{{border-inline-start:1px dashed #b8b8b8}}"
            + "</style></head><body><div class=\"sheets\">" + sheets + "</div></body></html>";
    }

    /// <summary>A4 portrait with each sheet turned a quarter: a 148x210 mm sheet becomes 210x148 mm, so two fit one under the other (296 of 297 mm).</summary>
    private static string Turned(string summaryHtml, bool back, bool twice)
    {
        string frame = "<iframe class=\"sheet\" scrolling=\"no\" srcdoc=\"" + EncodedPlain(summaryHtml) + "\"></iframe>";
        string slot = "<div class=\"slot\">" + frame + "</div>";
        return "<!doctype html><html><head><meta charset=\"utf-8\"><style>"
            + "@page{size:A4 portrait;margin:0}html,body{margin:0;background:#fff}"
            + ".page{width:210mm;height:297mm;overflow:hidden}"
            + $".slot{{position:relative;width:210mm;height:{SheetWidthMm}mm;overflow:hidden}}.slot+.slot{{border-top:1px dashed #b8b8b8}}"
            + $".sheet{{position:absolute;left:0;top:0;width:{SheetWidthMm}mm;height:{SheetHeightMm}mm;border:0;background:#fff;transform-origin:0 0;"
            + (back ? $"transform:translate(0,{SheetWidthMm}mm) rotate(-90deg)" : $"transform:translate({SheetHeightMm}mm,0) rotate(90deg)") + "}"
            + "</style></head><body><div class=\"page\">" + (twice ? slot + slot : slot) + "</div></body></html>";
    }

    private static string EncodedPlain(string html) => WebUtility.HtmlEncode(Plain(html));

    /// <summary>The sheet with its on-screen grey surround removed, so the paper shows white round it.</summary>
    internal static string Plain(string html)
    {
        const string style = "<style>body{background:#fff!important}</style>";
        int head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return head < 0 ? style + html : html.Insert(head, style);
    }
}
