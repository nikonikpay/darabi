# Builds the copy for users: self-contained (no .NET to install, Windows 10 and 11, x64) and as few files as possible. It goes to its own
# folder and never touches artifacts\Mazesta-Web or its Data. The app and the tray are one exe each; what stays beside them is what must be
# real files: Redist (the PawnIO setup is run from disk), wwwroot (the page), and the WebView2 loader if the SDK leaves it out.
#   pwsh tools/publish-release.ps1                       # -> artifacts/Mazesta-Release
param([string]$Output = "artifacts/Mazesta-Release")
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$target = [IO.Path]::GetFullPath((Join-Path $repo $Output))
$running = Get-Process MazestaWeb, MazestaTray -ErrorAction SilentlyContinue | Where-Object { -not $_.Path -or $_.Path.StartsWith($target, [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw "Close Mazesta first (running: $($running.Name -join ', ')). Nothing was changed." }
if (Test-Path $target) { Remove-Item $target -Recurse -Force -Confirm:$false }
$flags = "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
         "-p:EnableCompressionInSingleFile=true", "-p:DebugType=none", "-p:DebugSymbols=false", "-p:GenerateDocumentationFile=false"
dotnet publish src/Mazesta.Web @flags -o $target
if ($LASTEXITCODE -ne 0) { throw "publishing the app failed ($LASTEXITCODE)." }
$tray = Join-Path ([IO.Path]::GetTempPath()) "mazesta-tray-publish"
if (Test-Path $tray) { Remove-Item $tray -Recurse -Force }
dotnet publish src/Mazesta.Tray @flags -o $tray
if ($LASTEXITCODE -ne 0) { throw "publishing the tray failed ($LASTEXITCODE)." }
Copy-Item (Join-Path $tray "MazestaTray.exe") $target
$pawn = Get-ChildItem src/Mazesta.Hardware/obj/redist -Recurse -Filter PawnIO_setup.exe | Select-Object -First 1
New-Item -ItemType Directory -Force (Join-Path $target "Redist") | Out-Null
Copy-Item $pawn.FullName (Join-Path $target "Redist")
Get-ChildItem $target -File | Where-Object { $_.Extension -in ".xml", ".json", ".pdb" } | Remove-Item -Force
Remove-Item $tray -Recurse -Force
Write-Host "Ready: $(Join-Path $target 'MazestaWeb.exe')"
