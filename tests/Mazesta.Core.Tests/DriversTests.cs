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
