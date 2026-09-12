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
| HidSharp | 2.6.4 | Apache License 2.0 | `Mazesta.Hardware` (transitive, via LibreHardwareMonitorLib) | USB HID access for some fan/RGB controllers. Version confirmed from `artifacts/Mazesta-Test/MazestaTest.deps.json` (`"HidSharp/2.6.4"`). |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | `Mazesta.Hardware` (transitive, via DiskInfoToolkit / RAMSPDToolkit-NDD) | Shared low-level helpers for the Blacktempel toolkits. Copyright Florian K. https://github.com/Blacktempel/BlackSharp |
| System.IO.Ports | 10.0.3 | MIT | `Mazesta.Hardware` (transitive, via LibreHardwareMonitorLib) | Serial-port access used by some controller backends. © Microsoft Corporation. |
| Mono.Posix.NETStandard (+ the native `MonoPosixHelper.dll` / `libMonoPosixHelper.dll` it ships) | 1.0.0 | MIT | `Mazesta.Hardware` (transitive, via System.IO.Ports) | POSIX interop for the non-Windows serial backend; dead weight on Windows but pulled in by the package graph. Licence per the package's `licenseUrl` (https://go.microsoft.com/fwlink/?linkid=869050, the .NET Library MIT licence). © Microsoft Corporation. The two native helper DLLs are dated 2018 and are shipped as-is from the package; they are never loaded on Windows. |
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
- **MPL-2.0** (BlackSharp.Core): as above - consumed as an unmodified NuGet
  binary.
- **MIT** (System.Management, Microsoft.Extensions.\*, CommunityToolkit.Mvvm):
  permissive, requires only notice/attribution, which this file provides.
- **SIL OFL 1.1** (Vazirmatn): permits embedding in an application, and
  requires the licence to travel with the font. The `.ttf` files are embedded
  as WPF `<Resource>`s inside `MazestaTest.dll`; `OFL.txt` is **not** a
  resource, so until the slice 1 final fix wave it was not shipped at all
  (an earlier version of this file wrongly claimed it was). It is now copied
  into the publish output by `build.ps1`, next to the executable.
- **PawnIO**: not redistributed by this project. Users obtain and accept
  its licence directly from its own site before installing it.

## Product image and contact information

Product image AM9: retrieved 2026-09-12 from https://www.dfmrendering.com/shop/systems/am9-architectural-rendering-ryzen-9900x-rtx5060ti/ (owner's own site), embedded for offline display. Contact numbers 09197588700 / 09197588701 from https://www.dfmrendering.com/contactus/ (retrieved 2026-09-12).
