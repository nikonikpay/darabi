# Mazesta Test (سیستم تست مازستا)

Mazesta Test is a Windows x64 hardware diagnostics, stress-test and reporting
tool built for a PC service shop. It enumerates hardware and reads sensors
independently of HWiNFO, OCCT and AIDA64, and presents live monitoring,
diagnostics and reporting in a single WPF desktop application. This
repository currently delivers Slice 1: the solution skeleton plus the
sensors and monitoring layers.

## Prerequisites

- Windows 10 21H2+ or Windows 11, x64
- .NET 10 Desktop Runtime, x64 (SDK 10.0.400+ to build)
- PawnIO driver, for CPU MSR-based sensors (temperatures, clocks, power)

## Build and run

```powershell
# from Windows PowerShell, or via dotnet.exe from WSL
dotnet build Mazesta.sln -c Release
dotnet test Mazesta.sln -c Release --filter "Category!=Hardware"  # unit tests
dotnet test Mazesta.sln -c Release --filter Category=Hardware   # elevated, dev box only
dotnet publish src/Mazesta.Desktop -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o artifacts/Mazesta-Test
```

`build.ps1 -Test -Publish` wraps the commands above.

## Documentation

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — layers, project graph, allowed references
- `docs/superpowers/specs/` — slice design documents
