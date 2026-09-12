# Mazesta Test — Slice 1 (Skeleton + Sensors & Monitoring) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A runnable Mazesta Test WPF app that reads real sensors through LibreHardwareMonitor 0.9.6 (PawnIO), shows a live grouped monitoring screen with bounded history and gap-aware chart windows, a dashboard with role-based cards, and persists versioned config — with every provider failure surfaced honestly and nothing fabricated.

**Architecture:** One elevated WPF process. Four class libraries (Core → pure model; Hardware → LHM + WMI providers behind `ISensorProvider`/`IInventoryProvider`; Monitoring → polling thread, history, statistics, events; Persistence → atomic versioned JSON + rolling log) and the Desktop app composed with plain DI. Provider calls happen only on the polling thread; the UI receives immutable snapshots.

**Tech Stack:** .NET 10 (`net10.0` / `net10.0-windows`), WPF, LibreHardwareMonitorLib 0.9.6 (MPL-2.0), System.Management 10.0.12, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection/Logging 10.0.12, xUnit 2.9.3. Build with Windows `dotnet.exe` invoked from WSL.

**Spec:** `docs/superpowers/specs/2026-09-12-mazesta-slice1-sensors-monitoring-design.md`

## Global Constraints

- Target framework: `net10.0` for Core, Monitoring, Persistence and their tests; `net10.0-windows` for Hardware, Desktop and Hardware.Tests. `PlatformTarget` x64 everywhere.
- `Nullable` enable, `ImplicitUsings` enable, `TreatWarningsAsErrors` true, zero build warnings.
- No fake data, no estimated sensor values, no placeholder numbers. Missing data is rendered as «دریافت نشد» (fa) / "Not available" (en).
- Component and sensor names stay English; user-facing explanations are Persian, right-to-left.
- No dependency on HWiNFO/OCCT/AIDA64, no shared-memory reading, no kernel-driver policy changes (never touch Memory Integrity).
- Red and orange colours are reserved for real alert states and are **not used** anywhere in this slice; Missing/Stale/Invalid states are grey with text + icon.
- No network request is made by the application in this slice.
- Idle targets to *measure and report*: < 80 MB working set, < 1 % CPU, < 3 s to window. No working-set trimming tricks.
- Build/test commands run from repo root `/mnt/f/Projects/darabi` with `DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"`. Default test filter excludes hardware tests: `--filter "Category!=Hardware"`.
- Commit after every task with the attribution trailer the session requires. Version `0.6.0` (continues the owner's 0.3/0.4/0.5 numbering).

---

## File structure

```
Mazesta.sln, Directory.Build.props, Directory.Packages.props, global.json, .editorconfig, build.ps1, README.md
src/Mazesta.Core/
  Hardware/HardwareKind.cs, HardwareVendor.cs, HardwareId.cs, SensorId.cs, SensorKind.cs, Unit.cs (enum + Units helpers), SensorRole.cs
  Hardware/DataQuality.cs, SensorDefinition.cs, HardwareNode.cs, SensorReading.cs, NodeStatus.cs, SensorSnapshot.cs, ProviderStatus.cs
  Hardware/ReadingValidator.cs
  Inventory/HardwareInventory.cs (all inventory records)
  Text/PersianDigits.cs
  Time/IClock.cs (IClock + SystemClock)
src/Mazesta.Hardware/
  ISensorProvider.cs (ISensorProvider, PollRequest, PollResult), IInventoryProvider.cs
  Lhm/ILhmComputer.cs, LhmComputerAdapter.cs, SensorRoleMap.cs, LhmHardwareMapper.cs, LibreHardwareMonitorProvider.cs
  Wmi/IWmiQuery.cs, WmiQuery.cs, WmiInventoryParser.cs, WmiInventoryProvider.cs
src/Mazesta.Monitoring/
  MonitoringOptions.cs, SensorStatistics.cs, StaleDetector.cs, HistoryStore.cs, EventLog.cs (IEventLog, EventEntry, EventLevel, BoundedEventLog), MonitoringFocus.cs, PollingEngine.cs
src/Mazesta.Persistence/
  AppPaths.cs, IVersionedDocument.cs, SchemaMigrator.cs (IMigration, SchemaMigrator), JsonStore.cs (JsonStore<T>, LoadOutcome, LoadResult<T>), AppConfig.cs (+ WindowPlacement, ChartWindowConfig, Migration0To1), RollingFileLogger.cs (provider + logger)
src/Mazesta.Desktop/
  App.xaml(.cs), app.manifest, app.debug.manifest, MainWindow.xaml(.cs), Mazesta.Desktop.csproj
  Composition/Bootstrapper.cs, SingleInstance.cs
  Localization/Loc.cs, Strings.resx, Strings.fa.resx, Help.fa.resx
  Themes/Dark.xaml, Controls/HelpTip.cs, Controls/TimeSeriesChart.cs
  ViewModels/ShellViewModel.cs, NavItem.cs, PlaceholderViewModel.cs, DashboardViewModel.cs, SensorCardViewModel.cs, MonitoringViewModel.cs, HardwareGroupViewModel.cs, SensorRowViewModel.cs, ChartWindowViewModel.cs, SettingsViewModel.cs
  Views/DashboardView.xaml, MonitoringView.xaml, SettingsView.xaml, PlaceholderView.xaml, ChartWindow.xaml(.cs)
  Services/ChartWindowService.cs
  Assets/am9.jpg, Fonts/Vazirmatn-Regular.ttf, Fonts/Vazirmatn-Bold.ttf, Fonts/OFL.txt
tests/Mazesta.Core.Tests/…, tests/Mazesta.Hardware.Tests/… (Fakes/FakeHardware.cs, Fakes/FakeSensor.cs, Fakes/FakeLhmComputer.cs), tests/Mazesta.Monitoring.Tests/… (Fakes/FakeClock.cs, Fakes/FakeSensorProvider.cs), tests/Mazesta.Persistence.Tests/…
tools/measure-idle.ps1
docs/ARCHITECTURE.md, PROVIDERS-AND-FALLBACKS.md, HARDWARE-MATRIX.md, VERIFICATION-slice1.md, GUIDE-FA.md, THIRD-PARTY-NOTICES.md
```

---

### Task 1: Solution skeleton and build configuration

**Files:**
- Create: `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, `Mazesta.sln`, `README.md`, `build.ps1`, `docs/ARCHITECTURE.md`
- Create: `src/Mazesta.Core/Mazesta.Core.csproj`, `src/Mazesta.Hardware/Mazesta.Hardware.csproj`, `src/Mazesta.Monitoring/Mazesta.Monitoring.csproj`, `src/Mazesta.Persistence/Mazesta.Persistence.csproj`, `src/Mazesta.Desktop/Mazesta.Desktop.csproj` (+ template App.xaml/MainWindow.xaml from `dotnet new wpf`)
- Create: `tests/Mazesta.Core.Tests/Mazesta.Core.Tests.csproj`, `tests/Mazesta.Hardware.Tests/…`, `tests/Mazesta.Monitoring.Tests/…`, `tests/Mazesta.Persistence.Tests/…`

**Interfaces:**
- Produces: the project graph every later task adds files to; `InternalsVisibleTo` from each `src` project to its test project.

- [ ] **Step 1: Write build props**

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <PlatformTarget>x64</PlatformTarget>
    <Version>0.6.0</Version>
    <Authors>Mazesta</Authors>
    <Product>Mazesta Test</Product>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <SatelliteResourceLanguages>en;fa</SatelliteResourceLanguages>
    <RootNamespace>$(MSBuildProjectName)</RootNamespace>
  </PropertyGroup>
</Project>
```
`Directory.Packages.props`:
```xml
<Project>
  <ItemGroup>
    <PackageVersion Include="LibreHardwareMonitorLib" Version="0.9.6" />
    <PackageVersion Include="System.Management" Version="10.0.12" />
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Logging" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.12" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.0" />
  </ItemGroup>
</Project>
```
`global.json`:
```json
{ "sdk": { "version": "10.0.400", "rollForward": "latestFeature" } }
```
`.editorconfig` (minimal): `root = true`, `[*.cs] indent_style = space`, `indent_size = 4`, `end_of_line = lf`, `charset = utf-8`, `csharp_style_namespace_declarations = file_scoped:warning`, `dotnet_diagnostic.CA1416.severity = error`.

- [ ] **Step 2: Create projects with the CLI**

```bash
DOTNET="/mnt/c/Program Files/dotnet/dotnet.exe"; cd /mnt/f/Projects/darabi
"$DOTNET" new sln -n Mazesta
for p in Core Monitoring Persistence; do "$DOTNET" new classlib -n Mazesta.$p -o src/Mazesta.$p --framework net10.0; rm src/Mazesta.$p/Class1.cs; done
"$DOTNET" new classlib -n Mazesta.Hardware -o src/Mazesta.Hardware; rm src/Mazesta.Hardware/Class1.cs   # TargetFramework is set to net10.0-windows in the csproj below
"$DOTNET" new wpf -n Mazesta.Desktop -o src/Mazesta.Desktop --framework net10.0-windows
for p in Core Hardware Monitoring Persistence; do "$DOTNET" new xunit -n Mazesta.$p.Tests -o tests/Mazesta.$p.Tests; rm tests/Mazesta.$p.Tests/UnitTest1.cs; done
"$DOTNET" sln Mazesta.sln add src/*/*.csproj tests/*/*.csproj
```
Then edit each csproj to the following shapes (remove `<TargetFramework>` lines that repeat the props default except where `net10.0-windows` is required, remove versions from `PackageReference`s because versions are central):

`src/Mazesta.Core/Mazesta.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup><InternalsVisibleTo Include="Mazesta.Core.Tests" /></ItemGroup>
</Project>
```
`src/Mazesta.Hardware/Mazesta.Hardware.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0-windows</TargetFramework></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="LibreHardwareMonitorLib" />
    <PackageReference Include="System.Management" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>
  <ItemGroup><ProjectReference Include="../Mazesta.Core/Mazesta.Core.csproj" /></ItemGroup>
  <ItemGroup><InternalsVisibleTo Include="Mazesta.Hardware.Tests" /></ItemGroup>
</Project>
```
`src/Mazesta.Monitoring/Mazesta.Monitoring.csproj`: references Core, `Microsoft.Extensions.Logging.Abstractions`, `InternalsVisibleTo Mazesta.Monitoring.Tests`.
`src/Mazesta.Persistence/Mazesta.Persistence.csproj`: references Core, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Logging`, `InternalsVisibleTo Mazesta.Persistence.Tests`.
`src/Mazesta.Desktop/Mazesta.Desktop.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <AssemblyName>MazestaTest</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationManifest Condition="'$(Configuration)' == 'Debug'">app.debug.manifest</ApplicationManifest>
    <ApplicationIcon></ApplicationIcon>
    <SatelliteResourceLanguages>en;fa</SatelliteResourceLanguages>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Mazesta.Core/Mazesta.Core.csproj" />
    <ProjectReference Include="../Mazesta.Hardware/Mazesta.Hardware.csproj" />
    <ProjectReference Include="../Mazesta.Monitoring/Mazesta.Monitoring.csproj" />
    <ProjectReference Include="../Mazesta.Persistence/Mazesta.Persistence.csproj" />
  </ItemGroup>
</Project>
```
Test csproj shape (all four; Hardware.Tests adds `<TargetFramework>net10.0-windows</TargetFramework>` and `<PackageReference Include="LibreHardwareMonitorLib" />`):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><IsPackable>false</IsPackable><IsTestProject>true</IsTestProject></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup><ProjectReference Include="../../src/Mazesta.Core/Mazesta.Core.csproj" /></ItemGroup>
</Project>
```
(Hardware.Tests references Hardware; Monitoring.Tests references Monitoring; Persistence.Tests references Persistence.)

Manifests: `app.manifest` with `<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />`, `<dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>`, `<longPathAware xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">true</longPathAware>`, and `supportedOS` GUIDs for Windows 10/11 (`{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}`). `app.debug.manifest` identical but `level="asInvoker"` (Debug runs unelevated; the app shows an "not elevated" status instead of failing).

- [ ] **Step 3: Write build.ps1, README, ARCHITECTURE.md**

`build.ps1`:
```powershell
param([switch]$Test, [switch]$Hardware, [switch]$Publish, [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
dotnet build Mazesta.sln -c $Configuration
if ($Test) { dotnet test Mazesta.sln -c $Configuration --no-build --filter "Category!=Hardware" }
if ($Hardware) { dotnet test tests/Mazesta.Hardware.Tests -c $Configuration --no-build --filter "Category=Hardware" }
if ($Publish) {
  dotnet publish src/Mazesta.Desktop -c $Configuration -r win-x64 --self-contained false -p:PublishReadyToRun=true -o artifacts/Mazesta-Test
  Copy-Item docs/GUIDE-FA.md, docs/THIRD-PARTY-NOTICES.md artifacts/Mazesta-Test/
}
```
`README.md`: product name (fa/en), one-paragraph purpose, prerequisites (.NET 10 Desktop Runtime x64, PawnIO driver for CPU sensors, Windows 10 21H2+/11 x64), build commands from §11 of the spec, link to docs.
`docs/ARCHITECTURE.md`: copy the layer table from spec §3.1 and the allowed-reference rules; add the sentence that Diagnostics, Reporting and Monitor projects are created by their own slices.

- [ ] **Step 4: Build and run the empty test suite**

Run: `"$DOTNET" build Mazesta.sln -c Debug` → Expected: `0 Warning(s) 0 Error(s)`.
Run: `"$DOTNET" test Mazesta.sln -c Debug --no-build --filter "Category!=Hardware"` → Expected: each test project reports `Passed! - Failed: 0, Passed: 0` (or "No test is available"), exit code 0.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "chore: solution skeleton, central package versions, build script"
```

---

### Task 2: Core identifiers, kinds, units, roles

**Files:**
- Create: `src/Mazesta.Core/Hardware/HardwareKind.cs`, `HardwareVendor.cs`, `HardwareId.cs`, `SensorId.cs`, `SensorKind.cs`, `Unit.cs`, `SensorRole.cs`
- Test: `tests/Mazesta.Core.Tests/IdentifierTests.cs`, `tests/Mazesta.Core.Tests/UnitsTests.cs`

**Interfaces:**
- Produces: `HardwareId.FromProviderPath(HardwareKind, string)`, `HardwareId.ForStorage(string serial)`, `SensorId.Create(HardwareId, string sensorPath)`, `SensorId.Hardware`, `Units.ForKind(SensorKind)`, `Units.Symbol(Unit)`, `Units.Format(double, Unit)`; enums `HardwareKind`, `HardwareVendor`, `SensorKind`, `Unit`, `SensorRole`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Mazesta.Core.Tests/IdentifierTests.cs
using Mazesta.Core.Hardware;
namespace Mazesta.Core.Tests;
public class IdentifierTests
{
    [Fact] public void ProviderPath_is_normalised_to_kind_slash_token()
        => Assert.Equal("gpu/nvidiagpu-0", HardwareId.FromProviderPath(HardwareKind.Gpu, "/nvidiagpu/0").Value);
    [Fact] public void SubHardware_path_keeps_all_segments()
        => Assert.Equal("motherboard/lpc-nct6687d-0", HardwareId.FromProviderPath(HardwareKind.Motherboard, "/lpc/nct6687d/0").Value);
    [Fact] public void Storage_id_uses_trimmed_serial_with_spaces_replaced()
        => Assert.Equal("storage/S6Z2NJ0T_123", HardwareId.ForStorage("  S6Z2NJ0T 123 ").Value);
    [Fact] public void SensorId_composes_and_exposes_hardware()
    {
        var hw = HardwareId.FromProviderPath(HardwareKind.Gpu, "/nvidiagpu/0");
        var s = SensorId.Create(hw, "temperature/2");
        Assert.Equal("gpu/nvidiagpu-0#temperature/2", s.Value);
        Assert.Equal(hw, s.Hardware);
    }
}
// tests/Mazesta.Core.Tests/UnitsTests.cs
using Mazesta.Core.Hardware;
namespace Mazesta.Core.Tests;
public class UnitsTests
{
    [Theory]
    [InlineData(SensorKind.Temperature, Unit.Celsius)] [InlineData(SensorKind.Clock, Unit.MegaHertz)]
    [InlineData(SensorKind.Load, Unit.Percent)] [InlineData(SensorKind.SmallData, Unit.Megabyte)]
    [InlineData(SensorKind.Throughput, Unit.BytesPerSecond)] [InlineData(SensorKind.Timing, Unit.Nanoseconds)]
    public void Kind_maps_to_unit(SensorKind kind, Unit unit) => Assert.Equal(unit, Units.ForKind(kind));
    [Fact] public void Format_uses_unit_precision() { Assert.Equal("45.5", Units.Format(45.49, Unit.Celsius)); Assert.Equal("5200", Units.Format(5200.4, Unit.MegaHertz)); Assert.Equal("1.234", Units.Format(1.2341, Unit.Volt)); }
    [Fact] public void Throughput_scales_to_readable_prefix() { Assert.Equal("12.5 MB/s", Units.FormatWithSymbol(12_500_000, Unit.BytesPerSecond)); Assert.Equal("800 B/s", Units.FormatWithSymbol(800, Unit.BytesPerSecond)); }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `"$DOTNET" test tests/Mazesta.Core.Tests -c Debug` → Expected: build error `HardwareId` / `Units` not found.

- [ ] **Step 3: Implement**

```csharp
// src/Mazesta.Core/Hardware/HardwareKind.cs
namespace Mazesta.Core.Hardware;
public enum HardwareKind { Cpu, Gpu, Memory, Motherboard, Storage, Network, Psu, Cooler, Other }
// HardwareVendor.cs
public enum HardwareVendor { Unknown, Intel, Amd, Nvidia }
// SensorKind.cs
public enum SensorKind { Temperature, Clock, Load, Voltage, Current, Power, Energy, Fan, Control, Level, Factor, Data, SmallData, Throughput, Timespan, Frequency, Timing, Noise, Humidity, Flow, Conductivity, Unknown }
// SensorRole.cs
public enum SensorRole
{
    None,
    CpuPackageTemp, CpuCoreTemp, CpuTctlTdie, CpuCcdTemp, CpuCoreClock, CpuCoreClockAverage, CpuEffectiveClock, CpuEffectiveClockAverage, CpuBusClock,
    CpuVcore, CpuPackagePower, CpuCorePower, CpuTotalLoad, CpuThreadLoad, CpuFan,
    GpuCoreTemp, GpuHotSpotTemp, GpuVramTemp, GpuCoreClock, GpuMemoryClock, GpuLoad3D, GpuLoadD3D3D, GpuLoadCompute, GpuLoadVideo, GpuLoadMemoryController,
    GpuPower, GpuVoltage, GpuFanRpm, GpuFanPercent, GpuVramTotal, GpuVramUsed, GpuVramFree,
    RamUsed, RamFree, RamTotal, RamLoad, DimmTemp,
    BoardTemp, ChipsetTemp, BoardFan, BoardVoltage,
    StorageTemp, StorageUsedSpace, StorageReadRate, StorageWriteRate, StorageRemainingLife, StoragePowerOnHours,
    NetUpload, NetDownload, NetUtilization
}
// HardwareId.cs
public readonly record struct HardwareId(string Value)
{
    public static HardwareId FromProviderPath(HardwareKind kind, string providerPath)
    {
        var token = providerPath.Trim('/').Replace('/', '-');
        return new HardwareId($"{KindToken(kind)}/{token}");
    }
    public static HardwareId ForStorage(string serial)
        => new($"storage/{string.Join('_', serial.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))}");
    public static string KindToken(HardwareKind kind) => kind.ToString().ToLowerInvariant();
    public override string ToString() => Value;
}
// SensorId.cs
public readonly record struct SensorId(string Value)
{
    public static SensorId Create(HardwareId hardware, string providerSensorPath) => new($"{hardware.Value}#{providerSensorPath.Trim('/')}");
    public HardwareId Hardware => new(Value[..Value.IndexOf('#')]);
    public override string ToString() => Value;
}
// Unit.cs
public enum Unit { Celsius, MegaHertz, Percent, Volt, Ampere, Watt, WattHour, Rpm, Gigabyte, Megabyte, BytesPerSecond, Seconds, Hertz, Nanoseconds, Decibel, Ratio, LitersPerHour, MicroSiemens, None }
public static class Units
{
    public static Unit ForKind(SensorKind kind) => kind switch
    {
        SensorKind.Temperature => Unit.Celsius, SensorKind.Clock => Unit.MegaHertz, SensorKind.Load or SensorKind.Control or SensorKind.Level or SensorKind.Humidity => Unit.Percent,
        SensorKind.Voltage => Unit.Volt, SensorKind.Current => Unit.Ampere, SensorKind.Power => Unit.Watt, SensorKind.Energy => Unit.WattHour, SensorKind.Fan => Unit.Rpm,
        SensorKind.Data => Unit.Gigabyte, SensorKind.SmallData => Unit.Megabyte, SensorKind.Throughput => Unit.BytesPerSecond, SensorKind.Timespan => Unit.Seconds,
        SensorKind.Frequency => Unit.Hertz, SensorKind.Timing => Unit.Nanoseconds, SensorKind.Noise => Unit.Decibel, SensorKind.Factor => Unit.Ratio,
        SensorKind.Flow => Unit.LitersPerHour, SensorKind.Conductivity => Unit.MicroSiemens, _ => Unit.None
    };
    public static string Symbol(Unit unit) => unit switch
    {
        Unit.Celsius => "°C", Unit.MegaHertz => "MHz", Unit.Percent => "%", Unit.Volt => "V", Unit.Ampere => "A", Unit.Watt => "W", Unit.WattHour => "Wh", Unit.Rpm => "RPM",
        Unit.Gigabyte => "GB", Unit.Megabyte => "MB", Unit.BytesPerSecond => "B/s", Unit.Seconds => "s", Unit.Hertz => "Hz", Unit.Nanoseconds => "ns", Unit.Decibel => "dB",
        Unit.Ratio => "", Unit.LitersPerHour => "L/h", Unit.MicroSiemens => "µS", _ => ""
    };
    public static int Decimals(Unit unit) => unit switch
    {
        Unit.Volt => 3, Unit.Celsius or Unit.Watt or Unit.Ampere or Unit.Gigabyte or Unit.Ratio or Unit.Nanoseconds => 1,
        _ => 0
    };
    public static string Format(double value, Unit unit) => value.ToString("F" + Decimals(unit), System.Globalization.CultureInfo.InvariantCulture);
    public static string FormatWithSymbol(double value, Unit unit)
    {
        if (unit == Unit.BytesPerSecond)
        {
            string[] prefixes = ["B/s", "KB/s", "MB/s", "GB/s"]; int i = 0; double v = value;
            while (v >= 1000 && i < prefixes.Length - 1) { v /= 1000; i++; }
            return $"{v.ToString(i == 0 ? "F0" : "F1", System.Globalization.CultureInfo.InvariantCulture)} {prefixes[i]}";
        }
        var s = Symbol(unit);
        return s.Length == 0 ? Format(value, unit) : $"{Format(value, unit)} {s}";
    }
}
```

- [ ] **Step 4: Run tests** → Expected: all `IdentifierTests` and `UnitsTests` pass.

- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(core): hardware/sensor identifiers, kinds, units and roles"`

---

### Task 3: Core readings, nodes, snapshot, provider status, validator

**Files:**
- Create: `src/Mazesta.Core/Hardware/DataQuality.cs`, `SensorDefinition.cs`, `HardwareNode.cs`, `SensorReading.cs`, `NodeStatus.cs`, `SensorSnapshot.cs`, `ProviderStatus.cs`, `ReadingValidator.cs`
- Test: `tests/Mazesta.Core.Tests/ReadingValidatorTests.cs`

**Interfaces:**
- Produces (used by every later task):
```csharp
public enum DataQuality { Ok, Missing, Stale, Invalid }
public sealed record SensorDefinition(SensorId Id, HardwareId Hardware, string Name, SensorKind Kind, Unit Unit, SensorRole Role, int Ordinal);
public sealed record HardwareNode(HardwareId Id, HardwareKind Kind, HardwareVendor Vendor, string Name, HardwareId? ParentId, bool IdIsStable, IReadOnlyList<SensorDefinition> Sensors);
public readonly record struct SensorReading(SensorId Id, double? Value, DateTimeOffset Timestamp, DataQuality Quality, string Source);
public sealed record NodeStatus(bool IsOk, string? FailureReason, DateTimeOffset? FailingSince, DateTimeOffset? LastSuccessfulUpdate);
public sealed record SensorSnapshot(long Sequence, DateTimeOffset Timestamp, IReadOnlyList<SensorReading> Readings, IReadOnlyDictionary<HardwareId, NodeStatus> NodeStatus);
public enum ProviderState { NotStarted, Starting, Ready, Degraded, Failed }
public sealed record ProviderStatus(ProviderState State, int SensorCount, string? ReasonKey, string? Detail);
public static class ReadingValidator { public static DataQuality Validate(SensorKind kind, double? value); }
```

- [ ] **Step 1: Write the failing tests**

```csharp
using Mazesta.Core.Hardware;
namespace Mazesta.Core.Tests;
public class ReadingValidatorTests
{
    [Theory]
    [InlineData(SensorKind.Temperature, 0.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Temperature, -5.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Temperature, 151.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Temperature, 45.0, DataQuality.Ok)]
    [InlineData(SensorKind.Clock, 0.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Clock, 4800.0, DataQuality.Ok)]
    [InlineData(SensorKind.Load, 100.0, DataQuality.Ok)]
    [InlineData(SensorKind.Load, 100.5, DataQuality.Invalid)]
    [InlineData(SensorKind.Voltage, 12.1, DataQuality.Ok)]
    [InlineData(SensorKind.Voltage, 21.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Fan, 0.0, DataQuality.Ok)]
    [InlineData(SensorKind.Power, -1.0, DataQuality.Invalid)]
    [InlineData(SensorKind.Timing, -3.0, DataQuality.Ok)]
    public void Ranges(SensorKind kind, double value, DataQuality expected) => Assert.Equal(expected, ReadingValidator.Validate(kind, value));
    [Fact] public void Null_is_missing() => Assert.Equal(DataQuality.Missing, ReadingValidator.Validate(SensorKind.Temperature, null));
    [Fact] public void NaN_and_infinity_are_invalid() { Assert.Equal(DataQuality.Invalid, ReadingValidator.Validate(SensorKind.Fan, double.NaN)); Assert.Equal(DataQuality.Invalid, ReadingValidator.Validate(SensorKind.Fan, double.PositiveInfinity)); }
    [Fact] public void ProviderStatus_factories()
    {
        Assert.Equal(ProviderState.Ready, ProviderStatus.Ready(12).State);
        var d = ProviderStatus.Degraded("Provider.PawnIoMissing", "not installed", 3);
        Assert.Equal((ProviderState.Degraded, 3, "Provider.PawnIoMissing"), (d.State, d.SensorCount, d.ReasonKey));
    }
}
```

- [ ] **Step 2: Run tests** → Expected: compile failure (types missing).

- [ ] **Step 3: Implement**

```csharp
namespace Mazesta.Core.Hardware;
public enum DataQuality { Ok, Missing, Stale, Invalid }
public sealed record SensorDefinition(SensorId Id, HardwareId Hardware, string Name, SensorKind Kind, Unit Unit, SensorRole Role, int Ordinal);
public sealed record HardwareNode(HardwareId Id, HardwareKind Kind, HardwareVendor Vendor, string Name, HardwareId? ParentId, bool IdIsStable, IReadOnlyList<SensorDefinition> Sensors);
public readonly record struct SensorReading(SensorId Id, double? Value, DateTimeOffset Timestamp, DataQuality Quality, string Source);
public sealed record NodeStatus(bool IsOk, string? FailureReason, DateTimeOffset? FailingSince, DateTimeOffset? LastSuccessfulUpdate)
{
    public static NodeStatus Healthy(DateTimeOffset lastUpdate) => new(true, null, null, lastUpdate);
    public static NodeStatus Failed(string reason, DateTimeOffset since, DateTimeOffset? lastUpdate) => new(false, reason, since, lastUpdate);
    public static readonly NodeStatus NeverUpdated = new(true, null, null, null);
}
public sealed record SensorSnapshot(long Sequence, DateTimeOffset Timestamp, IReadOnlyList<SensorReading> Readings, IReadOnlyDictionary<HardwareId, NodeStatus> NodeStatus);
public enum ProviderState { NotStarted, Starting, Ready, Degraded, Failed }
public sealed record ProviderStatus(ProviderState State, int SensorCount, string? ReasonKey, string? Detail)
{
    public static readonly ProviderStatus NotStarted = new(ProviderState.NotStarted, 0, null, null);
    public static readonly ProviderStatus Starting = new(ProviderState.Starting, 0, null, null);
    public static ProviderStatus Ready(int sensorCount) => new(ProviderState.Ready, sensorCount, null, null);
    public static ProviderStatus Degraded(string reasonKey, string detail, int sensorCount) => new(ProviderState.Degraded, sensorCount, reasonKey, detail);
    public static ProviderStatus Failed(string reasonKey, string detail) => new(ProviderState.Failed, 0, reasonKey, detail);
}
public static class ReadingValidator
{
    public static DataQuality Validate(SensorKind kind, double? value)
    {
        if (value is null) return DataQuality.Missing;
        double v = value.Value;
        if (double.IsNaN(v) || double.IsInfinity(v)) return DataQuality.Invalid;
        bool invalid = kind switch
        {
            SensorKind.Temperature => v <= 0 || v > 150,
            SensorKind.Clock => v <= 0,
            SensorKind.Load or SensorKind.Level or SensorKind.Control => v < 0 || v > 100,
            SensorKind.Voltage => v < 0 || v > 20,
            SensorKind.Power or SensorKind.Current or SensorKind.Fan or SensorKind.Data or SensorKind.SmallData or SensorKind.Throughput or SensorKind.Energy => v < 0,
            _ => false
        };
        return invalid ? DataQuality.Invalid : DataQuality.Ok;
    }
}
```

- [ ] **Step 4: Run tests** → Expected: all pass.
- [ ] **Step 5: Commit** — `git commit -am "feat(core): readings, nodes, snapshot, provider status, reading validator"`

---

### Task 4: Core utilities — PersianDigits, IClock, inventory records

**Files:**
- Create: `src/Mazesta.Core/Text/PersianDigits.cs`, `src/Mazesta.Core/Time/IClock.cs`, `src/Mazesta.Core/Inventory/HardwareInventory.cs`
- Test: `tests/Mazesta.Core.Tests/PersianDigitsTests.cs`

**Interfaces:**
- Produces: `PersianDigits.Normalize(string)`, `PersianDigits.TryParseDouble(string, out double)`, `PersianDigits.TryParseInt(string, out int)`; `IClock { DateTimeOffset UtcNow { get; } }`, `SystemClock`; inventory records below.

- [ ] **Step 1: Write the failing tests**

```csharp
using Mazesta.Core.Text;
namespace Mazesta.Core.Tests;
public class PersianDigitsTests
{
    [Fact] public void Persian_digits_become_ascii() => Assert.Equal("1234567890", PersianDigits.Normalize("۱۲۳۴۵۶۷۸۹۰"));
    [Fact] public void Arabic_indic_digits_become_ascii() => Assert.Equal("0123", PersianDigits.Normalize("٠١٢٣"));
    [Fact] public void Persian_decimal_separator_is_dot() => Assert.True(PersianDigits.TryParseDouble("۰٫۵", out var v) && v == 0.5);
    [Fact] public void Mixed_input_parses_as_int() => Assert.True(PersianDigits.TryParseInt(" ۹۰0 ", out var v) && v == 900);
    [Fact] public void Garbage_fails() => Assert.False(PersianDigits.TryParseInt("abc", out _));
}
```

- [ ] **Step 2: Run tests** → Expected: compile failure.

- [ ] **Step 3: Implement**

```csharp
// src/Mazesta.Core/Text/PersianDigits.cs
using System.Globalization; using System.Text;
namespace Mazesta.Core.Text;
public static class PersianDigits
{
    private const string Persian = "۰۱۲۳۴۵۶۷۸۹", ArabicIndic = "٠١٢٣٤٥٦٧٨٩";
    public static string Normalize(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            int p = Persian.IndexOf(c), a = ArabicIndic.IndexOf(c);
            sb.Append(p >= 0 ? (char)('0' + p) : a >= 0 ? (char)('0' + a) : c is '٫' or '،' ? '.' : c);
        }
        return sb.ToString();
    }
    public static bool TryParseDouble(string input, out double value)
        => double.TryParse(Normalize(input).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    public static bool TryParseInt(string input, out int value)
        => int.TryParse(Normalize(input).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
}
// src/Mazesta.Core/Time/IClock.cs
namespace Mazesta.Core.Time;
public interface IClock { DateTimeOffset UtcNow { get; } }
public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
// src/Mazesta.Core/Inventory/HardwareInventory.cs
using Mazesta.Core.Hardware;
namespace Mazesta.Core.Inventory;
public sealed record CpuInfo(string? Name, HardwareVendor Vendor, int? PhysicalCores, int? LogicalProcessors, int? MaxClockMhz, string? Socket);
public sealed record GpuInfo(string? Name, string? DriverVersion, long? AdapterRamBytes, string? PnpDeviceId);
public sealed record MemoryModuleInfo(string? Slot, long? CapacityBytes, string? Manufacturer, string? PartNumber, int? ConfiguredSpeedMts, int? SpeedMts);
public sealed record MotherboardInfo(string? Manufacturer, string? Product, string? Version, string? SerialNumber);
public sealed record BiosInfo(string? Vendor, string? Version, DateTime? ReleaseDate, string? SmbiosVersion);
public sealed record StorageDeviceInfo(string? FriendlyName, string? SerialNumber, string? MediaType, string? BusType, long? SizeBytes, string? FirmwareVersion, string? HealthStatus);
public sealed record NetworkAdapterInfo(string? Name, string? MacAddress, IReadOnlyList<string> IpAddresses, long? LinkSpeedBps, bool IsUp);
public sealed record OsInfo(string? Caption, string? Version, string? BuildNumber, string? Architecture);
public sealed record HardwareInventory(
    CpuInfo? Cpu, IReadOnlyList<GpuInfo> Gpus, IReadOnlyList<MemoryModuleInfo> MemoryModules, long? TotalPhysicalMemoryBytes,
    MotherboardInfo? Motherboard, BiosInfo? Bios, IReadOnlyList<StorageDeviceInfo> Storage, IReadOnlyList<NetworkAdapterInfo> NetworkAdapters,
    OsInfo? Os, IReadOnlyList<string> Errors)
{
    public static readonly HardwareInventory Empty = new(null, [], [], null, null, null, [], [], null, []);
}
```

- [ ] **Step 4: Run tests** → Expected: pass.
- [ ] **Step 5: Commit** — `git commit -am "feat(core): Persian digit normalisation, clock abstraction, inventory records"`

---

### Task 5: Hardware interfaces and the sensor role map

**Files:**
- Create: `src/Mazesta.Hardware/ISensorProvider.cs`, `src/Mazesta.Hardware/IInventoryProvider.cs`, `src/Mazesta.Hardware/Lhm/SensorRoleMap.cs`
- Test: `tests/Mazesta.Hardware.Tests/SensorRoleMapTests.cs`

**Interfaces:**
- Produces:
```csharp
namespace Mazesta.Hardware;
public readonly record struct PollRequest(DateTimeOffset Now, IReadOnlySet<HardwareId> NodesToUpdate);
public sealed record PollResult(IReadOnlyList<SensorReading> Readings, IReadOnlyDictionary<HardwareId, NodeStatus> NodeStatus);
public interface ISensorProvider : IDisposable
{
    string Name { get; }
    ProviderStatus Status { get; }
    event Action<ProviderStatus>? StatusChanged;
    void Start();                                   // polling thread; never throws
    IReadOnlyList<HardwareNode> Hardware { get; }   // valid after Start()
    PollResult Poll(PollRequest request);           // polling thread; never throws
}
public interface IInventoryProvider { Task<HardwareInventory> ReadAsync(CancellationToken ct); }
// Mazesta.Hardware.Lhm
internal static class SensorRoleMap { public static SensorRole Resolve(HardwareType hardwareType, SensorType sensorType, string name, string hardwareIdentifier); }
```
(`PollResult` carries node status in addition to the spec's reading list, because the snapshot needs both from the same poll.)

- [ ] **Step 1: Write the failing tests**

```csharp
using LibreHardwareMonitor.Hardware; using Mazesta.Core.Hardware; using Mazesta.Hardware.Lhm;
namespace Mazesta.Hardware.Tests;
public class SensorRoleMapTests
{
    [Theory]
    [InlineData(HardwareType.GpuNvidia, SensorType.Temperature, "GPU Hot Spot", SensorRole.GpuHotSpotTemp)]
    [InlineData(HardwareType.GpuNvidia, SensorType.Temperature, "GPU Core", SensorRole.GpuCoreTemp)]
    [InlineData(HardwareType.GpuNvidia, SensorType.Temperature, "GPU Memory Junction", SensorRole.GpuVramTemp)]
    [InlineData(HardwareType.GpuAmd, SensorType.Temperature, "GPU Memory", SensorRole.GpuVramTemp)]
    [InlineData(HardwareType.GpuNvidia, SensorType.Load, "GPU Core", SensorRole.GpuLoad3D)]
    [InlineData(HardwareType.GpuNvidia, SensorType.Load, "D3D 3D", SensorRole.GpuLoadD3D3D)]
    [InlineData(HardwareType.GpuIntel, SensorType.Load, "D3D Compute_0", SensorRole.GpuLoadCompute)]
    [InlineData(HardwareType.GpuNvidia, SensorType.Load, "GPU Video Engine", SensorRole.GpuLoadVideo)]
    [InlineData(HardwareType.GpuNvidia, SensorType.Load, "GPU Memory", SensorRole.None)]
    [InlineData(HardwareType.GpuNvidia, SensorType.SmallData, "GPU Memory Used", SensorRole.GpuVramUsed)]
    [InlineData(HardwareType.GpuNvidia, SensorType.SmallData, "D3D Dedicated Memory Used", SensorRole.None)]
    [InlineData(HardwareType.GpuNvidia, SensorType.Voltage, "GPU Core Voltage", SensorRole.GpuVoltage)]
    [InlineData(HardwareType.GpuAmd, SensorType.Voltage, "GPU Core", SensorRole.GpuVoltage)]
    [InlineData(HardwareType.GpuNvidia, SensorType.Fan, "GPU Fan", SensorRole.GpuFanRpm)]
    [InlineData(HardwareType.GpuNvidia, SensorType.Control, "GPU Fan", SensorRole.GpuFanPercent)]
    [InlineData(HardwareType.GpuNvidia, SensorType.Power, "GPU Package", SensorRole.GpuPower)]
    [InlineData(HardwareType.Cpu, SensorType.Temperature, "CPU Package", SensorRole.CpuPackageTemp)]
    [InlineData(HardwareType.Cpu, SensorType.Temperature, "P-Core #3", SensorRole.CpuCoreTemp)]
    [InlineData(HardwareType.Cpu, SensorType.Temperature, "CPU Core #12", SensorRole.CpuCoreTemp)]
    [InlineData(HardwareType.Cpu, SensorType.Temperature, "CPU Core #1 Distance to TjMax", SensorRole.None)]
    [InlineData(HardwareType.Cpu, SensorType.Temperature, "Core (Tctl/Tdie)", SensorRole.CpuTctlTdie)]
    [InlineData(HardwareType.Cpu, SensorType.Temperature, "CCD2 (Tdie)", SensorRole.CpuCcdTemp)]
    [InlineData(HardwareType.Cpu, SensorType.Temperature, "CCDs Max (Tdie)", SensorRole.None)]
    [InlineData(HardwareType.Cpu, SensorType.Clock, "Core #4 (Effective)", SensorRole.CpuEffectiveClock)]
    [InlineData(HardwareType.Cpu, SensorType.Clock, "Cores (Average Effective)", SensorRole.CpuEffectiveClockAverage)]
    [InlineData(HardwareType.Cpu, SensorType.Clock, "E-Core #2", SensorRole.CpuCoreClock)]
    [InlineData(HardwareType.Cpu, SensorType.Clock, "Bus Speed", SensorRole.CpuBusClock)]
    [InlineData(HardwareType.Cpu, SensorType.Voltage, "CPU Core", SensorRole.CpuVcore)]
    [InlineData(HardwareType.Cpu, SensorType.Voltage, "Core (SVI2 TFN)", SensorRole.CpuVcore)]
    [InlineData(HardwareType.Cpu, SensorType.Voltage, "P-Core #1", SensorRole.None)]
    [InlineData(HardwareType.Cpu, SensorType.Power, "CPU Package", SensorRole.CpuPackagePower)]
    [InlineData(HardwareType.Cpu, SensorType.Power, "Package", SensorRole.CpuPackagePower)]
    [InlineData(HardwareType.Cpu, SensorType.Power, "CPU Cores", SensorRole.CpuCorePower)]
    [InlineData(HardwareType.Cpu, SensorType.Load, "CPU Total", SensorRole.CpuTotalLoad)]
    [InlineData(HardwareType.Cpu, SensorType.Load, "CPU Core #3 Thread #2", SensorRole.CpuThreadLoad)]
    [InlineData(HardwareType.Cpu, SensorType.Load, "CPU Core Max", SensorRole.None)]
    [InlineData(HardwareType.Memory, SensorType.Load, "Memory", SensorRole.RamLoad)]
    [InlineData(HardwareType.Memory, SensorType.Data, "Memory Used", SensorRole.RamUsed)]
    [InlineData(HardwareType.Memory, SensorType.Data, "Memory Available", SensorRole.RamFree)]
    [InlineData(HardwareType.Memory, SensorType.Temperature, "DIMM #1", SensorRole.DimmTemp)]
    [InlineData(HardwareType.SuperIO, SensorType.Temperature, "Chipset", SensorRole.ChipsetTemp)]
    [InlineData(HardwareType.SuperIO, SensorType.Temperature, "PCH", SensorRole.ChipsetTemp)]
    [InlineData(HardwareType.SuperIO, SensorType.Temperature, "CPU", SensorRole.BoardTemp)]
    [InlineData(HardwareType.SuperIO, SensorType.Fan, "CPU Fan", SensorRole.CpuFan)]
    [InlineData(HardwareType.SuperIO, SensorType.Fan, "System Fan #2", SensorRole.BoardFan)]
    [InlineData(HardwareType.SuperIO, SensorType.Voltage, "+12V", SensorRole.BoardVoltage)]
    [InlineData(HardwareType.SuperIO, SensorType.Voltage, "Vcore", SensorRole.BoardVoltage)]
    [InlineData(HardwareType.SuperIO, SensorType.Voltage, "Voltage #7", SensorRole.None)]
    [InlineData(HardwareType.Storage, SensorType.Temperature, "Temperature", SensorRole.StorageTemp)]
    [InlineData(HardwareType.Storage, SensorType.Temperature, "Composite Temperature", SensorRole.StorageTemp)]
    [InlineData(HardwareType.Storage, SensorType.Temperature, "Warning Temperature", SensorRole.None)]
    [InlineData(HardwareType.Storage, SensorType.Level, "Life", SensorRole.StorageRemainingLife)]
    [InlineData(HardwareType.Storage, SensorType.Factor, "Power On Hours", SensorRole.StoragePowerOnHours)]
    [InlineData(HardwareType.Storage, SensorType.Load, "Used Space", SensorRole.StorageUsedSpace)]
    [InlineData(HardwareType.Storage, SensorType.Throughput, "Read Rate", SensorRole.StorageReadRate)]
    [InlineData(HardwareType.Network, SensorType.Throughput, "Upload Speed", SensorRole.NetUpload)]
    [InlineData(HardwareType.Network, SensorType.Throughput, "Download Speed", SensorRole.NetDownload)]
    [InlineData(HardwareType.Network, SensorType.Load, "Network Utilization", SensorRole.NetUtilization)]
    public void Maps_exact_lhm_names(HardwareType hw, SensorType st, string name, SensorRole expected)
        => Assert.Equal(expected, SensorRoleMap.Resolve(hw, st, name, "/x/0"));

    [Fact] public void Virtual_memory_node_gets_no_ram_roles()
        => Assert.Equal(SensorRole.None, SensorRoleMap.Resolve(HardwareType.Memory, SensorType.Data, "Memory Used", "/vram"));
    [Fact] public void Gpu_without_hot_spot_has_no_hot_spot_role_from_core()
        => Assert.NotEqual(SensorRole.GpuHotSpotTemp, SensorRoleMap.Resolve(HardwareType.GpuNvidia, SensorType.Temperature, "GPU Core", "/nvidiagpu/0"));
}
```

- [ ] **Step 2: Run** `"$DOTNET" test tests/Mazesta.Hardware.Tests -c Debug` → Expected: compile failure.

- [ ] **Step 3: Implement interfaces and the map**

```csharp
// src/Mazesta.Hardware/ISensorProvider.cs
using Mazesta.Core.Hardware;
namespace Mazesta.Hardware;
public readonly record struct PollRequest(DateTimeOffset Now, IReadOnlySet<HardwareId> NodesToUpdate);
public sealed record PollResult(IReadOnlyList<SensorReading> Readings, IReadOnlyDictionary<HardwareId, NodeStatus> NodeStatus)
{ public static readonly PollResult Empty = new([], new Dictionary<HardwareId, NodeStatus>()); }
public interface ISensorProvider : IDisposable
{
    string Name { get; }
    ProviderStatus Status { get; }
    event Action<ProviderStatus>? StatusChanged;
    void Start();
    IReadOnlyList<HardwareNode> Hardware { get; }
    PollResult Poll(PollRequest request);
}
// src/Mazesta.Hardware/IInventoryProvider.cs
using Mazesta.Core.Inventory;
namespace Mazesta.Hardware;
public interface IInventoryProvider { Task<HardwareInventory> ReadAsync(CancellationToken ct); }

// src/Mazesta.Hardware/Lhm/SensorRoleMap.cs
using System.Text.RegularExpressions; using LibreHardwareMonitor.Hardware; using Mazesta.Core.Hardware;
namespace Mazesta.Hardware.Lhm;
internal static partial class SensorRoleMap
{
    [GeneratedRegex(@"^(CPU Core|P-Core|E-Core) #\d+$")] private static partial Regex CoreName();
    [GeneratedRegex(@"^(CPU Core|P-Core|E-Core) #\d+( Thread #\d+)?$")] private static partial Regex ThreadLoadName();
    [GeneratedRegex(@"^Core #\d+$")] private static partial Regex AmdCoreClock();
    [GeneratedRegex(@"^Core #\d+ \(Effective\)$")] private static partial Regex AmdEffective();
    [GeneratedRegex(@"^CCD\d+ \(Tdie\)$")] private static partial Regex Ccd();
    [GeneratedRegex(@"^DIMM #\d+$")] private static partial Regex Dimm();
    [GeneratedRegex(@"^D3D Compute")] private static partial Regex D3DCompute();
    private static readonly HashSet<string> BoardVoltages = new(StringComparer.Ordinal)
    { "Vcore", "Vcore SoC", "+12V", "+5V", "+3.3V", "+3V Standby", "AVCC", "3VCC", "VBat", "DIMM", "CPU Termination", "CPU System Agent", "VTT", "VRM", "CPU Core" };

    public static SensorRole Resolve(HardwareType hw, SensorType st, string name, string hardwareIdentifier)
    {
        switch (hw)
        {
            case HardwareType.Cpu: return Cpu(st, name);
            case HardwareType.GpuNvidia: case HardwareType.GpuAmd: case HardwareType.GpuIntel: return Gpu(hw, st, name);
            case HardwareType.Memory: return hardwareIdentifier.StartsWith("/vram", StringComparison.Ordinal) ? SensorRole.None : Memory(st, name);
            case HardwareType.Motherboard: case HardwareType.SuperIO: case HardwareType.EmbeddedController: return Board(st, name);
            case HardwareType.Storage: return Storage(st, name);
            case HardwareType.Network: return Network(st, name);
            default: return SensorRole.None;
        }
    }
    private static SensorRole Cpu(SensorType st, string n) => st switch
    {
        SensorType.Temperature when n == "CPU Package" => SensorRole.CpuPackageTemp,
        SensorType.Temperature when n is "Core (Tctl/Tdie)" or "Core (Tdie)" or "Core (Tctl)" => SensorRole.CpuTctlTdie,
        SensorType.Temperature when Ccd().IsMatch(n) => SensorRole.CpuCcdTemp,
        SensorType.Temperature when CoreName().IsMatch(n) => SensorRole.CpuCoreTemp,
        SensorType.Clock when n == "Bus Speed" => SensorRole.CpuBusClock,
        SensorType.Clock when n == "Cores (Average Effective)" => SensorRole.CpuEffectiveClockAverage,
        SensorType.Clock when n == "Cores (Average)" => SensorRole.CpuCoreClockAverage,
        SensorType.Clock when AmdEffective().IsMatch(n) => SensorRole.CpuEffectiveClock,
        SensorType.Clock when CoreName().IsMatch(n) || AmdCoreClock().IsMatch(n) => SensorRole.CpuCoreClock,
        SensorType.Voltage when n is "CPU Core" or "Core (SVI2 TFN)" => SensorRole.CpuVcore,
        SensorType.Power when n is "CPU Package" or "Package" => SensorRole.CpuPackagePower,
        SensorType.Power when n == "CPU Cores" => SensorRole.CpuCorePower,
        SensorType.Load when n == "CPU Total" => SensorRole.CpuTotalLoad,
        SensorType.Load when ThreadLoadName().IsMatch(n) => SensorRole.CpuThreadLoad,
        _ => SensorRole.None
    };
    private static SensorRole Gpu(HardwareType hw, SensorType st, string n) => st switch
    {
        SensorType.Temperature when n == "GPU Core" => SensorRole.GpuCoreTemp,
        SensorType.Temperature when n == "GPU Hot Spot" => SensorRole.GpuHotSpotTemp,
        SensorType.Temperature when n == "GPU Memory Junction" || (n == "GPU Memory" && hw != HardwareType.GpuNvidia) => SensorRole.GpuVramTemp,
        SensorType.Clock when n == "GPU Core" => SensorRole.GpuCoreClock,
        SensorType.Clock when n == "GPU Memory" => SensorRole.GpuMemoryClock,
        SensorType.Load when n == "GPU Core" => SensorRole.GpuLoad3D,
        SensorType.Load when n == "D3D 3D" => SensorRole.GpuLoadD3D3D,
        SensorType.Load when D3DCompute().IsMatch(n) => SensorRole.GpuLoadCompute,
        SensorType.Load when n is "GPU Video Engine" or "GPU Media" => SensorRole.GpuLoadVideo,
        SensorType.Load when n == "GPU Memory Controller" => SensorRole.GpuLoadMemoryController,
        SensorType.Power when n is "GPU Package" or "GPU Power" => SensorRole.GpuPower,
        SensorType.Voltage when n is "GPU Core Voltage" or "GPU Core" => SensorRole.GpuVoltage,
        SensorType.Fan => SensorRole.GpuFanRpm,
        SensorType.Control when n == "GPU Fan" => SensorRole.GpuFanPercent,
        SensorType.SmallData when n == "GPU Memory Total" => SensorRole.GpuVramTotal,
        SensorType.SmallData when n == "GPU Memory Used" => SensorRole.GpuVramUsed,
        SensorType.SmallData when n == "GPU Memory Free" => SensorRole.GpuVramFree,
        _ => SensorRole.None
    };
    private static SensorRole Memory(SensorType st, string n) => st switch
    {
        SensorType.Load when n == "Memory" => SensorRole.RamLoad,
        SensorType.Data when n == "Memory Used" => SensorRole.RamUsed,
        SensorType.Data when n == "Memory Available" => SensorRole.RamFree,
        SensorType.Temperature when Dimm().IsMatch(n) => SensorRole.DimmTemp,
        _ => SensorRole.None
    };
    private static SensorRole Board(SensorType st, string n) => st switch
    {
        SensorType.Temperature when n is "Chipset" or "PCH" => SensorRole.ChipsetTemp,
        SensorType.Temperature => SensorRole.BoardTemp,
        SensorType.Fan when n.StartsWith("CPU Fan", StringComparison.Ordinal) => SensorRole.CpuFan,
        SensorType.Fan => SensorRole.BoardFan,
        SensorType.Voltage when BoardVoltages.Contains(n) => SensorRole.BoardVoltage,
        _ => SensorRole.None
    };
    private static SensorRole Storage(SensorType st, string n) => st switch
    {
        SensorType.Temperature when n is "Temperature" or "Composite Temperature" => SensorRole.StorageTemp,
        SensorType.Level when n == "Life" => SensorRole.StorageRemainingLife,
        SensorType.Factor when n == "Power On Hours" => SensorRole.StoragePowerOnHours,
        SensorType.Load when n == "Used Space" => SensorRole.StorageUsedSpace,
        SensorType.Throughput when n == "Read Rate" => SensorRole.StorageReadRate,
        SensorType.Throughput when n == "Write Rate" => SensorRole.StorageWriteRate,
        _ => SensorRole.None
    };
    private static SensorRole Network(SensorType st, string n) => st switch
    {
        SensorType.Throughput when n == "Upload Speed" => SensorRole.NetUpload,
        SensorType.Throughput when n == "Download Speed" => SensorRole.NetDownload,
        SensorType.Load when n == "Network Utilization" => SensorRole.NetUtilization,
        _ => SensorRole.None
    };
}
```

- [ ] **Step 4: Run tests** → Expected: all `SensorRoleMapTests` pass.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(hardware): provider interfaces and LHM sensor role map"`

---

### Task 6: LHM tree mapper with fake hardware doubles

**Files:**
- Create: `src/Mazesta.Hardware/Lhm/LhmHardwareMapper.cs`
- Create: `tests/Mazesta.Hardware.Tests/Fakes/FakeHardware.cs`, `tests/Mazesta.Hardware.Tests/Fakes/FakeSensor.cs`
- Test: `tests/Mazesta.Hardware.Tests/LhmHardwareMapperTests.cs`

**Interfaces:**
- Consumes: `SensorRoleMap.Resolve`, `HardwareId`, `SensorId`, `SensorDefinition`, `HardwareNode`, `Units.ForKind`.
- Produces:
```csharp
internal sealed record MappedSensor(SensorDefinition Definition, ISensor Source);
internal sealed record MappedNode(HardwareNode Node, IHardware Source, IReadOnlyList<MappedSensor> Sensors);
internal sealed class LhmHardwareMapper(Func<IHardware, string?> storageSerialResolver)
{
    public IReadOnlyList<MappedNode> Map(IEnumerable<IHardware> roots);   // flattens SubHardware, skips IsDefaultHidden sensors
    public static HardwareKind KindOf(HardwareType t); public static HardwareVendor VendorOf(HardwareType t, string identifier); public static SensorKind SensorKindOf(SensorType t);
}
```

- [ ] **Step 1: Write the fakes and failing tests**

```csharp
// tests/Mazesta.Hardware.Tests/Fakes/FakeSensor.cs
using LibreHardwareMonitor.Hardware;
namespace Mazesta.Hardware.Tests.Fakes;
public sealed class FakeSensor(IHardware hardware, string name, SensorType type, int index, float? value, bool hidden = false) : ISensor
{
    public IControl Control => null!;
    public IHardware Hardware { get; } = hardware;
    public Identifier Identifier { get; } = new(hardware.Identifier, type.ToString().ToLowerInvariant(), index.ToString());
    public int Index { get; } = index;
    public bool IsDefaultHidden { get; } = hidden;
    public float? Max => Value; public float? Min => Value;
    public string Name { get; set; } = name;
    public IReadOnlyList<IParameter> Parameters => [];
    public SensorType SensorType { get; } = type;
    public float? Value { get; set; } = value;
    public IEnumerable<SensorValue> Values => [];
    public TimeSpan ValuesTimeWindow { get; set; }
    public void ResetMin() { } public void ResetMax() { } public void ClearValues() { }
    public void Accept(IVisitor visitor) { } public void Traverse(IVisitor visitor) { }
}
// tests/Mazesta.Hardware.Tests/Fakes/FakeHardware.cs
using LibreHardwareMonitor.Hardware;
namespace Mazesta.Hardware.Tests.Fakes;
public sealed class FakeHardware(HardwareType type, string identifier, string name, IHardware? parent = null) : IHardware
{
    private readonly List<ISensor> _sensors = [];
    public HardwareType HardwareType { get; } = type;
    public Identifier Identifier { get; } = new(identifier.Trim('/').Split('/'));
    public string Name { get; set; } = name;
    public IHardware Parent { get; } = parent!;
    public ISensor[] Sensors => _sensors.ToArray();
    public IHardware[] SubHardware { get; set; } = [];
    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
    public int UpdateCalls; public Exception? ThrowOnUpdate;
    public event SensorEventHandler? SensorAdded; public event SensorEventHandler? SensorRemoved;
    public string GetReport() => "";
    public void Update() { UpdateCalls++; if (ThrowOnUpdate is not null) throw ThrowOnUpdate; }
    public void Accept(IVisitor visitor) { } public void Traverse(IVisitor visitor) { }
    public FakeSensor Add(string name, SensorType type, int index, float? value, bool hidden = false)
    { var s = new FakeSensor(this, name, type, index, value, hidden); _sensors.Add(s); SensorAdded?.Invoke(s); return s; }
    public void Remove(ISensor s) { _sensors.Remove(s); SensorRemoved?.Invoke(s); }
}
// tests/Mazesta.Hardware.Tests/LhmHardwareMapperTests.cs
using LibreHardwareMonitor.Hardware; using Mazesta.Core.Hardware; using Mazesta.Hardware.Lhm; using Mazesta.Hardware.Tests.Fakes;
namespace Mazesta.Hardware.Tests;
public class LhmHardwareMapperTests
{
    private static LhmHardwareMapper Mapper(string? serial = null) => new(_ => serial);
    [Fact] public void Nvidia_gpu_maps_kind_vendor_id_and_roles()
    {
        var gpu = new FakeHardware(HardwareType.GpuNvidia, "/nvidiagpu/0", "NVIDIA GeForce RTX 4090");
        gpu.Add("GPU Core", SensorType.Temperature, 0, 41); gpu.Add("GPU Hot Spot", SensorType.Temperature, 1, 52);
        var node = Assert.Single(Mapper().Map([gpu]));
        Assert.Equal((HardwareKind.Gpu, HardwareVendor.Nvidia, "gpu/nvidiagpu-0", true), (node.Node.Kind, node.Node.Vendor, node.Node.Id.Value, node.Node.IdIsStable));
        Assert.Contains(node.Sensors, s => s.Definition.Role == SensorRole.GpuHotSpotTemp && s.Definition.Id.Value == "gpu/nvidiagpu-0#temperature/1");
        Assert.Equal(Unit.Celsius, node.Sensors[0].Definition.Unit);
    }
    [Fact] public void Gpu_without_hot_spot_yields_no_hot_spot_role()
    {
        var gpu = new FakeHardware(HardwareType.GpuIntel, "/gpu-intel-integrated/0", "Intel(R) UHD Graphics 770");
        gpu.Add("GPU Core", SensorType.Temperature, 0, 40);
        Assert.DoesNotContain(Mapper().Map([gpu])[0].Sensors, s => s.Definition.Role == SensorRole.GpuHotSpotTemp);
    }
    [Fact] public void Cpu_vendor_comes_from_identifier_not_name()
    {
        var cpu = new FakeHardware(HardwareType.Cpu, "/amdcpu/0", "Some GPU-looking name");
        Assert.Equal(HardwareVendor.Amd, Mapper().Map([cpu])[0].Node.Vendor);
        Assert.Equal(HardwareVendor.Intel, Mapper().Map([new FakeHardware(HardwareType.Cpu, "/intelcpu/0", "x")])[0].Node.Vendor);
    }
    [Fact] public void Storage_uses_serial_when_available_and_provider_path_otherwise()
    {
        var disk = new FakeHardware(HardwareType.Storage, "/nvme/0", "Samsung SSD 990 PRO 2TB");
        Assert.Equal(("storage/S7KXNJ0X", true), (Mapper("S7KXNJ0X").Map([disk])[0].Node.Id.Value, Mapper("S7KXNJ0X").Map([disk])[0].Node.IdIsStable));
        Assert.Equal(("storage/nvme-0", false), (Mapper(null).Map([disk])[0].Node.Id.Value, Mapper(null).Map([disk])[0].Node.IdIsStable));
    }
    [Fact] public void SubHardware_is_flattened_with_parent_id()
    {
        var board = new FakeHardware(HardwareType.Motherboard, "/motherboard", "MSI Z790");
        var sio = new FakeHardware(HardwareType.SuperIO, "/lpc/nct6687d/0", "Nuvoton NCT6687D", board); sio.Add("CPU Fan", SensorType.Fan, 0, 900);
        board.SubHardware = [sio];
        var nodes = Mapper().Map([board]);
        Assert.Equal(2, nodes.Count);
        Assert.Equal(nodes[0].Node.Id, nodes[1].Node.ParentId);
        Assert.Equal(HardwareKind.Motherboard, nodes[1].Node.Kind);
    }
    [Fact] public void Hidden_sensors_are_skipped()
    {
        var mem = new FakeHardware(HardwareType.Memory, "/memory/dimm/0", "DIMM"); mem.Add("Thermal Sensor Low Limit", SensorType.Temperature, 2, 0, hidden: true); mem.Add("DIMM #1", SensorType.Temperature, 0, 38);
        Assert.Single(Mapper().Map([mem])[0].Sensors);
    }
}
```

- [ ] **Step 2: Run tests** → Expected: compile failure (`LhmHardwareMapper` missing).

- [ ] **Step 3: Implement the mapper**

```csharp
// src/Mazesta.Hardware/Lhm/LhmHardwareMapper.cs
using LibreHardwareMonitor.Hardware; using Mazesta.Core.Hardware;
namespace Mazesta.Hardware.Lhm;
internal sealed record MappedSensor(SensorDefinition Definition, ISensor Source);
internal sealed record MappedNode(HardwareNode Node, IHardware Source, IReadOnlyList<MappedSensor> Sensors);
internal sealed class LhmHardwareMapper(Func<IHardware, string?> storageSerialResolver)
{
    public IReadOnlyList<MappedNode> Map(IEnumerable<IHardware> roots)
    {
        var result = new List<MappedNode>();
        foreach (var root in roots) Visit(root, null, result);
        return result;
    }
    private void Visit(IHardware hw, HardwareId? parentId, List<MappedNode> into)
    {
        var kind = KindOf(hw.HardwareType);
        string path = hw.Identifier.ToString();
        string? serial = kind == HardwareKind.Storage ? Normalize(storageSerialResolver(hw)) : null;
        var id = serial is not null ? HardwareId.ForStorage(serial) : HardwareId.FromProviderPath(kind, path);
        var sensors = new List<MappedSensor>();
        int ordinal = 0;
        foreach (var s in hw.Sensors.Where(s => !s.IsDefaultHidden).OrderBy(s => s.SensorType).ThenBy(s => s.Index))
        {
            var sensorKind = SensorKindOf(s.SensorType);
            string sensorPath = s.Identifier.ToString()[path.Length..];   // "/temperature/1"
            var def = new SensorDefinition(SensorId.Create(id, sensorPath), id, s.Name, sensorKind, Units.ForKind(sensorKind),
                SensorRoleMap.Resolve(hw.HardwareType, s.SensorType, s.Name, path), ordinal++);
            sensors.Add(new MappedSensor(def, s));
        }
        var node = new HardwareNode(id, kind, VendorOf(hw.HardwareType, path), hw.Name, parentId, serial is not null || kind != HardwareKind.Storage, sensors.Select(m => m.Definition).ToList());
        into.Add(new MappedNode(node, hw, sensors));
        foreach (var sub in hw.SubHardware) Visit(sub, id, into);
    }
    private static string? Normalize(string? serial) => string.IsNullOrWhiteSpace(serial) ? null : serial.Trim();
    public static HardwareKind KindOf(HardwareType t) => t switch
    {
        HardwareType.Cpu => HardwareKind.Cpu,
        HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel => HardwareKind.Gpu,
        HardwareType.Memory => HardwareKind.Memory,
        HardwareType.Motherboard or HardwareType.SuperIO or HardwareType.EmbeddedController => HardwareKind.Motherboard,
        HardwareType.Storage => HardwareKind.Storage, HardwareType.Network => HardwareKind.Network,
        HardwareType.Psu => HardwareKind.Psu, HardwareType.Cooler => HardwareKind.Cooler, _ => HardwareKind.Other
    };
    public static HardwareVendor VendorOf(HardwareType t, string identifier) => t switch
    {
        HardwareType.GpuNvidia => HardwareVendor.Nvidia, HardwareType.GpuAmd => HardwareVendor.Amd, HardwareType.GpuIntel => HardwareVendor.Intel,
        HardwareType.Cpu when identifier.StartsWith("/intelcpu", StringComparison.Ordinal) => HardwareVendor.Intel,
        HardwareType.Cpu when identifier.StartsWith("/amdcpu", StringComparison.Ordinal) => HardwareVendor.Amd,
        _ => HardwareVendor.Unknown
    };
    public static SensorKind SensorKindOf(SensorType t) => Enum.TryParse<SensorKind>(t.ToString(), out var k) ? k : t == SensorType.TimeSpan ? SensorKind.Timespan : SensorKind.Unknown;
}
```

- [ ] **Step 4: Run tests** → Expected: all mapper tests pass (if `Identifier(params string[])` produces `/nvidiagpu/0` for `["nvidiagpu","0"]` — it does in 0.9.6; if a test shows a different prefix, fix the fake, not the mapper).
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(hardware): map LHM hardware tree to Mazesta nodes with stable ids"`

---

### Task 7: LibreHardwareMonitorProvider (start, poll, cadence, isolation, PawnIO status)

**Files:**
- Create: `src/Mazesta.Hardware/Lhm/ILhmComputer.cs`, `src/Mazesta.Hardware/Lhm/LhmComputerAdapter.cs`, `src/Mazesta.Hardware/Lhm/LibreHardwareMonitorProvider.cs`
- Create: `tests/Mazesta.Hardware.Tests/Fakes/FakeLhmComputer.cs`
- Test: `tests/Mazesta.Hardware.Tests/LibreHardwareMonitorProviderTests.cs`

**Interfaces:**
- Consumes: `LhmHardwareMapper`, `ReadingValidator`, `IClock`, `ISensorProvider`.
- Produces:
```csharp
public interface ILhmComputer : IDisposable { void Open(); void Close(); IReadOnlyList<IHardware> Hardware { get; } }
public sealed class LibreHardwareMonitorProvider : ISensorProvider
{
    public LibreHardwareMonitorProvider(ILhmComputer computer, Func<bool> isPawnIoInstalled, Func<bool> isElevated, Func<IHardware, string?> storageSerialResolver, IClock clock, ILogger<LibreHardwareMonitorProvider> logger);
    public static LibreHardwareMonitorProvider CreateDefault(IClock clock, ILoggerFactory loggerFactory);
    public const string ReasonPawnIoMissing = "Provider.PawnIoMissing", ReasonNotElevated = "Provider.NotElevated", ReasonOpenFailed = "Provider.OpenFailed", ReasonNoHardware = "Provider.NoHardware";
}
```

- [ ] **Step 1: Write the fake computer and failing tests**

```csharp
// tests/Mazesta.Hardware.Tests/Fakes/FakeLhmComputer.cs
using LibreHardwareMonitor.Hardware; using Mazesta.Hardware.Lhm;
namespace Mazesta.Hardware.Tests.Fakes;
public sealed class FakeLhmComputer : ILhmComputer
{
    public List<IHardware> Roots { get; } = [];
    public Exception? ThrowOnOpen; public bool Opened, Closed, Disposed;
    public IReadOnlyList<IHardware> Hardware => Roots;
    public void Open() { if (ThrowOnOpen is not null) throw ThrowOnOpen; Opened = true; }
    public void Close() => Closed = true;
    public void Dispose() => Disposed = true;
}
// tests/Mazesta.Hardware.Tests/LibreHardwareMonitorProviderTests.cs
using LibreHardwareMonitor.Hardware; using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Mazesta.Hardware.Lhm; using Mazesta.Hardware.Tests.Fakes; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Hardware.Tests;
public class LibreHardwareMonitorProviderTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; set; } = now; }
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
    private static (LibreHardwareMonitorProvider p, FakeLhmComputer c, FixedClock clock) Build(bool pawn = true, bool elevated = true, string? serial = "SER1")
    {
        var c = new FakeLhmComputer(); var clock = new FixedClock(T0);
        var p = new LibreHardwareMonitorProvider(c, () => pawn, () => elevated, _ => serial, clock, NullLogger<LibreHardwareMonitorProvider>.Instance);
        return (p, c, clock);
    }
    private static FakeHardware Gpu(float? temp = 45) { var g = new FakeHardware(HardwareType.GpuNvidia, "/nvidiagpu/0", "RTX"); g.Add("GPU Core", SensorType.Temperature, 0, temp); return g; }
    private static FakeHardware Disk() { var d = new FakeHardware(HardwareType.Storage, "/nvme/0", "SSD"); d.Add("Temperature", SensorType.Temperature, 0, 38); return d; }

    [Fact] public void Start_ready_when_open_succeeds_and_driver_present()
    {
        var (p, c, _) = Build(); c.Roots.Add(Gpu()); p.Start();
        Assert.True(c.Opened); Assert.Equal(ProviderState.Ready, p.Status.State); Assert.Equal(1, p.Status.SensorCount); Assert.Single(p.Hardware);
    }
    [Fact] public void Start_degraded_when_pawnio_missing()
    {
        var (p, c, _) = Build(pawn: false); c.Roots.Add(Gpu()); p.Start();
        Assert.Equal((ProviderState.Degraded, LibreHardwareMonitorProvider.ReasonPawnIoMissing), (p.Status.State, p.Status.ReasonKey));
    }
    [Fact] public void Start_degraded_when_not_elevated_takes_precedence()
    {
        var (p, c, _) = Build(pawn: false, elevated: false); c.Roots.Add(Gpu()); p.Start();
        Assert.Equal(LibreHardwareMonitorProvider.ReasonNotElevated, p.Status.ReasonKey);
    }
    [Fact] public void Start_failed_when_open_throws_and_never_propagates()
    {
        var (p, c, _) = Build(); c.ThrowOnOpen = new InvalidOperationException("boom"); p.Start();
        Assert.Equal((ProviderState.Failed, LibreHardwareMonitorProvider.ReasonOpenFailed), (p.Status.State, p.Status.ReasonKey)); Assert.Contains("boom", p.Status.Detail);
        Assert.Empty(p.Hardware); Assert.Equal(PollResult.Empty.Readings.Count, p.Poll(new PollRequest(T0, new HashSet<HardwareId>())).Readings.Count);
    }
    [Fact] public void Poll_updates_only_requested_nodes_but_emits_all_readings()
    {
        var (p, c, clock) = Build(); var gpu = Gpu(); var disk = Disk(); c.Roots.AddRange([gpu, disk]); p.Start();
        var all = p.Hardware.Select(h => h.Id).ToHashSet();
        var r1 = p.Poll(new PollRequest(clock.UtcNow, all));
        Assert.Equal((1, 1), (gpu.UpdateCalls, disk.UpdateCalls)); Assert.Equal(2, r1.Readings.Count);
        clock.UtcNow = T0.AddSeconds(2);
        var r2 = p.Poll(new PollRequest(clock.UtcNow, new HashSet<HardwareId> { p.Hardware[0].Id }));
        Assert.Equal((2, 1), (gpu.UpdateCalls, disk.UpdateCalls)); Assert.Equal(2, r2.Readings.Count);
        var diskReading = r2.Readings.Single(x => x.Id.Hardware.Value == "storage/SER1");
        Assert.Equal(T0, diskReading.Timestamp);                       // timestamp of the last actual read
        Assert.Equal(T0, r2.NodeStatus[diskReading.Id.Hardware].LastSuccessfulUpdate);
    }
    [Fact] public void Failing_node_is_isolated_and_marked_stale()
    {
        var (p, c, clock) = Build(); var gpu = Gpu(); var disk = Disk(); disk.ThrowOnUpdate = new IOException("smart failed"); c.Roots.AddRange([gpu, disk]); p.Start();
        var r = p.Poll(new PollRequest(clock.UtcNow, p.Hardware.Select(h => h.Id).ToHashSet()));
        var g = r.Readings.Single(x => x.Id.Hardware.Value.StartsWith("gpu/")); var d = r.Readings.Single(x => x.Id.Hardware.Value.StartsWith("storage/"));
        Assert.Equal(DataQuality.Ok, g.Quality); Assert.Equal(DataQuality.Stale, d.Quality);
        var st = r.NodeStatus[d.Id.Hardware]; Assert.False(st.IsOk); Assert.Contains("smart failed", st.FailureReason); Assert.Null(st.LastSuccessfulUpdate);
    }
    [Fact] public void Null_value_is_missing_and_zero_temperature_is_invalid()
    {
        var (p, c, clock) = Build(); var gpu = Gpu(null); gpu.Add("GPU Hot Spot", SensorType.Temperature, 1, 0); c.Roots.Add(gpu); p.Start();
        var r = p.Poll(new PollRequest(clock.UtcNow, p.Hardware.Select(h => h.Id).ToHashSet()));
        Assert.Equal(DataQuality.Missing, r.Readings[0].Quality); Assert.Equal(DataQuality.Invalid, r.Readings[1].Quality);
    }
    [Fact] public void Status_change_raises_event_and_dispose_closes()
    {
        var (p, c, _) = Build(); c.Roots.Add(Gpu()); var seen = new List<ProviderState>(); p.StatusChanged += s => seen.Add(s.State); p.Start(); p.Dispose();
        Assert.Equal([ProviderState.Starting, ProviderState.Ready], seen); Assert.True(c.Closed && c.Disposed);
    }
}
```

- [ ] **Step 2: Run tests** → Expected: compile failure.

- [ ] **Step 3: Implement**

```csharp
// src/Mazesta.Hardware/Lhm/ILhmComputer.cs
using LibreHardwareMonitor.Hardware;
namespace Mazesta.Hardware.Lhm;
public interface ILhmComputer : IDisposable { void Open(); void Close(); IReadOnlyList<IHardware> Hardware { get; } }
// src/Mazesta.Hardware/Lhm/LhmComputerAdapter.cs
using LibreHardwareMonitor.Hardware;
namespace Mazesta.Hardware.Lhm;
internal sealed class LhmComputerAdapter : ILhmComputer
{
    private readonly Computer _computer = new()
    { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true, IsStorageEnabled = true, IsNetworkEnabled = true,
      IsControllerEnabled = false, IsPsuEnabled = false, IsBatteryEnabled = false, IsPowerMonitorEnabled = false };
    public void Open() => _computer.Open();
    public void Close() => _computer.Close();
    public IReadOnlyList<IHardware> Hardware => _computer.Hardware.ToList();
    public void Dispose() => _computer.Close();
}
// src/Mazesta.Hardware/Lhm/LibreHardwareMonitorProvider.cs
using System.Security.Principal; using LibreHardwareMonitor.Hardware; using LibreHardwareMonitor.Hardware.Storage; using LibreHardwareMonitor.PawnIo;
using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Microsoft.Extensions.Logging;
namespace Mazesta.Hardware.Lhm;
public sealed class LibreHardwareMonitorProvider : ISensorProvider
{
    public const string ReasonPawnIoMissing = "Provider.PawnIoMissing", ReasonNotElevated = "Provider.NotElevated", ReasonOpenFailed = "Provider.OpenFailed", ReasonNoHardware = "Provider.NoHardware";
    private sealed class NodeState(MappedNode mapped) { public MappedNode Mapped = mapped; public DateTimeOffset? LastOk; public string? Failure; public DateTimeOffset? FailingSince; public int ConsecutiveFailures; }
    private readonly ILhmComputer _computer; private readonly Func<bool> _pawnIo, _elevated; private readonly IClock _clock; private readonly ILogger _log;
    private readonly LhmHardwareMapper _mapper; private readonly List<NodeState> _nodes = []; private readonly Dictionary<HardwareId, NodeState> _byId = [];
    private ProviderStatus _status = ProviderStatus.NotStarted;
    public string Name => "LibreHardwareMonitor";
    public ProviderStatus Status { get => _status; private set { _status = value; StatusChanged?.Invoke(value); } }
    public event Action<ProviderStatus>? StatusChanged;
    public IReadOnlyList<HardwareNode> Hardware { get; private set; } = [];

    public LibreHardwareMonitorProvider(ILhmComputer computer, Func<bool> isPawnIoInstalled, Func<bool> isElevated, Func<IHardware, string?> storageSerialResolver, IClock clock, ILogger<LibreHardwareMonitorProvider> logger)
    { _computer = computer; _pawnIo = isPawnIoInstalled; _elevated = isElevated; _clock = clock; _log = logger; _mapper = new LhmHardwareMapper(storageSerialResolver); }

    public static LibreHardwareMonitorProvider CreateDefault(IClock clock, ILoggerFactory loggerFactory) => new(
        new LhmComputerAdapter(), () => PawnIo.IsInstalled, IsProcessElevated,
        hw => (hw as StorageDevice)?.Storage?.SerialNumber, clock, loggerFactory.CreateLogger<LibreHardwareMonitorProvider>());
    private static bool IsProcessElevated() { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }

    public void Start()
    {
        Status = ProviderStatus.Starting;
        try { _computer.Open(); }
        catch (Exception ex) { _log.LogError(ex, "LHM open failed"); Status = ProviderStatus.Failed(ReasonOpenFailed, ex.Message); return; }
        try
        {
            foreach (var m in _mapper.Map(_computer.Hardware)) { var s = new NodeState(m); _nodes.Add(s); _byId[m.Node.Id] = s; }
            Hardware = _nodes.Select(n => n.Mapped.Node).ToList();
        }
        catch (Exception ex) { _log.LogError(ex, "LHM enumeration failed"); Status = ProviderStatus.Failed(ReasonOpenFailed, ex.Message); return; }
        int count = _nodes.Sum(n => n.Mapped.Sensors.Count);
        if (!_elevated()) Status = ProviderStatus.Degraded(ReasonNotElevated, "Process is not elevated; CPU and motherboard sensors are unavailable.", count);
        else if (!_pawnIo()) Status = ProviderStatus.Degraded(ReasonPawnIoMissing, "PawnIO driver is not installed; CPU MSR sensors are unavailable.", count);
        else if (count == 0) Status = ProviderStatus.Degraded(ReasonNoHardware, "LHM returned no sensors.", 0);
        else Status = ProviderStatus.Ready(count);
    }

    public PollResult Poll(PollRequest request)
    {
        if (_nodes.Count == 0) return PollResult.Empty;
        foreach (var n in _nodes)
        {
            if (!request.NodesToUpdate.Contains(n.Mapped.Node.Id)) continue;
            try { n.Mapped.Source.Update(); n.LastOk = request.Now; n.Failure = null; n.FailingSince = null; n.ConsecutiveFailures = 0; }
            catch (Exception ex)
            {
                n.Failure = ex.Message; n.FailingSince ??= request.Now; n.ConsecutiveFailures++;
                if (n.ConsecutiveFailures == 3) _log.LogWarning(ex, "Node {Node} failed 3 consecutive updates", n.Mapped.Node.Id);
            }
        }
        var readings = new List<SensorReading>(_nodes.Sum(n => n.Mapped.Sensors.Count));
        var status = new Dictionary<HardwareId, NodeStatus>(_nodes.Count);
        foreach (var n in _nodes)
        {
            bool failed = n.Failure is not null;
            status[n.Mapped.Node.Id] = failed ? NodeStatus.Failed(n.Failure!, n.FailingSince!.Value, n.LastOk) : n.LastOk is null ? NodeStatus.NeverUpdated : NodeStatus.Healthy(n.LastOk.Value);
            foreach (var s in n.Mapped.Sensors)
            {
                double? value = s.Source.Value;
                var quality = failed || n.LastOk is null ? DataQuality.Stale : ReadingValidator.Validate(s.Definition.Kind, value);
                readings.Add(new SensorReading(s.Definition.Id, value, n.LastOk ?? request.Now, quality, Name));
            }
        }
        return new PollResult(readings, status);
    }
    public void Dispose() { try { _computer.Close(); } catch (Exception ex) { _log.LogWarning(ex, "LHM close failed"); } _computer.Dispose(); }
}
```
Note: if `StorageDevice.Storage.SerialNumber` does not compile against 0.9.6 / DiskInfoToolkit 1.1.2, run `strings` on `DiskInfoToolkit.dll` in `~/.nuget/packages/diskinfotoolkit/1.1.2/lib/net10.0/` — the property is `SerialNumber` on `DiskInfoToolkit.Storage` (verified 2026-09-12); adjust only the lambda in `CreateDefault`.

- [ ] **Step 4: Run tests** → Expected: all provider tests pass.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(hardware): LibreHardwareMonitor provider with cadence, isolation and driver status"`

---

### Task 8: WMI inventory provider

**Files:**
- Create: `src/Mazesta.Hardware/Wmi/IWmiQuery.cs`, `src/Mazesta.Hardware/Wmi/WmiQuery.cs`, `src/Mazesta.Hardware/Wmi/WmiInventoryParser.cs`, `src/Mazesta.Hardware/Wmi/WmiInventoryProvider.cs`
- Test: `tests/Mazesta.Hardware.Tests/WmiInventoryParserTests.cs`

**Interfaces:**
- Produces:
```csharp
public interface IWmiQuery { IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql); }   // throws on failure
internal static class WmiInventoryParser { CpuInfo? Cpu(rows); IReadOnlyList<GpuInfo> Gpus(rows); IReadOnlyList<MemoryModuleInfo> Memory(rows); MotherboardInfo? Board(rows); BiosInfo? Bios(rows); IReadOnlyList<StorageDeviceInfo> Disks(rows); IReadOnlyList<NetworkAdapterInfo> Adapters(adapterRows, configRows); OsInfo? Os(rows); long? TotalMemory(rows); }
public sealed class WmiInventoryProvider(IWmiQuery query, ILogger<WmiInventoryProvider> logger) : IInventoryProvider
```

- [ ] **Step 1: Write the failing tests**

```csharp
using Mazesta.Core.Hardware; using Mazesta.Hardware.Wmi;
namespace Mazesta.Hardware.Tests;
public class WmiInventoryParserTests
{
    private static IReadOnlyDictionary<string, object?> Row(params (string k, object? v)[] kv) => kv.ToDictionary(x => x.k, x => x.v);
    [Fact] public void Cpu_vendor_from_manufacturer_field()
    {
        var cpu = WmiInventoryParser.Cpu([Row(("Name", "Intel(R) Core(TM) i9-14900K"), ("Manufacturer", "GenuineIntel"), ("NumberOfCores", 24u), ("NumberOfLogicalProcessors", 32u), ("MaxClockSpeed", 3200u), ("SocketDesignation", "LGA1700"))]);
        Assert.NotNull(cpu); Assert.Equal((HardwareVendor.Intel, 24, 32), (cpu!.Vendor, cpu.PhysicalCores, cpu.LogicalProcessors));
        Assert.Equal(HardwareVendor.Amd, WmiInventoryParser.Cpu([Row(("Manufacturer", "AuthenticAMD"))])!.Vendor);
    }
    [Fact] public void Disk_media_and_bus_codes_are_named()
    {
        var d = WmiInventoryParser.Disks([Row(("FriendlyName", "Samsung SSD 990 PRO 2TB"), ("SerialNumber", " S7KX "), ("MediaType", (ushort)4), ("BusType", (ushort)17), ("Size", 2000398934016ul), ("FirmwareVersion", "4B2QJXD7"), ("HealthStatus", (ushort)0))]);
        Assert.Equal(("S7KX", "SSD", "NVMe", "Healthy"), (d[0].SerialNumber, d[0].MediaType, d[0].BusType, d[0].HealthStatus));
    }
    [Fact] public void Adapters_join_ip_configuration_by_index()
    {
        var a = WmiInventoryParser.Adapters(
            [Row(("Name", "Intel Wi-Fi"), ("MACAddress", "AA:BB"), ("Speed", 866000000ul), ("NetEnabled", true), ("InterfaceIndex", 12u))],
            [Row(("InterfaceIndex", 12u), ("IPAddress", new[] { "192.168.1.5", "fe80::1" }))]);
        Assert.Equal(["192.168.1.5", "fe80::1"], a[0].IpAddresses); Assert.True(a[0].IsUp); Assert.Equal(866000000L, a[0].LinkSpeedBps);
    }
    [Fact] public void Missing_fields_become_null_not_defaults()
    {
        var m = WmiInventoryParser.Memory([Row(("DeviceLocator", "DIMM_A1"))]);
        Assert.Null(m[0].CapacityBytes); Assert.Null(m[0].ConfiguredSpeedMts); Assert.Equal("DIMM_A1", m[0].Slot);
    }
}
```

- [ ] **Step 2: Run tests** → Expected: compile failure.

- [ ] **Step 3: Implement**

```csharp
// src/Mazesta.Hardware/Wmi/IWmiQuery.cs
namespace Mazesta.Hardware.Wmi;
public interface IWmiQuery { IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql); }
// src/Mazesta.Hardware/Wmi/WmiQuery.cs
using System.Management;
namespace Mazesta.Hardware.Wmi;
public sealed class WmiQuery : IWmiQuery
{
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Query(string scope, string wql)
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        using var results = searcher.Get();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        foreach (ManagementBaseObject o in results)
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in o.Properties) row[p.Name] = p.Value;
            rows.Add(row); o.Dispose();
        }
        return rows;
    }
}
// src/Mazesta.Hardware/Wmi/WmiInventoryParser.cs
using System.Globalization; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory;
namespace Mazesta.Hardware.Wmi;
internal static class WmiInventoryParser
{
    private static string? S(IReadOnlyDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v is not null ? Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim() is { Length: > 0 } s ? s : null : null;
    private static long? L(IReadOnlyDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v is not null ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : null;
    private static int? I(IReadOnlyDictionary<string, object?> r, string k) => L(r, k) is { } l ? checked((int)l) : null;
    private static bool? B(IReadOnlyDictionary<string, object?> r, string k) => r.TryGetValue(k, out var v) && v is bool b ? b : null;

    public static CpuInfo? Cpu(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        if (rows.Count == 0) return null; var r = rows[0];
        var vendor = S(r, "Manufacturer") switch { "GenuineIntel" => HardwareVendor.Intel, "AuthenticAMD" => HardwareVendor.Amd, _ => HardwareVendor.Unknown };
        return new CpuInfo(S(r, "Name"), vendor, I(r, "NumberOfCores"), I(r, "NumberOfLogicalProcessors"), I(r, "MaxClockSpeed"), S(r, "SocketDesignation"));
    }
    public static IReadOnlyList<GpuInfo> Gpus(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        => rows.Select(r => new GpuInfo(S(r, "Name"), S(r, "DriverVersion"), L(r, "AdapterRAM"), S(r, "PNPDeviceID"))).ToList();
    public static IReadOnlyList<MemoryModuleInfo> Memory(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        => rows.Select(r => new MemoryModuleInfo(S(r, "DeviceLocator"), L(r, "Capacity"), S(r, "Manufacturer"), S(r, "PartNumber"), I(r, "ConfiguredClockSpeed"), I(r, "Speed"))).ToList();
    public static long? TotalMemory(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) => rows.Count == 0 ? null : L(rows[0], "TotalPhysicalMemory");
    public static MotherboardInfo? Board(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        => rows.Count == 0 ? null : new MotherboardInfo(S(rows[0], "Manufacturer"), S(rows[0], "Product"), S(rows[0], "Version"), S(rows[0], "SerialNumber"));
    public static BiosInfo? Bios(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        if (rows.Count == 0) return null; var r = rows[0];
        DateTime? date = S(r, "ReleaseDate") is { Length: >= 8 } d && DateTime.TryParseExact(d[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;
        string? smbios = S(r, "SMBIOSMajorVersion") is { } maj && S(r, "SMBIOSMinorVersion") is { } min ? $"{maj}.{min}" : null;
        return new BiosInfo(S(r, "Manufacturer"), S(r, "SMBIOSBIOSVersion"), date, smbios);
    }
    public static IReadOnlyList<StorageDeviceInfo> Disks(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows) => rows.Select(r => new StorageDeviceInfo(
        S(r, "FriendlyName"), S(r, "SerialNumber"),
        I(r, "MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", null => null, _ => "Unspecified" },
        I(r, "BusType") switch { 1 => "SCSI", 3 => "ATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 11 => "SATA", 17 => "NVMe", null => null, var b => $"Bus {b}" },
        L(r, "Size"), S(r, "FirmwareVersion"),
        I(r, "HealthStatus") switch { 0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", null => null, _ => "Unknown" })).ToList();
    public static IReadOnlyList<NetworkAdapterInfo> Adapters(IReadOnlyList<IReadOnlyDictionary<string, object?>> adapters, IReadOnlyList<IReadOnlyDictionary<string, object?>> configs)
    {
        var ips = configs.Where(c => L(c, "InterfaceIndex") is not null).ToDictionary(c => L(c, "InterfaceIndex")!.Value, c => c.TryGetValue("IPAddress", out var v) && v is string[] a ? a : []);
        return adapters.Select(a => new NetworkAdapterInfo(S(a, "Name"), S(a, "MACAddress"), L(a, "InterfaceIndex") is { } ix && ips.TryGetValue(ix, out var list) ? list : [], L(a, "Speed"), B(a, "NetEnabled") == true)).ToList();
    }
    public static OsInfo? Os(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
        => rows.Count == 0 ? null : new OsInfo(S(rows[0], "Caption"), S(rows[0], "Version"), S(rows[0], "BuildNumber"), S(rows[0], "OSArchitecture"));
}
// src/Mazesta.Hardware/Wmi/WmiInventoryProvider.cs
using Mazesta.Core.Inventory; using Microsoft.Extensions.Logging;
namespace Mazesta.Hardware.Wmi;
public sealed class WmiInventoryProvider(IWmiQuery query, ILogger<WmiInventoryProvider> logger) : IInventoryProvider
{
    private const string Cimv2 = @"root\cimv2", Storage = @"root\Microsoft\Windows\Storage";
    public Task<HardwareInventory> ReadAsync(CancellationToken ct) => Task.Run(() =>
    {
        var errors = new List<string>();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> Q(string scope, string wql, string section)
        {
            ct.ThrowIfCancellationRequested();
            try { return query.Query(scope, wql); }
            catch (Exception ex) { logger.LogWarning(ex, "WMI {Section} failed", section); errors.Add($"{section}: {ex.Message}"); return []; }
        }
        return new HardwareInventory(
            WmiInventoryParser.Cpu(Q(Cimv2, "SELECT Name,Manufacturer,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,SocketDesignation FROM Win32_Processor", "cpu")),
            WmiInventoryParser.Gpus(Q(Cimv2, "SELECT Name,DriverVersion,AdapterRAM,PNPDeviceID FROM Win32_VideoController", "gpu")),
            WmiInventoryParser.Memory(Q(Cimv2, "SELECT DeviceLocator,Capacity,Manufacturer,PartNumber,ConfiguredClockSpeed,Speed FROM Win32_PhysicalMemory", "memory")),
            WmiInventoryParser.TotalMemory(Q(Cimv2, "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", "computer")),
            WmiInventoryParser.Board(Q(Cimv2, "SELECT Manufacturer,Product,Version,SerialNumber FROM Win32_BaseBoard", "board")),
            WmiInventoryParser.Bios(Q(Cimv2, "SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate,SMBIOSMajorVersion,SMBIOSMinorVersion FROM Win32_BIOS", "bios")),
            WmiInventoryParser.Disks(Q(Storage, "SELECT FriendlyName,SerialNumber,MediaType,BusType,Size,FirmwareVersion,HealthStatus FROM MSFT_PhysicalDisk", "disks")),
            WmiInventoryParser.Adapters(Q(Cimv2, "SELECT Name,MACAddress,Speed,NetEnabled,InterfaceIndex FROM Win32_NetworkAdapter WHERE PhysicalAdapter=TRUE", "adapters"),
                                        Q(Cimv2, "SELECT InterfaceIndex,IPAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=TRUE", "ipconfig")),
            WmiInventoryParser.Os(Q(Cimv2, "SELECT Caption,Version,BuildNumber,OSArchitecture FROM Win32_OperatingSystem", "os")),
            errors);
    }, ct);
}
```

- [ ] **Step 4: Run tests** → Expected: parser tests pass; whole solution builds with 0 warnings (add `[SupportedOSPlatform("windows")]` on `WmiQuery` if CA1416 fires).
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(hardware): WMI inventory provider with isolated queries"`

---

### Task 9: SensorStatistics and StaleDetector

**Files:**
- Create: `src/Mazesta.Monitoring/MonitoringOptions.cs`, `src/Mazesta.Monitoring/SensorStatistics.cs`, `src/Mazesta.Monitoring/StaleDetector.cs`
- Test: `tests/Mazesta.Monitoring.Tests/SensorStatisticsTests.cs`, `tests/Mazesta.Monitoring.Tests/StaleDetectorTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed class MonitoringOptions { public TimeSpan FastInterval { get; set; } = TimeSpan.FromSeconds(2); public TimeSpan StorageInterval { get; set; } = TimeSpan.FromMinutes(15); public static readonly int[] AllowedFastSeconds = [1, 2, 5, 30]; public TimeSpan CadenceFor(HardwareKind kind) => kind == HardwareKind.Storage ? StorageInterval : FastInterval; }
public readonly record struct SensorStats(double? Min, double? Max, double? Average, long Count, DateTimeOffset Since);
public sealed class SensorStatistics { public SensorStatistics(DateTimeOffset since); public void Apply(SensorSnapshot s); public SensorStats Get(SensorId id); public void ResetAll(DateTimeOffset now); public void Reset(HardwareId hardware, DateTimeOffset now); }
public static class StaleDetector { public static DataQuality Apply(SensorReading r, NodeStatus? status, TimeSpan cadence, DateTimeOffset now); public const int StaleMultiplier = 3; }
```

- [ ] **Step 1: Write the failing tests**

```csharp
using Mazesta.Core.Hardware; using Mazesta.Monitoring;
namespace Mazesta.Monitoring.Tests;
public class SensorStatisticsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly SensorId A = new("cpu/intelcpu-0#temperature/0"), B = new("gpu/nvidiagpu-0#temperature/0");
    private static SensorSnapshot Snap(long seq, params (SensorId id, double? v, DataQuality q)[] r)
        => new(seq, T0.AddSeconds(seq * 2), r.Select(x => new SensorReading(x.id, x.v, T0, x.q, "t")).ToList(), new Dictionary<HardwareId, NodeStatus>());
    [Fact] public void Tracks_min_max_average_of_ok_readings_only()
    {
        var s = new SensorStatistics(T0);
        s.Apply(Snap(1, (A, 40, DataQuality.Ok))); s.Apply(Snap(2, (A, 60, DataQuality.Ok))); s.Apply(Snap(3, (A, 999, DataQuality.Invalid))); s.Apply(Snap(4, (A, null, DataQuality.Missing))); s.Apply(Snap(5, (A, 50, DataQuality.Stale)));
        var st = s.Get(A); Assert.Equal((40.0, 60.0, 50.0, 2L), (st.Min, st.Max, st.Average, st.Count));
    }
    [Fact] public void Unknown_sensor_returns_empty_stats() { var st = new SensorStatistics(T0).Get(A); Assert.Null(st.Min); Assert.Equal(0, st.Count); }
    [Fact] public void Reset_by_hardware_only_clears_that_hardware()
    {
        var s = new SensorStatistics(T0); s.Apply(Snap(1, (A, 40, DataQuality.Ok), (B, 70, DataQuality.Ok)));
        s.Reset(new HardwareId("cpu/intelcpu-0"), T0.AddMinutes(1));
        Assert.Equal(0, s.Get(A).Count); Assert.Equal(1, s.Get(B).Count); Assert.Equal(T0.AddMinutes(1), s.Get(A).Since);
    }
}
public class StaleDetectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static SensorReading Ok(DateTimeOffset ts) => new(new SensorId("x#y"), 1, ts, DataQuality.Ok, "t");
    [Fact] public void Fresh_reading_stays_ok() => Assert.Equal(DataQuality.Ok, StaleDetector.Apply(Ok(T0), NodeStatus.Healthy(T0), TimeSpan.FromSeconds(2), T0.AddSeconds(5)));
    [Fact] public void Older_than_three_cadences_is_stale() => Assert.Equal(DataQuality.Stale, StaleDetector.Apply(Ok(T0), NodeStatus.Healthy(T0), TimeSpan.FromSeconds(2), T0.AddSeconds(7)));
    [Fact] public void Slow_cadence_tolerates_long_gaps() => Assert.Equal(DataQuality.Ok, StaleDetector.Apply(Ok(T0), NodeStatus.Healthy(T0), TimeSpan.FromMinutes(15), T0.AddMinutes(40)));
    [Fact] public void Failed_node_is_stale() => Assert.Equal(DataQuality.Stale, StaleDetector.Apply(Ok(T0), NodeStatus.Failed("x", T0, T0), TimeSpan.FromSeconds(2), T0));
    [Fact] public void Non_ok_quality_is_preserved() { var r = Ok(T0) with { Quality = DataQuality.Invalid }; Assert.Equal(DataQuality.Invalid, StaleDetector.Apply(r, NodeStatus.Healthy(T0), TimeSpan.FromSeconds(2), T0.AddMinutes(1))); }
    [Fact] public void Missing_status_is_stale() => Assert.Equal(DataQuality.Stale, StaleDetector.Apply(Ok(T0), null, TimeSpan.FromSeconds(2), T0));
}
```

- [ ] **Step 2: Run** `"$DOTNET" test tests/Mazesta.Monitoring.Tests -c Debug` → Expected: compile failure.

- [ ] **Step 3: Implement**

```csharp
// src/Mazesta.Monitoring/MonitoringOptions.cs
using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring;
public sealed class MonitoringOptions
{
    public static readonly int[] AllowedFastSeconds = [1, 2, 5, 30];
    public TimeSpan FastInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan StorageInterval { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan CadenceFor(HardwareKind kind) => kind == HardwareKind.Storage ? StorageInterval : FastInterval;
}
// src/Mazesta.Monitoring/SensorStatistics.cs
using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring;
public readonly record struct SensorStats(double? Min, double? Max, double? Average, long Count, DateTimeOffset Since);
public sealed class SensorStatistics(DateTimeOffset since)
{
    private sealed class Acc(DateTimeOffset since) { public double Min = double.MaxValue, Max = double.MinValue, Sum; public long Count; public DateTimeOffset Since = since; }
    private readonly Dictionary<SensorId, Acc> _acc = []; private readonly object _lock = new(); private DateTimeOffset _since = since;
    public void Apply(SensorSnapshot s)
    {
        lock (_lock)
            foreach (var r in s.Readings)
            {
                if (r.Quality != DataQuality.Ok || r.Value is null) continue;
                if (!_acc.TryGetValue(r.Id, out var a)) _acc[r.Id] = a = new Acc(_since);
                double v = r.Value.Value; a.Min = Math.Min(a.Min, v); a.Max = Math.Max(a.Max, v); a.Sum += v; a.Count++;
            }
    }
    public SensorStats Get(SensorId id)
    {
        lock (_lock) return _acc.TryGetValue(id, out var a) && a.Count > 0 ? new SensorStats(a.Min, a.Max, a.Sum / a.Count, a.Count, a.Since) : new SensorStats(null, null, null, 0, _acc.TryGetValue(id, out var e) ? e.Since : _since);
    }
    public void ResetAll(DateTimeOffset now) { lock (_lock) { _acc.Clear(); _since = now; } }
    public void Reset(HardwareId hardware, DateTimeOffset now)
    { lock (_lock) foreach (var id in _acc.Keys.Where(k => k.Hardware == hardware).ToList()) _acc[id] = new Acc(now); }
}
// src/Mazesta.Monitoring/StaleDetector.cs
using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring;
public static class StaleDetector
{
    public const int StaleMultiplier = 3;
    public static DataQuality Apply(SensorReading r, NodeStatus? status, TimeSpan cadence, DateTimeOffset now)
    {
        if (r.Quality != DataQuality.Ok) return r.Quality;
        if (status is null || !status.IsOk || status.LastSuccessfulUpdate is null) return DataQuality.Stale;
        return now - status.LastSuccessfulUpdate.Value > cadence * StaleMultiplier ? DataQuality.Stale : DataQuality.Ok;
    }
}
```

- [ ] **Step 4: Run tests** → Expected: pass.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(monitoring): running statistics and stale detection"`

---

### Task 10: HistoryStore (two-tier bounded history)

**Files:**
- Create: `src/Mazesta.Monitoring/HistoryStore.cs`
- Test: `tests/Mazesta.Monitoring.Tests/HistoryStoreTests.cs`

**Interfaces:**
- Produces:
```csharp
public readonly record struct RawSeries(int[] Seconds, float[] Values);                      // chronological; NaN = gap
public readonly record struct MinuteSeries(int[] Minute, float[] Min, float[] Max, float[] Avg); // NaN = no Ok samples in that minute
public sealed class HistoryStore
{
    public HistoryStore(DateTimeOffset epoch, int rawCapacity = 900, int minuteCapacity = 2880);
    public DateTimeOffset Epoch { get; } public int RawCapacity { get; } public int MinuteCapacity { get; }
    public void Append(SensorSnapshot snapshot);          // polling thread
    public RawSeries GetRaw(SensorId id);                  // any thread; copies
    public MinuteSeries GetMinutes(SensorId id);
    public long EstimatedBytes { get; }
    public int SecondsSinceEpoch(DateTimeOffset t);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
using Mazesta.Core.Hardware; using Mazesta.Monitoring;
namespace Mazesta.Monitoring.Tests;
public class HistoryStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly SensorId A = new("cpu/x#temperature/0");
    private static SensorSnapshot Snap(long seq, int seconds, double? v, DataQuality q = DataQuality.Ok)
        => new(seq, T0.AddSeconds(seconds), [new SensorReading(A, v, T0.AddSeconds(seconds), q, "t")], new Dictionary<HardwareId, NodeStatus>());
    [Fact] public void Raw_ring_keeps_only_capacity_newest()
    {
        var h = new HistoryStore(T0, rawCapacity: 3, minuteCapacity: 10);
        for (int i = 0; i < 5; i++) h.Append(Snap(i, i * 2, 40 + i));
        var raw = h.GetRaw(A); Assert.Equal([4, 6, 8], raw.Seconds); Assert.Equal([42f, 43f, 44f], raw.Values);
    }
    [Fact] public void Non_ok_readings_store_nan_gap()
    {
        var h = new HistoryStore(T0); h.Append(Snap(0, 0, 40)); h.Append(Snap(1, 2, null, DataQuality.Missing)); h.Append(Snap(2, 4, 999, DataQuality.Invalid)); h.Append(Snap(3, 6, 41, DataQuality.Stale));
        var raw = h.GetRaw(A); Assert.Equal(4, raw.Values.Length); Assert.True(float.IsNaN(raw.Values[1]) && float.IsNaN(raw.Values[2]) && float.IsNaN(raw.Values[3]));
    }
    [Fact] public void Minute_tier_aggregates_ok_samples_per_minute()
    {
        var h = new HistoryStore(T0);
        h.Append(Snap(0, 10, 40)); h.Append(Snap(1, 30, 60)); h.Append(Snap(2, 50, null, DataQuality.Missing)); h.Append(Snap(3, 70, 100));
        var m = h.GetMinutes(A); Assert.Equal([0, 1], m.Minute); Assert.Equal((40f, 60f, 50f), (m.Min[0], m.Max[0], m.Avg[0])); Assert.Equal(100f, m.Avg[1]);
    }
    [Fact] public void Minute_with_no_ok_samples_is_nan_gap()
    {
        var h = new HistoryStore(T0); h.Append(Snap(0, 5, null, DataQuality.Missing));
        var m = h.GetMinutes(A); Assert.Single(m.Minute); Assert.True(float.IsNaN(m.Avg[0]));
    }
    [Fact] public void Minute_ring_is_bounded()
    {
        var h = new HistoryStore(T0, rawCapacity: 10, minuteCapacity: 2);
        for (int i = 0; i < 5; i++) h.Append(Snap(i, i * 60, i));
        Assert.Equal([3, 4], h.GetMinutes(A).Minute);
    }
    [Fact] public void Byte_budget_for_200_sensors_is_under_13_MB()
    {
        var h = new HistoryStore(T0);
        var readings = Enumerable.Range(0, 200).Select(i => new SensorReading(new SensorId($"n#{i}"), 1, T0, DataQuality.Ok, "t")).ToList();
        h.Append(new SensorSnapshot(0, T0, readings, new Dictionary<HardwareId, NodeStatus>()));
        Assert.InRange(h.EstimatedBytes, 1, 13L * 1024 * 1024);
    }
    [Fact] public void Unknown_sensor_returns_empty_series() => Assert.Empty(new HistoryStore(T0).GetRaw(A).Seconds);
}
```

- [ ] **Step 2: Run tests** → Expected: compile failure.

- [ ] **Step 3: Implement**

```csharp
// src/Mazesta.Monitoring/HistoryStore.cs
using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring;
public readonly record struct RawSeries(int[] Seconds, float[] Values);
public readonly record struct MinuteSeries(int[] Minute, float[] Min, float[] Max, float[] Avg);
public sealed class HistoryStore(DateTimeOffset epoch, int rawCapacity = 900, int minuteCapacity = 2880)
{
    private sealed class Series(int rawCap, int minCap)
    {
        public readonly int[] Sec = new int[rawCap]; public readonly float[] Val = new float[rawCap]; public int RawHead, RawCount;
        public readonly int[] Min = new int[minCap]; public readonly float[] MMin = new float[minCap], MMax = new float[minCap], MSum = new float[minCap]; public readonly ushort[] MCount = new ushort[minCap]; public int MinHead, MinCount;
        public void AddRaw(int sec, float v)
        {
            int idx = (RawHead + RawCount) % Sec.Length;
            if (RawCount == Sec.Length) { RawHead = (RawHead + 1) % Sec.Length; idx = (RawHead + RawCount - 1) % Sec.Length; } else RawCount++;
            Sec[idx] = sec; Val[idx] = v;
        }
        public void AddMinute(int minute, float v)
        {
            int last = MinCount == 0 ? -1 : (MinHead + MinCount - 1) % Min.Length;
            if (last < 0 || Min[last] != minute)
            {
                if (MinCount == Min.Length) { MinHead = (MinHead + 1) % Min.Length; last = (MinHead + MinCount - 1) % Min.Length; } else { last = (MinHead + MinCount) % Min.Length; MinCount++; }
                Min[last] = minute; MMin[last] = float.MaxValue; MMax[last] = float.MinValue; MSum[last] = 0; MCount[last] = 0;
            }
            if (float.IsNaN(v)) return;
            MMin[last] = Math.Min(MMin[last], v); MMax[last] = Math.Max(MMax[last], v); MSum[last] += v; MCount[last]++;
        }
        public const int BytesPerRawSlot = sizeof(int) + sizeof(float), BytesPerMinuteSlot = sizeof(int) + 3 * sizeof(float) + sizeof(ushort);
    }
    private readonly Dictionary<SensorId, Series> _series = []; private readonly object _lock = new();
    public DateTimeOffset Epoch { get; } = epoch; public int RawCapacity { get; } = rawCapacity; public int MinuteCapacity { get; } = minuteCapacity;
    public int SecondsSinceEpoch(DateTimeOffset t) => (int)Math.Round((t - Epoch).TotalSeconds);
    public void Append(SensorSnapshot snapshot)
    {
        int sec = SecondsSinceEpoch(snapshot.Timestamp);
        lock (_lock)
            foreach (var r in snapshot.Readings)
            {
                if (!_series.TryGetValue(r.Id, out var s)) _series[r.Id] = s = new Series(RawCapacity, MinuteCapacity);
                float v = r.Quality == DataQuality.Ok && r.Value is { } d ? (float)d : float.NaN;
                s.AddRaw(sec, v); s.AddMinute(sec / 60, v);
            }
    }
    public RawSeries GetRaw(SensorId id)
    {
        lock (_lock)
        {
            if (!_series.TryGetValue(id, out var s)) return new RawSeries([], []);
            var sec = new int[s.RawCount]; var val = new float[s.RawCount];
            for (int i = 0; i < s.RawCount; i++) { int idx = (s.RawHead + i) % s.Sec.Length; sec[i] = s.Sec[idx]; val[i] = s.Val[idx]; }
            return new RawSeries(sec, val);
        }
    }
    public MinuteSeries GetMinutes(SensorId id)
    {
        lock (_lock)
        {
            if (!_series.TryGetValue(id, out var s)) return new MinuteSeries([], [], [], []);
            int n = s.MinCount; var m = new int[n]; var mn = new float[n]; var mx = new float[n]; var av = new float[n];
            for (int i = 0; i < n; i++)
            {
                int idx = (s.MinHead + i) % s.Min.Length; m[i] = s.Min[idx];
                bool empty = s.MCount[idx] == 0; mn[i] = empty ? float.NaN : s.MMin[idx]; mx[i] = empty ? float.NaN : s.MMax[idx]; av[i] = empty ? float.NaN : s.MSum[idx] / s.MCount[idx];
            }
            return new MinuteSeries(m, mn, mx, av);
        }
    }
    public long EstimatedBytes { get { lock (_lock) return (long)_series.Count * (RawCapacity * Series.BytesPerRawSlot + MinuteCapacity * Series.BytesPerMinuteSlot); } }
}
```

- [ ] **Step 4: Run tests** → Expected: pass (200 sensors × (900×8 + 2880×18) = 11.8 MB).
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(monitoring): bounded two-tier history store with gap preservation"`

---

### Task 11: EventLog and MonitoringFocus

**Files:**
- Create: `src/Mazesta.Monitoring/EventLog.cs`, `src/Mazesta.Monitoring/MonitoringFocus.cs`
- Test: `tests/Mazesta.Monitoring.Tests/EventLogTests.cs`, `tests/Mazesta.Monitoring.Tests/MonitoringFocusTests.cs`

**Interfaces:**
- Produces:
```csharp
public enum EventLevel { Info, Warning, Error }
public readonly record struct EventEntry(DateTimeOffset At, EventLevel Level, string Key, string Detail);
public interface IEventLog { void Log(EventLevel level, string key, string detail); IReadOnlyList<EventEntry> Snapshot(); event Action<EventEntry>? Logged; }
public sealed class BoundedEventLog(IClock clock, ILogger logger, int capacity = 1000) : IEventLog
public readonly record struct FocusRequest(IReadOnlySet<HardwareKind> Kinds, string Reason);
public sealed class MonitoringFocus { public event Action<FocusRequest>? FocusRequested; public void RequestFocus(IReadOnlySet<HardwareKind> kinds, string reason); }
```

- [ ] **Step 1: Write the failing tests**

```csharp
using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Mazesta.Monitoring; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Monitoring.Tests;
public class EventLogTests
{
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero); }
    [Fact] public void Keeps_newest_entries_up_to_capacity()
    {
        var log = new BoundedEventLog(new Clock(), NullLogger.Instance, capacity: 3);
        for (int i = 0; i < 5; i++) log.Log(EventLevel.Info, "k", i.ToString());
        Assert.Equal(["2", "3", "4"], log.Snapshot().Select(e => e.Detail));
    }
    [Fact] public void Raises_logged_event() { var log = new BoundedEventLog(new Clock(), NullLogger.Instance); EventEntry? got = null; log.Logged += e => got = e; log.Log(EventLevel.Warning, "Engine.PollOverrun", "x"); Assert.Equal("Engine.PollOverrun", got!.Value.Key); }
}
public class MonitoringFocusTests
{
    [Fact] public void Request_raises_once_with_kinds()
    {
        var f = new MonitoringFocus(); var got = new List<FocusRequest>(); f.FocusRequested += r => got.Add(r);
        f.RequestFocus(new HashSet<HardwareKind> { HardwareKind.Cpu, HardwareKind.Gpu }, "test:power");
        Assert.Single(got); Assert.Contains(HardwareKind.Gpu, got[0].Kinds); Assert.Equal("test:power", got[0].Reason);
    }
}
```

- [ ] **Step 2: Run tests** → Expected: compile failure.

- [ ] **Step 3: Implement**

```csharp
// src/Mazesta.Monitoring/EventLog.cs
using Mazesta.Core.Time; using Microsoft.Extensions.Logging;
namespace Mazesta.Monitoring;
public enum EventLevel { Info, Warning, Error }
public readonly record struct EventEntry(DateTimeOffset At, EventLevel Level, string Key, string Detail);
public interface IEventLog { void Log(EventLevel level, string key, string detail); IReadOnlyList<EventEntry> Snapshot(); event Action<EventEntry>? Logged; }
public sealed class BoundedEventLog(IClock clock, ILogger logger, int capacity = 1000) : IEventLog
{
    private readonly Queue<EventEntry> _q = new(capacity); private readonly object _lock = new();
    public event Action<EventEntry>? Logged;
    public void Log(EventLevel level, string key, string detail)
    {
        var e = new EventEntry(clock.UtcNow, level, key, detail);
        lock (_lock) { if (_q.Count == capacity) _q.Dequeue(); _q.Enqueue(e); }
        logger.Log(level switch { EventLevel.Error => LogLevel.Error, EventLevel.Warning => LogLevel.Warning, _ => LogLevel.Information }, "{Key}: {Detail}", key, detail);
        Logged?.Invoke(e);
    }
    public IReadOnlyList<EventEntry> Snapshot() { lock (_lock) return _q.ToList(); }
}
// src/Mazesta.Monitoring/MonitoringFocus.cs
using Mazesta.Core.Hardware;
namespace Mazesta.Monitoring;
public readonly record struct FocusRequest(IReadOnlySet<HardwareKind> Kinds, string Reason);
public sealed class MonitoringFocus
{
    public event Action<FocusRequest>? FocusRequested;
    public void RequestFocus(IReadOnlySet<HardwareKind> kinds, string reason) => FocusRequested?.Invoke(new FocusRequest(kinds, reason));
}
```

- [ ] **Step 4: Run tests** → Expected: pass.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(monitoring): bounded event log and focus request API"`

---

### Task 12: PollingEngine

**Files:**
- Create: `src/Mazesta.Monitoring/PollingEngine.cs`
- Create: `tests/Mazesta.Monitoring.Tests/Fakes/FakeClock.cs`, `tests/Mazesta.Monitoring.Tests/Fakes/FakeSensorProvider.cs`
- Test: `tests/Mazesta.Monitoring.Tests/PollingEngineTests.cs`

**Interfaces:**
- Consumes: `ISensorProvider`, `PollRequest`, `PollResult`, `HistoryStore`, `SensorStatistics`, `StaleDetector`, `IEventLog`, `MonitoringOptions`, `IClock`.
- Produces:
```csharp
public enum EngineState { Stopped, Running, Paused, Failed }
public sealed class PollingEngine : IDisposable
{
    public PollingEngine(ISensorProvider provider, IClock clock, MonitoringOptions options, IEventLog events);
    public ISensorProvider Provider { get; } public HistoryStore History { get; } public SensorStatistics Statistics { get; }
    public EngineState State { get; } public TimeSpan FastInterval { get; } public IReadOnlyList<HardwareNode> Hardware => Provider.Hardware;
    public event Action<SensorSnapshot>? SnapshotPublished;   // raised on the polling thread
    public event Action<EngineState>? StateChanged;
    public void Start(); public void Pause(); public void Resume(); public void Stop(); public void SetFastInterval(TimeSpan interval);
    internal SensorSnapshot? TickOnce();                        // one poll if any node is due; exposed to tests
    internal void PrepareForManualTicks();                      // tests only: starts the provider and marks Running without a thread
    public const string KeyPollOverrun = "Engine.PollOverrun", KeyEngineFailed = "Engine.Failed";
}
```

- [ ] **Step 1: Write fakes and failing tests**

```csharp
// tests/Mazesta.Monitoring.Tests/Fakes/FakeClock.cs
using Mazesta.Core.Time;
namespace Mazesta.Monitoring.Tests.Fakes;
public sealed class FakeClock(DateTimeOffset start) : IClock { public DateTimeOffset UtcNow { get; set; } = start; public void Advance(TimeSpan t) => UtcNow += t; }
// tests/Mazesta.Monitoring.Tests/Fakes/FakeSensorProvider.cs
using Mazesta.Core.Hardware; using Mazesta.Hardware;
namespace Mazesta.Monitoring.Tests.Fakes;
public sealed class FakeSensorProvider : ISensorProvider
{
    public string Name => "fake"; public ProviderStatus Status { get; set; } = ProviderStatus.NotStarted; public event Action<ProviderStatus>? StatusChanged;
    public List<HardwareNode> Nodes { get; } = []; public IReadOnlyList<HardwareNode> Hardware => Nodes;
    public List<PollRequest> Requests { get; } = []; public Func<PollRequest, PollResult>? OnPoll; public Action? OnPollSideEffect; public bool Started, Disposed;
    public void Start() { Started = true; Status = ProviderStatus.Ready(Nodes.Sum(n => n.Sensors.Count)); StatusChanged?.Invoke(Status); }
    public PollResult Poll(PollRequest r)
    {
        Requests.Add(r); OnPollSideEffect?.Invoke();
        if (OnPoll is not null) return OnPoll(r);
        var readings = Nodes.SelectMany(n => n.Sensors).Select(s => new SensorReading(s.Id, 42, r.Now, DataQuality.Ok, Name)).ToList();
        return new PollResult(readings, Nodes.ToDictionary(n => n.Id, n => NodeStatus.Healthy(r.Now)));
    }
    public void Dispose() => Disposed = true;
    public static HardwareNode Node(HardwareKind kind, string id, params string[] sensors)
    { var hid = new HardwareId(id); return new HardwareNode(hid, kind, HardwareVendor.Unknown, id, null, true, sensors.Select((s, i) => new SensorDefinition(SensorId.Create(hid, s), hid, s, SensorKind.Temperature, Unit.Celsius, SensorRole.None, i)).ToList()); }
}
// tests/Mazesta.Monitoring.Tests/PollingEngineTests.cs
using Mazesta.Core.Hardware; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Monitoring.Tests;
public class PollingEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static (PollingEngine e, FakeSensorProvider p, FakeClock c, BoundedEventLog log) Build()
    {
        var c = new FakeClock(T0); var p = new FakeSensorProvider();
        p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Cpu, "cpu/x", "temperature/0")); p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Storage, "storage/S1", "temperature/0"));
        var log = new BoundedEventLog(c, NullLogger.Instance);
        return (new PollingEngine(p, c, new MonitoringOptions { FastInterval = TimeSpan.FromSeconds(2), StorageInterval = TimeSpan.FromMinutes(15) }, log), p, c, log);
    }
    [Fact] public void First_tick_polls_every_node_then_storage_waits_for_slow_cadence()
    {
        var (e, p, c, _) = Build(); e.PrepareForManualTicks();   // no thread: every poll below comes from an explicit TickOnce()
        e.TickOnce();
        Assert.Equal(2, p.Requests[^1].NodesToUpdate.Count);
        c.Advance(TimeSpan.FromSeconds(2)); e.TickOnce();
        Assert.Equal(["cpu/x"], p.Requests[^1].NodesToUpdate.Select(x => x.Value));
        c.Advance(TimeSpan.FromMinutes(15)); e.TickOnce();
        Assert.Equal(2, p.Requests[^1].NodesToUpdate.Count);
    }
    [Fact] public void Snapshot_is_published_with_history_and_stats_applied()
    {
        var (e, p, c, _) = Build(); SensorSnapshot? got = null; e.SnapshotPublished += s => got = s;
        e.PrepareForManualTicks(); e.TickOnce();
        Assert.NotNull(got); Assert.Equal(2, got!.Readings.Count);
        Assert.Equal(42.0, e.Statistics.Get(p.Nodes[0].Sensors[0].Id).Max); Assert.NotEmpty(e.History.GetRaw(p.Nodes[0].Sensors[0].Id).Seconds);
    }
    [Fact] public void Stale_detection_uses_node_cadence()
    {
        var (e, p, c, _) = Build(); e.PrepareForManualTicks();
        p.OnPoll = r => new PollResult([new SensorReading(p.Nodes[1].Sensors[0].Id, 30, T0, DataQuality.Ok, "fake"), new SensorReading(p.Nodes[0].Sensors[0].Id, 50, T0, DataQuality.Ok, "fake")],
                                       new Dictionary<HardwareId, NodeStatus> { [p.Nodes[0].Id] = NodeStatus.Healthy(T0), [p.Nodes[1].Id] = NodeStatus.Healthy(T0) });
        c.Advance(TimeSpan.FromSeconds(10)); var s = e.TickOnce()!;
        Assert.Equal(DataQuality.Stale, s.Readings.Single(r => r.Id.Hardware.Value == "cpu/x").Quality);          // 10 s > 3 × 2 s
        Assert.Equal(DataQuality.Ok, s.Readings.Single(r => r.Id.Hardware.Value == "storage/S1").Quality);        // 10 s < 3 × 15 min
    }
    [Fact] public void Interval_change_applies_and_rejects_invalid()
    {
        var (e, _, _, _) = Build(); e.SetFastInterval(TimeSpan.FromSeconds(5)); Assert.Equal(TimeSpan.FromSeconds(5), e.FastInterval);
        Assert.Throws<ArgumentOutOfRangeException>(() => e.SetFastInterval(TimeSpan.FromSeconds(3)));
    }
    [Fact] public void Pause_stops_ticks_and_resume_continues()
    {
        var (e, p, c, _) = Build(); e.PrepareForManualTicks();
        e.Pause(); Assert.Equal(EngineState.Paused, e.State); c.Advance(TimeSpan.FromSeconds(4)); Assert.Null(e.TickOnce()); Assert.Empty(p.Requests);
        e.Resume(); Assert.Equal(EngineState.Running, e.State); Assert.NotNull(e.TickOnce());
    }
    [Fact] public void Overrun_is_logged_once_per_minute()
    {
        var (e, p, c, log) = Build(); e.PrepareForManualTicks();
        p.OnPollSideEffect = () => c.Advance(TimeSpan.FromSeconds(3));   // poll "takes" 3 s > 2 s interval
        e.TickOnce(); e.TickOnce(); e.TickOnce();
        Assert.Single(log.Snapshot().Where(x => x.Key == PollingEngine.KeyPollOverrun));
        c.Advance(TimeSpan.FromSeconds(61)); e.TickOnce();
        Assert.Equal(2, log.Snapshot().Count(x => x.Key == PollingEngine.KeyPollOverrun));
    }
    [Fact] public void Provider_exception_outside_contract_fails_engine_not_process()
    {
        var (e, p, _, log) = Build(); e.PrepareForManualTicks(); p.OnPoll = _ => throw new InvalidOperationException("bug");
        Assert.Null(e.TickOnce()); Assert.Equal(EngineState.Failed, e.State); Assert.Contains(log.Snapshot(), x => x.Key == PollingEngine.KeyEngineFailed);
    }
    [Fact] public void Real_thread_publishes_and_stop_disposes_provider()
    {
        var (e, p, _, _) = Build(); var published = new ManualResetEventSlim(); e.SnapshotPublished += _ => published.Set();
        e.Start(); Assert.True(published.Wait(TimeSpan.FromSeconds(5))); e.Stop(); e.Dispose();
        Assert.True(p.Started && p.Disposed); Assert.Equal(EngineState.Stopped, e.State);
    }
}
```

- [ ] **Step 2: Run tests** → Expected: compile failure.

- [ ] **Step 3: Implement**

```csharp
// src/Mazesta.Monitoring/PollingEngine.cs
using System.Diagnostics; using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Mazesta.Hardware;
namespace Mazesta.Monitoring;
public enum EngineState { Stopped, Running, Paused, Failed }
public sealed class PollingEngine : IDisposable
{
    public const string KeyPollOverrun = "Engine.PollOverrun", KeyEngineFailed = "Engine.Failed";
    private readonly IClock _clock; private readonly MonitoringOptions _options; private readonly IEventLog _events;
    private readonly Dictionary<HardwareId, DateTimeOffset> _nextDue = []; private readonly object _lock = new();
    private readonly ManualResetEventSlim _wake = new(false); private Thread? _thread; private volatile bool _stopRequested; private bool _providerStarted;
    private long _sequence; private DateTimeOffset _lastOverrunLog = DateTimeOffset.MinValue; private EngineState _state = EngineState.Stopped;
    public ISensorProvider Provider { get; } public HistoryStore History { get; } public SensorStatistics Statistics { get; }
    public TimeSpan FastInterval { get; private set; }
    public IReadOnlyList<HardwareNode> Hardware => Provider.Hardware;
    public EngineState State { get => _state; private set { if (_state == value) return; _state = value; StateChanged?.Invoke(value); } }
    public event Action<SensorSnapshot>? SnapshotPublished; public event Action<EngineState>? StateChanged;

    public PollingEngine(ISensorProvider provider, IClock clock, MonitoringOptions options, IEventLog events)
    { Provider = provider; _clock = clock; _options = options; _events = events; FastInterval = options.FastInterval; History = new HistoryStore(clock.UtcNow); Statistics = new SensorStatistics(clock.UtcNow); }

    public void Start()
    {
        if (_thread is not null) return;
        _stopRequested = false; State = EngineState.Running;
        _thread = new Thread(Loop) { Name = "Mazesta.Polling", IsBackground = true }; _thread.Start();
    }
    public void Stop() { _stopRequested = true; _wake.Set(); _thread?.Join(TimeSpan.FromSeconds(10)); _thread = null; if (State != EngineState.Failed) State = EngineState.Stopped; }
    internal void PrepareForManualTicks() { EnsureProviderStarted(); State = EngineState.Running; }
    public void Pause() { if (State == EngineState.Running) State = EngineState.Paused; }
    public void Resume() { if (State == EngineState.Paused) { State = EngineState.Running; _wake.Set(); } }
    public void SetFastInterval(TimeSpan interval)
    {
        if (!MonitoringOptions.AllowedFastSeconds.Contains((int)interval.TotalSeconds) || interval.TotalSeconds != Math.Floor(interval.TotalSeconds)) throw new ArgumentOutOfRangeException(nameof(interval));
        lock (_lock) { FastInterval = interval; _options.FastInterval = interval; foreach (var n in Hardware.Where(n => n.Kind != HardwareKind.Storage)) _nextDue[n.Id] = _clock.UtcNow; }
        _wake.Set();
    }
    private void EnsureProviderStarted() { if (_providerStarted) return; _providerStarted = true; Provider.Start(); }
    private void Loop()
    {
        EnsureProviderStarted();
        while (!_stopRequested)
        {
            TimeSpan wait;
            if (State == EngineState.Running) { var sw = Stopwatch.StartNew(); TickOnce(); wait = FastInterval - sw.Elapsed; if (wait < TimeSpan.Zero) wait = TimeSpan.Zero; }
            else wait = TimeSpan.FromMilliseconds(250);
            _wake.Wait(wait); _wake.Reset();
        }
    }
    internal SensorSnapshot? TickOnce()
    {
        if (State != EngineState.Running) return null;
        EnsureProviderStarted();
        try
        {
            var now = _clock.UtcNow; var due = new HashSet<HardwareId>();
            lock (_lock)
                foreach (var n in Hardware)
                    if (!_nextDue.TryGetValue(n.Id, out var d) || d <= now) { due.Add(n.Id); _nextDue[n.Id] = now + _options.CadenceFor(n.Kind); }
            var result = Provider.Poll(new PollRequest(now, due));
            var after = _clock.UtcNow;
            if (after - now > FastInterval && after - _lastOverrunLog > TimeSpan.FromMinutes(1))
            { _lastOverrunLog = after; _events.Log(EventLevel.Warning, KeyPollOverrun, $"Poll took {(after - now).TotalMilliseconds:F0} ms, interval {FastInterval.TotalSeconds} s"); }
            var kinds = Hardware.ToDictionary(n => n.Id, n => n.Kind);
            var readings = new List<SensorReading>(result.Readings.Count);
            foreach (var r in result.Readings)
            {
                result.NodeStatus.TryGetValue(r.Id.Hardware, out var st);
                var cadence = kinds.TryGetValue(r.Id.Hardware, out var k) ? _options.CadenceFor(k) : FastInterval;
                readings.Add(r with { Quality = StaleDetector.Apply(r, st, cadence, after) });
            }
            var snapshot = new SensorSnapshot(Interlocked.Increment(ref _sequence), after, readings, result.NodeStatus);
            History.Append(snapshot); Statistics.Apply(snapshot); SnapshotPublished?.Invoke(snapshot);
            return snapshot;
        }
        catch (Exception ex) { _events.Log(EventLevel.Error, KeyEngineFailed, ex.ToString()); State = EngineState.Failed; return null; }
    }
    public void Dispose() { Stop(); Provider.Dispose(); _wake.Dispose(); }
}
```

- [ ] **Step 4: Run tests** → Expected: all engine tests pass deterministically (only `Real_thread_publishes…` starts the thread).
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(monitoring): polling engine with cadence, pause, overrun and failure isolation"`

---

### Task 13: AppPaths, JsonStore, SchemaMigrator, AppConfig

**Files:**
- Create: `src/Mazesta.Persistence/AppPaths.cs`, `IVersionedDocument.cs`, `SchemaMigrator.cs`, `JsonStore.cs`, `AppConfig.cs`
- Test: `tests/Mazesta.Persistence.Tests/AppPathsTests.cs`, `JsonStoreTests.cs`, `AppConfigMigrationTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed class AppPaths { public bool IsPortable; public string DataRoot, ConfigDir, LogsDir, SessionsDir, HistoryDir; public string ConfigFile => Path.Combine(ConfigDir, "appconfig.json");
                              public static AppPaths Detect(); public static AppPaths Create(string exeDirectory, string localAppData, bool portableMarkerExists); public void EnsureDirectories(); public const string PortableMarker = "portable.marker"; }
public interface IVersionedDocument { int SchemaVersion { get; set; } }
public interface IMigration { int From { get; } JsonObject Apply(JsonObject document); }     // From → From+1
public sealed class SchemaMigrator(IReadOnlyList<IMigration> migrations) { public JsonObject Migrate(JsonObject doc, int target, out int appliedSteps); }
public enum LoadOutcome { Loaded, Defaulted, Migrated, Corrupt }
public readonly record struct LoadResult<T>(T Value, LoadOutcome Outcome, string? Detail);
public sealed class JsonStore<T>(string path, SchemaMigrator migrator, int currentVersion, ILogger logger) where T : class, IVersionedDocument, new()
{ public LoadResult<T> Load(); public void Save(T value); public static readonly JsonSerializerOptions Options; }
public sealed class AppConfig : IVersionedDocument { const int CurrentSchemaVersion = 1; string Language = "en"; int FastIntervalSeconds = 2; int StorageIntervalSeconds = 900; string ShopName = "مازستا"; List<string> ExpandedGroups; WindowPlacement? MainWindow; List<ChartWindowConfig> ChartWindows; }
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
public sealed record ChartWindowConfig(string SensorId, WindowPlacement? Placement, int WindowMinutes);
public sealed class Migration0To1 : IMigration   // renames "pollSeconds" → "fastIntervalSeconds", drops unknown keys, sets schemaVersion 1
```

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json.Nodes; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Persistence.Tests;
public class AppPathsTests
{
    [Fact] public void Portable_marker_puts_data_next_to_exe()
    { var p = AppPaths.Create(@"C:\Apps\Mazesta", @"C:\Users\u\AppData\Local", portableMarkerExists: true); Assert.True(p.IsPortable); Assert.Equal(@"C:\Apps\Mazesta\Data", p.DataRoot); Assert.Equal(@"C:\Apps\Mazesta\Data\config\appconfig.json", p.ConfigFile); }
    [Fact] public void Without_marker_uses_local_app_data()
    { var p = AppPaths.Create(@"C:\Apps\Mazesta", @"C:\Users\u\AppData\Local", false); Assert.False(p.IsPortable); Assert.Equal(@"C:\Users\u\AppData\Local\Mazesta\Test", p.DataRoot); Assert.EndsWith(@"\logs", p.LogsDir); }
}
public class JsonStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-tests-" + Guid.NewGuid().ToString("N"));
    public JsonStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);
    private JsonStore<AppConfig> Store() => new(Path.Combine(_dir, "appconfig.json"), new SchemaMigrator([new Migration0To1()]), AppConfig.CurrentSchemaVersion, NullLogger.Instance);
    [Fact] public void Missing_file_returns_defaults()
    { var r = Store().Load(); Assert.Equal(LoadOutcome.Defaulted, r.Outcome); Assert.Equal(2, r.Value.FastIntervalSeconds); Assert.Equal("en", r.Value.Language); }
    [Fact] public void Save_then_load_round_trips_and_leaves_no_temp_file()
    {
        var s = Store(); s.Save(new AppConfig { Language = "fa", ShopName = "فروشگاه", ExpandedGroups = ["cpu/intelcpu-0"] });
        var r = s.Load(); Assert.Equal((LoadOutcome.Loaded, "fa", "فروشگاه"), (r.Outcome, r.Value.Language, r.Value.ShopName)); Assert.Equal(["cpu/intelcpu-0"], r.Value.ExpandedGroups);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }
    [Fact] public void Corrupt_file_is_renamed_aside_and_defaults_used()
    {
        File.WriteAllText(Path.Combine(_dir, "appconfig.json"), "{ not json");
        var r = Store().Load(); Assert.Equal(LoadOutcome.Corrupt, r.Outcome); Assert.Single(Directory.GetFiles(_dir, "appconfig.json.corrupt-*")); Assert.Equal(2, r.Value.FastIntervalSeconds);
    }
    [Fact] public void Newer_schema_than_supported_is_treated_as_corrupt()
    {
        File.WriteAllText(Path.Combine(_dir, "appconfig.json"), """{"schemaVersion": 99}"""); Assert.Equal(LoadOutcome.Corrupt, Store().Load().Outcome);
    }
}
public class AppConfigMigrationTests
{
    [Fact] public void V0_document_migrates_pollSeconds_to_fastIntervalSeconds()
    {
        var doc = JsonNode.Parse("""{"pollSeconds": 5, "language": "fa", "unknownThing": 1}""")!.AsObject();
        var migrated = new SchemaMigrator([new Migration0To1()]).Migrate(doc, 1, out var steps);
        Assert.Equal(1, steps); Assert.Equal(5, (int)migrated["fastIntervalSeconds"]!); Assert.Equal(1, (int)migrated["schemaVersion"]!); Assert.Null(migrated["pollSeconds"]); Assert.Equal("fa", (string)migrated["language"]!);
    }
    [Fact] public void Migrator_throws_when_a_step_is_missing()
        => Assert.Throws<InvalidOperationException>(() => new SchemaMigrator([]).Migrate(JsonNode.Parse("""{"schemaVersion":0}""")!.AsObject(), 1, out _));
}
```

- [ ] **Step 2: Run** `"$DOTNET" test tests/Mazesta.Persistence.Tests -c Debug` → Expected: compile failure.

- [ ] **Step 3: Implement**

```csharp
// src/Mazesta.Persistence/AppPaths.cs
namespace Mazesta.Persistence;
public sealed class AppPaths
{
    public const string PortableMarker = "portable.marker";
    public bool IsPortable { get; private init; } public string DataRoot { get; private init; } = "";
    public string ConfigDir => Path.Combine(DataRoot, "config"); public string LogsDir => Path.Combine(DataRoot, "logs");
    public string SessionsDir => Path.Combine(DataRoot, "sessions"); public string HistoryDir => Path.Combine(DataRoot, "history");
    public string ConfigFile => Path.Combine(ConfigDir, "appconfig.json");
    public static AppPaths Create(string exeDirectory, string localAppData, bool portableMarkerExists) => portableMarkerExists
        ? new AppPaths { IsPortable = true, DataRoot = Path.Combine(exeDirectory, "Data") }
        : new AppPaths { IsPortable = false, DataRoot = Path.Combine(localAppData, "Mazesta", "Test") };
    public static AppPaths Detect()
    {
        string exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        return Create(exeDir, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), File.Exists(Path.Combine(exeDir, PortableMarker)));
    }
    public void EnsureDirectories() { foreach (var d in new[] { ConfigDir, LogsDir, SessionsDir, HistoryDir }) Directory.CreateDirectory(d); }
}
// src/Mazesta.Persistence/IVersionedDocument.cs
namespace Mazesta.Persistence;
public interface IVersionedDocument { int SchemaVersion { get; set; } }
// src/Mazesta.Persistence/SchemaMigrator.cs
using System.Text.Json.Nodes;
namespace Mazesta.Persistence;
public interface IMigration { int From { get; } JsonObject Apply(JsonObject document); }
public sealed class SchemaMigrator(IReadOnlyList<IMigration> migrations)
{
    public JsonObject Migrate(JsonObject doc, int target, out int appliedSteps)
    {
        appliedSteps = 0; int version = doc["schemaVersion"]?.GetValue<int>() ?? 0;
        while (version < target)
        {
            var step = migrations.FirstOrDefault(m => m.From == version) ?? throw new InvalidOperationException($"No migration from schema version {version}.");
            doc = step.Apply(doc); doc["schemaVersion"] = version + 1; version++; appliedSteps++;
        }
        return doc;
    }
}
// src/Mazesta.Persistence/JsonStore.cs
using System.Text.Json; using System.Text.Json.Nodes; using System.Text.Json.Serialization; using Microsoft.Extensions.Logging;
namespace Mazesta.Persistence;
public enum LoadOutcome { Loaded, Defaulted, Migrated, Corrupt }
public readonly record struct LoadResult<T>(T Value, LoadOutcome Outcome, string? Detail);
public sealed class JsonStore<T>(string path, SchemaMigrator migrator, int currentVersion, ILogger logger) where T : class, IVersionedDocument, new()
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public LoadResult<T> Load()
    {
        if (!File.Exists(path)) return new(new T { SchemaVersion = currentVersion }, LoadOutcome.Defaulted, null);
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException("Root is not an object.");
            int version = node["schemaVersion"]?.GetValue<int>() ?? 0;
            if (version > currentVersion) throw new JsonException($"Schema version {version} is newer than supported {currentVersion}.");
            int steps = 0; if (version < currentVersion) node = migrator.Migrate(node, currentVersion, out steps);
            var value = node.Deserialize<T>(Options) ?? throw new JsonException("Deserialised to null."); value.SchemaVersion = currentVersion;
            if (steps > 0) { Save(value); return new(value, LoadOutcome.Migrated, $"{steps} migration step(s)"); }
            return new(value, LoadOutcome.Loaded, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
        {
            string aside = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";
            try { File.Move(path, aside, overwrite: true); } catch (IOException ioe) { logger.LogWarning(ioe, "Could not move corrupt config aside"); }
            logger.LogWarning(ex, "Config unreadable; defaults used, file moved to {Aside}", aside);
            return new(new T { SchemaVersion = currentVersion }, LoadOutcome.Corrupt, ex.Message);
        }
    }
    public void Save(T value)
    {
        value.SchemaVersion = currentVersion; Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
    }
}
// src/Mazesta.Persistence/AppConfig.cs
using System.Text.Json.Nodes;
namespace Mazesta.Persistence;
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
public sealed record ChartWindowConfig(string SensorId, WindowPlacement? Placement, int WindowMinutes);
public sealed class AppConfig : IVersionedDocument
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Language { get; set; } = "en";
    public int FastIntervalSeconds { get; set; } = 2;
    public int StorageIntervalSeconds { get; set; } = 900;
    public string ShopName { get; set; } = "مازستا";
    public List<string> ExpandedGroups { get; set; } = [];
    public WindowPlacement? MainWindow { get; set; }
    public List<ChartWindowConfig> ChartWindows { get; set; } = [];
}
public sealed class Migration0To1 : IMigration
{
    public int From => 0;
    public JsonObject Apply(JsonObject d)
    {
        var o = new JsonObject();
        if (d["pollSeconds"] is JsonNode p) o["fastIntervalSeconds"] = p.DeepClone();
        foreach (var key in new[] { "language", "fastIntervalSeconds", "storageIntervalSeconds", "shopName", "expandedGroups", "mainWindow", "chartWindows" })
            if (d[key] is JsonNode n) o[key] = n.DeepClone();
        return o;
    }
}
```

- [ ] **Step 4: Run tests** → Expected: pass.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(persistence): portable-aware paths, atomic versioned JSON store, app config v1"`

---

### Task 14: RollingFileLogger

**Files:**
- Create: `src/Mazesta.Persistence/RollingFileLogger.cs`
- Test: `tests/Mazesta.Persistence.Tests/RollingFileLoggerTests.cs`

**Interfaces:**
- Produces: `public sealed class RollingFileLoggerProvider(string directory, string prefix = "mazesta-test", int keepFiles = 7, Func<DateTime>? now = null) : ILoggerProvider` and `public static ILoggerFactory LoggingSetup.CreateFactory(string logsDir, LogLevel minimum)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Mazesta.Persistence; using Microsoft.Extensions.Logging;
namespace Mazesta.Persistence.Tests;
public class RollingFileLoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-log-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    [Fact] public void Writes_line_with_level_category_and_message()
    {
        using var p = new RollingFileLoggerProvider(_dir, now: () => new DateTime(2026, 9, 12, 10, 0, 0));
        p.CreateLogger("Mazesta.Test").LogWarning("hello {Name}", "world"); p.Flush();
        var text = File.ReadAllText(Path.Combine(_dir, "mazesta-test-20260912.log"));
        Assert.Contains("WRN", text); Assert.Contains("Mazesta.Test", text); Assert.Contains("hello world", text);
    }
    [Fact] public void Keeps_only_newest_files()
    {
        Directory.CreateDirectory(_dir);
        for (int i = 1; i <= 9; i++) File.WriteAllText(Path.Combine(_dir, $"mazesta-test-202609{i:00}.log"), "x");
        using var p = new RollingFileLoggerProvider(_dir, keepFiles: 7, now: () => new DateTime(2026, 9, 12));
        p.CreateLogger("c").LogInformation("new"); p.Flush();
        Assert.Equal(7, Directory.GetFiles(_dir, "mazesta-test-*.log").Length); Assert.False(File.Exists(Path.Combine(_dir, "mazesta-test-20260901.log")));
    }
}
```

- [ ] **Step 2: Run tests** → Expected: compile failure.

- [ ] **Step 3: Implement**

```csharp
// src/Mazesta.Persistence/RollingFileLogger.cs
using System.Text; using Microsoft.Extensions.Logging;
namespace Mazesta.Persistence;
public sealed class RollingFileLoggerProvider(string directory, string prefix = "mazesta-test", int keepFiles = 7, Func<DateTime>? now = null) : ILoggerProvider
{
    private readonly Func<DateTime> _now = now ?? (() => DateTime.Now); private readonly object _lock = new(); private StreamWriter? _writer; private string? _currentFile;
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    internal void Write(LogLevel level, string category, string message, Exception? ex)
    {
        var t = _now(); string file = Path.Combine(directory, $"{prefix}-{t:yyyyMMdd}.log");
        lock (_lock)
        {
            if (file != _currentFile) { _writer?.Dispose(); Directory.CreateDirectory(directory); _writer = new StreamWriter(file, append: true, Encoding.UTF8); _currentFile = file; Prune(); }
            _writer!.Write($"{t:HH:mm:ss.fff} {Abbrev(level)} {category} {message}"); if (ex is not null) _writer.Write($" | {ex}"); _writer.WriteLine();
        }
    }
    private void Prune()
    {
        foreach (var old in Directory.GetFiles(directory, $"{prefix}-*.log").OrderByDescending(f => f, StringComparer.Ordinal).Skip(keepFiles))
            try { File.Delete(old); } catch (IOException) { }
    }
    private static string Abbrev(LogLevel l) => l switch { LogLevel.Trace => "TRC", LogLevel.Debug => "DBG", LogLevel.Information => "INF", LogLevel.Warning => "WRN", LogLevel.Error => "ERR", LogLevel.Critical => "CRT", _ => "???" };
    public void Flush() { lock (_lock) _writer?.Flush(); }
    public void Dispose() { lock (_lock) { _writer?.Dispose(); _writer = null; } }
    private sealed class FileLogger(RollingFileLoggerProvider p, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => p.Write(logLevel, category, formatter(state, exception), exception);
    }
}
public static class LoggingSetup
{
    public static ILoggerFactory CreateFactory(string logsDir, LogLevel minimum = LogLevel.Information)
        => LoggerFactory.Create(b => { b.SetMinimumLevel(minimum); b.AddProvider(new RollingFileLoggerProvider(logsDir)); });
}
```
Note: the first test's expected filename uses `_now`, so the provider must name files from the injected clock (it does). Log lines are UTF-8, so Persian text is preserved.

- [ ] **Step 4: Run tests** → Expected: pass.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(persistence): rolling daily file logger"`

---

### Task 15: Desktop bootstrap — DI, theme, localization, shell window, placeholder pages, status bar

**Files:**
- Create/Modify: `src/Mazesta.Desktop/App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `Composition/Bootstrapper.cs`, `Localization/Loc.cs`, `Localization/Strings.resx`, `Localization/Strings.fa.resx`, `Themes/Dark.xaml`, `ViewModels/ShellViewModel.cs`, `ViewModels/NavItem.cs`, `ViewModels/PlaceholderViewModel.cs`, `Views/PlaceholderView.xaml`, `Fonts/Vazirmatn-Regular.ttf`, `Fonts/Vazirmatn-Bold.ttf`, `Fonts/OFL.txt`
- Test: manual launch smoke (WPF has no unit-testable surface here; the view models added in later tasks are tested there).

**Interfaces:**
- Consumes: `PollingEngine`, `LibreHardwareMonitorProvider.CreateDefault`, `WmiInventoryProvider`, `AppPaths`, `JsonStore<AppConfig>`, `BoundedEventLog`, `LoggingSetup`, `MonitoringFocus`.
- Produces:
```csharp
public static class Loc { public static string Get(string key); public static bool IsRtl { get; } public static CultureInfo Culture { get; } }
[MarkupExtensionReturnType(typeof(string))] public sealed class LocExtension(string key) : MarkupExtension
public sealed partial class ShellViewModel : ObservableObject { ObservableCollection<NavItem> Items; NavItem? Selected; object? CurrentPage; string ProviderStatusText; string EngineStateText; bool IsPaused; IRelayCommand TogglePauseCommand; string IntervalText; }
public sealed record NavItem(string Key, string Glyph, Func<object> PageFactory);
public sealed class Bootstrapper { public static ServiceProvider Build(AppPaths paths, AppConfig config, JsonStore<AppConfig> store); }
```

- [ ] **Step 1: Fonts and resources**

```bash
cd /mnt/f/Projects/darabi/src/Mazesta.Desktop && mkdir -p Fonts Assets Localization Themes Controls ViewModels Views Services Composition
for f in Vazirmatn-Regular Vazirmatn-Bold; do curl -sL -o Fonts/$f.ttf "https://raw.githubusercontent.com/rastikerdar/vazirmatn/master/fonts/ttf/$f.ttf"; done
curl -sL -o Fonts/OFL.txt "https://raw.githubusercontent.com/rastikerdar/vazirmatn/master/OFL.txt"
```
Add to `Mazesta.Desktop.csproj`: `<ItemGroup><Resource Include="Fonts\*.ttf" /><Resource Include="Assets\*.jpg" /><EmbeddedResource Update="Localization\*.resx" /></ItemGroup>`.

`Localization/Strings.resx` keys (English values) — create with `<data name="…"><value>…</value></data>` entries:
`App_Title=Mazesta Test`, `Nav_Dashboard=Dashboard`, `Nav_Monitoring=Monitoring`, `Nav_Tests=Tests`, `Nav_Benchmarks=Benchmarks`, `Nav_Gpu=GPU`, `Nav_Cpu=CPU`, `Nav_Network=Network`, `Nav_Storage=Storage`, `Nav_Gaming=Gaming`, `Nav_WindowsTools=Windows Tools`, `Nav_Reports=Reports`, `Nav_Settings=Settings`, `Placeholder_Body=This section is built in a later phase. Nothing here is simulated.`, `Status_Provider_Ready=Sensors: ready ({0} sensors)`, `Status_Provider_Starting=Sensors: starting…`, `Status_Provider_Degraded=Sensors: degraded — {0}`, `Status_Provider_Failed=Sensors: failed — {0}`, `Provider.PawnIoMissing=PawnIO driver is not installed. CPU temperatures and clocks are unavailable. Install PawnIO from its official page, then restart.`, `Provider.NotElevated=Not running as administrator. CPU and motherboard sensors are unavailable.`, `Provider.OpenFailed=Sensor library failed to start.`, `Provider.NoHardware=No sensors were found.`, `Status_Pause=Pause`, `Status_Resume=Resume`, `Status_Interval=Interval: {0} s`, `Value_NotAvailable=Not available`, `Value_Stale=Stale`, `Value_Invalid=Invalid`, `Column_Sensor=Sensor`, `Column_Current=Current`, `Column_Min=Min`, `Column_Max=Max`, `Column_Avg=Avg`, `Column_Unit=Unit`, `Column_State=State`, `Monitoring_Search=Search hardware or sensor`, `Monitoring_ResetStats=Reset statistics`, `Monitoring_OpenChart=Open chart`, `Chart_Window=Window`, `Dashboard_Cpu=CPU`, `Dashboard_Gpu=GPU`, `Dashboard_HotSpot=GPU Hot Spot`, `Dashboard_Ram=RAM`, `Dashboard_Inventory=System`, `Dashboard_Mazesta_Title=Mazesta services`, `Dashboard_Mazesta_L1=System service, maintenance and upgrades`, `Dashboard_Mazesta_L2=Consulting and purchase of specialised systems with one-year golden warranty`, `Dashboard_Mazesta_Site=Official website`, `Dashboard_Mazesta_Contact=Contact page`, `Dashboard_Product_View=View product`, `Settings_Language=Language`, `Settings_FastInterval=Sensor interval (seconds)`, `Settings_StorageInterval=Disk SMART interval (seconds)`, `Settings_ShopName=Shop name`, `Settings_DataFolder=Data folder`, `Settings_OpenFolder=Open folder`, `Settings_Mode_Portable=Portable`, `Settings_Mode_Installed=Installed`, `Settings_Save=Save`, `Settings_RestartNote=Restart the app to apply the language.`, `Settings_Invalid_Interval=Enter a whole number of seconds (60 or more).`, `Settings_Saved=Saved.`, `Crash_Title=Unexpected error`, `Crash_Body=An unexpected error occurred and was written to the log. Continue?`, `Config_Corrupt=Settings file was unreadable and has been reset. The old file was kept beside it.`

`Localization/Strings.fa.resx`: the same keys in Persian, e.g. `Nav_Dashboard=داشبورد`, `Nav_Monitoring=مانیتورینگ`, `Nav_Tests=تست‌ها`, `Nav_Benchmarks=بنچمارک‌ها`, `Nav_Gpu=GPU`, `Nav_Cpu=CPU`, `Nav_Network=شبکه`, `Nav_Storage=ذخیره‌سازی`, `Nav_Gaming=بازی`, `Nav_WindowsTools=ابزارهای ویندوز`, `Nav_Reports=گزارش‌ها`, `Nav_Settings=تنظیمات`, `Placeholder_Body=این بخش در مرحله بعدی ساخته می‌شود. هیچ‌چیز در اینجا شبیه‌سازی نشده است.`, `Status_Provider_Ready=سنسورها: آماده ({0} سنسور)`, `Status_Provider_Degraded=سنسورها: ناقص — {0}`, `Status_Provider_Failed=سنسورها: خطا — {0}`, `Provider.PawnIoMissing=درایور PawnIO نصب نیست. دمای و فرکانس CPU در دسترس نیست. PawnIO را از صفحه رسمی آن نصب کنید و برنامه را دوباره اجرا کنید.`, `Provider.NotElevated=برنامه با دسترسی Administrator اجرا نشده است. سنسورهای CPU و مادربرد در دسترس نیستند.`, `Value_NotAvailable=دریافت نشد`, `Value_Stale=قدیمی`, `Value_Invalid=نامعتبر`, `Dashboard_Mazesta_Title=خدمات مازستا`, `Dashboard_Mazesta_L1=خدمات سرویس، نگهداری و ارتقای سیستم`, `Dashboard_Mazesta_L2=مشاوره و خرید سیستم تخصصی با یک سال گارانتی طلایی`, `Dashboard_Product_View=مشاهده محصول`, `Settings_RestartNote=برای اعمال زبان، برنامه را دوباره اجرا کنید.`, `Config_Corrupt=فایل تنظیمات خوانا نبود و بازنشانی شد. فایل قبلی کنار آن نگه داشته شده است.` … (translate every key; component names such as CPU/GPU/SMART stay English).

- [ ] **Step 2: Localization helpers**

```csharp
// Localization/Loc.cs
using System.Globalization; using System.Resources; using System.Windows.Markup;
namespace Mazesta.Desktop.Localization;
public static class Loc
{
    private static readonly ResourceManager Rm = new("Mazesta.Desktop.Localization.Strings", typeof(Loc).Assembly);
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");
    public static bool IsRtl => Culture.TextInfo.IsRightToLeft;
    public static void SetLanguage(string code) { Culture = CultureInfo.GetCultureInfo(code == "fa" ? "fa-IR" : "en"); CultureInfo.CurrentUICulture = Culture; Thread.CurrentThread.CurrentUICulture = Culture; }
    public static string Get(string key) => Rm.GetString(key, Culture) ?? key;
    public static string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, Get(key), args);
}
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension { public override object ProvideValue(IServiceProvider sp) => Loc.Get(key); }
```

- [ ] **Step 3: Theme tokens**

`Themes/Dark.xaml` (ResourceDictionary): `Color`/`SolidColorBrush` pairs — `Brush.Background #14171C`, `Brush.Surface #1C2026`, `Brush.SurfaceAlt #232830`, `Brush.Border #303640`, `Brush.Text #E6E9EE`, `Brush.TextMuted #9AA3B2`, `Brush.Accent #4F8CFF`, `Brush.StateMissing #6B7380`; series colours `Brush.Series.Cpu #4FC3F7`, `Brush.Series.Gpu #81C784`, `Brush.Series.Memory #BA68C8`, `Brush.Series.Storage #90A4AE`, `Brush.Series.Motherboard #A1887F`, `Brush.Series.Network #4DB6AC`; `FontFamily App.Font` = `pack://application:,,,/Fonts/#Vazirmatn, Segoe UI`; implicit styles for `Window`, `TextBlock`, `Button`, `TextBox`, `ComboBox`, `ListView`, `Expander`, `GridViewColumnHeader` using those brushes (flat, 6 px corner radius, 1 px border). No red/orange anywhere.

- [ ] **Step 4: App start-up, DI, single instance (mutex only; window activation in Task 21)**

```csharp
// Composition/Bootstrapper.cs
using Mazesta.Core.Time; using Mazesta.Hardware; using Mazesta.Hardware.Lhm; using Mazesta.Hardware.Wmi; using Mazesta.Monitoring; using Mazesta.Persistence; using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Desktop.Composition;
public static class Bootstrapper
{
    public static ServiceProvider Build(AppPaths paths, AppConfig config, JsonStore<AppConfig> store)
    {
        var s = new ServiceCollection();
        var lf = LoggingSetup.CreateFactory(paths.LogsDir);
        s.AddSingleton(lf); s.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        s.AddSingleton(paths); s.AddSingleton(config); s.AddSingleton(store);
        s.AddSingleton<IClock, SystemClock>();
        s.AddSingleton<IEventLog>(sp => new BoundedEventLog(sp.GetRequiredService<IClock>(), lf.CreateLogger("Events")));
        s.AddSingleton(new MonitoringOptions { FastInterval = TimeSpan.FromSeconds(config.FastIntervalSeconds), StorageInterval = TimeSpan.FromSeconds(config.StorageIntervalSeconds) });
        s.AddSingleton<ISensorProvider>(sp => LibreHardwareMonitorProvider.CreateDefault(sp.GetRequiredService<IClock>(), lf));
        s.AddSingleton<PollingEngine>();
        s.AddSingleton<MonitoringFocus>();
        s.AddSingleton<IWmiQuery, WmiQuery>(); s.AddSingleton<IInventoryProvider, WmiInventoryProvider>();
        s.AddSingleton<ViewModels.ShellViewModel>();
        // Tasks 17–20 add: IChartWindowService/ChartWindowService, MonitoringViewModel, DashboardViewModel, SettingsViewModel
        return s.BuildServiceProvider();
    }
}
// App.xaml.cs
using System.Windows; using Mazesta.Desktop.Localization; using Mazesta.Persistence; using Microsoft.Extensions.DependencyInjection; using Microsoft.Extensions.Logging;
namespace Mazesta.Desktop;
public partial class App : Application
{
    private static readonly Mutex SingleInstance = new(true, @"Global\Mazesta.Test.SingleInstance", out var createdNew); public static bool IsFirstInstance = createdNew;
    public static ServiceProvider Services { get; private set; } = null!; public static System.Diagnostics.Stopwatch StartupClock { get; } = System.Diagnostics.Stopwatch.StartNew();
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!IsFirstInstance) { Shutdown(); return; }
        var paths = AppPaths.Detect(); paths.EnsureDirectories();
        var lf = LoggingSetup.CreateFactory(paths.LogsDir); var startupLog = lf.CreateLogger("Startup");
        var store = new JsonStore<AppConfig>(paths.ConfigFile, new SchemaMigrator([new Migration0To1()]), AppConfig.CurrentSchemaVersion, startupLog);
        var load = store.Load(); var config = load.Value;
        Loc.SetLanguage(config.Language);
        Services = Composition.Bootstrapper.Build(paths, config, store);
        var shell = Services.GetRequiredService<ViewModels.ShellViewModel>();
        if (load.Outcome == LoadOutcome.Corrupt) shell.ShowBanner(Loc.Get("Config_Corrupt"));
        var window = new MainWindow { DataContext = shell, FlowDirection = Loc.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        MainWindow = window; window.Show(); shell.Selected = shell.Items[0];
        startupLog.LogInformation("Window shown at {Ms} ms", StartupClock.ElapsedMilliseconds);
        Services.GetRequiredService<Mazesta.Monitoring.PollingEngine>().Start();
        base.OnStartup(e);
    }
    public static void LogStartup(string what) { if (Services is null) return; Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup").LogInformation("{What} at {Ms} ms", what, StartupClock.ElapsedMilliseconds); }
    protected override void OnExit(ExitEventArgs e) { if (IsFirstInstance) { Services.GetRequiredService<Mazesta.Monitoring.PollingEngine>().Dispose(); Services.Dispose(); } base.OnExit(e); }
}
```
`App.xaml`: `<Application.Resources><ResourceDictionary><ResourceDictionary.MergedDictionaries><ResourceDictionary Source="Themes/Dark.xaml"/></ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources>`; remove `StartupUri`.

- [ ] **Step 5: Shell view model, nav items, placeholder**

```csharp
// ViewModels/NavItem.cs
namespace Mazesta.Desktop.ViewModels;
public sealed record NavItem(string Key, string Glyph, Func<object> PageFactory) { public string Label => Localization.Loc.Get(Key); }
// ViewModels/PlaceholderViewModel.cs
namespace Mazesta.Desktop.ViewModels;
public sealed class PlaceholderViewModel(string titleKey) { public string Title => Localization.Loc.Get(titleKey); public string Body => Localization.Loc.Get("Placeholder_Body"); }
// ViewModels/ShellViewModel.cs
using System.Collections.ObjectModel; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Monitoring; using Microsoft.Extensions.DependencyInjection;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly PollingEngine _engine; private readonly IServiceProvider _sp;
    [ObservableProperty] private NavItem? _selected; [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private string _providerStatusText = Loc.Get("Status_Provider_Starting"); [ObservableProperty] private string _intervalText = "";
    [ObservableProperty] private bool _isPaused; [ObservableProperty] private string? _banner;
    public ObservableCollection<NavItem> Items { get; }
    public ShellViewModel(PollingEngine engine, IServiceProvider sp)
    {
        _engine = engine; _sp = sp;
        Items = new(
        [
            new("Nav_Dashboard", "\uE80F", () => new PlaceholderViewModel("Nav_Dashboard")),     // Task 19 replaces with sp.GetRequiredService<DashboardViewModel>()
            new("Nav_Monitoring", "\uE9D9", () => new PlaceholderViewModel("Nav_Monitoring")),   // Task 17 replaces with sp.GetRequiredService<MonitoringViewModel>()
            new("Nav_Tests", "\uE9D9", () => new PlaceholderViewModel("Nav_Tests")), new("Nav_Benchmarks", "\uE9D2", () => new PlaceholderViewModel("Nav_Benchmarks")),
            new("Nav_Gpu", "\uE7F4", () => new PlaceholderViewModel("Nav_Gpu")), new("Nav_Cpu", "\uE950", () => new PlaceholderViewModel("Nav_Cpu")),
            new("Nav_Network", "\uE968", () => new PlaceholderViewModel("Nav_Network")), new("Nav_Storage", "\uEDA2", () => new PlaceholderViewModel("Nav_Storage")),
            new("Nav_Gaming", "\uE7FC", () => new PlaceholderViewModel("Nav_Gaming")), new("Nav_WindowsTools", "\uE90F", () => new PlaceholderViewModel("Nav_WindowsTools")),
            new("Nav_Reports", "\uE9F9", () => new PlaceholderViewModel("Nav_Reports")),
            new("Nav_Settings", "\uE713", () => new PlaceholderViewModel("Nav_Settings")),     // Task 20 replaces with sp.GetRequiredService<SettingsViewModel>()
        ]);
        engine.Provider.StatusChanged += s => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => ProviderStatusText = Describe(s));
        engine.StateChanged += s => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => IsPaused = s == EngineState.Paused);
        IntervalText = Loc.Format("Status_Interval", engine.FastInterval.TotalSeconds);
    }
    partial void OnSelectedChanged(NavItem? value) { if (value is not null) CurrentPage = value.PageFactory(); }
    [RelayCommand] private void TogglePause() { if (_engine.State == EngineState.Paused) _engine.Resume(); else _engine.Pause(); }
    public void ShowBanner(string text) => Banner = text;
    public void RefreshInterval() => IntervalText = Loc.Format("Status_Interval", _engine.FastInterval.TotalSeconds);
    public static string Describe(ProviderStatus s) => s.State switch
    {
        ProviderState.Ready => Loc.Format("Status_Provider_Ready", s.SensorCount),
        ProviderState.Degraded => Loc.Format("Status_Provider_Degraded", Loc.Get(s.ReasonKey ?? "")),
        ProviderState.Failed => Loc.Format("Status_Provider_Failed", Loc.Get(s.ReasonKey ?? "") + (s.Detail is null ? "" : $" ({s.Detail})")),
        _ => Loc.Get("Status_Provider_Starting")
    };
}
```
(Glyphs are Segoe MDL2 Assets code points; set `FontFamily="Segoe MDL2 Assets"` on the icon TextBlock. `App.OnStartup` selects the first item after the window is created so page factories run only when the DI graph is complete.)

`MainWindow.xaml`: `Window` (Title `{loc:Loc App_Title}`, MinWidth 960, MinHeight 600, Background `{StaticResource Brush.Background}`), root `Grid` with two rows (content, 28 px status bar) and two columns (200 px sidebar, *). Sidebar: `ListBox ItemsSource="{Binding Items}" SelectedItem="{Binding Selected}"` with an item template of icon `TextBlock` + label `TextBlock Text="{Binding Label}" TextTrimming="None" TextWrapping="NoWrap"`. Content: `ContentControl Content="{Binding CurrentPage}"` with `DataTemplate`s mapping `PlaceholderViewModel`→`PlaceholderView`, `DashboardViewModel`→`DashboardView`, `MonitoringViewModel`→`MonitoringView`, `SettingsViewModel`→`SettingsView`. Banner: a `Border` above content bound to `Banner` (collapsed when null). Status bar: `TextBlock Text="{Binding ProviderStatusText}"`, `TextBlock Text="{Binding IntervalText}"`, `Button Command="{Binding TogglePauseCommand}"` whose content switches between `Status_Pause`/`Status_Resume` by `IsPaused` (DataTrigger). `PlaceholderView.xaml`: title + body text blocks.
`MainWindow.xaml.cs`: on `Closing`, save `MainWindow` placement into `AppConfig` and call `store.Save(config)` (get both from `App.Services`).

- [ ] **Step 6: Build and launch smoke**

Run: `"$DOTNET" build Mazesta.sln -c Debug` → Expected: 0 warnings.
Run: `"$DOTNET" run --project src/Mazesta.Desktop -c Debug` (from WSL this opens a Windows window; unelevated in Debug). Expected: window with a sidebar of 12 entries (Dashboard, Monitoring, Tests…Settings), every page a placeholder for now, status bar shows `Sensors: degraded — Not running as administrator…` (Debug/unelevated) or `Sensors: ready (N sensors)` when launched elevated. No exception in `%LocalAppData%\Mazesta\Test\logs`. Close the window; process exits.
Screenshot for the record: `powershell.exe -Command "Add-Type -AssemblyName System.Windows.Forms,System.Drawing; $b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $bmp=New-Object System.Drawing.Bitmap $b.Width,$b.Height; $g=[System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size); $bmp.Save('F:\Projects\darabi\artifacts\shots\shell.png')"` (create the folder first).

- [ ] **Step 7: Commit** — `git add -A && git commit -m "feat(desktop): app shell with DI, dark theme, localization, status bar and placeholder pages"`

---

### Task 16: HelpTip control and Persian help resources

**Files:**
- Create: `src/Mazesta.Desktop/Controls/HelpTip.cs`, `src/Mazesta.Desktop/Localization/Help.fa.resx`, `src/Mazesta.Desktop/Localization/HelpText.cs`
- Modify: `Themes/Dark.xaml` (HelpTip style)

**Interfaces:**
- Produces: `public sealed class HelpTip : Button { public static readonly DependencyProperty HelpKeyProperty; public string HelpKey { get; set; } }` — click or Enter/Space opens a RTL Persian `Popup`; `HelpText.Get(key)` reads `Help.fa.resx` (always Persian, regardless of UI language, per spec §9.3).

- [ ] **Step 1: Help resource file**

`Localization/Help.fa.resx` keys (Persian explanations, one per feature label used in Dashboard, Monitoring and Settings): `Monitoring_Search=جست‌وجو بر اساس نام قطعه یا سنسور. فهرست به‌صورت زنده فیلتر می‌شود.`, `Monitoring_Interval=فاصله خواندن سنسورها. مقدار کمتر یعنی به‌روزرسانی سریع‌تر و مصرف CPU کمی بیشتر.`, `Monitoring_ResetStats=کمینه، بیشینه و میانگین همه سنسورها از این لحظه دوباره محاسبه می‌شوند.`, `Monitoring_Pause=خواندن سنسورها متوقف می‌شود. در نمودارها این بازه به‌صورت شکاف نمایش داده می‌شود.`, `Column_Current=آخرین مقدار خوانده‌شده. «دریافت نشد» یعنی سنسور مقداری نداده است؛ عدد تخمینی نمایش داده نمی‌شود.`, `Column_MinMaxAvg=کمینه، بیشینه و میانگین از زمان آخرین بازنشانی آمار.`, `Column_State=وضعیت داده: قدیمی یعنی قطعه در آخرین خواندن پاسخ نداده؛ نامعتبر یعنی مقدار خارج از محدوده فیزیکی است.`, `Dashboard_Cpu=دمای پکیج، فرکانس مؤثر، بار و توان CPU.`, `Dashboard_Gpu=دمای هسته، بار، فرکانس، توان و حافظه GPU. هر کارت گرافیک جداگانه نمایش داده می‌شود.`, `Dashboard_HotSpot=داغ‌ترین نقطه GPU که توسط درایور گزارش می‌شود. اگر کارت این سنسور را ندارد، «دریافت نشد» نمایش داده می‌شود و با دمای هسته جایگزین نمی‌شود.`, `Dashboard_Ram=حافظه استفاده‌شده، آزاد و کل به همراه ماژول‌های نصب‌شده.`, `Dashboard_Inventory=مشخصات سیستم از Windows (WMI) و کتابخانه سنسور.`, `Chart_Window=بازه زمانی نمایش نمودار. بازه‌های بلندتر از داده دقیقه‌ای (کمینه/بیشینه/میانگین) استفاده می‌کنند.`, `Settings_Language=زبان رابط کاربری. نام قطعات همیشه انگلیسی می‌ماند.`, `Settings_FastInterval=فاصله خواندن دما، فرکانس و بار.`, `Settings_StorageInterval=فاصله خواندن SMART دیسک‌ها. خواندن مکرر SMART توصیه نمی‌شود؛ حداقل ۶۰ ثانیه.`, `Settings_ShopName=نام فروشگاه که در گزارش مشتری چاپ می‌شود. نام محصول قابل تغییر نیست.`, `Settings_DataFolder=محل ذخیره تنظیمات و لاگ. در حالت Portable کنار فایل اجرایی است.`, `Status_Provider=وضعیت کتابخانه سنسور. «ناقص» یعنی بخشی از سنسورها به دلیل نبود دسترسی Administrator یا درایور PawnIO در دسترس نیست.`

- [ ] **Step 2: Implement the control**

```csharp
// Localization/HelpText.cs
using System.Resources;
namespace Mazesta.Desktop.Localization;
public static class HelpText
{
    private static readonly ResourceManager Rm = new("Mazesta.Desktop.Localization.Help", typeof(HelpText).Assembly);
    public static string Get(string key) => Rm.GetString(key, System.Globalization.CultureInfo.GetCultureInfo("fa-IR")) ?? key;
}
// Controls/HelpTip.cs
using System.Windows; using System.Windows.Controls; using System.Windows.Controls.Primitives; using System.Windows.Input; using System.Windows.Media;
namespace Mazesta.Desktop.Controls;
public sealed class HelpTip : Button
{
    public static readonly DependencyProperty HelpKeyProperty = DependencyProperty.Register(nameof(HelpKey), typeof(string), typeof(HelpTip), new PropertyMetadata(""));
    public string HelpKey { get => (string)GetValue(HelpKeyProperty); set => SetValue(HelpKeyProperty, value); }
    private readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    public HelpTip()
    {
        Content = "?"; Width = 18; Height = 18; Padding = new Thickness(0); Margin = new Thickness(4, 0, 4, 0); Focusable = true; ToolTip = "راهنما";
        SetResourceReference(StyleProperty, "HelpTipStyle");
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 320, FlowDirection = FlowDirection.RightToLeft, TextAlignment = TextAlignment.Right, Margin = new Thickness(12) };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "App.Font"); text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        var border = new Border { Child = text, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt"); border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        _popup.Child = border; _popup.PlacementTarget = this;
        Click += (_, _) => { text.Text = Localization.HelpText.Get(HelpKey); _popup.IsOpen = !_popup.IsOpen; };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { _popup.IsOpen = false; e.Handled = true; } };
    }
}
```
`Dark.xaml` adds `HelpTipStyle` (TargetType `Controls:HelpTip`, round `Border` template with `CornerRadius=9`, muted foreground, accent background on `IsMouseOver`/`IsKeyboardFocused`, visible focus ring).

- [ ] **Step 3: Verify** — Build with 0 warnings. Temporarily drop `<controls:HelpTip HelpKey="Status_Provider"/>` beside the status-bar provider text in `MainWindow.xaml` (keep it: it is the help for that label); run the app, click and Tab+Enter to the "?" — a Persian RTL popup appears; Escape closes it.
- [ ] **Step 4: Commit** — `git add -A && git commit -m "feat(desktop): Persian help popup control and help resources"`

---

### Task 17: Monitoring page (grouped grid, search, interval, reset, pause, focus, persisted expansion)

**Files:**
- Create: `src/Mazesta.Desktop/ViewModels/SensorRowViewModel.cs`, `ViewModels/HardwareGroupViewModel.cs`, `ViewModels/MonitoringViewModel.cs`, `Views/MonitoringView.xaml(.cs)`
- Test: `tests/Mazesta.Desktop.Tests/MonitoringViewModelTests.cs` — create project `tests/Mazesta.Desktop.Tests` (`net10.0-windows`, `UseWPF=true`, references Desktop + Monitoring.Tests fakes via `<Compile Include="../Mazesta.Monitoring.Tests/Fakes/*.cs" />`); add to the solution; add `<InternalsVisibleTo Include="Mazesta.Desktop.Tests" />` to **both** `Mazesta.Desktop.csproj` and `Mazesta.Monitoring.csproj` (the tests call `PollingEngine.TickOnce`/`PrepareForManualTicks`).

**Interfaces:**
- Consumes: `PollingEngine` (`Hardware`, `Statistics`, `SetFastInterval`, `Pause/Resume`), a `Func<Action, object>` dispatch delegate (the app passes `Dispatcher.BeginInvoke`; tests pass a synchronous invoker), `MonitoringFocus`, `AppConfig.ExpandedGroups`, `ChartWindowService.Open(SensorDefinition, HardwareNode)` (Task 18; stub interface `IChartWindowService` here).
- Produces:
```csharp
public interface IChartWindowService { void Open(SensorDefinition sensor, HardwareNode node); }
public sealed partial class SensorRowViewModel : ObservableObject { SensorDefinition Definition; string SubGroup; string Name; string Current; string Min; string Max; string Avg; string Unit; string State; DataQuality Quality; bool IsVisible; void Apply(SensorReading r, SensorStats s); }
public sealed partial class HardwareGroupViewModel : ObservableObject { HardwareNode Node; string Title; HardwareKind Kind; bool IsExpanded; bool IsVisible; ObservableCollection<SensorRowViewModel> Rows; }
public sealed partial class MonitoringViewModel : ObservableObject, IDisposable
{
    public MonitoringViewModel(PollingEngine engine, MonitoringFocus focus, AppConfig config, IChartWindowService charts, Func<Action, object> dispatch);
    ObservableCollection<HardwareGroupViewModel> Groups; string FilterText; int SelectedIntervalSeconds; int[] Intervals => MonitoringOptions.AllowedFastSeconds; bool IsPaused;
    IRelayCommand ResetStatsCommand, TogglePauseCommand; IRelayCommand<SensorRowViewModel> OpenChartCommand;
    internal void ApplySnapshot(SensorSnapshot s); internal void ApplyFocus(FocusRequest r);
}
```

- [ ] **Step 1: Write the failing tests**

```csharp
using Mazesta.Core.Hardware; using Mazesta.Desktop.ViewModels; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Desktop.Tests;
public class MonitoringViewModelTests
{
    private sealed class NoCharts : IChartWindowService { public List<SensorDefinition> Opened = []; public void Open(SensorDefinition s, HardwareNode n) => Opened.Add(s); }
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static (MonitoringViewModel vm, PollingEngine e, FakeSensorProvider p, AppConfig cfg, NoCharts charts, MonitoringFocus focus) Build()
    {
        var c = new FakeClock(T0); var p = new FakeSensorProvider();
        p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Cpu, "cpu/intelcpu-0", "temperature/0", "clock/0")); p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Gpu, "gpu/nvidiagpu-0", "temperature/0")); p.Nodes.Add(FakeSensorProvider.Node(HardwareKind.Storage, "storage/S1", "temperature/0"));
        var e = new PollingEngine(p, c, new MonitoringOptions(), new BoundedEventLog(c, NullLogger.Instance)); e.PrepareForManualTicks();
        var cfg = new AppConfig { ExpandedGroups = ["gpu/nvidiagpu-0"] }; var charts = new NoCharts(); var focus = new MonitoringFocus();
        return (new MonitoringViewModel(e, focus, cfg, charts, a => { a(); return null!; }), e, p, cfg, charts, focus);
    }
    [Fact] public void Groups_follow_hardware_and_expansion_comes_from_config()
    { var (vm, _, _, _, _, _) = Build(); Assert.Equal(3, vm.Groups.Count); Assert.False(vm.Groups[0].IsExpanded); Assert.True(vm.Groups[1].IsExpanded); }
    [Fact] public void Snapshot_updates_rows_without_changing_expansion()
    {
        var (vm, e, _, _, _, _) = Build(); vm.Groups[0].IsExpanded = true; var s = e.TickOnce()!; vm.ApplySnapshot(s);
        Assert.Equal("42.0", vm.Groups[0].Rows[0].Current); Assert.True(vm.Groups[0].IsExpanded); Assert.True(vm.Groups[1].IsExpanded);
    }
    [Fact] public void Missing_reading_shows_not_available_text_not_zero()
    {
        var (vm, e, p, _, _, _) = Build(); var id = p.Nodes[0].Sensors[0].Id;
        p.OnPoll = r => new PollResult([new SensorReading(id, null, r.Now, DataQuality.Missing, "f")], new Dictionary<HardwareId, NodeStatus> { [p.Nodes[0].Id] = NodeStatus.Healthy(r.Now) });
        vm.ApplySnapshot(e.TickOnce()!); var row = vm.Groups[0].Rows[0];
        Assert.Equal(Mazesta.Desktop.Localization.Loc.Get("Value_NotAvailable"), row.Current); Assert.Equal(DataQuality.Missing, row.Quality); Assert.NotEqual("0", row.Current);
    }
    [Fact] public void Filter_hides_non_matching_rows_and_groups()
    { var (vm, _, _, _, _, _) = Build(); vm.FilterText = "clock"; Assert.True(vm.Groups[0].IsVisible); Assert.False(vm.Groups[0].Rows[0].IsVisible); Assert.True(vm.Groups[0].Rows[1].IsVisible); Assert.False(vm.Groups[1].IsVisible); vm.FilterText = "nvidia"; Assert.True(vm.Groups[1].IsVisible); }
    [Fact] public void Interval_change_goes_to_engine_and_config()
    { var (vm, e, _, cfg, _, _) = Build(); vm.SelectedIntervalSeconds = 5; Assert.Equal(TimeSpan.FromSeconds(5), e.FastInterval); Assert.Equal(5, cfg.FastIntervalSeconds); }
    [Fact] public void Expansion_change_is_written_to_config()
    { var (vm, _, _, cfg, _, _) = Build(); vm.Groups[0].IsExpanded = true; Assert.Contains("cpu/intelcpu-0", cfg.ExpandedGroups); vm.Groups[1].IsExpanded = false; Assert.DoesNotContain("gpu/nvidiagpu-0", cfg.ExpandedGroups); }
    [Fact] public void Focus_request_expands_only_requested_kinds_once()
    {
        var (vm, e, _, _, _, focus) = Build(); vm.Groups[0].IsExpanded = true;
        focus.RequestFocus(new HashSet<HardwareKind> { HardwareKind.Gpu, HardwareKind.Storage }, "test");
        Assert.Equal([false, true, true], vm.Groups.Select(g => g.IsExpanded));
        vm.Groups[0].IsExpanded = true; vm.ApplySnapshot(e.TickOnce()!); Assert.True(vm.Groups[0].IsExpanded);   // ticks never reset user choice
    }
    [Fact] public void Open_chart_command_uses_chart_service()
    { var (vm, _, _, _, charts, _) = Build(); vm.OpenChartCommand.Execute(vm.Groups[0].Rows[1]); Assert.Single(charts.Opened); Assert.Equal("clock/0", charts.Opened[0].Name); }
    [Fact] public void Reset_clears_statistics()
    { var (vm, e, p, _, _, _) = Build(); vm.ApplySnapshot(e.TickOnce()!); vm.ResetStatsCommand.Execute(null); Assert.Equal(0, e.Statistics.Get(p.Nodes[0].Sensors[0].Id).Count); }
}
```

- [ ] **Step 2: Run** `"$DOTNET" test tests/Mazesta.Desktop.Tests -c Debug` → Expected: compile failure.

- [ ] **Step 3: Implement view models**

```csharp
// ViewModels/SensorRowViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Monitoring;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class SensorRowViewModel(SensorDefinition definition, string subGroup) : ObservableObject
{
    public SensorDefinition Definition { get; } = definition; public string SubGroup { get; } = subGroup; public string Name => Definition.Name; public string Unit => Units.Symbol(Definition.Unit);
    [ObservableProperty] private string _current = Loc.Get("Value_NotAvailable"); [ObservableProperty] private string _min = ""; [ObservableProperty] private string _max = ""; [ObservableProperty] private string _avg = "";
    [ObservableProperty] private string _state = ""; [ObservableProperty] private DataQuality _quality = DataQuality.Missing; [ObservableProperty] private bool _isVisible = true;
    public void Apply(SensorReading r, SensorStats s)
    {
        Quality = r.Quality;
        Current = r.Quality == DataQuality.Ok && r.Value is { } v ? Units.Format(v, Definition.Unit)
                : r.Quality == DataQuality.Stale && r.Value is { } sv ? Units.Format(sv, Definition.Unit) : Loc.Get("Value_NotAvailable");
        State = r.Quality switch { DataQuality.Ok => "", DataQuality.Stale => Loc.Get("Value_Stale"), DataQuality.Invalid => Loc.Get("Value_Invalid"), _ => Loc.Get("Value_NotAvailable") };
        Min = s.Min is { } mn ? Units.Format(mn, Definition.Unit) : ""; Max = s.Max is { } mx ? Units.Format(mx, Definition.Unit) : ""; Avg = s.Average is { } av ? Units.Format(av, Definition.Unit) : "";
    }
}
// ViewModels/HardwareGroupViewModel.cs
using System.Collections.ObjectModel; using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Hardware;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class HardwareGroupViewModel(HardwareNode node) : ObservableObject
{
    public HardwareNode Node { get; } = node; public string Title => Node.Name; public HardwareKind Kind => Node.Kind; public string Id => Node.Id.Value;
    [ObservableProperty] private bool _isExpanded; [ObservableProperty] private bool _isVisible = true;
    public ObservableCollection<SensorRowViewModel> Rows { get; } = [];
}
// ViewModels/MonitoringViewModel.cs
using System.Collections.ObjectModel; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Core.Hardware; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.ViewModels;
public interface IChartWindowService { void Open(SensorDefinition sensor, HardwareNode node); }
public sealed partial class MonitoringViewModel : ObservableObject, IDisposable
{
    private readonly PollingEngine _engine; private readonly MonitoringFocus _focus; private readonly AppConfig _config; private readonly IChartWindowService _charts; private readonly Func<Action, object> _dispatch;
    private readonly Dictionary<SensorId, SensorRowViewModel> _rows = []; private bool _suppressConfig;
    public ObservableCollection<HardwareGroupViewModel> Groups { get; } = [];
    public int[] Intervals => MonitoringOptions.AllowedFastSeconds;
    [ObservableProperty] private string _filterText = ""; [ObservableProperty] private int _selectedIntervalSeconds; [ObservableProperty] private bool _isPaused;
    public MonitoringViewModel(PollingEngine engine, MonitoringFocus focus, AppConfig config, IChartWindowService charts, Func<Action, object> dispatch)
    {
        _engine = engine; _focus = focus; _config = config; _charts = charts; _dispatch = dispatch; _selectedIntervalSeconds = (int)engine.FastInterval.TotalSeconds; _isPaused = engine.State == EngineState.Paused;
        var byId = engine.Hardware.ToDictionary(n => n.Id);
        foreach (var node in engine.Hardware.Where(n => n.ParentId is null))
        {
            var g = new HardwareGroupViewModel(node) { IsExpanded = config.ExpandedGroups.Contains(node.Id.Value) };
            AddRows(g, node, ""); foreach (var sub in engine.Hardware.Where(n => n.ParentId == node.Id)) AddRows(g, sub, sub.Name);
            g.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HardwareGroupViewModel.IsExpanded) && !_suppressConfig) Persist(g); };
            Groups.Add(g);
        }
        engine.SnapshotPublished += OnSnapshot; engine.StateChanged += OnState; focus.FocusRequested += OnFocus;
    }
    private void AddRows(HardwareGroupViewModel g, HardwareNode node, string sub) { foreach (var s in node.Sensors) { var row = new SensorRowViewModel(s, sub); _rows[s.Id] = row; g.Rows.Add(row); } }
    private void Persist(HardwareGroupViewModel g) { if (g.IsExpanded) { if (!_config.ExpandedGroups.Contains(g.Id)) _config.ExpandedGroups.Add(g.Id); } else _config.ExpandedGroups.Remove(g.Id); }
    private void OnSnapshot(SensorSnapshot s) => _dispatch(() => ApplySnapshot(s));
    private void OnState(EngineState s) => _dispatch(() => IsPaused = s == EngineState.Paused);
    private void OnFocus(FocusRequest r) => _dispatch(() => ApplyFocus(r));
    internal void ApplySnapshot(SensorSnapshot s) { foreach (var r in s.Readings) if (_rows.TryGetValue(r.Id, out var row)) row.Apply(r, _engine.Statistics.Get(r.Id)); }
    internal void ApplyFocus(FocusRequest r) { foreach (var g in Groups) g.IsExpanded = r.Kinds.Contains(g.Kind); }
    partial void OnFilterTextChanged(string value)
    {
        foreach (var g in Groups)
        {
            bool groupMatch = value.Length == 0 || g.Title.Contains(value, StringComparison.OrdinalIgnoreCase);
            bool any = false; foreach (var row in g.Rows) { row.IsVisible = groupMatch || row.Name.Contains(value, StringComparison.OrdinalIgnoreCase) || row.SubGroup.Contains(value, StringComparison.OrdinalIgnoreCase); any |= row.IsVisible; }
            g.IsVisible = any;
        }
    }
    partial void OnSelectedIntervalSecondsChanged(int value) { _engine.SetFastInterval(TimeSpan.FromSeconds(value)); _config.FastIntervalSeconds = value; }
    [RelayCommand] private void ResetStats() => _engine.Statistics.ResetAll(DateTimeOffset.UtcNow);
    [RelayCommand] private void TogglePause() { if (_engine.State == EngineState.Paused) _engine.Resume(); else _engine.Pause(); }
    [RelayCommand] private void OpenChart(SensorRowViewModel? row) { if (row is null) return; var node = _engine.Hardware.First(n => n.Id == row.Definition.Hardware); _charts.Open(row.Definition, node); }
    public void Dispose() { _engine.SnapshotPublished -= OnSnapshot; _engine.StateChanged -= OnState; _focus.FocusRequested -= OnFocus; }
}
```
Register in `Bootstrapper`: `s.AddTransient<MonitoringViewModel>(sp => new MonitoringViewModel(sp.GetRequiredService<PollingEngine>(), sp.GetRequiredService<MonitoringFocus>(), sp.GetRequiredService<AppConfig>(), sp.GetRequiredService<IChartWindowService>(), a => System.Windows.Application.Current.Dispatcher.BeginInvoke(a)));` and `s.AddSingleton<IChartWindowService>(new NoOpChartWindowService())` where `NoOpChartWindowService : IChartWindowService { public void Open(SensorDefinition s, HardwareNode n) { } }` lives in `Services/` until Task 18 replaces it. In `ShellViewModel`, change the `Nav_Monitoring` factory to `() => sp.GetRequiredService<MonitoringViewModel>()`.

- [ ] **Step 4: View**

`Views/MonitoringView.xaml`: toolbar `DockPanel` (TextBox `Text="{Binding FilterText, UpdateSourceTrigger=PropertyChanged}"` with `HelpTip HelpKey="Monitoring_Search"`; `ComboBox ItemsSource="{Binding Intervals}" SelectedItem="{Binding SelectedIntervalSeconds}"` + HelpTip `Monitoring_Interval`; `Button Content="{loc:Loc Monitoring_ResetStats}" Command="{Binding ResetStatsCommand}"` + HelpTip; pause button bound to `TogglePauseCommand` with `IsPaused` trigger + HelpTip `Monitoring_Pause`). Body: `ScrollViewer` → `ItemsControl ItemsSource="{Binding Groups}"`; item template = `Expander IsExpanded="{Binding IsExpanded, Mode=TwoWay}" Visibility="{Binding IsVisible, Converter={StaticResource BoolToVis}}"` with header = kind-coloured dot + `Title`; content = `ListView ItemsSource="{Binding Rows}"` (`VirtualizingPanel.IsVirtualizing=True`, `ScrollViewer.CanContentScroll=True`) with `GridView` columns Sensor (shows `SubGroup · Name` when SubGroup non-empty), Current, Min, Max, Avg, Unit, State; row style: `Foreground` = `Brush.TextMuted` when `Quality != Ok`, State cell shows a glyph (`` info) plus text; `Visibility` bound to `IsVisible`. Column headers carry HelpTips (`Column_Current`, `Column_MinMaxAvg`, `Column_State`). Double-click on a row (`MouseDoubleClick` in code-behind → `OpenChartCommand.Execute(row)`); keyboard: Enter on selected row does the same. `MonitoringView.xaml.cs` also calls `Dispose()` on the VM when unloaded.

- [ ] **Step 5: Run tests and app** — `"$DOTNET" test tests/Mazesta.Desktop.Tests` passes; launch elevated (`build.ps1` then run `artifacts` exe, or `dotnet run -c Release` which triggers UAC): groups for CPU, GPUs, Total Memory, Virtual Memory, DIMMs, motherboard (with SuperIO sub-rows), NVMe, network adapters appear; search, interval, reset and pause work; expansion persists after restart. Screenshot to `artifacts/shots/monitoring.png`.
- [ ] **Step 6: Commit** — `git add -A && git commit -m "feat(desktop): live monitoring page with grouped grid, search, interval, pause and focus"`

---

### Task 18: TimeSeriesChart control, ChartWindow, ChartWindowService with restore

**Files:**
- Create: `src/Mazesta.Desktop/Controls/TimeSeriesChart.cs`, `ViewModels/ChartWindowViewModel.cs`, `Views/ChartWindow.xaml(.cs)`, `Services/ChartWindowService.cs`
- Test: `tests/Mazesta.Desktop.Tests/TimeSeriesChartTests.cs` (pure layout math), `tests/Mazesta.Desktop.Tests/ChartWindowViewModelTests.cs`

**Interfaces:**
- Produces:
```csharp
public readonly record struct ChartPoint(double X, double Y, bool Gap);
public static class ChartScale { public static (double min, double max, double step) Nice(double dataMin, double dataMax, int targetTicks = 5); public static IReadOnlyList<ChartPoint> Project(RawSeries raw, int windowSeconds, int nowSeconds, double width, double height, double yMin, double yMax); }
public sealed class TimeSeriesChart : FrameworkElement { DP RawSeries Series; DP MinuteSeries Minutes; DP bool UseMinutes; DP int WindowSeconds; DP int NowSeconds; DP Brush SeriesBrush; DP string UnitSymbol; }
public sealed partial class ChartWindowViewModel : ObservableObject, IDisposable { string Title; int[] WindowChoices => [1,5,10,30,60,360,1440]; int WindowMinutes; RawSeries Raw; MinuteSeries Minutes; bool UseMinutes; string CurrentText, MinText, MaxText; int NowSeconds; Brush SeriesBrush; internal void Refresh(); }
public sealed class ChartWindowService(PollingEngine engine, AppConfig config) : IChartWindowService { public void Open(SensorDefinition, HardwareNode); public void RestoreFromConfig(); public void PersistOpenWindows(); }
```

- [ ] **Step 1: Write the failing tests**

```csharp
using Mazesta.Desktop.Controls; using Mazesta.Monitoring;
namespace Mazesta.Desktop.Tests;
public class TimeSeriesChartTests
{
    [Fact] public void Nice_scale_pads_and_rounds() { var (mn, mx, step) = ChartScale.Nice(41.2, 58.9); Assert.True(mn <= 41.2 && mx >= 58.9); Assert.Equal(5, step); Assert.Equal(40, mn); Assert.Equal(60, mx); }
    [Fact] public void Flat_series_still_gets_a_visible_range() { var (mn, mx, _) = ChartScale.Nice(50, 50); Assert.True(mx > mn); }
    [Fact] public void Projection_keeps_window_and_marks_gaps()
    {
        var raw = new RawSeries([0, 2, 4, 6, 8], [1f, 2f, float.NaN, 4f, 5f]);
        var pts = ChartScale.Project(raw, windowSeconds: 6, nowSeconds: 8, width: 100, height: 50, yMin: 0, yMax: 10);
        Assert.Equal(4, pts.Count);                                        // seconds 2..8 only
        Assert.True(pts[1].Gap); Assert.Equal(100, pts[^1].X, 3); Assert.Equal(25, pts[^1].Y, 3);   // y=5 of 0..10 → middle, top-left origin
    }
}
public class ChartWindowViewModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    [Fact] public void Uses_minute_tier_when_window_exceeds_raw_coverage()
    {
        var c = new Mazesta.Monitoring.Tests.Fakes.FakeClock(T0); var p = new Mazesta.Monitoring.Tests.Fakes.FakeSensorProvider(); p.Nodes.Add(Mazesta.Monitoring.Tests.Fakes.FakeSensorProvider.Node(Mazesta.Core.Hardware.HardwareKind.Cpu, "cpu/x", "temperature/0"));
        var e = new PollingEngine(p, c, new MonitoringOptions(), new BoundedEventLog(c, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)); e.PrepareForManualTicks(); e.TickOnce();
        var vm = new Mazesta.Desktop.ViewModels.ChartWindowViewModel(e, p.Nodes[0].Sensors[0], p.Nodes[0], a => { a(); return null!; });
        vm.WindowMinutes = 10; vm.Refresh(); Assert.False(vm.UseMinutes);       // 10 min × 2 s = 300 points ≤ 900 raw
        vm.WindowMinutes = 360; vm.Refresh(); Assert.True(vm.UseMinutes);       // 6 h > raw coverage
        Assert.Equal("42.0", vm.CurrentText);
    }
}
```

- [ ] **Step 2: Run tests** → Expected: compile failure.

- [ ] **Step 3: Implement**

```csharp
// Controls/TimeSeriesChart.cs
using System.Globalization; using System.Windows; using System.Windows.Media; using Mazesta.Monitoring;
namespace Mazesta.Desktop.Controls;
public readonly record struct ChartPoint(double X, double Y, bool Gap);
public static class ChartScale
{
    public static (double min, double max, double step) Nice(double dataMin, double dataMax, int targetTicks = 5)
    {
        if (double.IsNaN(dataMin) || double.IsNaN(dataMax)) return (0, 1, 0.2);
        if (dataMax - dataMin < 1e-9) { dataMin -= 1; dataMax += 1; }
        double range = dataMax - dataMin, rough = range / targetTicks, mag = Math.Pow(10, Math.Floor(Math.Log10(rough))), norm = rough / mag;
        double step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10) * mag;
        return (Math.Floor(dataMin / step) * step, Math.Ceiling(dataMax / step) * step, step);
    }
    public static IReadOnlyList<ChartPoint> Project(RawSeries raw, int windowSeconds, int nowSeconds, double width, double height, double yMin, double yMax)
    {
        var pts = new List<ChartPoint>(); int start = nowSeconds - windowSeconds; double ySpan = Math.Max(yMax - yMin, 1e-9);
        for (int i = 0; i < raw.Seconds.Length; i++)
        {
            if (raw.Seconds[i] < start) continue;
            double x = (raw.Seconds[i] - start) / (double)windowSeconds * width; bool gap = float.IsNaN(raw.Values[i]);
            pts.Add(new ChartPoint(x, gap ? 0 : height - (raw.Values[i] - yMin) / ySpan * height, gap));
        }
        return pts;
    }
}
public sealed class TimeSeriesChart : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = Register(nameof(Series), typeof(RawSeries)), MinutesProperty = Register(nameof(Minutes), typeof(MinuteSeries)), UseMinutesProperty = Register(nameof(UseMinutes), typeof(bool)),
        WindowSecondsProperty = Register(nameof(WindowSeconds), typeof(int)), NowSecondsProperty = Register(nameof(NowSeconds), typeof(int)), SeriesBrushProperty = Register(nameof(SeriesBrush), typeof(Brush)), UnitSymbolProperty = Register(nameof(UnitSymbol), typeof(string));
    private static DependencyProperty Register(string n, Type t) => DependencyProperty.Register(n, t, typeof(TimeSeriesChart), new FrameworkPropertyMetadata(t.IsValueType ? Activator.CreateInstance(t) : null, FrameworkPropertyMetadataOptions.AffectsRender));
    public RawSeries Series { get => (RawSeries)GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public MinuteSeries Minutes { get => (MinuteSeries)GetValue(MinutesProperty); set => SetValue(MinutesProperty, value); }
    public bool UseMinutes { get => (bool)GetValue(UseMinutesProperty); set => SetValue(UseMinutesProperty, value); }
    public int WindowSeconds { get => (int)GetValue(WindowSecondsProperty); set => SetValue(WindowSecondsProperty, value); }
    public int NowSeconds { get => (int)GetValue(NowSecondsProperty); set => SetValue(NowSecondsProperty, value); }
    public Brush SeriesBrush { get => (Brush)GetValue(SeriesBrushProperty); set => SetValue(SeriesBrushProperty, value); }
    public string UnitSymbol { get => (string)GetValue(UnitSymbolProperty) ?? ""; set => SetValue(UnitSymbolProperty, value); }
    private const double LeftAxis = 56, Bottom = 24, Top = 8, Right = 8;
    protected override void OnRender(DrawingContext dc)
    {
        double w = Math.Max(ActualWidth - LeftAxis - Right, 10), h = Math.Max(ActualHeight - Top - Bottom, 10);
        var axisPen = new Pen((Brush)FindResource("Brush.Border"), 1); var textBrush = (Brush)FindResource("Brush.TextMuted"); var font = new Typeface((FontFamily)FindResource("App.Font"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        dc.DrawRectangle((Brush)FindResource("Brush.Surface"), null, new Rect(0, 0, ActualWidth, ActualHeight));
        // data range
        var minutes = Minutes.Minute is null ? new MinuteSeries([], [], [], []) : Minutes; var series = Series.Seconds is null ? new RawSeries([], []) : Series;
        RawSeries raw = UseMinutes ? new RawSeries(minutes.Minute.Select(m => m * 60 + 30).ToArray(), minutes.Avg) : series;
        int start = NowSeconds - WindowSeconds; var visible = raw.Values.Where((v, i) => raw.Seconds[i] >= start && !float.IsNaN(v)).ToArray();
        var (yMin, yMax, step) = visible.Length == 0 ? (0, 1, 0.2) : ChartScale.Nice(visible.Min(), visible.Max());
        // grid + y labels
        for (double y = yMin; y <= yMax + 1e-9; y += step)
        {
            double py = Top + h - (y - yMin) / (yMax - yMin) * h; dc.DrawLine(axisPen, new Point(LeftAxis, py), new Point(LeftAxis + w, py));
            var ft = new FormattedText(y.ToString("0.#", CultureInfo.InvariantCulture) + " " + UnitSymbol, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 11, textBrush, 1.0); dc.DrawText(ft, new Point(LeftAxis - ft.Width - 4, py - ft.Height / 2));
        }
        // x labels (4 ticks, "-mm:ss" or "-hh:mm")
        for (int i = 0; i <= 4; i++)
        {
            double px = LeftAxis + w * i / 4; int secAgo = WindowSeconds - WindowSeconds * i / 4; string label = WindowSeconds >= 3600 ? $"-{secAgo / 3600}:{secAgo % 3600 / 60:00}" : $"-{secAgo / 60}:{secAgo % 60:00}";
            var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, 11, textBrush, 1.0); dc.DrawText(ft, new Point(px - ft.Width / 2, Top + h + 4));
        }
        // min/max band for minute tier
        if (UseMinutes && minutes.Minute.Length > 0)
        {
            var band = new StreamGeometry(); using (var g = band.Open())
            {
                bool open = false; var backs = new List<Point>();
                for (int i = 0; i < minutes.Minute.Length; i++)
                {
                    int sec = minutes.Minute[i] * 60 + 30; if (sec < start || float.IsNaN(minutes.Max[i])) { if (open) { for (int b = backs.Count - 1; b >= 0; b--) g.LineTo(backs[b], true, false); backs.Clear(); open = false; } continue; }
                    double px = LeftAxis + (sec - start) / (double)WindowSeconds * w, pMax = Top + h - (minutes.Max[i] - yMin) / (yMax - yMin) * h, pMin = Top + h - (minutes.Min[i] - yMin) / (yMax - yMin) * h;
                    if (!open) { g.BeginFigure(new Point(px, pMax), true, true); open = true; } else g.LineTo(new Point(px, pMax), true, false); backs.Add(new Point(px, pMin));
                }
                if (open) for (int b = backs.Count - 1; b >= 0; b--) g.LineTo(backs[b], true, false);
            }
            var bandBrush = SeriesBrush.Clone(); bandBrush.Opacity = 0.18; dc.DrawGeometry(bandBrush, null, band);
        }
        // line with gaps
        var pts = ChartScale.Project(raw, WindowSeconds, NowSeconds, w, h, yMin, yMax); var pen = new Pen(SeriesBrush, 1.6) { LineJoin = PenLineJoin.Round };
        var geo = new StreamGeometry(); using (var g = geo.Open()) { bool pendown = false; foreach (var p in pts) { if (p.Gap) { pendown = false; continue; } var pt = new Point(LeftAxis + p.X, Top + p.Y); if (!pendown) { g.BeginFigure(pt, false, false); pendown = true; } else g.LineTo(pt, true, false); } }
        dc.DrawGeometry(null, pen, geo);
        // gap markers: hatched vertical strip between neighbours of a gap
        var gapBrush = (Brush)FindResource("Brush.StateMissing"); for (int i = 1; i < pts.Count; i++) if (pts[i].Gap && !pts[i - 1].Gap) { double x0 = LeftAxis + pts[i - 1].X; int j = i; while (j < pts.Count && pts[j].Gap) j++; double x1 = LeftAxis + (j < pts.Count ? pts[j].X : w); dc.DrawRectangle(new SolidColorBrush(((SolidColorBrush)gapBrush).Color) { Opacity = 0.25 }, null, new Rect(x0, Top, Math.Max(x1 - x0, 2), h)); }
    }
}
// ViewModels/ChartWindowViewModel.cs
using System.Windows.Media; using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization; using Mazesta.Monitoring;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class ChartWindowViewModel : ObservableObject, IDisposable
{
    private readonly PollingEngine _engine; private readonly Func<Action, object> _dispatch;
    public SensorDefinition Sensor { get; } public HardwareNode Node { get; }
    public string Title => $"{Node.Name} — {Sensor.Name} ({Units.Symbol(Sensor.Unit)})"; public string UnitSymbol => Units.Symbol(Sensor.Unit);
    public int[] WindowChoices => [1, 5, 10, 30, 60, 360, 1440];
    [ObservableProperty] private int _windowMinutes = 10; [ObservableProperty] private RawSeries _raw; [ObservableProperty] private MinuteSeries _minutes; [ObservableProperty] private bool _useMinutes;
    [ObservableProperty] private string _currentText = Loc.Get("Value_NotAvailable"); [ObservableProperty] private string _minText = ""; [ObservableProperty] private string _maxText = ""; [ObservableProperty] private int _nowSeconds;
    public Brush SeriesBrush => (Brush)System.Windows.Application.Current.FindResource($"Brush.Series.{Node.Kind}");
    public ChartWindowViewModel(PollingEngine engine, SensorDefinition sensor, HardwareNode node, Func<Action, object> dispatch)
    { _engine = engine; Sensor = sensor; Node = node; _dispatch = dispatch; engine.SnapshotPublished += OnSnapshot; }
    private void OnSnapshot(SensorSnapshot s) => _dispatch(Refresh);
    partial void OnWindowMinutesChanged(int value) => Refresh();
    internal void Refresh()
    {
        int windowSeconds = WindowMinutes * 60; NowSeconds = _engine.History.SecondsSinceEpoch(DateTimeOffset.UtcNow);
        UseMinutes = windowSeconds > _engine.History.RawCapacity * (int)_engine.FastInterval.TotalSeconds;
        Raw = _engine.History.GetRaw(Sensor.Id); Minutes = _engine.History.GetMinutes(Sensor.Id);
        var last = Raw.Values.Length > 0 ? Raw.Values[^1] : float.NaN; CurrentText = float.IsNaN(last) ? Loc.Get("Value_NotAvailable") : Units.Format(last, Sensor.Unit);
        var st = _engine.Statistics.Get(Sensor.Id); MinText = st.Min is { } mn ? Units.Format(mn, Sensor.Unit) : ""; MaxText = st.Max is { } mx ? Units.Format(mx, Sensor.Unit) : "";
    }
    public void Dispose() => _engine.SnapshotPublished -= OnSnapshot;
}
// Services/ChartWindowService.cs
using System.Windows; using Mazesta.Core.Hardware; using Mazesta.Desktop.ViewModels; using Mazesta.Desktop.Views; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.Services;
public sealed class ChartWindowService(PollingEngine engine, AppConfig config) : IChartWindowService
{
    private readonly List<ChartWindow> _open = [];
    public void Open(SensorDefinition sensor, HardwareNode node) => Open(sensor, node, null, 10);
    private void Open(SensorDefinition sensor, HardwareNode node, WindowPlacement? placement, int minutes)
    {
        var vm = new ChartWindowViewModel(engine, sensor, node, a => Application.Current.Dispatcher.BeginInvoke(a)) { WindowMinutes = minutes };
        var w = new ChartWindow { DataContext = vm, Owner = Application.Current.MainWindow };
        if (placement is { } p) { w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = p.Left; w.Top = p.Top; w.Width = p.Width; w.Height = p.Height; }
        w.Closed += (_, _) => { _open.Remove(w); vm.Dispose(); }; _open.Add(w); w.Show(); vm.Refresh();
    }
    public void RestoreFromConfig()
    {
        foreach (var c in config.ChartWindows.ToList())
        {
            var sensor = engine.Hardware.SelectMany(n => n.Sensors).FirstOrDefault(s => s.Id.Value == c.SensorId); if (sensor is null) continue;
            Open(sensor, engine.Hardware.First(n => n.Id == sensor.Hardware), c.Placement, c.WindowMinutes);
        }
    }
    public void PersistOpenWindows() => config.ChartWindows = _open.Select(w => { var vm = (ChartWindowViewModel)w.DataContext; return new ChartWindowConfig(vm.Sensor.Id.Value, new WindowPlacement(w.Left, w.Top, w.Width, w.Height, false), vm.WindowMinutes); }).ToList();
}
```
`Views/ChartWindow.xaml`: `Window` (Title `{Binding Title}`, 640×360, `FlowDirection` follows `Loc.IsRtl` set in code-behind) with a header row (title, `ComboBox ItemsSource="{Binding WindowChoices}" SelectedItem="{Binding WindowMinutes}"` + `HelpTip HelpKey="Chart_Window"`, current/min/max text) and `controls:TimeSeriesChart Series="{Binding Raw}" Minutes="{Binding Minutes}" UseMinutes="{Binding UseMinutes}" WindowSeconds="{Binding WindowMinutes, Converter={StaticResource MinutesToSeconds}}" NowSeconds="{Binding NowSeconds}" SeriesBrush="{Binding SeriesBrush}" UnitSymbol="{Binding UnitSymbol}"`. Add `MinutesToSeconds` converter to Dark.xaml resources (`IValueConverter` class in Controls).
Wire-up: replace the `NoOpChartWindowService` registration with `s.AddSingleton<IChartWindowService, Services.ChartWindowService>()` and delete the no-op class. `App.OnStartup` calls `RestoreFromConfig()` after the engine reports `Ready`/`Degraded` (subscribe once to `Provider.StatusChanged`); `MainWindow.Closing` calls `PersistOpenWindows()` before saving config.

- [ ] **Step 4: Run tests** → Expected: chart tests pass. Launch elevated, double-click three sensors, press Pause for 10 s then Resume → each chart shows a grey gap strip; switch a window to 6 h → min/max band renders; close and reopen the app → the same chart windows reopen.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(desktop): gap-aware time-series chart windows with restore"`

---

### Task 19: Dashboard page (role cards, inventory, Mazesta card)

**Files:**
- Create: `src/Mazesta.Desktop/ViewModels/SensorCardViewModel.cs`, `ViewModels/DashboardViewModel.cs`, `Views/DashboardView.xaml(.cs)`, `Assets/am9.jpg`
- Modify: `docs/THIRD-PARTY-NOTICES.md` (image source line)
- Test: `tests/Mazesta.Desktop.Tests/DashboardViewModelTests.cs`

**Interfaces:**
- Consumes: `PollingEngine`, `IInventoryProvider`, `SensorRole`, `HardwareInventory`, `AppConfig.ShopName`.
- Produces:
```csharp
public sealed partial class SensorCardViewModel : ObservableObject { string TitleKey; string Title; HardwareKind Kind; Brush Accent; ObservableCollection<CardLine> Lines; bool HasAnySensor; }
public sealed partial class CardLine : ObservableObject { string Label; string Value; SensorId? Id; }
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    public DashboardViewModel(PollingEngine engine, IInventoryProvider inventory, AppConfig config, Func<Action, object> dispatch);
    SensorCardViewModel Cpu, HotSpot, Ram; ObservableCollection<SensorCardViewModel> Gpus; ObservableCollection<(string Label, string Value)> Inventory; string ShopName; string InventoryStatus;
    string ProductName, ProductDescription, ProductUrl, SiteUrl, ContactUrl, Phones; IRelayCommand<string> OpenUrlCommand;
    internal void ApplySnapshot(SensorSnapshot s); internal static SensorDefinition? Pick(HardwareNode node, params SensorRole[] preference);
}
```

- [ ] **Step 1: Product image asset**

```bash
cd /mnt/f/Projects/darabi/src/Mazesta.Desktop/Assets
curl -sL -A "Mozilla/5.0" -o am9.jpg "https://www.dfmrendering.com/wp-content/uploads/2026/07/%D8%B3%DB%8C%D8%B3%D8%AA%D9%85-%D8%B1%D9%86%D8%AF%D8%B1-%D9%85%D8%B9%D9%85%D8%A7%D8%B1%DB%8C-%D9%88-%D8%B4%D8%A8%DB%8C%D9%87-%D8%B3%D8%A7%D8%B2%DB%8C-AM9-%D9%85%D8%A7%D8%B2%D8%B3%D8%AA%D8%A7-6-1.jpg"
ls -la am9.jpg; file am9.jpg
```
If the file is larger than 400 KB, resize with PowerShell: `powershell.exe -Command "Add-Type -AssemblyName System.Drawing; $i=[System.Drawing.Image]::FromFile('F:\Projects\darabi\src\Mazesta.Desktop\Assets\am9.jpg'); $r=[int](640*$i.Height/$i.Width); $b=New-Object System.Drawing.Bitmap 640,$r; $g=[System.Drawing.Graphics]::FromImage($b); $g.InterpolationMode='HighQualityBicubic'; $g.DrawImage($i,0,0,640,$r); $i.Dispose(); $b.Save('F:\Projects\darabi\src\Mazesta.Desktop\Assets\am9-small.jpg',[System.Drawing.Imaging.ImageFormat]::Jpeg)"` then replace `am9.jpg` with the small file. Append to `docs/THIRD-PARTY-NOTICES.md`: `Product image AM9: retrieved 2026-09-12 from https://www.dfmrendering.com/shop/systems/am9-architectural-rendering-ryzen-9900x-rtx5060ti/ (owner's own site), embedded for offline display. Contact numbers 09197588700 / 09197588701 from https://www.dfmrendering.com/contactus/ (retrieved 2026-09-12).`

- [ ] **Step 2: Write the failing tests**

```csharp
using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Desktop.ViewModels; using Mazesta.Hardware; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Desktop.Tests;
public class DashboardViewModelTests
{
    private sealed class Inv(HardwareInventory i) : IInventoryProvider { public Task<HardwareInventory> ReadAsync(CancellationToken ct) => Task.FromResult(i); }
    private static readonly DateTimeOffset T0 = new(2026, 9, 12, 0, 0, 0, TimeSpan.Zero);
    private static HardwareNode Gpu(bool hotSpot)
    {
        var id = new HardwareId("gpu/nvidiagpu-0"); var sensors = new List<SensorDefinition> { new(SensorId.Create(id, "temperature/0"), id, "GPU Core", SensorKind.Temperature, Unit.Celsius, SensorRole.GpuCoreTemp, 0) };
        if (hotSpot) sensors.Add(new(SensorId.Create(id, "temperature/1"), id, "GPU Hot Spot", SensorKind.Temperature, Unit.Celsius, SensorRole.GpuHotSpotTemp, 1));
        return new HardwareNode(id, HardwareKind.Gpu, HardwareVendor.Nvidia, "RTX 4090", null, true, sensors);
    }
    private static (DashboardViewModel vm, PollingEngine e, FakeSensorProvider p) Build(bool hotSpot)
    {
        var c = new FakeClock(T0); var p = new FakeSensorProvider(); p.Nodes.Add(Gpu(hotSpot));
        var e = new PollingEngine(p, c, new MonitoringOptions(), new BoundedEventLog(c, NullLogger.Instance)); e.PrepareForManualTicks();
        var inv = HardwareInventory.Empty with { Cpu = new CpuInfo("i9", HardwareVendor.Intel, 24, 32, 3200, null), Errors = ["bios: access denied"] };
        return (new DashboardViewModel(e, new Inv(inv), new AppConfig { ShopName = "X" }, a => { a(); return null!; }), e, p);
    }
    [Fact] public void Hot_spot_card_reads_not_available_when_gpu_lacks_sensor()
    { var (vm, e, _) = Build(false); vm.ApplySnapshot(e.TickOnce()!); Assert.False(vm.HotSpot.HasAnySensor); Assert.Equal(Mazesta.Desktop.Localization.Loc.Get("Value_NotAvailable"), vm.HotSpot.Lines[0].Value); }
    [Fact] public void Hot_spot_card_shows_value_when_present()
    { var (vm, e, _) = Build(true); vm.ApplySnapshot(e.TickOnce()!); Assert.True(vm.HotSpot.HasAnySensor); Assert.Equal("42.0 °C", vm.HotSpot.Lines[0].Value); }
    [Fact] public void Pick_prefers_first_role_in_order()
    {
        var node = Gpu(true); Assert.Equal(SensorRole.GpuHotSpotTemp, DashboardViewModel.Pick(node, SensorRole.GpuHotSpotTemp, SensorRole.GpuCoreTemp)!.Role);
        Assert.Equal(SensorRole.GpuCoreTemp, DashboardViewModel.Pick(Gpu(false), SensorRole.GpuHotSpotTemp, SensorRole.GpuCoreTemp)!.Role); Assert.Null(DashboardViewModel.Pick(Gpu(false), SensorRole.GpuVramTemp));
    }
    [Fact] public async Task Inventory_lists_available_fields_and_surfaces_errors()
    { var (vm, _, _) = Build(true); await vm.InventoryLoaded; Assert.Contains(vm.Inventory, i => i.Label == "CPU" && i.Value.Contains("i9")); Assert.Contains("bios", vm.InventoryStatus); }
    [Fact] public void Product_card_has_no_price_and_official_links()
    { var (vm, _, _) = Build(true); Assert.DoesNotContain("تومان", vm.ProductDescription); Assert.StartsWith("https://www.dfmrendering.com/", vm.ProductUrl); Assert.Equal("https://www.dfmrendering.com/contactus/", vm.ContactUrl); }
}
```

- [ ] **Step 3: Run tests** → Expected: compile failure.

- [ ] **Step 4: Implement**

```csharp
// ViewModels/SensorCardViewModel.cs
using System.Collections.ObjectModel; using System.Windows.Media; using CommunityToolkit.Mvvm.ComponentModel; using Mazesta.Core.Hardware; using Mazesta.Desktop.Localization;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class CardLine(string label, SensorId? id, Unit unit) : ObservableObject
{ public string Label { get; } = label; public SensorId? Id { get; } = id; public Unit Unit { get; } = unit; [ObservableProperty] private string _value = Loc.Get("Value_NotAvailable"); }
public sealed partial class SensorCardViewModel(string titleKey, HardwareKind kind, string? subtitle = null) : ObservableObject
{
    public string Title => Loc.Get(titleKey) + (subtitle is null ? "" : $" · {subtitle}"); public string HelpKey => titleKey; public HardwareKind Kind => kind;
    public Brush Accent => (Brush)System.Windows.Application.Current.FindResource($"Brush.Series.{kind}");
    public ObservableCollection<CardLine> Lines { get; } = []; public bool HasAnySensor => Lines.Any(l => l.Id is not null);
}
// ViewModels/DashboardViewModel.cs
using System.Collections.ObjectModel; using System.Diagnostics; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Core.Hardware; using Mazesta.Core.Inventory; using Mazesta.Desktop.Localization; using Mazesta.Hardware; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly PollingEngine _engine; private readonly Func<Action, object> _dispatch; private readonly Dictionary<SensorId, List<CardLine>> _lines = [];
    public SensorCardViewModel Cpu { get; } = new("Dashboard_Cpu", HardwareKind.Cpu); public SensorCardViewModel HotSpot { get; } = new("Dashboard_HotSpot", HardwareKind.Gpu); public SensorCardViewModel Ram { get; } = new("Dashboard_Ram", HardwareKind.Memory);
    public ObservableCollection<SensorCardViewModel> Gpus { get; } = []; public ObservableCollection<(string Label, string Value)> Inventory { get; } = [];
    [ObservableProperty] private string _inventoryStatus = ""; public string ShopName { get; }
    public string ProductName => "سیستم رندر معماری و شبیه‌سازی AM9"; public string ProductDescription => "Ryzen 9 9900X · RTX 5060 Ti 16GB · 48GB DDR5";
    public string ProductUrl => "https://www.dfmrendering.com/shop/systems/am9-architectural-rendering-ryzen-9900x-rtx5060ti/"; public string SiteUrl => "https://www.dfmrendering.com/"; public string ContactUrl => "https://www.dfmrendering.com/contactus/"; public string Phones => "09197588700 · 09197588701";
    public Task InventoryLoaded { get; }
    public DashboardViewModel(PollingEngine engine, IInventoryProvider inventory, AppConfig config, Func<Action, object> dispatch)
    {
        _engine = engine; _dispatch = dispatch; ShopName = config.ShopName;
        foreach (var node in engine.Hardware.Where(n => n.ParentId is null))
            switch (node.Kind)
            {
                case HardwareKind.Cpu: Line(Cpu, "Package", node, SensorRole.CpuPackageTemp, SensorRole.CpuTctlTdie); Line(Cpu, "Clock", node, SensorRole.CpuEffectiveClockAverage, SensorRole.CpuCoreClockAverage, SensorRole.CpuCoreClock); Line(Cpu, "Load", node, SensorRole.CpuTotalLoad); Line(Cpu, "Power", node, SensorRole.CpuPackagePower); break;
                case HardwareKind.Gpu:
                    var card = new SensorCardViewModel("Dashboard_Gpu", HardwareKind.Gpu, node.Name); Gpus.Add(card);
                    Line(card, "Core", node, SensorRole.GpuCoreTemp); Line(card, "Load", node, SensorRole.GpuLoad3D, SensorRole.GpuLoadD3D3D); Line(card, "Clock", node, SensorRole.GpuCoreClock); Line(card, "Power", node, SensorRole.GpuPower); Line(card, "VRAM used", node, SensorRole.GpuVramUsed); Line(card, "VRAM total", node, SensorRole.GpuVramTotal);
                    Line(HotSpot, node.Name, node, SensorRole.GpuHotSpotTemp); break;
                case HardwareKind.Memory when node.Id.Value == "memory/ram": Line(Ram, "Used", node, SensorRole.RamUsed); Line(Ram, "Free", node, SensorRole.RamFree); Line(Ram, "Load", node, SensorRole.RamLoad); break;
            }
        if (HotSpot.Lines.Count == 0) HotSpot.Lines.Add(new CardLine("GPU", null, Unit.Celsius));
        engine.SnapshotPublished += OnSnapshot;
        InventoryLoaded = LoadInventoryAsync(inventory);
    }
    private void Line(SensorCardViewModel card, string label, HardwareNode node, params SensorRole[] roles)
    {
        var def = Pick(node, roles); var line = new CardLine(label, def?.Id, def?.Unit ?? Unit.None); card.Lines.Add(line);
        if (def is not null) { if (!_lines.TryGetValue(def.Id, out var list)) _lines[def.Id] = list = []; list.Add(line); }
    }
    internal static SensorDefinition? Pick(HardwareNode node, params SensorRole[] preference) => preference.Select(r => node.Sensors.FirstOrDefault(s => s.Role == r)).FirstOrDefault(s => s is not null);
    private void OnSnapshot(SensorSnapshot s) => _dispatch(() => ApplySnapshot(s));
    internal void ApplySnapshot(SensorSnapshot s)
    {
        foreach (var r in s.Readings) if (_lines.TryGetValue(r.Id, out var lines)) foreach (var l in lines) l.Value = r.Quality == DataQuality.Ok && r.Value is { } v ? Units.FormatWithSymbol(v, l.Unit) : Loc.Get("Value_NotAvailable");
    }
    private async Task LoadInventoryAsync(IInventoryProvider provider)
    {
        var inv = await provider.ReadAsync(CancellationToken.None).ConfigureAwait(false);
        _dispatch(() =>
        {
            void Add(string label, string? value) => Inventory.Add((label, value ?? Loc.Get("Value_NotAvailable")));
            Add("CPU", inv.Cpu is { } c ? $"{c.Name} ({c.PhysicalCores}C/{c.LogicalProcessors}T)" : null);
            foreach (var g in inv.Gpus) Add("GPU", $"{g.Name} · driver {g.DriverVersion}");
            Add("RAM", inv.TotalPhysicalMemoryBytes is { } t ? $"{t / 1024.0 / 1024 / 1024:F0} GB · {inv.MemoryModules.Count} modules" : null);
            Add("Motherboard", inv.Motherboard is { } m ? $"{m.Manufacturer} {m.Product}" : null); Add("BIOS", inv.Bios is { } b ? $"{b.Version} ({b.ReleaseDate:yyyy-MM-dd})" : null);
            foreach (var d in inv.Storage) Add("Disk", $"{d.FriendlyName} · {d.BusType} · {d.SizeBytes / 1e9:F0} GB · {d.HealthStatus}");
            Add("OS", inv.Os is { } o ? $"{o.Caption} {o.Version}" : null);
            InventoryStatus = inv.Errors.Count == 0 ? "" : string.Join("; ", inv.Errors);
            App.LogStartup("Inventory ready");
        });
    }
    [RelayCommand] private void OpenUrl(string? url) { if (url is not null) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    public void Dispose() => _engine.SnapshotPublished -= OnSnapshot;
}
```
`App.LogStartup` already exists (Task 15) and is a no-op when `App.Services` is null, so the view model is testable. Register `DashboardViewModel` with the dispatcher lambda as for `MonitoringViewModel`, and change the `Nav_Dashboard` factory in `ShellViewModel` to `() => sp.GetRequiredService<DashboardViewModel>()`.

`Views/DashboardView.xaml`: `WrapPanel` of cards (each `Border` with the card's `Accent` as a 3 px top stripe, title + `HelpTip HelpKey="{Binding HelpKey}"`, `ItemsControl` of `Lines` as label/value rows); the `Gpus` collection renders one card per GPU; then an inventory card (`ItemsControl` of `Inventory`, `InventoryStatus` in muted text when non-empty); then the Mazesta card: shop name, the two service lines, buttons `Dashboard_Mazesta_Site`/`Dashboard_Mazesta_Contact` bound to `OpenUrlCommand` with the URLs, phones text; nested smaller product card: `Image Source="pack://application:,,,/Assets/am9.jpg"` (max height 120), `ProductName`, `ProductDescription`, button `Dashboard_Product_View` → `ProductUrl`. No price field exists anywhere.

- [ ] **Step 5: Run tests and app** → Dashboard tests pass. Elevated run on the dev box: CPU card shows package temp/clock/load/power; RTX 4090 and UHD 770 cards; Hot Spot card shows the 4090 value; RAM card; inventory lists CPU/GPU/RAM/board/BIOS/990 PRO/OS; product card image visible offline. Screenshot to `artifacts/shots/dashboard.png`.
- [ ] **Step 6: Commit** — `git add -A && git commit -m "feat(desktop): dashboard with role-based cards, inventory and offline Mazesta card"`

---

### Task 20: Settings page

**Files:**
- Create: `src/Mazesta.Desktop/ViewModels/SettingsViewModel.cs`, `Views/SettingsView.xaml(.cs)`
- Test: `tests/Mazesta.Desktop.Tests/SettingsViewModelTests.cs`

**Interfaces:**
- Produces: `public sealed partial class SettingsViewModel : ObservableObject { string Language; string FastIntervalText; string StorageIntervalText; string ShopName; string DataFolder; string ModeText; string Version; string Message; IRelayCommand SaveCommand, OpenFolderCommand; internal bool TryValidate(out int fast, out int storage, out string error); }` — `SettingsViewModel(AppConfig config, JsonStore<AppConfig> store, AppPaths paths, PollingEngine engine, MonitoringOptions options, ShellViewModel shell, Action<string> openFolder)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Mazesta.Desktop.ViewModels; using Mazesta.Monitoring; using Mazesta.Monitoring.Tests.Fakes; using Mazesta.Persistence; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Desktop.Tests;
public class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mazesta-settings-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
    private (SettingsViewModel vm, AppConfig cfg, JsonStore<AppConfig> store, PollingEngine e) Build()
    {
        var cfg = new AppConfig(); var store = new JsonStore<AppConfig>(Path.Combine(_dir, "appconfig.json"), new SchemaMigrator([new Migration0To1()]), AppConfig.CurrentSchemaVersion, NullLogger.Instance);
        var c = new FakeClock(DateTimeOffset.UnixEpoch); var opts = new MonitoringOptions(); var e = new PollingEngine(new FakeSensorProvider(), c, opts, new BoundedEventLog(c, NullLogger.Instance));
        var shell = new ShellViewModel(e, new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider());
        return (new SettingsViewModel(cfg, store, AppPaths.Create(_dir, _dir, true), e, opts, shell, _ => { }), cfg, store, e);
    }
    [Fact] public void Persian_digits_are_accepted_for_intervals()
    { var (vm, cfg, _, e) = Build(); vm.FastIntervalText = "۵"; vm.StorageIntervalText = "۱۲۰"; vm.SaveCommand.Execute(null); Assert.Equal((5, 120), (cfg.FastIntervalSeconds, cfg.StorageIntervalSeconds)); Assert.Equal(TimeSpan.FromSeconds(5), e.FastInterval); }
    [Fact] public void Storage_interval_below_60_is_rejected()
    { var (vm, cfg, _, _) = Build(); vm.StorageIntervalText = "5"; vm.SaveCommand.Execute(null); Assert.Equal(900, cfg.StorageIntervalSeconds); Assert.Equal(Mazesta.Desktop.Localization.Loc.Get("Settings_Invalid_Interval"), vm.Message); }
    [Fact] public void Fast_interval_must_be_an_allowed_value()
    { var (vm, cfg, _, _) = Build(); vm.FastIntervalText = "3"; vm.SaveCommand.Execute(null); Assert.Equal(2, cfg.FastIntervalSeconds); }
    [Fact] public void Save_writes_file_and_language_change_shows_restart_note()
    { var (vm, _, store, _) = Build(); vm.Language = "fa"; vm.ShopName = "فروشگاه"; vm.SaveCommand.Execute(null); var r = store.Load(); Assert.Equal(("fa", "فروشگاه"), (r.Value.Language, r.Value.ShopName)); Assert.Contains(Mazesta.Desktop.Localization.Loc.Get("Settings_RestartNote"), vm.Message); }
}
```

- [ ] **Step 2: Run tests** → Expected: compile failure.

- [ ] **Step 3: Implement**

```csharp
// ViewModels/SettingsViewModel.cs
using System.Reflection; using CommunityToolkit.Mvvm.ComponentModel; using CommunityToolkit.Mvvm.Input; using Mazesta.Core.Text; using Mazesta.Desktop.Localization; using Mazesta.Monitoring; using Mazesta.Persistence;
namespace Mazesta.Desktop.ViewModels;
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _config; private readonly JsonStore<AppConfig> _store; private readonly PollingEngine _engine; private readonly MonitoringOptions _options; private readonly ShellViewModel _shell; private readonly Action<string> _openFolder;
    public string[] Languages => ["en", "fa"];
    [ObservableProperty] private string _language; [ObservableProperty] private string _fastIntervalText; [ObservableProperty] private string _storageIntervalText; [ObservableProperty] private string _shopName; [ObservableProperty] private string _message = "";
    public string DataFolder { get; } public string ModeText { get; } public string Version { get; } = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
    public SettingsViewModel(AppConfig config, JsonStore<AppConfig> store, AppPaths paths, PollingEngine engine, MonitoringOptions options, ShellViewModel shell, Action<string> openFolder)
    {
        _config = config; _store = store; _engine = engine; _options = options; _shell = shell; _openFolder = openFolder;
        _language = config.Language; _fastIntervalText = config.FastIntervalSeconds.ToString(); _storageIntervalText = config.StorageIntervalSeconds.ToString(); _shopName = config.ShopName;
        DataFolder = paths.DataRoot; ModeText = Loc.Get(paths.IsPortable ? "Settings_Mode_Portable" : "Settings_Mode_Installed");
    }
    internal bool TryValidate(out int fast, out int storage, out string error)
    {
        error = ""; storage = 0;
        if (!PersianDigits.TryParseInt(FastIntervalText, out fast) || !MonitoringOptions.AllowedFastSeconds.Contains(fast)) { error = Loc.Get("Settings_Invalid_Interval"); return false; }
        if (!PersianDigits.TryParseInt(StorageIntervalText, out storage) || storage < 60) { error = Loc.Get("Settings_Invalid_Interval"); return false; }
        return true;
    }
    [RelayCommand] private void Save()
    {
        if (!TryValidate(out int fast, out int storage, out string error)) { Message = error; return; }
        bool langChanged = _config.Language != Language;
        _config.Language = Language; _config.FastIntervalSeconds = fast; _config.StorageIntervalSeconds = storage; _config.ShopName = ShopName.Trim().Length == 0 ? _config.ShopName : ShopName.Trim();
        _options.StorageInterval = TimeSpan.FromSeconds(storage); if (_engine.FastInterval != TimeSpan.FromSeconds(fast)) _engine.SetFastInterval(TimeSpan.FromSeconds(fast));
        _store.Save(_config); _shell.RefreshInterval();
        Message = Loc.Get("Settings_Saved") + (langChanged ? " " + Loc.Get("Settings_RestartNote") : "");
    }
    [RelayCommand] private void OpenFolder() => _openFolder(DataFolder);
}
```
Register: `s.AddTransient<SettingsViewModel>(sp => new SettingsViewModel(sp.GetRequiredService<AppConfig>(), sp.GetRequiredService<JsonStore<AppConfig>>(), sp.GetRequiredService<AppPaths>(), sp.GetRequiredService<PollingEngine>(), sp.GetRequiredService<MonitoringOptions>(), sp.GetRequiredService<ShellViewModel>(), dir => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true })));` and change the `Nav_Settings` factory in `ShellViewModel` to `() => sp.GetRequiredService<SettingsViewModel>()`.
`Views/SettingsView.xaml`: a two-column `Grid` of label (+ `HelpTip` with matching `Settings_*` key) / control rows: `ComboBox` for `Language`, `TextBox` for `FastIntervalText`, `StorageIntervalText`, `ShopName`; read-only `TextBox` for `DataFolder` + `Button` `Settings_OpenFolder`; `ModeText`, `Version`; `Button Settings_Save` bound to `SaveCommand`; `TextBlock` for `Message`.

- [ ] **Step 4: Run tests and app** → tests pass; in the app, Persian digits work in the interval boxes, saving updates the status-bar interval, language switch shows the restart note and after restart the shell is RTL Persian.
- [ ] **Step 5: Commit** — `git add -A && git commit -m "feat(desktop): settings page with validation and Persian digit input"`

---

### Task 21: Robustness — second-instance activation, unhandled exception handling, startup timing, dev focus shortcut

**Files:**
- Create: `src/Mazesta.Desktop/Composition/SingleInstance.cs`
- Modify: `App.xaml.cs`, `MainWindow.xaml.cs`

**Interfaces:**
- Produces: `SingleInstance.ActivateExisting()` (finds the window titled `Loc.Get("App_Title")` via `FindWindow` and calls `SetForegroundWindow`; used when `App.IsFirstInstance` is false), `App.OnDispatcherUnhandledException`, `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` handlers.

- [ ] **Step 1: Implement**

```csharp
// Composition/SingleInstance.cs
using System.Runtime.InteropServices;
namespace Mazesta.Desktop.Composition;
internal static partial class SingleInstance
{
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)] private static partial IntPtr FindWindowW(string? lpClassName, string lpWindowName);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetForegroundWindow(IntPtr hWnd);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);
    public static void ActivateExisting(string title) { var h = FindWindowW(null, title); if (h != IntPtr.Zero) { ShowWindow(h, 9 /* SW_RESTORE */); SetForegroundWindow(h); } }
}
```
In `App.OnStartup`, replace `if (!IsFirstInstance) { Shutdown(); return; }` with `if (!IsFirstInstance) { Composition.SingleInstance.ActivateExisting(Loc.Get("App_Title")); Shutdown(); return; }` (call `Loc.SetLanguage` from a quick config read first, or use the English title — both instances share the same config, so read it the same way).
Exception handlers in `OnStartup` before anything else:
```csharp
DispatcherUnhandledException += (_, e) =>
{
    Services?.GetService<ILoggerFactory>()?.CreateLogger("Unhandled").LogError(e.Exception, "Dispatcher exception");
    var r = MessageBox.Show(Loc.Get("Crash_Body"), Loc.Get("Crash_Title"), MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.Yes, Loc.IsRtl ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : 0);
    e.Handled = r == MessageBoxResult.Yes; if (!e.Handled) Shutdown(1);
};
AppDomain.CurrentDomain.UnhandledException += (_, e) => Services?.GetService<ILoggerFactory>()?.CreateLogger("Unhandled").LogCritical(e.ExceptionObject as Exception, "AppDomain exception");
TaskScheduler.UnobservedTaskException += (_, e) => { Services?.GetService<ILoggerFactory>()?.CreateLogger("Unhandled").LogError(e.Exception, "Unobserved task exception"); e.SetObserved(); };
```
Engine failure surfacing: in `ShellViewModel`, subscribe `engine.StateChanged` and when `EngineState.Failed`, set `Banner = Loc.Get("Engine_Failed")` (add key: en `Sensor engine stopped after an internal error. See the log.` / fa `موتور سنسور پس از یک خطای داخلی متوقف شد. لاگ را ببینید.`).
Developer focus shortcut for acceptance §15-7: in `MainWindow.xaml.cs` handle `PreviewKeyDown` for `Ctrl+Shift+F` → `App.Services.GetRequiredService<MonitoringFocus>().RequestFocus(new HashSet<HardwareKind> { HardwareKind.Cpu, HardwareKind.Gpu }, "dev-shortcut")` and select the Monitoring nav item.
Startup timing: `App.LogStartup("Window shown")` already exists; add `LogStartup("Provider ready")` on the first `Ready`/`Degraded` status.

- [ ] **Step 2: Verify manually** — Launch twice: the second launch brings the first window to front and exits (check `tasklist | findstr MazestaTest` shows one process). Temporarily throw from a button handler in Debug, confirm the dialog and the log line, remove the throw. Press `Ctrl+Shift+F` on the Monitoring page: only CPU and GPU groups expand, others collapse, and manual re-expansion of another group survives the next tick. Check the log for `Window shown at N ms`, `Provider ready at N ms`, `Inventory ready at N ms`.
- [ ] **Step 3: Commit** — `git add -A && git commit -m "feat(desktop): single-instance activation, crash handling, startup timing, focus shortcut"`

---

### Task 22: Hardware-category tests, idle measurements, publish, and slice documentation

**Files:**
- Create: `tests/Mazesta.Hardware.Tests/DevBoxHardwareTests.cs`, `tools/measure-idle.ps1`, `docs/PROVIDERS-AND-FALLBACKS.md`, `docs/HARDWARE-MATRIX.md`, `docs/VERIFICATION-slice1.md`, `docs/GUIDE-FA.md`, `docs/THIRD-PARTY-NOTICES.md` (complete), `artifacts/shots/*.png` (kept out of git; referenced by path in the docs)
- Modify: `README.md`

**Interfaces:**
- Consumes: `LibreHardwareMonitorProvider.CreateDefault`, `WmiInventoryProvider`, `PollRequest`, roles.

- [ ] **Step 1: Hardware tests (run elevated only)**

```csharp
using LibreHardwareMonitor.Hardware; using Mazesta.Core.Hardware; using Mazesta.Core.Time; using Mazesta.Hardware; using Mazesta.Hardware.Lhm; using Mazesta.Hardware.Wmi; using Microsoft.Extensions.Logging.Abstractions;
namespace Mazesta.Hardware.Tests;
[Trait("Category", "Hardware")]
public class DevBoxHardwareTests : IDisposable
{
    private readonly LibreHardwareMonitorProvider _p = LibreHardwareMonitorProvider.CreateDefault(new SystemClock(), NullLoggerFactory.Instance);
    private PollResult PollAll() { _p.Start(); return _p.Poll(new PollRequest(DateTimeOffset.UtcNow, _p.Hardware.Select(h => h.Id).ToHashSet())); }
    public void Dispose() => _p.Dispose();
    private static bool Elevated() { using var id = System.Security.Principal.WindowsIdentity.GetCurrent(); return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
    [Fact] public void Provider_is_ready_or_explains_why()
    { PollAll(); Assert.NotEqual(ProviderState.Failed, _p.Status.State); if (_p.Status.State == ProviderState.Degraded) Assert.Contains(_p.Status.ReasonKey, new[] { LibreHardwareMonitorProvider.ReasonPawnIoMissing, LibreHardwareMonitorProvider.ReasonNotElevated }); }
    [Fact] public void Cpu_package_temperature_present_when_elevated_with_pawnio()
    {
        var r = PollAll(); if (!Elevated() || !LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled) return;   // documented prerequisite; the status test covers the reason
        var def = _p.Hardware.SelectMany(h => h.Sensors).First(s => s.Role == SensorRole.CpuPackageTemp);
        var reading = r.Readings.Single(x => x.Id == def.Id); Assert.Equal(DataQuality.Ok, reading.Quality); Assert.InRange(reading.Value!.Value, 15, 110);
    }
    [Fact] public void Nvidia_gpu_exposes_hot_spot_and_igpu_is_separate_node()
    {
        PollAll(); var gpus = _p.Hardware.Where(h => h.Kind == HardwareKind.Gpu).ToList();
        Assert.Contains(gpus, g => g.Vendor == HardwareVendor.Nvidia && g.Sensors.Any(s => s.Role == SensorRole.GpuHotSpotTemp));
        Assert.Contains(gpus, g => g.Vendor == HardwareVendor.Intel);
    }
    [Fact] public async Task Nvme_node_id_matches_wmi_serial()
    {
        PollAll(); var inv = await new WmiInventoryProvider(new WmiQuery(), NullLogger<WmiInventoryProvider>.Instance).ReadAsync(CancellationToken.None);
        var serials = inv.Storage.Select(d => d.SerialNumber?.Trim()).Where(s => s is not null).ToList();
        Assert.Contains(_p.Hardware.Where(h => h.Kind == HardwareKind.Storage), h => serials.Any(s => h.Id == HardwareId.ForStorage(s!)));
    }
    [Fact] public void No_temperature_reports_zero_as_ok()
    { var r = PollAll(); var temps = _p.Hardware.SelectMany(h => h.Sensors).Where(s => s.Kind == SensorKind.Temperature).Select(s => s.Id).ToHashSet(); Assert.DoesNotContain(r.Readings, x => temps.Contains(x.Id) && x.Quality == DataQuality.Ok && x.Value == 0); }
}
```
Run elevated from an admin PowerShell: `dotnet test tests/Mazesta.Hardware.Tests -c Release --filter "Category=Hardware"` → record pass/fail per test in `docs/HARDWARE-MATRIX.md` for the dev box (i9-14900K / RTX 4090 / UHD 770 / MSI Z790 / 990 PRO / Win11 26200), plus which mandatory sensors from spec §3.2 were present, missing, or unsupported by the provider (e.g. Intel effective clocks, thermal-throttling flags, C-states → "not exposed by LibreHardwareMonitor 0.9.6"; VRAM temperature on the 4090 → record observed).

- [ ] **Step 2: Publish and measure idle**

```bash
cd /mnt/f/Projects/darabi && powershell.exe -ExecutionPolicy Bypass -File build.ps1 -Test -Publish
```
`tools/measure-idle.ps1`:
```powershell
param([string]$Exe = "artifacts/Mazesta-Test/MazestaTest.exe", [int]$SettleSeconds = 300, [int]$SampleSeconds = 30)
$p = Start-Process -FilePath $Exe -PassThru -Verb RunAs
Start-Sleep -Seconds $SettleSeconds
$p.Refresh(); $cpu0 = $p.TotalProcessorTime; $t0 = Get-Date
Start-Sleep -Seconds $SampleSeconds
$p.Refresh(); $cpu1 = $p.TotalProcessorTime; $t1 = Get-Date
$cpuPct = ($cpu1 - $cpu0).TotalSeconds / ($t1 - $t0).TotalSeconds / [Environment]::ProcessorCount * 100
[pscustomobject]@{ WorkingSetMB = [math]::Round($p.WorkingSet64 / 1MB, 1); PrivateMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1); IdleCpuPercent = [math]::Round($cpuPct, 2); Threads = $p.Threads.Count } | Format-List
Stop-Process $p
```
Run it (UAC prompt appears) with the app left on the Dashboard; also read `Window shown at N ms` / `Provider ready` / `Inventory ready` from the newest log. Record all numbers in `docs/VERIFICATION-slice1.md` as measured, with "met" or "missed" against < 80 MB / < 1 % / < 3 s. If memory is missed, try in this order and re-measure after each: (1) confirm `PublishReadyToRun=true` was used, (2) set `<TieredPGO>true</TieredPGO>` and `<UseSystemResourceKeys>true</UseSystemResourceKeys>` in the Desktop csproj, (3) disable LHM `IsNetworkEnabled` temporarily to see its share and put the number in the doc (then re-enable; network sensors are required). Never add working-set trimming.

- [ ] **Step 3: Acceptance evidence**

With HWiNFO, OCCT and AIDA64 closed: screenshot Dashboard and Monitoring; then close Mazesta, open HWiNFO, screenshot its CPU package / core / GPU hot-spot values within a minute, and put the pairs in `docs/HARDWARE-MATRIX.md` with the ±2 °C check and an explanation of package vs core vs hot spot. Three chart windows with a pause gap → `artifacts/shots/charts.png`. Language switched to Persian → `artifacts/shots/shell-fa.png` (RTL, Vazirmatn glyphs). PawnIO-absent check: from an admin PowerShell run `sc.exe stop PawnIO` (if the service exists), launch the app, confirm the Degraded banner and that CPU temperature rows read «دریافت نشد», then `sc.exe start PawnIO`; record it. Network check: run `netstat -b -n` (admin) twice during a 10-minute session and confirm no `MazestaTest.exe` connections; record it.

- [ ] **Step 4: Docs**

- `docs/PROVIDERS-AND-FALLBACKS.md`: the table from spec §5.4 plus the observed PawnIO detection method (`PawnIo.IsInstalled`), the Debug `asInvoker` vs Release `requireAdministrator` manifests, and the exact LHM sensor names per role (copy the `SensorRoleMap` table).
- `docs/HARDWARE-MATRIX.md`: per-machine table (this box only for now; AMD "untested on hardware").
- `docs/VERIFICATION-slice1.md`: date, build hash, test counts (unit + hardware), measurements, acceptance items 1–10 from the spec with evidence paths, and an honest "known gaps" list (e.g. sensor-id stability for duplicate GPUs, sensors LHM does not expose).
- `docs/GUIDE-FA.md`: راهنمای فارسی for this slice: اجرا با Administrator، نصب PawnIO، صفحه داشبورد، مانیتورینگ (جست‌وجو، فاصله، بازنشانی، توقف، نمودار با دوبار کلیک، معنی «دریافت نشد/قدیمی/نامعتبر»)، تنظیمات (زبان، فاصله‌ها، نام فروشگاه، پوشه داده، حالت Portable با فایل `portable.marker`).
- `docs/THIRD-PARTY-NOTICES.md`: LibreHardwareMonitorLib 0.9.6 (MPL-2.0), DiskInfoToolkit 1.1.2 (MPL-2.0), RAMSPDToolkit-NDD 1.4.2 (MPL-2.0), HidSharp (Apache-2.0), System.Management / Microsoft.Extensions.* (MIT), CommunityToolkit.Mvvm (MIT), Vazirmatn (SIL OFL 1.1), PawnIO (separate install; licence per its site), product image and contact sources.
- `README.md`: update prerequisites (PawnIO link, .NET 10 Desktop Runtime), build/test/publish commands, portable marker note.

- [ ] **Step 5: Final full run and commit**

Run: `"$DOTNET" build Mazesta.sln -c Release` → 0 warnings; `"$DOTNET" test Mazesta.sln -c Release --filter "Category!=Hardware"` → all pass (report the count).
```bash
git add -A && git commit -m "test(hardware): dev-box hardware tests; docs: providers, hardware matrix, verification, Persian guide, notices"
```

---

## Self-review notes (kept for the executor)

- Spec §3.3 "Reset stats" is `ResetStatsCommand` (Task 17); "interval 1/2/5/30" is `MonitoringOptions.AllowedFastSeconds` (Tasks 9, 17, 20); "auto-expand once at test start" is `MonitoringFocus` + `ApplyFocus` (Tasks 11, 17, 21); "gap in charts" is NaN storage (Task 10) + gap strips (Task 18); "bounded history" is Task 10 with the byte-budget test.
- Spec §5.2 isolation and cadence: Task 7; §5.3 WMI: Task 8; §6 engine, stale, stats, events: Tasks 9–12; §7 persistence: Tasks 13–14; §8 desktop: Tasks 15–21; §10 tests and §13 acceptance: every task's tests plus Task 22.
- Names used across tasks: `PollResult`, `PollRequest`, `NodeStatus.Healthy/Failed/NeverUpdated`, `ProviderStatus.Ready/Degraded/Failed`, `SensorStats`, `RawSeries`, `MinuteSeries`, `FocusRequest`, `IChartWindowService`, `Loc.Get/Format`, `HelpTip.HelpKey`, `App.Services`, `App.LogStartup` — spelled identically in every task.
- Deviations from the spec text, all additive: `PollResult` carries node status; `SensorRole` gains `CpuCoreClockAverage`, `CpuEffectiveClockAverage`, `GpuLoadD3D3D`, `StoragePowerOnHours`; `HardwareInventory` gains `TotalPhysicalMemoryBytes`; Debug builds use an `asInvoker` manifest with an explicit "not elevated" status so unelevated runs stay honest.
