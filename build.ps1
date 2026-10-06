param([switch]$Test, [switch]$Hardware, [switch]$Publish, [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
dotnet build Mazesta.sln -c $Configuration
if ($Test) { dotnet test Mazesta.sln -c $Configuration --no-build --filter "Category!=Hardware" }
if ($Hardware) { dotnet test tests/Mazesta.Hardware.Tests -c $Configuration --no-build --filter "Category=Hardware" }
if ($Publish) {
  dotnet publish src/Mazesta.App -c $Configuration -r win-x64 --self-contained false -p:PublishReadyToRun=true -o artifacts/Mazesta-Admin
  Copy-Item docs/GUIDE-FA.md, docs/THIRD-PARTY-NOTICES.md artifacts/Mazesta-Admin/
  # Symbols are a developer artifact; the shop's machines get the app without them.
  Remove-Item artifacts/Mazesta-Admin/*.pdb -Force -ErrorAction SilentlyContinue
}
