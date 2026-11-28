# Handoff: CPU temperature and lights on a second PC (2026-11-28)

## برای صاحب پروژه (فارسی)

روی یک سیستم دیگر دمای CPU و نور رم‌ها درست نبود. لاگ آن سیستم یک ریشه نشان داد (درایور سنسور PawnIO) و اصلاح شده، ولی **روی خود آن سیستم هنوز آزمایش نشده**.

**اول این را امتحان کنید (بدون Claude):** نصاب تازه (`artifacts\Mazesta-Setup\MazestaTestSetup.exe`) را روی آن سیستم نصب کنید، ویندوز را ری‌استارت کنید و برنامه را باز کنید. اگر دمای CPU و نور رم درست شد کار تمام است. اگر نشد، خروجی عیب‌یابی برنامه را بگیرید و مراحل زیر را بروید.

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

### The failing PC (its diagnostics export of 2026-11-27, app 0.15.21)
- i5-14600K, ASUS PRIME Z790-P D4, Corsair `CMW32GX4M2E3200C16` memory, RTX 3060. Memory integrity on.
- PawnIO service **RUNNING with no listed version** on every run back to 0.15.7; all 31 CPU temperatures, every clock and power, the board's Super I/O (0 fans, 0 temperatures) and the memory's SPD (0 modules) read nothing; no memory lights. The graphics card's own sensors read.
- One driver is behind all of it. Seen on the main PC: a lights server started while PawnIO could not be opened logs `Could not open PawnIO` for every SMBus module and finds no memory; started again with the driver up, it finds both modules.
- **Reproduced on the main PC**: `PawnIO_setup.exe -uninstall -silent` while the driver is loaded leaves it RUNNING and unlisted; the app then showed the same missing CPU temperatures, board fans and memory lights, and running the setup again brought all of them back without a restart. How the failing PC got into that state is not known.
- Changed since (after `2af630b`): every new version of the app removes and installs the carried driver, and an unlisted or older driver is set up at the next start (`PawnIoDriver.IsDue`, `Reinstall`); the lights server starts only after the driver step; the log names the driver's file and version.
- **Not yet run on that PC.** Install the new build, restart Windows, then check: `CPU Package` has a value, `Hardware details ready:` shows more than 0 SPD modules, and `OpenRGB devices` lists the memory. If the log says the setup did not take, remove the old driver (`sc.exe delete PawnIO` from an elevated shell, restart Windows) with the owner's OK and start the app again.
- Its graphics card is also missing from the lights (subsystem `1043:8A8F`); ask whether it has any lighting before looking further.

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
