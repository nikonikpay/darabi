using Xunit; using Mazesta.Core.Drivers;
namespace Mazesta.Core.Tests;

public class DriversTests
{
    private const string Series = """<?xml version="1.0"?><LookupValueSearch><LookupValues><LookupValue ParentID="1"><Name>GeForce RTX 30 Series</Name><Value>120</Value></LookupValue><LookupValue ParentID="1"><Name>GeForce RTX 40 Series (Notebooks)</Name><Value>129</Value></LookupValue><LookupValue ParentID="1"><Name>GeForce RTX 40 Series</Name><Value>127</Value></LookupValue><LookupValue ParentID="3"><Name>NVIDIA RTX Series</Name><Value>122</Value></LookupValue></LookupValues></LookupValueSearch>""";
    private const string Products = """<?xml version="1.0"?><LookupValueSearch><LookupValues><LookupValue ParentID="120"><Name>GeForce RTX 3090 Ti</Name><Value>985</Value></LookupValue><LookupValue ParentID="120"><Name>GeForce RTX 3090</Name><Value>930</Value></LookupValue><LookupValue ParentID="127"><Name>NVIDIA GeForce RTX 4060</Name><Value>1023</Value></LookupValue><LookupValue ParentID="129"><Name>GeForce RTX 4060 Laptop GPU</Name><Value>1007</Value></LookupValue><LookupValue ParentID="122"><Name>NVIDIA RTX A4000</Name><Value>944</Value></LookupValue></LookupValues></LookupValueSearch>""";

    [Fact] public void Windows_driver_version_is_nvidias_last_five_digits()
    {
        Assert.Equal("610.62", NvidiaDrivers.FromWindowsVersion("32.0.16.1062"));
        Assert.Equal("560.94", NvidiaDrivers.FromWindowsVersion("32.0.15.6094"));
        Assert.Null(NvidiaDrivers.FromWindowsVersion("31.0.101.5186x")); Assert.Null(NvidiaDrivers.FromWindowsVersion(null));
        Assert.True(NvidiaDrivers.Compare("617.14", "610.62") > 0); Assert.True(NvidiaDrivers.Compare("99.1", "610.62") < 0);
    }
    [Fact] public void A_card_is_found_only_by_its_exact_name()
    {
        Assert.Equal((120, 930, true), NvidiaDrivers.FindProduct("NVIDIA GeForce RTX 3090", Series, Products) is { } p ? (p.SeriesId, p.ProductId, p.GeForce) : default);
        Assert.Equal(1007, NvidiaDrivers.FindProduct("NVIDIA GeForce RTX 4060 Laptop GPU", Series, Products)?.ProductId);
        Assert.Equal(1023, NvidiaDrivers.FindProduct("NVIDIA GeForce RTX 4060", Series, Products)?.ProductId);
        Assert.False(NvidiaDrivers.FindProduct("NVIDIA RTX A4000", Series, Products)!.GeForce);
        Assert.Null(NvidiaDrivers.FindProduct("NVIDIA GeForce RTX 3080", Series, Products));
    }
    [Fact] public void Releases_are_of_the_line_asked_for_newest_first_without_betas()
    {
        const string json = """
            { "Success": "1", "IDS": [
            { "downloadInfo": { "Version": "616.56", "IsCRD": "1", "IsBeta": "0", "Name": "NVIDIA%20Studio%20Driver", "ReleaseDateTime": "Wed Aug 26, 2026", "DownloadURL": "https://us.download.nvidia.com/a.exe", "DownloadURLFileSize": "990 MB" } },
            { "downloadInfo": { "Version": "616.92", "IsCRD": "1", "IsBeta": "0", "Name": "NVIDIA%20Studio%20Driver", "ReleaseDateTime": "Wed Sep 09, 2026", "DownloadURL": "https://us.download.nvidia.com/b.exe" } },
            { "downloadInfo": { "Version": "617.50", "IsCRD": "1", "IsBeta": "1", "DownloadURL": "https://us.download.nvidia.com/c.exe" } },
            { "downloadInfo": { "Version": "617.14", "IsCRD": "0", "IsBeta": "0", "DownloadURL": "https://us.download.nvidia.com/d.exe" } } ] }
            """;
        var r = NvidiaDrivers.ParseReleases(json, studio: true);
        Assert.Equal(["616.92", "616.56"], r.Select(x => x.Version));
        Assert.Equal((new DateOnly(2026, 9, 9), "NVIDIA Studio Driver"), (r[0].Date!.Value, r[0].Name));
    }
    private const string Asus = """
        {"Result":{"Count":3,"Obj":[
         {"Name":"Chipset","Count":2,"Files":[
          {"Version":"6.05.16.221","Title":"AMD Chipset driver v6.05.16.221","FileSize":"64.26 MB","ReleaseDate":"2024/07/03","IsRelease":"1","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/ASUS/mb/old.zip?model=X"},"sha256":""},
          {"Version":"7.09.23.2230","Title":"AMD Chipset driver v7.09.23.2230 for Windows 10/11 64-bit.","FileSize":"72.74 MB","ReleaseDate":"2025/12/31","IsRelease":"1","DownloadUrl":{"Global":"https://dlcdnets.asus.com/pub/ASUS/mb/new.zip?model=X"},"sha256":"8A27"}]},
         {"Name":"Wireless","Count":2,"Files":[
          {"Version":"23.60.1.2","Title":"Intel WiFi Driver","ReleaseDate":"2025/01/01","IsRelease":"1","DownloadUrl":{"Global":"https://dlcdnets.asus.com/i.zip"}},
          {"Version":"3.0.0.1","Title":"MediaTek WiFi Driver","ReleaseDate":"2025/02/01","IsRelease":"1","DownloadUrl":{"Global":"https://dlcdnets.asus.com/m.zip"}}]},
         {"Name":"Software and Utility","Count":1,"Files":[{"Version":"7.13.0","Title":"WinRAR","IsRelease":"1","DownloadUrl":{"Global":"https://dlcdnets.asus.com/w.exe"}}]}]},
         "Status":"SUCCESS","Message":"SUCCESS"}
        """;

    [Fact] public void Asus_newest_of_each_part_and_maker_utilities_left_out()
    {
        var p = BoardDrivers.ParseAsus(Asus)!;
        Assert.Equal(3, p.Count);
        var chip = p.Single(x => x.Part == BoardPart.Chipset);
        Assert.Equal(("7.09.23.2230", "https://dlcdnets.asus.com/pub/ASUS/mb/new.zip", "8A27", "AMD", new DateOnly(2025, 12, 31)), (chip.Version, chip.Url, chip.Sha256, chip.Vendor, chip.Date!.Value));
        Assert.Equal(["Intel", "MediaTek"], p.Where(x => x.Part == BoardPart.Wireless).Select(x => x.Vendor).Order());
        Assert.Null(BoardDrivers.ParseAsus("""{"Result":null,"Status":"FAIL","Message":"no"}"""));
    }
    [Fact] public void Amd_chipset_version_size_and_date_are_read_from_its_page()
    {
        const string html = """<div><h3>AMD Chipset Drivers</h3><dl><dt><strong>Revision Number</strong></dt><dd>8.08.12.551</dd><dt><strong>File Size</strong></dt><dd>79 MB</dd><dt><strong>Release Date</strong></dt><dd>2026-08-14</dd></dl><a href="https://drivers.amd.com/drivers/AMD_Chipset_Software_8.08.12.551.exe">Download</a></div>""";
        var p = BoardDrivers.ParseAmdChipset(html)!;
        Assert.Equal(("8.08.12.551", "79 MB", new DateOnly(2026, 8, 14), "https://drivers.amd.com/drivers/AMD_Chipset_Software_8.08.12.551.exe"), (p.Version, p.Size, p.Date!.Value, p.Url));
        Assert.Null(BoardDrivers.ParseAmdChipset("<html>nothing</html>"));
        Assert.Contains("/am4/", BoardDrivers.AmdChipsetPage("PRIME B550M-A")); Assert.Contains("/am5/", BoardDrivers.AmdChipsetPage("ROG STRIX X870E-E GAMING WIFI"));
    }
    [Fact] public void Intel_packages_carry_their_driver_version_ids_and_hash()
    {
        const string json = """
            [{"Id":1,"Version":"24.70.0","DisplayReleaseDate":"2026-09-08T00:00:00Z","Name":"Intel® Wireless Wi-Fi Drivers for Windows® 10 and Windows 11*","IsBeta":false,
              "Files":[{"Url":"https://downloadmirror.intel.com/926939/WiFi-24.70.0-Driver64-Win10-Win11.exe","Hash":"f2e5","Size":54400872}],
              "Components":[{"Category":"Wireless","Version":"24.70.0.3","DetectionValues":["VEN_8086&DEV_2725&SUBSYS_*8086"]}]},
             {"Id":2,"Version":"1","Name":"Graphics","IsBeta":false,"Files":[{"Url":"https://downloadmirror.intel.com/g.exe"}],"Components":[{"Category":"Graphics","Version":"1","DetectionValues":["VEN_8086&DEV_1"]}]}]
            """;
        var p = Assert.Single(BoardDrivers.ParseIntel(json));
        Assert.Equal((BoardPart.Wireless, "24.70.0.3", "f2e5", "51.9 MB", "Intel Wireless Wi-Fi Drivers for Windows 10 and Windows 11"), (p.Part, p.Version, p.Sha1, p.Size, p.Title));
        Assert.True(BoardDrivers.Matches(@"PCI\VEN_8086&DEV_2725&SUBSYS_00248086&REV_1A\4&1", "VEN_8086&DEV_2725&SUBSYS_*8086"));
        Assert.False(BoardDrivers.Matches(@"PCI\VEN_8086&DEV_2725&SUBSYS_00241043&REV_1A\4&1", "VEN_8086&DEV_2725&SUBSYS_*8086"));
        Assert.True(BoardDrivers.Matches(@"PCI\VEN_8086&DEV_1502&SUBSYS_00011179&REV_04\3", "VEN_8086&DEV_1502"));
        Assert.False(BoardDrivers.Matches(@"PCI\VEN_8086&DEV_15020&SUBSYS_00011179\3", "VEN_8086&DEV_1502"));
    }
    [Fact] public void Versions_are_compared_only_part_by_part_of_the_same_shape()
    {
        Assert.True(BoardDrivers.Compare("8.08.12.551", "6.01.25.342") > 0); Assert.True(BoardDrivers.Compare("1168.27.50.919", "1168.9.614.2022") > 0);
        Assert.Equal(0, BoardDrivers.Compare("3645", "3645")); Assert.True(BoardDrivers.Compare("3404", "3645") < 0);
        Assert.Null(BoardDrivers.Compare("24.70.0", "24.70.0.3")); Assert.Null(BoardDrivers.Compare("1.2a", "1.2")); Assert.Null(BoardDrivers.Compare("1.2", null));
    }
    [Fact] public void Packages_meet_the_device_or_program_they_are_for()
    {
        var devices = new BoardDevice[]
        {
            new(@"PCI\VEN_10EC&DEV_8168&SUBSYS_86771043\01", "Realtek PCIe GbE Family Controller", "NET", "Realtek", "1168.9.614.2022", new(2022, 6, 14)),
            new(@"USB\VID_0BDA&PID_8812\123", "Realtek 8812AU Wireless LAN 802.11ac USB NIC", "NET", "Realtek Semiconductor Corp.", "1030.52.1216.2025", null),
            new(@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0887\4", "Realtek High Definition Audio", "MEDIA", "Realtek", "6.0.1.7841", null),
            new(@"PCI\VEN_8086&DEV_2725&SUBSYS_00248086&REV_1A\4", "Intel(R) Wi-Fi 6E AX210 160MHz", "NET", "Intel Corporation", "23.60.1.2", null),
        };
        var packages = new BoardPackage[]
        {
            new(BoardPart.Lan, "asus", "Realtek LAN driver", "1168.27.50.919", null, null, "u1", Vendor: "Realtek"),
            new(BoardPart.Audio, "asus", "Realtek Audio Driver", "6.0.9888.1", null, null, "u2", Vendor: "Realtek"),
            new(BoardPart.Wireless, "asus", "MediaTek WiFi Driver", "3.0.0.1", null, null, "u3", Vendor: "MediaTek"),
            new(BoardPart.Chipset, "amd", "AMD Chipset Software", "8.08.12.551", null, null, "u4", Vendor: "AMD"),
            new(BoardPart.Bios, "asus", "", "3645", null, null, "u5", Vendor: "ASUS"),
            new(BoardPart.Wireless, "intel", "Intel Wi-Fi old", "24.10.0.1", null, null, "u6", Vendor: "Intel", HardwareIds: ["VEN_8086&DEV_2725&SUBSYS_*8086"]),
            new(BoardPart.Wireless, "intel", "Intel Wi-Fi new", "24.70.0.3", null, null, "u7", Vendor: "Intel", HardwareIds: ["VEN_8086&DEV_2725&SUBSYS_*8086"]),
            new(BoardPart.Lan, "intel", "Intel Ethernet", "12.19.2.65", null, null, "u8", Vendor: "Intel", HardwareIds: ["VEN_8086&DEV_1502"]),
        };
        var items = BoardDrivers.Match(packages, devices, [("AMD Chipset Software", "6.01.25.342"), ("Google Chrome", "1")], "3404");
        BoardItem Of(string url) => items.Single(i => i.Package.Url == url);
        Assert.Equal(("Realtek PCIe GbE Family Controller", true), (Of("u1").DeviceName, Of("u1").Newer));   // the USB Wi-Fi stick is not the board's LAN
        Assert.True(Of("u2").Newer);
        Assert.True(Of("u3").Missing); Assert.Null(Of("u3").Newer);
        Assert.Equal(("6.01.25.342", true), (Of("u4").Installed, Of("u4").Newer));
        Assert.Equal(("3404", true), (Of("u5").Installed, Of("u5").Newer));
        Assert.Equal("Intel Wi-Fi new", Assert.Single(items, i => i.Package.Source == "intel").Package.Title);   // the newest that fits; the Ethernet one fits nothing
    }
    [Fact] public void Studio_for_creative_programs_game_ready_for_games_and_the_user_decides_for_both()
    {
        Assert.Equal(NvidiaLine.Studio, DriverAdvice.Advise(["Lumion 2024", "Autodesk 3ds Max 2025", "Google Chrome"]).Suggested);
        Assert.Equal(NvidiaLine.GameReady, DriverAdvice.Advise(["Steam", "Google Chrome"]).Suggested);
        var both = DriverAdvice.Advise(["Blender 4.2", "Epic Games Launcher"]);
        Assert.Null(both.Suggested); Assert.Equal(["Blender 4.2"], both.Creative); Assert.Equal(["Epic Games Launcher"], both.Games);
        Assert.Equal(NvidiaLine.GameReady, DriverAdvice.Advise(["Google Chrome"]).Suggested);
        Assert.Empty(DriverAdvice.Advise(["Steamworks Common Redistributables", "Mario Kart Tool"]).Games);
    }
}
