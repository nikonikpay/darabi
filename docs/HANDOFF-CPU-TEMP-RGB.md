# Handoff: CPU temperature and lights on a second PC (2026-11-28)

## برای صاحب پروژه (فارسی)

مشکل این است که روی یک سیستم دیگر دمای CPU و نورها درست نیستند. علت هنوز **معلوم نیست**؛ باید روی همان سیستم بررسی شود.

**۱. روی سیستم اصلی:** دو فایل پوشه‌ی `artifacts\hw-kit` را روی فلش بریزید (`hw-session.ps1` و `Mazesta-OpenRGB-kit.zip`). فایل zip لازم است، چون لینک دانلود بخش نورها منقضی شده و بدون آن برنامه روی سیستم تازه ساخته نمی‌شود.

**۲. روی سیستم دوم:** PowerShell را باز کنید و این یک دستور را بزنید (به‌جای `E:` حرف فلش):

```powershell
powershell -ExecutionPolicy Bypass -File E:\hw-session.ps1
```

این دستور Git، PowerShell 7، .NET 10 SDK و Claude Code را اگر نباشند نصب می‌کند (winget ممکن است تأیید بخواهد)، کد را در `%USERPROFILE%\MazestaWork\Mazesta` می‌گیرد و Claude را با همین راهنما اجرا می‌کند. اینترنت لازم است.

**۳. کارهایی که خود شما باید بکنید:** ورود به Claude در بار اول، ورود به GitHub وقتی برای push خواست، تأیید پنجره‌ی مدیر ویندوز (UAC)، و گفتن این‌که دقیقاً چه چیزی غلط است (عکس صفحه کمک می‌کند).

اگر برنامه‌ی دسکتاپ Claude را ترجیح می‌دهید: پوشه‌ی `%USERPROFILE%\MazestaWork\Mazesta` را در آن باز کنید و بنویسید: «docs/HANDOFF-CPU-TEMP-RGB.md را بخوان و اجرا کن».

**۴. بعد از رفع مشکل:** تغییرها روی شاخه‌ی `slice-17/cpu-temp-rgb` در GitHub هستند.

---

## For the agent

### Where things stand
- Branch **`slice-17/cpu-temp-rgb`**, cut from `slice-16/crm-acc-offline` (app 0.15.22). Work and push here. `CLAUDE.md` names an older working branch; this note overrides it for this task.
- Last change in this area: `06d6686` (an older PawnIO is updated to the carried 2.2.0; sensors are not reopened while a CPU temperature already reads; LibreHardwareMonitorLib 0.9.7-pre785; OpenRGB pipeline build f899bc88). Verified **only on a Ryzen 9 3950X** (5 CPU temperatures, 7 fans, 4 lights). It was not run on an i7-14700K or a 9800X3D, and the failing PC's parts are not recorded.
- The symptom is unknown. **Ask first**: is the temperature missing or wrong (which number, against which tool, idle or loaded)? Which lights (board, RAM, graphics card, fans/strips; brand; screenshot)?

### Working rules (the main PC's memory does not travel)
- Answer in **Persian**. Smallest complete change; end each reply with one or two lines on what was not checked and the risk.
- Commit and push after every completed change; never push to `main`, never force-push. Set the repo-local git identity as `CLAUDE.md` states it.
- Nothing is fixed until the **published app on this PC** shows it fixed (see `AGENTS.md`). Close whatever is in the way; never delete a `Data` folder. Games are not a concern.
- The repository is **public**: no logs, diagnostics zips, serial numbers, PC names, keys or customer details in commits.
- Release notes never name OpenRGB or LibreHardwareMonitor.
- `graphify` and the main PC's hooks may be absent here; search the code directly then.

### Steps
1. `pwsh tools/publish.ps1 -Only Mazesta` (it also builds the staff program; a "not elevated" warning about the Windows 11 menu is harmless). If OpenRGB cannot be downloaded, its folder belongs in `..\Mazesta-Redist\OpenRGB-f899bc88` (from `Mazesta-OpenRGB-kit.zip`).
2. `pwsh tools/hw-doctor.ps1` (asks for admin, closes Mazesta and its tray, changes the lights for about a minute and puts them back). Read, in `artifacts\hw-doctor\<time>\`: `facts.txt`, `hardware-report.txt` (every sensor with its role and highest value), `selftest-lights.txt`, `mazesta-web*.log`, `openrgb\*.log`.
3. Find the cause from the evidence (tables below), fix it with a test, publish, run `hw-doctor` again, check the page itself, commit, push, and report what was measured before and after.

### CPU temperature
| What the evidence shows | Look at |
|---|---|
| `Provider.*` reason or `CPU sensors read nothing:` in the log | `src/Mazesta.Hardware/Lhm/LibreHardwareMonitorProvider.cs` (reasons), `PawnIoDriver.cs` (install, update, service state) |
| Memory integrity, driver blocklist or Smart App Control on; Code Integrity events | Windows may be refusing the driver. Turning a protection off is the owner's decision: say what it exposes and ask |
| Another monitoring or vendor tool running | `PawnIoDriver.Competing`; close it and measure again |
| The sensor is in the report with `role=-` | its exact name in `SensorRoleMap.cs`; test in `tests/Mazesta.Hardware.Tests` |
| A value that is wrong, not missing | which role each screen picks, from `CpuPackageTemp`, `CpuTctlTdie`, `CpuCoreTemp`: `wwwroot/js/pages/dashboard.js`, `monitoring.js`, `component.js`, and the tray's figure in `src/Mazesta.Core/Health/HealthSampler.cs` (check that they agree). Compare with HWiNFO only **after** closing Mazesta and the tray, never both at once |
| No CPU temperature sensor at all, or the library does not know the chip | `LibreHardwareMonitorLib` in `Directory.Packages.props`; check nuget.org for a newer build |

### Lights
| Log line or symptom | Look at |
|---|---|
| `OpenRGB.exe was not found` | the publish output has no `OpenRGB` folder |
| `server could not be started` / `did not come up` | `src/Mazesta.Hardware/Rgb/OpenRgbHost.cs` (ports 6742-6747), the OpenRGB log |
| `found no device`, or found once and not again | elevation; a leftover server; the OpenRGB log's bus lines; `RgbSession.cs` |
| A maker's lighting program is running | `RgbConflicts.cs`; `Mazesta-Admin.exe --selftest=makers` |
| One part is not listed | match the USB vendor/product id or the graphics card's subsystem id from `facts.txt` against OpenRGB's source. A newer pipeline build may list it: change the address, hash and commit in `tools/publish-single.ps1` and `docs/THIRD-PARTY-NOTICES.md`, and pack a new kit (its hash is in `tools/hw-session.ps1`) |
| Listed but stays dark or wrong | `RgbSession.ApplyLookAsync` (mode choice), the header's LED count (`SizeZonesAsync`), `selftest-lights.txt` |
| Lost after sleep or restart | `RgbSession.RecoverAsync`, `src/Mazesta.Tray` |

The page's bridge: `src/Mazesta.App/WebBridge.Rgb.cs`.

### More
`AGENTS.md` (build, tests, rules), `docs/BUILD.md`, `docs/ARCHITECTURE.md`, `docs/PROVIDERS-AND-FALLBACKS.md`. Self-tests are listed at the top of `src/Mazesta.App/SelfTest.cs`.
