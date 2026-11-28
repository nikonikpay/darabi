# One command that gathers what is needed to fix "CPU temperature / lights are wrong on this PC": the machine's parts, the sensor driver's state,
# the programs that may be in the way, and what the published app itself reads (its hardware report, the lights self-test and both logs), in one zip.
#   pwsh tools/hw-doctor.ps1                      # uses artifacts\Mazesta-Admin (publish first: pwsh tools/publish.ps1 -Only Mazesta)
# It asks for administrator rights, CLOSES a running Mazesta and its tray, and the lights self-test changes the lights for about a minute and puts
# them back. No serial numbers are collected by this script (the app's own logs carry the PC's name); the zip stays on this PC (artifacts\ is not in git).
param([string]$App = "artifacts/Mazesta-Admin/Mazesta-Admin.exe", [switch]$FactsOnly)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$exe = [IO.Path]::GetFullPath($App, $repo)
if (-not $FactsOnly -and -not (Test-Path $exe)) { throw "$exe is not there. Build it first: pwsh tools/publish.ps1 -Only Mazesta" }
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin -and -not $FactsOnly) {
    $forward = "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", "`"$PSCommandPath`"", "-App", "`"$exe`""
    $before = Get-Date
    $p = Start-Process (Get-Process -Id $PID).Path -Verb RunAs -Wait -PassThru -ArgumentList $forward
    $last = Get-ChildItem (Join-Path $repo "artifacts/hw-doctor") -Filter *.zip -ErrorAction SilentlyContinue | Where-Object LastWriteTime -ge $before | Sort-Object LastWriteTime | Select-Object -Last 1
    Write-Host "Elevated run ended ($($p.ExitCode)). Result: $(if ($last) { $last.FullName } else { 'none written' })"; exit $p.ExitCode
}

$out = Join-Path $repo ("artifacts/hw-doctor/" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force $out | Out-Null
$facts = Join-Path $out "facts.txt"
function Section([string]$Title, [scriptblock]$Body) {
    "", "=== $Title" | Add-Content $facts
    try { $text = (& $Body | Out-String -Width 220).TrimEnd(); $(if ($text) { $text } else { "(nothing)" }) | Add-Content $facts }
    catch { "(not readable: $($_.Exception.Message))" | Add-Content $facts }
}
$related = "asus|armoury|aura|rog|lighting|rgb|corsair|icue|msi|mystic|dragon|gigabyte|gcc|asrock|polychrome|nzxt|razer|logi|steelseries|hyperx|coolermaster|thermaltake|lianli|l-connect|hwinfo|hwmonitor|cpuz|cpu-z|afterburner|rtss|ryzen|xtu|openrgb|signalrgb|fancontrol|mazesta|pawnio|winring"

Section "Windows" { Get-CimInstance Win32_OperatingSystem | Format-List Caption, Version, BuildNumber, LastBootUpTime; "commit: " + (git -C $repo log -1 --format="%h %s") }
Section "System and board" { Get-CimInstance Win32_ComputerSystem | Format-List Manufacturer, Model; Get-CimInstance Win32_BaseBoard | Format-List Manufacturer, Product, Version; Get-CimInstance Win32_BIOS | Format-List SMBIOSBIOSVersion, ReleaseDate }
Section "Processor" { Get-CimInstance Win32_Processor | Format-List Name, Description, NumberOfCores, NumberOfLogicalProcessors }
Section "Graphics" { Get-CimInstance Win32_VideoController | Format-List Name, @{ n = "Id"; e = { ($_.PNPDeviceID -split "\\")[0..1] -join "\" } }, DriverVersion }
Section "Memory modules" { Get-CimInstance Win32_PhysicalMemory | Format-Table Manufacturer, PartNumber, Speed, Capacity -AutoSize }
Section "Driver blockers (1 = on)" {
    foreach ($r in @(@("memory integrity", "SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled"), @("driver blocklist", "SYSTEM\CurrentControlSet\Control\CI\Config", "VulnerableDriverBlocklistEnable"),
                     @("Smart App Control", "SYSTEM\CurrentControlSet\Control\CI\Policy", "VerifiedAndReputablePolicyState"), @("Secure Boot", "SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled"))) {
        "{0}: {1}" -f $r[0], (Get-ItemProperty "HKLM:\$($r[1])" -ErrorAction SilentlyContinue).($r[2])
    }
}
Section "PawnIO driver" { sc.exe query PawnIO; "installed version: " + (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO" -ErrorAction SilentlyContinue).DisplayVersion }
Section "Code Integrity events naming PawnIO or WinRing" { Get-WinEvent -LogName Microsoft-Windows-CodeIntegrity/Operational -MaxEvents 500 -ErrorAction SilentlyContinue | Where-Object Message -match "pawnio|winring" | Select-Object -First 10 | Format-List TimeCreated, Id, Message }
Section "Related processes" { Get-Process | Where-Object ProcessName -match $related | Group-Object ProcessName | Sort-Object Name | Format-Table Name, Count -AutoSize }
Section "Related services" { Get-Service -ErrorAction SilentlyContinue | Where-Object { "$($_.Name) $($_.DisplayName)" -match $related } | Format-Table Name, Status, StartType, DisplayName -AutoSize }
# Only the vendor and product ids: the rest of an instance id can be a serial number.
Section "USB and HID devices (vendor and product id)" {
    Get-PnpDevice -PresentOnly | Where-Object InstanceId -match "^(USB|HID)\\VID_" | ForEach-Object { [pscustomobject]@{ Id = [regex]::Match($_.InstanceId, "VID_[0-9A-F]{4}&PID_[0-9A-F]{4}(&MI_[0-9A-F]{2})?").Value; Class = $_.Class; Name = $_.FriendlyName } } | Sort-Object Id, Class, Name -Unique | Format-Table -AutoSize
}
if ($FactsOnly) { Write-Host "Facts: $facts"; exit 0 }

$data = Join-Path (Split-Path $exe) "Data"; $logs = Join-Path $data "logs"
$report = Join-Path $logs "hardware-report.txt"; $lights = Join-Path $logs "selftest-lights.txt"
Get-Process Mazesta, Mazesta-Admin, MazestaTray -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
$started = Get-Date
$proc = Start-Process $exe -ArgumentList "--selftest=lights" -PassThru
Write-Host "Reading the sensors and testing the lights (up to 5 minutes)..."
$sensorsDone = $false; $lightsDone = $false
while (((Get-Date) - $started).TotalSeconds -lt 300 -and -not $proc.HasExited -and -not ($sensorsDone -and $lightsDone)) {
    Start-Sleep -Seconds 3
    $sensorsDone = (Test-Path $report) -and (Get-Item $report).LastWriteTime -ge $started
    $lightsDone = (Test-Path $lights) -and (Get-Item $lights).LastWriteTime -ge $started -and (Select-String -Path $lights -Pattern "  DONE$|  FAILED:" -Quiet)
}
$summary = "sensor report written: $sensorsDone; lights self-test finished: $lightsDone; app exited by itself: $($proc.HasExited)"
Get-Process Mazesta, Mazesta-Admin, MazestaTray -ErrorAction SilentlyContinue | Stop-Process -Force
"", "=== Run", $summary | Add-Content $facts

# Only what this run wrote or what describes it; an old report must not pass for today's.
foreach ($f in @($report, $lights)) { if ((Test-Path $f) -and (Get-Item $f).LastWriteTime -ge $started) { Copy-Item $f $out } }
foreach ($pattern in @("logs/mazesta-web*.log", "logs/startup-error.txt", "logs/tray*.log", "openrgb/logs/*.log")) {
    Get-ChildItem (Join-Path $data $pattern) -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 2 | ForEach-Object {
        $folder = if ($pattern -like "openrgb*") { Join-Path $out "openrgb" } else { $out }
        New-Item -ItemType Directory -Force $folder | Out-Null; Copy-Item $_.FullName $folder
    }
}
Compress-Archive -Path (Join-Path $out "*") -DestinationPath "$out.zip" -Force
Write-Host $summary
Write-Host "Folder: $out"
Write-Host "Zip:    $out.zip"
if (-not ($sensorsDone -and $lightsDone)) { exit 1 }
