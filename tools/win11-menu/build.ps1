# Builds MazestaShell.dll (native, ~15 KB), wraps it in a signed sparse package and registers it for the app folder, so Windows 11 shows the file tools in the
# top-level right-click menu. Needs: Visual Studio C++ tools, the Windows SDK (makeappx, signtool) and an elevated shell (the signing certificate is trusted
# machine-wide). The certificate is Mazesta's own, self-signed: a user's machine without it does not trust the package (the classic menu still works there).
param([Parameter(Mandatory)][string]$AppDir)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot; $out = Join-Path $here 'out'; $AppDir = (Resolve-Path $AppDir).Path
New-Item -ItemType Directory -Force "$out\pkg\Assets" | Out-Null

$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -property installationPath
$kit = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory | Where-Object { Test-Path "$($_.FullName)\x64\makeappx.exe" } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $vs -or -not $kit) { throw 'Visual Studio C++ tools and the Windows SDK are needed.' }
$tools = "$($kit.FullName)\x64"

# 1. the DLL
$dll = "$out\MazestaShell.dll"
cmd /c "`"$vs\VC\Auxiliary\Build\vcvars64.bat`" >nul && cl /nologo /std:c++17 /LD /O1 /EHsc /MT /W3 `"$here\MazestaShell.cpp`" /Fo:`"$out\\`" /Fe:`"$dll`" /link /DEF:`"$here\MazestaShell.def`" /NOLOGO"
if (-not (Test-Path $dll)) { throw 'the DLL was not built' }

# 2. logos (plain yellow squares; the menu shows the tray program's icon)
Add-Type -AssemblyName System.Drawing
foreach ($n in 'StoreLogo', 'Logo') { $b = New-Object Drawing.Bitmap 50, 50; $g = [Drawing.Graphics]::FromImage($b); $g.Clear([Drawing.Color]::FromArgb(253, 212, 0)); $b.Save("$out\pkg\Assets\$n.png"); $g.Dispose(); $b.Dispose() }
Copy-Item "$here\AppxManifest.xml" "$out\pkg\AppxManifest.xml" -Force

# 3. certificate (made once, trusted for packages) and the signed package
$subject = 'CN=Mazesta Test'
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject -and $_.NotAfter -gt (Get-Date).AddMonths(1) } | Select-Object -First 1
if (-not $cert) { $cert = New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature -FriendlyName 'Mazesta file menu' -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(10) -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') }
Export-Certificate -Cert $cert -FilePath "$out\Mazesta.cer" -Force | Out-Null
Import-Certificate -FilePath "$out\Mazesta.cer" -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
$msix = "$out\MazestaFileMenu.msix"; Remove-Item $msix -ErrorAction SilentlyContinue
& "$tools\makeappx.exe" pack /o /d "$out\pkg" /p $msix /nv | Out-Null; if ($LASTEXITCODE) { throw 'makeappx failed' }
& "$tools\signtool.exe" sign /fd SHA256 /sha1 $cert.Thumbprint $msix | Out-Null; if ($LASTEXITCODE) { throw 'signtool failed' }

# 4. the DLL beside the app, the package registered for that folder
New-Item -ItemType Directory -Force "$AppDir\ShellMenu" | Out-Null
Get-AppxPackage -Name Mazesta.FileMenu | Remove-AppxPackage -ErrorAction SilentlyContinue
# Explorer's surrogate (dllhost) keeps the DLL loaded for a while; it starts again on the next right-click
Get-Process dllhost -ErrorAction SilentlyContinue | Where-Object { try { $_.Modules.ModuleName -contains 'MazestaShell.dll' } catch { $false } } | Stop-Process -Force -ErrorAction SilentlyContinue
Copy-Item $dll "$AppDir\ShellMenu\MazestaShell.dll" -Force
Add-AppxPackage -Path $msix -ExternalLocation $AppDir
"registered: $((Get-AppxPackage -Name Mazesta.FileMenu).PackageFullName)"
