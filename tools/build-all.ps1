# Builds everything at its current version in one go:
#   1. the site plugin: artifacts\site\mazesta-connect.zip and artifacts\mazesta-connect-<version>.zip (the version from the plugin's header;
#      older numbered zips are removed so the newest is the only one), plus site\mazesta-connect.zip in the repository;
#   2. tools\publish.ps1: Mazesta-Admin, Mazesta-Client, Mazesta-Setup (installer), Mazesta-Flash and Mazesta-Desk;
#   3. with -Release, tools\release.ps1 -App: the signed update folder (artifacts\Mazesta-Update\mazesta); -Upload also sends it to the site.
# Running Mazesta programs and OpenRGB are closed first (they lock the folders), through an elevated shell when they run elevated.
#
#   pwsh tools/build-all.ps1
#   pwsh tools/build-all.ps1 -Release -NotesFa artifacts/notes-fa.txt [-Upload]
param(
    [switch]$Release,
    [switch]$Upload,
    [string]$NotesFa,
    [string]$NotesEn
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

$names = "Mazesta-Admin", "Mazesta", "MazestaTray", "MazestaDesk", "OpenRGB"
if (Get-Process -Name $names -ErrorAction SilentlyContinue) {
    Get-Process -Name $names -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    if (Get-Process -Name $names -ErrorAction SilentlyContinue) {
        $list = ($names | ForEach-Object { "'$_'" }) -join ","
        Start-Process pwsh -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList "-NoProfile", "-Command", "Get-Process -Name $list -ErrorAction SilentlyContinue | Stop-Process -Force"
        Start-Sleep -Seconds 1
    }
}

# Entries written by hand with '/' separators: WordPress unpacks a zip with '\' in its names into wrongly named files.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$src = Join-Path $repo "site/mazesta-connect"
$version = (Select-String -Path (Join-Path $src "mazesta-connect.php") -Pattern '^\s*\*\s*Version:\s*(\S+)').Matches[0].Groups[1].Value
New-Item -ItemType Directory -Force artifacts/site | Out-Null
Get-ChildItem artifacts -Filter "mazesta-connect-*.zip" | Remove-Item -Force
$zip = Join-Path $repo "artifacts/site/mazesta-connect.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
$z = [IO.Compression.ZipFile]::Open($zip, "Create")
try {
    foreach ($f in Get-ChildItem $src -Recurse -File) {
        $name = "mazesta-connect/" + $f.FullName.Substring($src.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $f.FullName, $name, "Optimal")
    }
} finally { $z.Dispose() }
Copy-Item $zip "artifacts/mazesta-connect-$version.zip" -Force
Copy-Item $zip "site/mazesta-connect.zip" -Force
Write-Host "Plugin $version -> $zip"

& (Join-Path $PSScriptRoot "publish.ps1")

if ($Release) {
    $relArgs = @{ App = $true }
    if ($NotesFa) { $relArgs.NotesFa = $NotesFa }
    if ($NotesEn) { $relArgs.NotesEn = $NotesEn }
    if ($Upload) { $relArgs.Upload = $true }
    & (Join-Path $PSScriptRoot "release.ps1") @relArgs
}

Write-Host ""
Write-Host "Done:"
Write-Host "  plugin $version      $(Resolve-Path "artifacts/mazesta-connect-$version.zip")"
foreach ($p in "artifacts/Mazesta-Admin/Mazesta-Admin.exe", "artifacts/Mazesta-Client/Mazesta.exe", "artifacts/Mazesta-Setup/MazestaTestSetup.exe", "artifacts/Mazesta-Desk/MazestaDesk.exe", "artifacts/Mazesta-Update/mazesta") {
    if (Test-Path $p) { Write-Host "  $(Resolve-Path $p)" }
}
