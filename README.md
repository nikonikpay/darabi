# Mazesta Test (سیستم تست مازستا)

Mazesta Test is a Windows x64 hardware diagnostics, stress-test and reporting
tool built for a PC service shop. It enumerates hardware and reads sensors
independently of HWiNFO, OCCT and AIDA64, and presents live monitoring,
diagnostics and reporting in a single WPF desktop application. This
repository delivers Slice 1 (solution skeleton, sensors and monitoring) and
the start of Slice 2 (test engine): a sequential test queue with cancellation,
repeat modes and crash-checkpoint detection, plus one real executor so far -
a CPU matrix-load stress test with its own correctness verification, wired
into the Test Center page. Specialised GPU/memory/storage/network executors
follow in later Slice 2/3 work.

## Prerequisites

- Windows 10 21H2+ or Windows 11, x64
- .NET 10 Desktop Runtime, x64 (SDK 10.0.400+ to build) — the framework-dependent
  publish in `artifacts/Mazesta-Test` needs the Desktop Runtime installed on
  the target machine; it is not self-contained.
- [PawnIO driver](https://pawnio.eu/), for CPU MSR-based sensors (temperatures,
  clocks, Vcore, package power). Without it the app still runs — the status
  bar shows a Degraded banner and CPU MSR-based rows read «دریافت نشد»
  instead of a fabricated number; every other sensor group (GPU, RAM,
  storage, network, per-thread CPU load, static inventory) works without it.
- The published Release build's manifest requests `requireAdministrator`
  (needed for CPU/motherboard sensor access); expect a UAC prompt on launch
  unless local policy auto-elevates for the account in use.

## Build and run

```powershell
# from Windows PowerShell, or via dotnet.exe from WSL
dotnet build Mazesta.sln -c Release
dotnet test Mazesta.sln -c Release --filter "Category!=Hardware"  # unit tests
dotnet test Mazesta.sln -c Release --filter Category=Hardware   # elevated, dev box only
dotnet publish src/Mazesta.Desktop -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o artifacts/Mazesta-Test
```

`build.ps1 -Test -Publish` wraps the commands above and also copies
`docs/GUIDE-FA.md` and `docs/THIRD-PARTY-NOTICES.md` into the publish
folder.

### Portable mode

Drop an empty file named `portable.marker` next to `MazestaTest.exe` in the
publish folder and the app stores its config and logs under a `Data\`
subfolder next to the executable instead of `%LocalAppData%\Mazesta\Test`.
See `docs/GUIDE-FA.md` for the full Persian walkthrough (also covers the
Dashboard, Monitoring, chart windows and Settings pages).

### Idle measurement

```powershell
tools/measure-idle.ps1                      # defaults: 300 s settle, 30 s sample
tools/measure-idle.ps1 -SettleSeconds 120   # shorter settle, for a quicker check
```
Launches the published exe elevated (UAC), reports working set, private
memory, idle CPU % and thread count once settled. See
`docs/VERIFICATION-slice1.md` for the dev box's recorded numbers against the
< 80 MB / < 1 % / < 3 s targets.

## Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — layers, project graph, allowed references
- [docs/PROVIDERS-AND-FALLBACKS.md](docs/PROVIDERS-AND-FALLBACKS.md) — what each provider supplies, what happens when it is absent, the LHM sensor-role map
- [docs/HARDWARE-MATRIX.md](docs/HARDWARE-MATRIX.md) — per-machine observed/missing sensors, HWiNFO cross-check
- [docs/VERIFICATION-slice1.md](docs/VERIFICATION-slice1.md) — measured build/test/publish/idle numbers, acceptance status, known gaps
- [docs/VERIFICATION-slice2-test-engine.md](docs/VERIFICATION-slice2-test-engine.md) — test engine + CPU matrix load: build/test numbers and what was not verified (no GUI session)
- [docs/GUIDE-FA.md](docs/GUIDE-FA.md) — راهنمای فارسی برای کاربر نهایی
- [docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md) — dependency licences and attributions
- `docs/superpowers/specs/` — slice design documents
