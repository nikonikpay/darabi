# Publishes the runnable app (the WebView2 edition, MazestaWeb.exe) in its two editions, without ever losing a Data folder (settings, reports,
# history, logs):
#   artifacts\Mazesta-Web      Mazesta's own edition (the service number, the link to the site, the reference marks)
#   artifacts\Mazesta-Client   the users' edition (none of those)
# For each folder it
#   1. refuses while the app or MazestaTray is running from it (its files are locked, and deleting around a running app is how its data was
#      lost on 2026-09-26);
#   2. copies Data to a timestamped backup OUTSIDE the repository (next to it, in Mazesta-Data-Backups), keeping the newest $Keep copies
#      (all but Data\ai, the AI models the app downloads again on request);
#   3. deletes everything in the folder except Data, publishes, and checks Data is still there.
# The source code is backed up by git; this protects what the app itself writes, which git never sees (artifacts/ is ignored).
# The WPF edition (MazestaTest.exe, artifacts\Mazesta-Test) was retired on 2026-09-28 (git tag wpf-edition-final); this script no longer
# builds it and never touches that folder, so the Data an older copy left there stays where it is.
#
#   pwsh tools/publish.ps1                      # both editions
#   pwsh tools/publish.ps1 -Only Mazesta        # one of them (Mazesta or Client)
#   pwsh tools/publish.ps1 -Output artifacts/x  # Mazesta's edition somewhere else
param(
    [ValidateSet("web")] [string]$Edition = "web",   # kept so older command lines (-Edition web) still work
    [ValidateSet("Both", "Mazesta", "Client")] [string]$Only = "Both",
    [string]$Output = "artifacts/Mazesta-Web",
    [string]$ClientOutput = "artifacts/Mazesta-Client",
    [string]$BackupRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) "../Mazesta-Data-Backups"),
    [int]$Keep = 20
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$project = "src/Mazesta.Web"
$exeName = "MazestaWeb.exe"
Set-Location $repo
$backups = [IO.Path]::GetFullPath($BackupRoot)

function Publish-Edition([string]$Folder, [string]$Name) {
    $target = [IO.Path]::GetFullPath((Join-Path $repo $Folder))
    $data = Join-Path $target "Data"
    $stamp = $null
    if (Test-Path $data) {
        $stamp = Join-Path $backups ((Get-Date -Format "yyyyMMdd-HHmmss") + $(if ($Name -eq "Client") { "-client" } else { "" }))
        New-Item -ItemType Directory -Force $stamp | Out-Null
        # Data\ai holds the AI benchmark's downloaded models (gigabytes each, fetched again at will): it stays in place but is not copied.
        New-Item -ItemType Directory -Force (Join-Path $stamp "Data") | Out-Null
        Get-ChildItem $data -Force | Where-Object Name -ne "ai" | Copy-Item -Recurse -Force -Destination (Join-Path $stamp "Data")
        Write-Host "Data backed up to $stamp"
        Get-ChildItem $backups -Directory | Sort-Object Name -Descending | Select-Object -Skip $Keep | Remove-Item -Recurse -Force -Confirm:$false
    }
    if (Test-Path $target) { Get-ChildItem $target -Force | Where-Object Name -ne "Data" | Remove-Item -Recurse -Force -Confirm:$false }
    dotnet publish $project -c Release -o $target "-p:MazestaEdition=$Name"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
    if ($stamp -and -not (Test-Path $data)) { throw "Data is missing after publishing - restore it from $stamp." }
    Write-Host "Ready ($Name): $(Join-Path $target $exeName)"
}

# The users' edition first, Mazesta's own last: what is left in bin/obj afterwards is the edition a plain build makes.
$editions = @()
if ($Only -ne "Mazesta") { $editions += , @($ClientOutput, "Client") }
if ($Only -ne "Client") { $editions += , @($Output, "Mazesta") }

# Checked for every folder before anything is touched. An elevated app's path cannot be read from a normal shell (Path is empty): then it
# may be ours, so stop as well.
foreach ($e in $editions) {
    $target = [IO.Path]::GetFullPath((Join-Path $repo $e[0]))
    $running = Get-Process MazestaTest, MazestaWeb, MazestaTray -ErrorAction SilentlyContinue | Where-Object { -not $_.Path -or $_.Path.StartsWith($target, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) { throw "Close Mazesta / Mazesta Monitor first (running: $($running.Name -join ', ')). Nothing was changed." }
}
foreach ($e in $editions) { Publish-Edition $e[0] $e[1] }
