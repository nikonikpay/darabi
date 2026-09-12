# Third-party notices

Mazesta Test is built on the open-source and third-party components listed
below. This file records what is bundled or referenced, at which version,
under which licence, and by which project in this repository.

## Software dependencies

| Package | Version | Licence | Used by | Notes |
|---|---|---|---|---|
| LibreHardwareMonitorLib | 0.9.6 | Mozilla Public License 2.0 (MPL-2.0) | `Mazesta.Hardware` | Sensor enumeration and reading (CPU/GPU/RAM/motherboard/storage/network). Full licence text: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/blob/master/LICENSE |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | `Mazesta.Hardware` (transitive, via LibreHardwareMonitorLib) | Storage SMART/identify data. |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | `Mazesta.Hardware` (transitive, via LibreHardwareMonitorLib) | DIMM SPD data for memory module inventory. |
| HidSharp | (as pinned by LibreHardwareMonitorLib) | Apache License 2.0 | `Mazesta.Hardware` (transitive, via LibreHardwareMonitorLib) | USB HID access for some fan/RGB controllers. |
| System.Management | 10.0.x | MIT | `Mazesta.Hardware` | WMI queries for hardware inventory (`WmiInventoryProvider`). |
| Microsoft.Extensions.* (DependencyInjection, Logging, Logging.Abstractions, Options, Primitives) | 10.0.x | MIT | `Mazesta.Desktop`, `Mazesta.Hardware`, `Mazesta.Monitoring`, `Mazesta.Persistence` | Dependency injection and logging abstractions. |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | `Mazesta.Desktop` | Source-generated observable properties and commands for the WPF ViewModels. |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk | 2.9.3 / 3.1.5 / 18.10.0 | Apache-2.0 / MIT | `tests/*` | Test framework and runner; not shipped in `artifacts/Mazesta-Test`. |
| Vazirmatn (Regular, Bold) | embedded `.ttf`, `src/Mazesta.Desktop/Fonts/` | SIL Open Font License 1.1 | `Mazesta.Desktop` | Persian/Arabic glyph rendering for the RTL UI; falls back to Segoe UI. Licence text bundled alongside the fonts: `src/Mazesta.Desktop/Fonts/OFL.txt`. |
| PawnIO | separate, user-installed driver (not bundled) | Licence per its own site — see https://pawnio.eu/ | Runtime prerequisite for `Mazesta.Hardware` CPU MSR sensors | Not distributed with Mazesta Test; the user installs it independently. Detected at runtime via `LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled`. |

No HWiNFO SDK, shared-memory interface, or any other third-party sensor
library is used. No telemetry, analytics or crash-reporting SDK is included.

### Licence obligation notes

- **MPL-2.0** (LibreHardwareMonitorLib, DiskInfoToolkit, RAMSPDToolkit-NDD):
  file-level copyleft. Mazesta Test consumes these as unmodified NuGet
  binaries; no MPL-covered source in this repository is a modified copy of
  MPL-licensed source. Full licence text is linked above rather than
  reproduced here.
- **Apache-2.0** (HidSharp, xunit-family): permissive, requires only
  notice/attribution, which this file provides.
- **MIT** (System.Management, Microsoft.Extensions.\*, CommunityToolkit.Mvvm):
  permissive, requires only notice/attribution, which this file provides.
- **SIL OFL 1.1** (Vazirmatn): permits embedding in an application; the
  full licence text ships alongside the font files in the publish output
  (`src/Mazesta.Desktop/Fonts/OFL.txt` is marked `<Resource>` in the
  Desktop project and is included in `artifacts/Mazesta-Test`).
- **PawnIO**: not redistributed by this project. Users obtain and accept
  its licence directly from its own site before installing it.

## Product image and contact information

Product image AM9: retrieved 2026-09-12 from https://www.dfmrendering.com/shop/systems/am9-architectural-rendering-ryzen-9900x-rtx5060ti/ (owner's own site), embedded for offline display. Contact numbers 09197588700 / 09197588701 from https://www.dfmrendering.com/contactus/ (retrieved 2026-09-12).
