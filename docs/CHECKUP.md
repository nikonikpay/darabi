# Checkup (عیب‌یابی هوشمند)

The Checkup tab (Tests family) says in words whether the machine works as it should, so nobody has to compare numbers themselves. Every
finding stands on what this machine measured or what its parts report about themselves; nothing comes from a table of models. A rule whose
measurement is missing says nothing. Findings go into the benchmark and test reports too (HTML and text).

Levels: **Good** (checked and held), **Note** (explains a measurement, not a fault), **Attention**, **Problem**.

## Code
- Rules (pure, unit-tested): `src/Mazesta.Core/Health/Checkup/` — `CpuCheck`, `GpuCheck`, `MemoryCheck`, `PlatformCheck`, `PeerCheck`.
- Readers: TjMax from Intel's "Distance to TjMax" sensors (`SensorRole.CpuTjMaxDistance`), `PciDevice.Slot` (PCIe link from the slot's side),
  `NvidiaRunProbe` (NVML clock event reasons, sampled during a GPU run), `PowerSettings` (mains/battery, power plan).
- App: `CheckupService` (judges each CPU/GPU benchmark run from the monitor's history), `CheckupText` (wording), `WebBridge.Checkup.cs`,
  peer finding in `WebBridge.Benchmarks.cs`, page `wwwroot/js/pages/checkup.js`.

## Rules and thresholds
| Part | Rule | Level |
|---|---|---|
| CPU | All-thread run with load under 85 %: nothing judged | Note |
| CPU | Clock under load below 1 GHz (laptop charger / BD PROCHOT, power plan) | Problem |
| CPU | All-core clock at the end of the run under 85 % / 70 % of the base clock the firmware reports | Attention / Problem |
| CPU (TjMax known) | Peak within 2 °C of TjMax; clock drop ≥ 10 % makes it a Problem | Attention / Problem |
| CPU (TjMax known) | Peak within 10 °C of TjMax | Note |
| CPU (no TjMax, e.g. AMD) | Late-run temperature flat at its top (≥ 85 °C, 60 % of samples within 1.5 °C, spread ≤ 3 °C) with the clock falling ≥ 3 % / ≥ 10 % | Attention / Problem |
| CPU (no TjMax) | Flat at the top, clock steady | Note |
| CPU | Power steps down ≥ 15 % partway through with room in temperature (short/long power limits, Intel default settings) | Note |
| Power plan | Maximum processor state under 100 % (no boost) / boost mode Disabled | Attention |
| Power | On battery | Note |
| RAM (DDR4 SPD) | Running below the modules' XMP speed | Attention |
| RAM | Below the modules' JEDEC speed | Note |
| RAM (DDR5) | Profiles not decoded yet: said so | Note |
| RAM | One socketed module; or every module in one channel by slot name (`DIMM_A1`, `ChannelA-…`, `P0 CHANNEL A`) | Attention |
| RAM | Mixed part numbers or sizes | Note |
| GPU (NVIDIA) | External power brake or hardware thermal slowdown ≥ 2 % of busy samples; hardware slowdown ≥ 5 % | Problem |
| GPU (NVIDIA) | Software thermal slowdown ≥ 10 % | Attention |
| GPU (NVIDIA) | Power cap ≥ 50 % of busy samples | Note |
| GPU | Hot spot minus core (median under load) ≥ 25 °C NVIDIA / 30 °C others | Attention |
| GPU / NVMe | Link narrower than card and slot allow; generation lower under load (NVIDIA only, read live) | Attention |
| GPU / NVMe | Slot narrower or older than the device; slot unknown and link narrower (e.g. APUs give x8) | Note |
| Any benchmark | Against the median of the same part model: ≥ −10 % Good, to −20 % Attention, below Problem; fewer than 3 systems Note. A gap with ≥ 15 % less power and no more heat is a power limit (Note / Attention); hotter by ≥ 8 °C points to cooling; clock ≤ 93 % points to settings | as stated |

## Verified
- Unit tests for every rule (synthetic runs for the cases above, including AMD held at 95 °C and the Intel power step).
- On the machine this was built on (GTX 950, AMD APU board): the power plan reads (100 %, boost on); the GPU's port is one Windows treats as plain PCI,
  so the link is read from the card's side (Gen3 x16 card at x8) and the slot stays unknown.

## Not verified yet
- Intel TjMax on a real Intel CPU (the sensor mapping follows LibreHardwareMonitor's names; no Intel machine at hand).
- NVML clock event reasons on a card under load; a full checkup run in the app.
