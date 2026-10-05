# Builds the update folder for the shop's site (see docs/UPDATES.md), ready to upload as it is to /mazesta/ on www.dfmrendering.com:
#   update.json + update.json.sig   the signed list of what is offered
#   MazestaWeb.zip                  the app (only with -App); the name has no version, so the download page's link never changes
#   benchdb/*.json                  the benchmark comparison lists, rebuilt from every run gathered so far
#
#   pwsh tools/release.ps1                                  # data only: gather the runs, rebuild the lists, re-sign (daily)
#   pwsh tools/release.ps1 -App -NotesFa notes-fa.txt       # also publish the app and offer it as the new release
#   pwsh tools/release.ps1 -App -NotesFa notes-fa.txt -Upload   # ... and send it all to the site through the Mazesta Connect plugin
#   pwsh tools/release.ps1 -Runs E:\Mazesta-Web\Data, F:\Mazesta-Web\Data   # runs from other copies (USB sticks) too
#
# The runs are kept in the archive (outside the repository, next to it) so a run brought in twice counts once, and nothing is lost when a
# copy's Data folder is cleaned. The private key never leaves this machine; the site only ever gets signed files.
param(
    [switch]$App,
    [switch]$Upload,   # send the folder to the site through the Mazesta Connect plugin when it is built
    [string]$SiteKey = "artifacts/Mazesta-Update/Keys/site-release-key.txt",
    [string[]]$Runs = @("artifacts/Mazesta-Web/Data"),
    [string]$NotesFa,
    [string]$NotesEn,
    [string]$Key = "artifacts/Mazesta-Update/Keys/mazesta-update-private.pem",
    [string]$Out = "artifacts/Mazesta-Update/mazesta",
    [string]$Archive = (Join-Path (Split-Path $PSScriptRoot -Parent) "../Mazesta-BenchArchive")
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
if (-not (Test-Path $Key)) { throw "The signing key $Key is missing. Restore it from its backup; a new key would make every installed copy refuse updates." }

$toolArgs = @("site", "--key", $Key, "--out", $Out, "--archive", $Archive)
foreach ($r in $Runs) { if (Test-Path $r) { $toolArgs += @("--runs", $r) } else { Write-Warning "No runs at $r" } }
if ($App) {
    # Checked before the build, which takes minutes: a notes file named but not written.
    foreach ($n in @($NotesFa, $NotesEn)) { if ($n -and -not (Test-Path $n)) { throw "The notes file $n is missing. Write what changed in it (one line per item, UTF-8), or leave the option out." } }
    # The copy users get: self-contained, one exe for the app and one for the tray, wwwroot and Redist beside them.
    & (Join-Path $PSScriptRoot "publish-release.ps1")
    $toolArgs += @("--app", "artifacts/Mazesta-Release")
    if ($NotesFa) { $toolArgs += @("--notes-fa", $NotesFa) }
    if ($NotesEn) { $toolArgs += @("--notes-en", $NotesEn) }
}
dotnet run --project tools/Mazesta.Release -c Release -- @toolArgs
if ($LASTEXITCODE -ne 0) { throw "mazesta-release failed ($LASTEXITCODE)." }
if ($Upload) {
    if (-not (Test-Path $SiteKey)) { throw "The release key file $SiteKey is missing. Copy the release key from the site (Mazesta Connect > settings) into it." }
    dotnet run --project tools/Mazesta.Release -c Release --no-build -- upload --dir $Out --key-file $SiteKey
    if ($LASTEXITCODE -ne 0) { throw "The upload failed ($LASTEXITCODE). The folder is ready in $Out; run again with -Upload, or upload it by hand." }
} else {
    Write-Host "Ready in $(Resolve-Path $Out). Send it with -Upload, or upload its contents by hand to /mazesta/ on the site (update.json and update.json.sig last)."
}
