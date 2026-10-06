# Publishes the runnable app (the WebView2 edition: Mazesta-Admin.exe for the company, Mazesta.exe for users) in its two editions, without ever losing a Data folder (settings, reports,
# history, logs):
#   artifacts\Mazesta-Web      Mazesta's own edition (the service number, the link to the site, the reference marks)
#   artifacts\Mazesta-Client   the users' edition (none of those)
#   artifacts\Mazesta-Setup    MazestaTestSetup.exe: the users' installer (carries the users' edition; Start menu, desktop, Installed apps)
#   artifacts\Mazesta-Print    MazestaPrint.exe: the secretary's program (lists the site's reports by service number, prints their summary)
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
    [string]$SetupOutput = "artifacts/Mazesta-Setup",
    [string]$PrintOutput = "artifacts/Mazesta-Print",
    [string]$BackupRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) "../Mazesta-Data-Backups"),
    [int]$Keep = 20
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$project = "src/Mazesta.Web"
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
    & (Join-Path $PSScriptRoot "publish-single.ps1") -Output $target -Edition $Name   # three things: the app exe, MazestaTray.exe, wwwroot (+ Redist)
    if ($stamp -and -not (Test-Path $data)) { throw "Data is missing after publishing - restore it from $stamp." }
}

# The users' edition first, Mazesta's own last: what is left in bin/obj afterwards is the edition a plain build makes.
$editions = @()
if ($Only -ne "Mazesta") { $editions += , @($ClientOutput, "Client") }
if ($Only -ne "Client") { $editions += , @($Output, "Mazesta") }

# Checked for every folder before anything is touched. An elevated app's path cannot be read from a normal shell (Path is empty): then it
# may be ours, so stop as well.
foreach ($e in $editions) {
    $target = [IO.Path]::GetFullPath((Join-Path $repo $e[0]))
    $running = Get-Process MazestaTest, Mazesta, Mazesta-Admin, MazestaTray -ErrorAction SilentlyContinue | Where-Object { -not $_.Path -or $_.Path.StartsWith($target, [StringComparison]::OrdinalIgnoreCase) }
    if ($running) { throw "Close Mazesta / Mazesta Monitor first (running: $($running.Name -join ', ')). Nothing was changed." }
}
foreach ($e in $editions) { Publish-Edition $e[0] $e[1] }

# The two small programs that are not the app. Each is one self-contained exe; the print program keeps its Data (its key) like the app does.
$flags = "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
         "-p:EnableCompressionInSingleFile=true", "-p:DebugType=none", "-p:DebugSymbols=false", "-p:GenerateDocumentationFile=false"
function Clear-Output([string]$Folder) {
    $t = [IO.Path]::GetFullPath((Join-Path $repo $Folder))
    if (Test-Path $t) { Get-ChildItem $t -Force | Where-Object Name -ne "Data" | Remove-Item -Recurse -Force -Confirm:$false }
    return $t
}

$print = [IO.Path]::GetFullPath((Join-Path $repo $PrintOutput))
$running = Get-Process MazestaPrint -ErrorAction SilentlyContinue | Where-Object { -not $_.Path -or $_.Path.StartsWith($print, [StringComparison]::OrdinalIgnoreCase) }
if ($running) { Write-Warning "MazestaPrint is running from $print; it was not republished." }   # checked before anything is deleted: a running exe is locked
else {
    $print = Clear-Output $PrintOutput
    dotnet publish src/Mazesta.Print @flags -o $print
    if ($LASTEXITCODE -ne 0) { throw "publishing the print program failed ($LASTEXITCODE)." }
    Get-ChildItem $print -File | Where-Object { $_.Extension -in ".xml", ".json", ".pdb" } | Remove-Item -Force
    Write-Host "Ready (print): $(Join-Path $print 'MazestaPrint.exe')"
}

# The installer carries the users' edition just built (everything in its folder but Data) as a zip.
if ($Only -ne "Mazesta") {
    $client = [IO.Path]::GetFullPath((Join-Path $repo $ClientOutput))
    $zip = Join-Path ([IO.Path]::GetTempPath()) "mazesta-setup-payload.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $z = [IO.Compression.ZipFile]::Open($zip, "Create")
    try {
        $sep = [string][IO.Path]::DirectorySeparatorChar
        Get-ChildItem $client -Recurse -File | Where-Object { -not $_.FullName.Substring($client.Length + 1).StartsWith("Data$sep", [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $_.FullName, $_.FullName.Substring($client.Length + 1).Replace($sep, '/'), [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $z.Dispose() }
    $setup = Clear-Output $SetupOutput
    dotnet publish src/Mazesta.Setup @flags "-p:PayloadZip=$zip" -o $setup
    if ($LASTEXITCODE -ne 0) { throw "publishing the installer failed ($LASTEXITCODE)." }
    Get-ChildItem $setup -File | Where-Object { $_.Extension -in ".xml", ".json", ".pdb" } | Remove-Item -Force
    Remove-Item $zip -Force
    Write-Host "Ready (installer): $(Join-Path $setup 'MazestaTestSetup.exe')"
}
