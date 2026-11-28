# One command on another PC to carry on the "CPU temperature and lights" work: installs what is missing (winget asks as usual), gets the code on its
# branch, puts the lights' server where the build looks for it, and starts Claude Code there on the handoff note. Runs in Windows PowerShell 5.1.
#   irm https://raw.githubusercontent.com/nikonikpay/darabi/refs/heads/slice-17/cpu-temp-rgb/tools/hw-session.ps1 | iex
# Carry Mazesta-OpenRGB-kit.zip (made on a PC that has built the app: artifacts\hw-kit) to Downloads, a USB drive's root or the folder you run this
# from: the download the build would otherwise make has expired. Overrides: MAZESTA_DIR, MAZESTA_REPO, MAZESTA_KIT (a folder), MAZESTA_NOCLAUDE=1.
$ErrorActionPreference = "Stop"
$branch = "slice-17/cpu-temp-rgb"
$url = if ($env:MAZESTA_REPO) { $env:MAZESTA_REPO } else { "https://github.com/nikonikpay/darabi.git" }
$dir = if ($env:MAZESTA_DIR) { $env:MAZESTA_DIR } else { Join-Path $env:USERPROFILE "MazestaWork\Mazesta" }
$kitName = "Mazesta-OpenRGB-kit.zip"
$kitHash = "D84CBAB61C1073FB93AFE6CF831260679745048CA702A64846E07B10C36747C1"

function Test-Tool([string]$Name) { [bool](Get-Command $Name -ErrorAction SilentlyContinue) }
function Install-IfMissing([string]$Name, [string]$Id, [scriptblock]$Present) {
    if (& $Present) { Write-Host "ok: $Name"; return }
    if (-not (Test-Tool "winget")) { throw "$Name is missing and winget is not available. Install $Name by hand, then run this again." }
    Write-Host "installing: $Name"
    winget install -e --id $Id
    $env:Path = [Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [Environment]::GetEnvironmentVariable("Path", "User")
    if (-not (& $Present)) { throw "$Name is still not found. Open a new PowerShell window and run this again (or install $Name by hand)." }
}
Install-IfMissing "Git" "Git.Git" { Test-Tool "git" }
Install-IfMissing "PowerShell 7" "Microsoft.PowerShell" { Test-Tool "pwsh" }
# global.json: SDK 10.0.400 or a later feature band.
Install-IfMissing ".NET 10 SDK" "Microsoft.DotNet.SDK.10" { (Test-Tool "dotnet") -and [bool](dotnet --list-sdks | Where-Object { $_ -match "^10\.0\.[4-9]\d\d" }) }
if (-not $env:MAZESTA_NOCLAUDE) { Install-IfMissing "Claude Code" "Anthropic.ClaudeCode" { Test-Tool "claude" } }

$parent = Split-Path $dir -Parent
New-Item -ItemType Directory -Force $parent | Out-Null
if (Test-Path (Join-Path $dir ".git")) {
    git -C $dir fetch origin
    if ($LASTEXITCODE -ne 0) { throw "git fetch failed ($LASTEXITCODE)." }
    git -C $dir switch $branch
    if ($LASTEXITCODE -ne 0) { throw "git switch $branch failed ($LASTEXITCODE)." }
    git -C $dir pull --ff-only
    if ($LASTEXITCODE -ne 0) { Write-Warning "git pull did not fast-forward; the folder is left as it is." }
} else {
    git clone --branch $branch $url $dir
    if ($LASTEXITCODE -ne 0) { throw "git clone failed ($LASTEXITCODE)." }
}

$redist = Join-Path $parent "Mazesta-Redist"
if (Test-Path (Join-Path $redist "OpenRGB-f899bc88\OpenRGB.exe")) { Write-Host "ok: OpenRGB cache" }
else {
    $places = @($env:MAZESTA_KIT, (Get-Location).Path, (Join-Path $env:USERPROFILE "Downloads")) + @(Get-PSDrive -PSProvider FileSystem | ForEach-Object { $_.Root })
    $kit = $places | Where-Object { $_ } | ForEach-Object { Join-Path $_ $kitName } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($kit) {
        if ((Get-FileHash $kit -Algorithm SHA256).Hash -ne $kitHash) { throw "$kit does not match the expected hash; it was not unpacked." }
        New-Item -ItemType Directory -Force $redist | Out-Null
        Expand-Archive $kit $redist -Force
        Write-Host "ok: OpenRGB cache from $kit"
    } else { Write-Warning "$kitName was not found (Downloads, drive roots, current folder). The build will fail at OpenRGB until it is copied and this is run again." }
}

Set-Location $dir
Write-Host "Ready: $dir"
if ($env:MAZESTA_NOCLAUDE) { return }
# The first run asks you to sign in to Claude in the browser.
claude "Read docs/HANDOFF-CPU-TEMP-RGB.md and do what it says. Answer me in Persian."
