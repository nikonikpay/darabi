# Publishes the runnable app (the WebView2 edition, MazestaWeb.exe) to artifacts\Mazesta-Web without ever losing its Data folder (settings,
# reports, history, logs).
#   1. refuses while the app or MazestaTray is running from the target (its files are locked, and deleting around a running app is how its
#      data was lost on 2026-09-26);
#   2. copies Data to a timestamped backup OUTSIDE the repository (next to it, in Mazesta-Data-Backups), keeping the newest $Keep copies
#      (all but Data\ai, the AI models the app downloads again on request);
#   3. deletes everything in the target except Data, publishes, and checks Data is still there.
# The source code is backed up by git; this protects what the app itself writes, which git never sees (artifacts/ is ignored).
# The WPF edition (MazestaTest.exe, artifacts\Mazesta-Test) was retired on 2026-09-28 (git tag wpf-edition-final); this script no longer
# builds it and never touches that folder, so the Data an older copy left there stays where it is.
#
#   pwsh tools/publish.ps1                      # the usual run
#   pwsh tools/publish.ps1 -Output artifacts/x  # somewhere else
param(
    [ValidateSet("web")] [string]$Edition = "web",   # kept so older command lines (-Edition web) still work
    [string]$Output = "artifacts/Mazesta-Web",
    [string]$BackupRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) "../Mazesta-Data-Backups"),
    [int]$Keep = 20
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$project = "src/Mazesta.Web"
$exeName = "MazestaWeb.exe"
Set-Location $repo
$target = [IO.Path]::GetFullPath((Join-Path $repo $Output))
$data = Join-Path $target "Data"
$stamp = $null

# An elevated app's path cannot be read from a normal shell (Path is empty): then it may be ours, so stop as well.
$running = Get-Process MazestaTest, MazestaWeb, MazestaTray -ErrorAction SilentlyContinue | Where-Object { -not $_.Path -or $_.Path.StartsWith($target, [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw "Close Mazesta / Mazesta Monitor first (running: $($running.Name -join ', ')). Nothing was changed." }

if (Test-Path $data) {
    $backups = [IO.Path]::GetFullPath($BackupRoot)
    $stamp = Join-Path $backups (Get-Date -Format "yyyyMMdd-HHmmss")
    New-Item -ItemType Directory -Force $stamp | Out-Null
    # Data\ai holds the AI benchmark's downloaded models (gigabytes each, fetched again at will): it stays in place but is not copied.
    New-Item -ItemType Directory -Force (Join-Path $stamp "Data") | Out-Null
    Get-ChildItem $data -Force | Where-Object Name -ne "ai" | Copy-Item -Recurse -Force -Destination (Join-Path $stamp "Data")
    Write-Host "Data backed up to $stamp"
    Get-ChildItem $backups -Directory | Sort-Object Name -Descending | Select-Object -Skip $Keep | Remove-Item -Recurse -Force -Confirm:$false
}

if (Test-Path $target) { Get-ChildItem $target -Force | Where-Object Name -ne "Data" | Remove-Item -Recurse -Force -Confirm:$false }
dotnet publish $project -c Release -o $target
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
if ($stamp -and -not (Test-Path $data)) { throw "Data is missing after publishing - restore it from $stamp." }
Write-Host "Ready: $(Join-Path $target $exeName)"
