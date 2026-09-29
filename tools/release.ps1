# Builds the update folder for the shop's site (see docs/UPDATES.md), ready to upload as it is to /mazesta/ on www.dfmrendering.com:
#   update.json + update.json.sig   the signed list of what is offered
#   MazestaWeb-<version>.zip        the app (only with -App)
#   benchdb/*.json                  the benchmark comparison lists, rebuilt from every run gathered so far
#
#   pwsh tools/release.ps1                                  # data only: gather the runs, rebuild the lists, re-sign (daily)
#   pwsh tools/release.ps1 -App -NotesFa notes-fa.txt       # also publish the app and offer it as the new release
#   pwsh tools/release.ps1 -Runs E:\Mazesta-Web\Data, F:\Mazesta-Web\Data   # runs from other copies (USB sticks) too
#
# The runs are kept in the archive (outside the repository, next to it) so a run brought in twice counts once, and nothing is lost when a
# copy's Data folder is cleaned. The private key never leaves this machine; the site only ever gets signed files.
param(
    [switch]$App,
    [string[]]$Runs = @("artifacts/Mazesta-Web/Data"),
    [string]$NotesFa,
    [string]$NotesEn,
    [string]$Key = "G:/Mazesta-Keys/mazesta-update-private.pem",
    [string]$Out = "artifacts/site/mazesta",
    [string]$Archive = (Join-Path (Split-Path $PSScriptRoot -Parent) "../Mazesta-BenchArchive")
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
if (-not (Test-Path $Key)) { throw "The signing key $Key is missing. Restore it from its backup; a new key would make every installed copy refuse updates." }

$toolArgs = @("site", "--key", $Key, "--out", $Out, "--archive", $Archive)
foreach ($r in $Runs) { if (Test-Path $r) { $toolArgs += @("--runs", $r) } else { Write-Warning "No runs at $r" } }
if ($App) {
    & (Join-Path $PSScriptRoot "publish.ps1")
    $toolArgs += @("--app", "artifacts/Mazesta-Web")
    if ($NotesFa) { $toolArgs += @("--notes-fa", $NotesFa) }
    if ($NotesEn) { $toolArgs += @("--notes-en", $NotesEn) }
}
dotnet run --project tools/Mazesta.Release -c Release -- @toolArgs
if ($LASTEXITCODE -ne 0) { throw "mazesta-release failed ($LASTEXITCODE)." }
Write-Host "Upload the contents of $(Resolve-Path $Out) to /mazesta/ on the site (update.json and update.json.sig last)."
