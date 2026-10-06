# Builds the copy for users (the users' edition: no service number, no link to the site, no reference marks): self-contained and as few files as possible (see publish-single.ps1). It goes to its own
# folder and never touches artifacts\Mazesta-Web or its Data. The app and the tray are one exe each; what stays beside them is what must be
# real files: Redist (the PawnIO setup is run from disk), wwwroot (the page), and the WebView2 loader if the SDK leaves it out.
#   pwsh tools/publish-release.ps1                       # -> artifacts/Mazesta-Release
param([string]$Output = "artifacts/Mazesta-Release")
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
$target = [IO.Path]::GetFullPath((Join-Path $repo $Output))
$running = Get-Process Mazesta, MazestaTray -ErrorAction SilentlyContinue | Where-Object { -not $_.Path -or $_.Path.StartsWith($target, [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw "Close Mazesta first (running: $($running.Name -join ', ')). Nothing was changed." }
if (Test-Path $target) { Remove-Item $target -Recurse -Force -Confirm:$false }
& (Join-Path $PSScriptRoot "publish-single.ps1") -Output $Output -Edition Client
