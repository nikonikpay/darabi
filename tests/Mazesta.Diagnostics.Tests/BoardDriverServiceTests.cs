using Xunit; using Mazesta.Core.Drivers; using Mazesta.Diagnostics.Drivers;
namespace Mazesta.Diagnostics.Tests;

public class BoardDriverServiceTests
{
    [Fact] public void A_network_package_installs_its_own_driver_files_and_leaves_out_other_processors()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mz-board-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var d in new[] { "WIN11/64", "x86" }) { Directory.CreateDirectory(Path.Combine(dir, d)); File.WriteAllText(Path.Combine(dir, d, "rt.inf"), ""); File.WriteAllText(Path.Combine(dir, d, "rt.cat"), ""); }
            File.WriteAllText(Path.Combine(dir, "AsusSetup.exe"), "");
            Directory.CreateDirectory(Path.Combine(dir, "nocat")); File.WriteAllText(Path.Combine(dir, "nocat", "x.inf"), "");
            string zip = dir + ".zip"; System.IO.Compression.ZipFile.CreateFromDirectory(dir, zip);
            var lan = BoardDriverService.Installer(zip, BoardPart.Lan);
            Assert.Null(lan.Exe); Assert.Equal(Path.Combine("WIN11", "64", "rt.inf"), Path.GetRelativePath(lan.Folder, Assert.Single(lan.Infs)));
            // Audio and chipset packages carry more than a driver (services, a control panel): the maker's setup runs.
            Assert.Equal("AsusSetup.exe", Path.GetFileName(BoardDriverService.Installer(zip, BoardPart.Audio).Exe));
            File.Delete(zip);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
