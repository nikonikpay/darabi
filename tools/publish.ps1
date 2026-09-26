# Publishes the runnable app to artifacts\Mazesta-Test without ever losing its Data folder (settings, reports, history, logs).
#   1. refuses while MazestaTest or MazestaTray is running from the target (its files are locked, and deleting around a running app is how
#      its data was lost on 2026-09-26);
#   2. copies Data to a timestamped backup OUTSIDE the repository (next to it, in Mazesta-Data-Backups), keeping the newest $Keep copies;
#   3. deletes everything in the target except Data, publishes, and checks Data is still there.
# The source code is backed up by git; this protects what the app itself writes, which git never sees (artifacts/ is ignored).
#
#   pwsh tools/publish.ps1                      # the usual run
#   pwsh tools/publish.ps1 -Output artifacts/x  # somewhere else
param(
    [string]$Output = "artifacts/Mazesta-Test",
    [string]$BackupRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) "../Mazesta-Data-Backups"),
    [int]$Keep = 20
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$target = [IO.Path]::GetFullPath((Join-Path $repo $Output))
$data = Join-Path $target "Data"
$stamp = $null

# An elevated app's path cannot be read from a normal shell (Path is empty): then it may be ours, so stop as well.
$running = Get-Process MazestaTest, MazestaTray -ErrorAction SilentlyContinue | Where-Object { -not $_.Path -or $_.Path.StartsWith($target, [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw "Close Mazesta Test / Mazesta Monitor first (running: $($running.Name -join ', ')). Nothing was changed." }

if (Test-Path $data) {
    $backups = [IO.Path]::GetFullPath($BackupRoot)
    $stamp = Join-Path $backups (Get-Date -Format "yyyyMMdd-HHmmss")
    New-Item -ItemType Directory -Force $stamp | Out-Null
    Copy-Item -Recurse -Force $data $stamp
    Write-Host "Data backed up to $stamp"
    Get-ChildItem $backups -Directory | Sort-Object Name -Descending | Select-Object -Skip $Keep | Remove-Item -Recurse -Force -Confirm:$false
}

if (Test-Path $target) { Get-ChildItem $target -Force | Where-Object Name -ne "Data" | Remove-Item -Recurse -Force -Confirm:$false }
dotnet publish src/Mazesta.Desktop -c Release -o $target
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
if ($stamp -and -not (Test-Path $data)) { throw "Data is missing after publishing - restore it from $stamp." }
Write-Host "Ready: $(Join-Path $target 'MazestaTest.exe')"
