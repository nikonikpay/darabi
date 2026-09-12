param([switch]$Test, [switch]$Hardware, [switch]$Publish, [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
dotnet build Mazesta.sln -c $Configuration
if ($Test) { dotnet test Mazesta.sln -c $Configuration --no-build --filter "Category!=Hardware" }
if ($Hardware) { dotnet test tests/Mazesta.Hardware.Tests -c $Configuration --no-build --filter "Category=Hardware" }
if ($Publish) {
  dotnet publish src/Mazesta.Desktop -c $Configuration -r win-x64 --self-contained false -p:PublishReadyToRun=true -o artifacts/Mazesta-Test
  # OFL.txt is the Vazirmatn licence. The .ttf files are embedded as WPF resources inside
  # MazestaTest.dll, so the licence text has to be copied next to the executable explicitly -
  # the SIL Open Font License requires it to travel with the font.
  Copy-Item docs/GUIDE-FA.md, docs/THIRD-PARTY-NOTICES.md, src/Mazesta.Desktop/Fonts/OFL.txt artifacts/Mazesta-Test/
  # Symbols are a developer artifact; the shop's machines get the app without them.
  Remove-Item artifacts/Mazesta-Test/*.pdb -Force -ErrorAction SilentlyContinue
}
