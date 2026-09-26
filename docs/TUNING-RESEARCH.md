# Overclocking and undervolting: what can be done from Windows

Research for slice 9 (2026-09-26). It records which interfaces exist, which the app uses, and why the rest are not used yet.
"Verified" means tried on the dev box. Everything else comes from vendor documentation or open-source projects and is marked as such.

## GPU

| Vendor | Interface | What it offers | Status in the app |
|---|---|---|---|
| NVIDIA | **NVML** (`nvml.dll`, ships with every driver, documented) | Clock offsets per P-state (`nvmlDeviceGet/SetClockOffsets`, driver 555+), core clock lock (`nvmlDeviceSetGpuLockedClocks`, Volta+), power limit, fan speed per fan and back to auto (`nvmlDeviceSetFanSpeed_v2` / `SetDefaultFanSpeed_v2`) | **Used.** All settings need admin and are dropped on a reboot or driver reset. |
| NVIDIA | NVAPI private calls (the V/F curve used by MSI Afterburner) | A per-point voltage/frequency curve and a voltage readout | Not used: undocumented, IDs change between drivers, and nothing can check it against documentation. |
| AMD | **ADLX** (AMD's SDK, `amdadlx64.dll` in the driver) | Manual GPU tuning (clock, voltage), VRAM tuning, power, fan curve, plus AMD's own *auto undervolt / auto overclock* presets | Not built in yet. C++/COM-style interfaces; needs a binding and an AMD card to verify on. |
| Intel Arc | **IGCL** (Intel Graphics Control Library, `ControlLib.dll`) | Overclock: frequency offset, voltage offset, power and temperature limits, fan | Not built in yet, same reasons. |

### Undervolting through NVML
NVML has no voltage setting. The documented way to undervolt (the same one Linux users use with `nvidia-smi -lgc` and a clock offset) is:
1. cap the core clock at the clock the card sustains at stock (`SetGpuLockedClocks(0, cap)`);
2. apply a positive core offset (`SetClockOffsets`), which shifts the whole voltage/frequency curve up.

The card then reaches the capped clock at a lower point of its curve, i.e. at a lower voltage. The voltage itself cannot be read through NVML, so
the app judges an undervolt by what it can measure: the same clock and compute throughput with less power or a lower temperature.

Offsets are written to every P-state with the same value: NVML applies the most restrictive offset across P-states
([NVIDIA forum](https://forums.developer.nvidia.com/t/nvmldevicegetminmaxclockofpstate-nvmldevicesetclockoffsets-issues/318332)).
The older `nvmlDeviceSetGpcClkVfOffset` returns *Not Supported* on the dev box's driver 610.62 (verified); `nvmlDeviceGetClockOffsets` works.

### The automatic search (Mazesta.Core.Tuning)
- **Undervolt:** measure stock (3 min, the first minute is warm-up) → cap = the median sustained clock, rounded down to a 15 MHz bin → offset +30,
  +60 … (max +300) with a 40 s verified compute load each → stop at the first step with a wrong result, a driver reset or a clock that no longer
  reaches the cap → back off 15 MHz → confirm with a 3 min run (up to three tries, stepping down) → keep only if the same throughput is reached
  with ≥ 2 % less power or ≥ 1 °C lower temperature.
- **Overclock:** from the undervolt (or stock), raise the cap +30 MHz per step while the card stays correct, actually clocks higher and draws no
  more than stock power (+2 %); then raise the memory offset +200 per step while the measured memory bandwidth keeps rising (GDDR6X retries its
  own errors, so an unstable memory clock shows as lower bandwidth first) → back off → confirm → keep only if the measured score beats stock by ≥ 1 %.
- A journal is written before every step and cleared after it; one found at start-up means the machine went down during that step, and the card
  is reset. The card is always put back to stock at the end; a found profile is applied only when the technician says so.

## CPU

### Intel
- **Undervolt:** the voltage offset is written through the *OC mailbox*, MSR `0x150` (what Intel XTU, ThrottleStop and `intel-undervolt` do). Since the
  Plundervolt mitigations (2019) many boards lock it, and 12th–14th gen desktop platforms add *Undervolt Protection*: with it on, only BIOS can
  change voltages. On such a board nothing from Windows can undervolt.
- **Power limits (PL1/PL2) and turbo ratios:** MSR `0x610` / `0x1AD` (and the MMIO mirror of the power limits). Writable unless the BIOS locks them.
- **Access:** all of this needs a kernel driver. The app already uses **PawnIO** (signed) for sensors; its IntelMSR module gained MSR writes for
  the package power limits and the OC mailbox in PawnIO.Modules 0.2.4
  ([release notes](https://github.com/namazso/PawnIO.Modules/releases)). WinRing0 is not an option (blocked as a vulnerable driver).
- **Not done yet:** it needs PawnIO module calls from the app, a per-board check of the locks, and a CPU stability search like the GPU one
  (the CPU stress test exists). Wrong values can hang the machine at once, so it needs real hardware to build on.

### AMD Ryzen
- **Undervolt = Curve Optimizer** (per-core negative offsets), **overclock = PBO limits (PPT/TDC/EDC) and boost override**. Both are SMU mailbox
  commands; the command IDs per CPU family are documented in the open-source **ZenStates-Core** (GPL-3), used by PBO2 Tuner, PboStudio, Zen Tuner.
  Mobile APUs: **RyzenAdj** (LGPL) sets power limits.
- **Access:** PCI config / SMN space through a driver; PawnIO has a **RyzenSMU** module. AMD Ryzen Master uses its own driver and has no public tuning API.
- **Not done yet:** same reasons as Intel, plus there is no AMD machine to verify on. GPL-3 code cannot be copied into this project without
  settling its licence; the command IDs themselves would have to be implemented from the documentation.

## Memory profiles (XMP / DOCP / EXPO)
- The profile is chosen by the BIOS while it trains the memory at power-on. There is no Windows or UEFI-runtime standard to change it.
- Vendor BIOS-setting interfaces exist on some business laptops and desktops (Dell, HP, Lenovo publish WMI classes for BIOS settings), but those
  machines usually have no XMP option at all; desktop board makers (ASUS, MSI, Gigabyte, ASRock) publish no interface for it.
- **What the app does:** shows each module's configured speed next to its SMBIOS speed (which of the two an XMP kit reports as "speed" varies by
  board, so no verdict is drawn), and restarts straight into BIOS setup (`shutdown /r /fw`, UEFI machines) after a confirmation.
