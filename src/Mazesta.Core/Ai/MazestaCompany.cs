namespace Mazesta.Core.Ai;

/// <summary>A ready-made system the company sells: its name and page on the site. The site shows no numeric price for any of them.</summary>
public sealed record ReadySystem(string Name, string Url);

/// <summary>
/// What the assistant may say about the company behind the app. "Mazesta" (مازستا) is the brand of DFM Rendering; its site is dfmrendering.com (there is no
/// mazesta.com, which a small model otherwise makes up). Contact details are the ones the app's Contact page shows (from dfmrendering.com/contactus,
/// 2026-09-29); the activities are from the home page (read 2026-10-03). The shop lists ready systems as "تماس بگیرید": the final price and parts
/// are confirmed by a daily inquiry and pre-invoice, so no price is ever given here.
/// </summary>
public static class MazestaCompany
{
    public const string Site = "https://www.dfmrendering.com/", SystemsUrl = "https://www.dfmrendering.com/product-category/systems/", ContactUrl = "https://www.dfmrendering.com/contactus/";

    public static readonly object Info = new
    {
        brand = "Mazesta (مازستا), the brand of DFM Rendering",
        website = "dfmrendering.com (not mazesta.com: that is not the company's site)",
        about = "A Tehran company with over 16 years in professional computers; it presents itself as the only specialist with an unconditional warranty.",
        sells = new[] { "architectural and design rendering systems", "RenderBox and render farms", "processing workstations", "video editing and effects systems", "professional gaming systems", "AI systems", "hand-built custom cases" },
        services = new[] { "free advice before buying", "specialist service and repair", "installing software and the operating system", "software support" },
        phones = new { sales = "09197588700", support = "09197588701", office = "021-41139" },
        email = "info@dfmrendering.com",
        hours = "Saturday to Wednesday 9:00 to 18:00; closed Thursday, Friday and public holidays",
        address = "Tehran, Enghelab St., South Bahar St., Somayyeh St. (westward), Khansari dead end, No. 6 (Faraz building), 4th floor, unit 15; postcode 1571837738",
        chat = new { sales_whatsapp = "https://wa.me/989197588700", support_whatsapp = "https://wa.me/989197588701", telegram = "https://t.me/DFMRendering", instagram = "https://www.instagram.com/dfm.rendering/" },
        pages = new { site = Site, ready_systems = SystemsUrl, contact = ContactUrl },
        note = "Say these exactly. The app's Sales and support page shows them with buttons; open_page can open it.",
    };

    /// <summary>The ready systems seen on the shop's category page on 2026-10-03 (12 of 31); the page has the rest and the parts of each.</summary>
    public static readonly IReadOnlyList<ReadySystem> Systems =
    [
        new("AM9 architectural rendering and simulation (Ryzen 9 9900X)", "https://www.dfmrendering.com/shop/systems/am9-architectural-rendering-ryzen-9900x-rtx5060ti/"),
        new("Intel U26 video editing and effects", "https://www.dfmrendering.com/shop/systems/سیستم-تدوین-و-افکت-ویدیویی-intel-u26/"),
        new("DaVinci AM9 video editing and effects", "https://www.dfmrendering.com/shop/systems/سیستم-تدوین-و-افکت-ویدیویی-داوینچی-am9/"),
        new("A95 architectural rendering and animation", "https://www.dfmrendering.com/shop/systems/سیستم-رندر-و-انیمیشن-معماری-a95/"),
        new("U85 architectural rendering", "https://www.dfmrendering.com/shop/systems/buying-architecture-renderingu85/"),
        new("AMD9W modeling and architectural rendering", "https://www.dfmrendering.com/shop/systems/buying-amd-am9w-rendering-system/"),
        new("A3C custom gaming (Ryzen 7 9800X3D)", "https://www.dfmrendering.com/shop/systems/custom-gaming-system-amd-9800x3d/"),
        new("UT9 Corona rendering (Core Ultra 9 285K)", "https://www.dfmrendering.com/shop/systems/buying-corona-rendering-system-intel-ultra-9-285k/"),
        new("R37 white gaming (Ryzen)", "https://www.dfmrendering.com/shop/systems/mazesta-r37-gaming-pc-ryzen/"),
        new("SQ1 trading-strategy computation", "https://www.dfmrendering.com/shop/systems/strategyquant-algorithmic-system/"),
        new("VW16 projection and 16-screen video wall", "https://www.dfmrendering.com/shop/systems/professional-video-wall-monitoring-projection-mapping/"),
        new("TR4 four-monitor trading", "https://www.dfmrendering.com/shop/systems/multi-monitor-trading-station-tr4-4m/"),
    ];

    public static object SystemsAnswer(string? use) => new
    {
        systems = Systems.Where(s => string.IsNullOrWhiteSpace(use) || s.Name.Contains(use.Trim(), StringComparison.OrdinalIgnoreCase)).Select(s => new { s.Name, s.Url }),
        partial = "12 of the 31 ready systems; the rest, their parts and the newest are on " + SystemsUrl,
        price = "The site publishes no price: each shows «تماس بگیرید» (final price and parts by daily inquiry and pre-invoice). Never state or guess a price or a budget fit; send the user to sales (09197588700) or the page.",
    };
}
