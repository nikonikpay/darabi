# Mazesta Test (سیستم تست مازستا)

Mazesta Test is a Windows x64 hardware diagnostics, stress-test, benchmark and reporting tool built for a PC service shop. It reads
sensors itself and, as one self-contained Persian-first (RTL) WPF application, offers:

- live monitoring (sensor tree, charts) and a dashboard;
- a sequential test queue with repeat, cancellation and crash checkpoint: CPU, memory, storage, network and GPU tests, each
  verifying its own results, with a WHEA hardware-error check afterwards;
- benchmarks (numbers only, no pass/fail): CPU single/all-thread, memory bandwidth, storage (uncached sequential and random), Direct3D 12
  rendering, DXR ray tracing, DirectML AI (FP32/FP16/INT8) and internet speed;
- CPU, GPU, Storage and Network pages (inventory, live sensors and benchmarks of one part), System Information;
- reports in Persian or English as HTML, plain text, JSON and PDF, benchmark reports, and a before/after comparison;
- "Mazesta Monitor", a low-footprint tray process for temperature/clock alerts (start with Windows is opt-in).

Gaming and Windows Tools are phase 2 in the spec and are placeholders. See `docs/ARCHITECTURE.md` for the design.

## Portable

The app is portable only: it is not installed and writes nothing to the user profile. Everything it creates (settings, logs,
reports, history) lives in `Data\` next to `MazestaTest.exe`; copy the whole folder to move it, results included.

## Prerequisites

- Windows 10 21H2+ or Windows 11, x64, with the .NET 10 Desktop Runtime (x64). The publish is framework-dependent.
- [PawnIO driver](https://pawnio.eu/) for CPU MSR sensors (temperatures, clocks, Vcore, package power). Without it the app runs
  and those rows read «دریافت نشد» instead of a made-up number.
- Administrator rights (the app requests them; expect a UAC prompt).
- For the GPU benchmarks: a DirectX 12 GPU; ray tracing needs DXR 1.1 (GeForce RTX, Radeon RX 6000+, Arc); AI uses Windows'
  own DirectML. The internet benchmark talks to speed.cloudflare.com only when started.

## Build, test and run

```powershell
dotnet build Mazesta.sln -c Release
dotnet test  Mazesta.sln -c Release --no-build --filter "Category!=Hardware"   # unit tests
dotnet test  Mazesta.sln -c Release --no-build --filter "Category=Hardware"    # real machine, elevated
dotnet publish src/Mazesta.Desktop -c Release -o artifacts/Mazesta-Test       # delete the folder first
```

Run `artifacts\Mazesta-Test\MazestaTest.exe`. `build.ps1 -Test -Publish` wraps these and copies the Persian guide and the notices
into the publish folder. After changing a shader in `src/Mazesta.Diagnostics.Gpu/Shaders`, run `tools/compile-gpu-shaders.ps1`
(needs the Windows SDK). `tools/measure-idle.ps1` measures idle memory and CPU of the published app.

## Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — projects, allowed references, pages, data layout
- [docs/GUIDE-FA.md](docs/GUIDE-FA.md) — راهنمای فارسی برای کاربر
- [docs/PROVIDERS-AND-FALLBACKS.md](docs/PROVIDERS-AND-FALLBACKS.md) — what each provider supplies and what happens without it
- [docs/HARDWARE-MATRIX.md](docs/HARDWARE-MATRIX.md) — per-machine observed/missing sensors, independent cross-check
- `docs/VERIFICATION-*.md` — what was actually verified, on real hardware, per slice, and what was not
- [docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md) — dependency licences and attributions
- [docs/CODEX-TASKS.md](docs/CODEX-TASKS.md) — the task board
- `docs/superpowers/specs/` — slice design documents
