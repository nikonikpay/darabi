# The compact build every published copy uses: self-contained (no .NET to install, Windows 10 and 11, x64), the app and the tray one exe each, so
# the folder holds three things - the app exe (Mazesta.exe, or Mazesta-Admin.exe for the Mazesta edition), MazestaTray.exe and what must stay real files: wwwroot (the page) and Redist (the PawnIO setup
# is run from disk). Called by publish.ps1 and publish-release.ps1; it only adds files to $Output (they clear the folder first, keeping Data).
#   pwsh tools/publish-single.ps1 -Output <folder> -Edition Mazesta|Client
param([Parameter(Mandatory)][string]$Output, [ValidateSet("Mazesta", "Client")][string]$Edition = "Mazesta")
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$target = [IO.Path]::GetFullPath($Output, $repo)
$flags = "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
         "-p:EnableCompressionInSingleFile=true", "-p:DebugType=none", "-p:DebugSymbols=false", "-p:GenerateDocumentationFile=false"
dotnet publish src/Mazesta.Web @flags "-p:MazestaEdition=$Edition" -o $target
if ($LASTEXITCODE -ne 0) { throw "publishing the app failed ($LASTEXITCODE)." }
$tray = Join-Path ([IO.Path]::GetTempPath()) "mazesta-tray-publish"
if (Test-Path $tray) { Remove-Item $tray -Recurse -Force }
dotnet publish src/Mazesta.Tray @flags -o $tray
if ($LASTEXITCODE -ne 0) { throw "publishing the tray failed ($LASTEXITCODE)." }
Copy-Item (Join-Path $tray "MazestaTray.exe") $target
$pawn = Get-ChildItem src/Mazesta.Hardware/obj/redist -Recurse -Filter PawnIO_setup.exe | Select-Object -First 1
New-Item -ItemType Directory -Force (Join-Path $target "Redist") | Out-Null
Copy-Item $pawn.FullName (Join-Path $target "Redist")
# OpenRGB (GPL-2.0, a separate program the app only talks to over a socket) rides along as its own folder. It is fetched once from the project's
# release page into a cache outside the repository and checked against a pinned hash; its source is at the address in the NOTICE file.
$cache = [IO.Path]::GetFullPath("../Mazesta-Redist/OpenRGB-1.0", $repo)
if (-not (Test-Path (Join-Path $cache "OpenRGB.exe"))) {
    $zip = Join-Path ([IO.Path]::GetTempPath()) "openrgb-1.0.zip"
    Invoke-WebRequest "https://codeberg.org/OpenRGB/OpenRGB/releases/download/release_1.0/OpenRGB_1.0_Windows_64_81bbe18.zip" -OutFile $zip
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne "182A52A3C97C4C4AE52C80286B4260C9666C51C3447DBC416EE8945F66192E90") { Remove-Item $zip -Force; throw "The OpenRGB download does not match the pinned hash." }
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "openrgb-unpack"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    Expand-Archive $zip $tmp
    New-Item -ItemType Directory -Force $cache | Out-Null
    Copy-Item (Join-Path (Get-ChildItem $tmp -Directory | Select-Object -First 1).FullName "*") $cache -Recurse -Force
    Remove-Item $tmp -Recurse -Force; Remove-Item $zip -Force
}
$rgb = Join-Path $target "OpenRGB"
Copy-Item $cache $rgb -Recurse -Force
Set-Content (Join-Path $rgb "NOTICE.txt") "OpenRGB 1.0, GPL-2.0-only, https://openrgb.org - source code: https://codeberg.org/OpenRGB/OpenRGB (tag release_1.0). Run as a separate program by Mazesta Test; not modified."
Get-ChildItem $target -File | Where-Object { $_.Extension -in ".xml", ".json", ".pdb" } | Remove-Item -Force
Remove-Item $tray -Recurse -Force
$exe = if ($Edition -eq "Client") { "Mazesta.exe" } else { "Mazesta-Admin.exe" }
Write-Host "Ready ($Edition): $(Join-Path $target $exe)"
