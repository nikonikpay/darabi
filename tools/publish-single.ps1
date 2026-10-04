# The compact build every published copy uses: self-contained (no .NET to install, Windows 10 and 11, x64), the app and the tray one exe each, so
# the folder holds three things - MazestaWeb.exe, MazestaTray.exe and what must stay real files: wwwroot (the page) and Redist (the PawnIO setup
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
Get-ChildItem $target -File | Where-Object { $_.Extension -in ".xml", ".json", ".pdb" } | Remove-Item -Force
Remove-Item $tray -Recurse -Force
Write-Host "Ready ($Edition): $(Join-Path $target 'MazestaWeb.exe')"
