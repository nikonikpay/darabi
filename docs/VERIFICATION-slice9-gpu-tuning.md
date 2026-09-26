# Slice 9 — GPU overclock / undervolt: verification

## What changed
- New page **Overclock & undervolt** (after Gaming in the sidebar).
- **GPU through NVML** (`Mazesta.Hardware/Nvidia`): live clock/memory/temperature/power/fan; manual core offset, memory offset, core clock cap,
  power limit and fixed fan speed, checked against the driver's ranges (refused, not clamped); back to stock; saved profiles tied to the GPU UUID.
- **Automatic undervolt and overclock** (`Mazesta.Core.Tuning` search logic, `Mazesta.Diagnostics.Tuning.GpuAutoTuner`, load in
  `Mazesta.Diagnostics.Gpu.Tuning.ComputeGpuLoad`): see docs/TUNING-RESEARCH.md. A crash journal resets the card at the next start.
- **CPU and memory profiles:** explained per vendor on the page, not changed; restart into BIOS setup. Research in docs/TUNING-RESEARCH.md.

## Verified (2026-09-26, owner's machine, not elevated)
- Build 0 warnings; 489 non-hardware tests pass (search decisions, tuner with a fake card, form parsing, range checks, profiles per card,
  crash-journal recovery, profile JSON round-trip, the view loads).
- Read-only on real hardware: the machine now has an **RTX 3090** (driver 610.62), not the 4090 in HARDWARE-MATRIX. NVML reports core offset
  −1000…+1000 MHz, memory offset −2000…+6000, power 100…365 W (default 350), 2 fans 30…100 %, max clock 2100 MHz; live readings work
  (`NvmlTuningHardwareTests` passes). The deprecated `nvmlDeviceGetGpcClkVfOffset` returns Not Supported on this driver; the new call works.
- The page rendered offscreen in Persian/RTL with the real card and looked at; numbers with units keep their order.

## Not verified
- **Nothing was written to the GPU.** Applying settings, the clock cap, the power limit, fan control, reset, and both automatic searches need
  the elevated app and change the card; they were not run from this session. In particular unknown:
  - whether `nvmlDeviceSetGpuLockedClocks` and `nvmlDeviceSetPowerManagementLimit` are allowed on GeForce under Windows (NVML documents them for
    Volta+/Kepler+ without excluding GeForce); if the cap is refused, the automatic search stops with "the driver refused a setting";
  - the unit of the memory offset (NVML reports it as MHz; on GDDR6X it may be the doubled data rate);
  - how ComputeSharp recovers after a real driver reset mid-search.
- Restart into BIOS setup (it reboots the machine).
- AMD and Intel GPUs, and any CPU tuning: not implemented.

## Suggested first run on the owner's machine
1. Open the page, check the live line and ranges. Tick *Core clock cap*, set 1800, Apply: the clock under load must stop at 1800. Back to stock.
2. Automatic undervolt (about 10–15 minutes), nothing else running. Note the result line and the saved profile.
3. Automatic overclock right after it.
