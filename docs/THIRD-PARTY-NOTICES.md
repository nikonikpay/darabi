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
| ComputeSharp (+ ComputeSharp.Core) | 3.2.0 | MIT | `Mazesta.Diagnostics.Gpu` | DirectX 12 compute shaders for the GPU tests (stress, VRAM, render). Copyright Sergio Pedri. https://github.com/Sergio0694/ComputeSharp |
| Microsoft.Web.WebView2 | 1.0.2903.40 | BSD-3-Clause-style Microsoft licence (the package's LICENSE.txt) | `Mazesta.Desktop`, `Mazesta.Web` | Prints report HTML to PDF with the WebView2 runtime installed in Windows (offline, scripts off), and draws the whole interface of the web edition from its local `wwwroot` folder. The runtime itself is part of Windows and not shipped. |
| System.Diagnostics.EventLog | 10.0.x | MIT | `Mazesta.Diagnostics` | Reads WHEA hardware-error events for the post-test check. © Microsoft Corporation. |
| Vortice.Direct3D12, Vortice.DirectML (+ Vortice.DXGI, Vortice.DirectX, Vortice.Mathematics 2.1.0) | 3.8.3 | MIT | `Mazesta.Diagnostics.Gpu` | Raw Direct3D 12 and DirectML bindings for the GPU benchmarks ComputeSharp cannot express: rasterisation, DXR inline ray tracing and DirectML matrix multiplies. Copyright Amer Koleci. https://github.com/amerkoleci/Vortice.Windows . Referenced with `ExcludeAssets="build;buildMultitargeting;native"`: the package's own DirectML.dll (~18 MB) is not shipped; the copy in Windows (System32) is used. |
| SharpGen.Runtime, SharpGen.Runtime.COM | 2.4.2-beta | MIT | `Mazesta.Diagnostics.Gpu` (transitive, via Vortice) | COM interop runtime of the Vortice bindings. |
| llama.cpp (llama-bench and its ggml/Vulkan/CPU libraries) | build b11265, `llama-b11265-bin-win-vulkan-x64.zip` | MIT (the package's LICENSE files; bundles libomp under the LLVM licence, LICENSE-LLVM-OpenMP) | `Mazesta.Diagnostics` (AI models page) | **Not bundled.** Downloaded only when the user presses Download on the AI page, from the project's GitHub release, pinned by size and SHA-256 (`AiCatalog.Runtime`), unpacked to `Data/ai/runtime`. Copyright the ggml authors. https://github.com/ggml-org/llama.cpp |
| Language models (Qwen3.5 0.8B and 4B, Qwen3 4B and 14B, Qwen3.8 27B, Qwen3.6 35B-A3B by Alibaba Qwen; Gemma 4 E2B and E4B by Google; gpt-oss 20B by OpenAI), GGUF files of ggml-org (Qwen3.5 4B: bartowski's quantisation, as ggml-org has none) | pinned per file in `AiCatalog.Models` | Apache License 2.0 (each model's card) | `Mazesta.Diagnostics` (AI models page, assistant) | **Not bundled.** Each is downloaded only on the user's request from Hugging Face (huggingface.co/ggml-org, huggingface.co/bartowski), checked against its SHA-256, kept in `Data/ai/models` and deletable from the page. |
| DirectML | part of Windows (System32), not bundled | Windows component | `Mazesta.Diagnostics.Gpu` (AI benchmark) | Loaded from the OS at run time; when it is missing the AI benchmark reports Unsupported. |
| xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk | 2.9.3 / 3.1.5 / 18.10.0 | Apache-2.0 / MIT | `tests/*` | Test framework and runner; not shipped in `artifacts/Mazesta-Test`. |
| IRANSansXFaNum (Regular, Bold) | embedded `.ttf`, `src/Mazesta.Desktop/Fonts/` | Supplied by the product owner for this application; licence terms are the owner's to confirm before any redistribution outside the shop | `Mazesta.Desktop` | Persian text (its digits are Persian-form, so technical values use Segoe UI instead); falls back to Segoe UI. |
| Vazirmatn (variable, woff2) | 33.003 | SIL Open Font License 1.1 (`src/Mazesta.Web/wwwroot/fonts/Vazirmatn-OFL.txt`) | `Mazesta.Web` | Persian text of the web edition. Copyright the Vazirmatn project authors (Saber Rastikerdar). https://github.com/rastikerdar/vazirmatn |
| Archivo (variable, width and weight axes) | Google Fonts `ofl/archivo` | SIL Open Font License 1.1 (`src/Mazesta.Web/wwwroot/fonts/Archivo-OFL.txt`) | `Mazesta.Web` | Latin values and the condensed poster numerals of the web edition. Copyright the Archivo project authors (Omnibus-Type). https://github.com/Omnibus-Type/Archivo |
| PawnIO (official signed setup, `PawnIO_setup.exe`) | 2.2.0 | Licence per its own site — see https://pawnio.eu/ | `Mazesta.Hardware` (`Redist/`) | Kernel driver LibreHardwareMonitor 0.9.5+ needs for CPU MSR, Ryzen SMU, Super I/O and SMBus sensors. The build downloads the official setup and refuses it unless its SHA-256 matches; it ships unmodified in `Redist/` and the app runs it silently (`-install -silent`) only when elevated and the driver is missing (`PawnIoDriver`). |
| 3-D models in the courtyard scene (`src/Mazesta.Diagnostics.Gpu/Scene/garden.mzscene`, from the owner's DFM_Courtyard_V10.blend) | BlenderKit, at 1K textures (the gate at 2K), simplified and baked into the scene file | BlenderKit Royalty Free licence, as the V10 folder's `asset_manifest.json` records them: Wooden Armchair (Lucas Paludo), Teak Sofa by Arne Wahl Iversen (Patrik), Wooden chair traditional style (Panos Tourlas), Wood carving coffee table (Lucian Moricz), Small Grass Patch, Wild Grass Patch 2, Wild White Flower Patch, Small Evergreen Shrub and Wild Alpine Shrub Evergreen (EB Adventure Photoscans), Thuja Plant and Marguerite Flowers (Paweł Wałasiewicz), Blossoming Tree (Mr_Lamppost), Persian Carpet (BlenderKit Community), Jarlo Outdoor Side Table (Vicky Nguyen), Terracotta Vase (GoldFish CG), Artificial tendril Ivy v1, Ivy white-green v1 and vine green v1 (Samuel Slávik), Big Old Gates (Dal Roron), Multi-stem Deciduous Tree (Ken Xie); CC0: Shrub (BlenderKit Community). Carried over from the earlier files and not in that manifest: Stained Glass Lantern, Old Terracotta Pottery - https://www.blenderkit.com/docs/licenses/ | `Mazesta.Diagnostics.Gpu` | Furniture, plants, lanterns, pots and the gate in the visual GPU tests' scene. Royalty Free allows use in a product but not passing the models on as models; they ship only inside the scene file, simplified. The lantern's and the pottery's licences were not re-checked here: confirm them in the file's BlenderKit panel before release. The logo, the mirror sphere and the fountain's water are Mazesta's own; the building, the fountain, the stone benches, the stone, plaster, wood and tile textures and the mountain panorama round the horizon are the owner's (the V10 folder's README: geometry made for this project, base images generated or supplied by the owner, the other maps baked from them in Blender). |

| OpenRGB 1.0 (separate program, shipped as its own `OpenRGB` folder) | any | GPL-2.0 — https://openrgb.org/ | `Mazesta.Hardware/Rgb`, `Mazesta.Web` (`WebBridge.Rgb`) | The RGB colour page runs the user's own copy of OpenRGB as a separate process and talks to it over its local SDK socket (port 6742); no OpenRGB code is linked into the app. `tools/publish-single.ps1` fetches the project's official portable release (pinned hash) and puts it unmodified in an `OpenRGB` folder beside the app, with a NOTICE naming its source (https://codeberg.org/OpenRGB/OpenRGB, tag release_1.0); an OpenRGB the user installed works too. |

No other monitoring program's SDK, shared-memory interface, or any other third-party sensor
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
- **MIT** (System.Management, Microsoft.Extensions.\*, CommunityToolkit.Mvvm, Vortice.\*, SharpGen.Runtime\*):
  permissive, requires only notice/attribution, which this file provides.
- **PawnIO**: the official, signed, unmodified setup is redistributed in `Redist/`
  and installed by the app when the driver is missing. Its redistribution terms
  are the owner's to confirm against https://pawnio.eu/ before any release outside
  the shop; until then treat this as internal use.

## Product image and contact information

Product image AM9: retrieved 2026-09-12 from https://www.dfmrendering.com/shop/systems/am9-architectural-rendering-ryzen-9900x-rtx5060ti/ (owner's own site), embedded for offline display. Contact numbers 09197588700 / 09197588701 from https://www.dfmrendering.com/contactus/ (retrieved 2026-09-12).
