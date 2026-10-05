# PhoenixGuardian review (2026-10-05)

What a competing Persian-market program (PhoenixGuardian 1.16.0, installed on the owner's PC) does, read from its install folder, its settings file and its page code (HTML/JS is plain text on disk). Nothing here was copied; it records techniques and what Mazesta did about each.

## RGB
- **No OpenRGB.** Its assemblies are `HidSharp` (USB HID), `RAMSPDToolkit` (SMBus), `LibreHardwareMonitorLib`, NvAPI (GPU I2C) and its own driver code: families such as ENE (RAM and boards), ASUS Aura (board over USB, GPU over NvAPI I2C), per-PCI-id tables of graphics cards, `SmbusAllowlist/Denylist`, mutex timeouts and back-off on the SMBus, and a `RgbConflictCloser` that closes the makers' programs (Armoury Crate, iCUE, ...) while it holds the lights.
- Its state is a **scene** in `settings.json` (`Rgb.scene`: on/off, one look for all devices, per-device switches, brightness, speed, "sleep_off"), applied again at start and after sleep; it also pauses in games.
- ARGB headers: an "ARGB header LED count" under advanced settings; nothing is asked in a window.
- **Mazesta now**: OpenRGB stays (writing native drivers for every maker is a project of its own) but runs as a bare server: no window, no tray icon, `--server-host 127.0.0.1`, its own `--config` folder, a free port when 6742 is taken (Windows hands the same numbers to outgoing connections; that made the server end at once on the owner's PC). The window the owner saw ("Zone Initialization") is OpenRGB's GUI, which `--startminimized` had started. A header with no length gets 30 LEDs and is resized through the SDK. What the user sets is the **scene** (`Data/config/rgb-scene.json`), put back by the app and by the tray at sign-in and after sleep; the tray menu switches it on/off.
- **Open**: a native HID driver for the ASUS Aura USB controller (the owner's board, `0B05:1939`, is HID) would remove OpenRGB for that board; RAM (ENE on SMBus) needs the PawnIO SMBus path and its safety rules (allow/deny lists) first. Not started.

## Fans and sensors
- It controls only the **GPU** fan (NVML/ADL), with a scanned "smart curve" and three profiles (silent, balanced, cool); the settings keep `FanPinSensorMap`, probe points and the learned minimum duty. It has no board-fan editor comparable to ours.
- **Identifying CPU fan / AIO pump across boards.** Findings (kernel `nct6775` documentation, FanControl's behaviour): the Super I/O names (SYSFAN, CPUFAN, AUXFAN) "do not necessarily correspond to the physical headers"; each fan output has its own *temperature source* register (SmartFan), which is how a chip knows which output follows the CPU; board makers' ACPI embedded controllers publish some header names (`asus_ec_sensors`). None of that is exposed by LibreHardwareMonitor. So no name is trusted blindly:
  1. the board's names are shown as they are, an output that never reports a speed is folded away (no fan wired);
  2. an output can be **run at full for 8 s** (only ever upward) and its speed watched: a speed that rises is a fan; one that stays is a pump on a fixed supply or an uncontrollable fan; none is "nothing wired". The page suggests a kind, the user confirms it and can name the output;
  3. a pump (by its name or by the user's say) is never slowed below 60 % and is left to the board by a ready-made profile.
- **Open**: a scan that pulses every output and pairs each with the tach that moved (to correct the control-to-tach pairing LibreHardwareMonitor assumes by index).

## Profiles and the tray
- Mazesta: profiles for all fans (auto, silent, standard, performance, full speed, and any the user saves), on the page and in the tray menu. The tray cannot hold a fan itself (its sensor driver is closed between checks), so it writes `fan-request.json` and starts the app with `--background` (no window); the app lives on while a profile is held and the tray runs, and ends when the profile goes back to automatic.

## Gaming
Profile based "Gaming Mode": RAM release when a game starts, pin the game to the V-Cache CCD on dual-CCD Ryzen, high CPU priority, CPU power-throttling off, Windows Game Mode, high-performance power plan, HAGS, GPU MSI mode, MMCSS profile, Game DVR off, services stopped; per-game profiles with auto-switch; playtime history; FPS by the bundled PresentMon; Smart Replay and a recorder (ffmpeg, hardware encoder); GPU undervolt/overclock profiles found by a stress search; per-profile **app blocking** (firewall rules) and **Ping Stability**, which limits background uploads of other programs while a game runs (a helper service with firewall/QoS access); Telegram alerts, scheduled wake and restarts.

## Monitoring
Same sensor library as Mazesta (LibreHardwareMonitor) plus DiskInfoToolkit and RAMSPDToolkit (RAM SPD temperatures). Its monitoring is game-centred (overlay, alerts, electricity cost), not a full sensor list; it has no name catalog beyond what the library gives. Mazesta's monitoring page and verified `SensorNameCatalog` are the more complete.

## Updates
`PhoenixGuardian.Updater.exe`, a signed manifest (public key and pinned certificate thumbprints), delta packages, download with pause/resume and progress, a signature and hash check, then install and restart by itself; a check every 6 hours; the release notes are shown before the user chooses. **Mazesta now**: the same shape on its own signed manifest: one button downloads, checks, installs and restarts (the old two buttons are gone); a notice with the release's changelog comes up when a new version is found, with "Update now" and "Later".
