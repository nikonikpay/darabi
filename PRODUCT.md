# Product

<!-- impeccable:product-schema 1 -->

## Platform

web (a WebView2 front end hosted inside a Windows desktop app; there is also a native WPF front end over the same core)

## Stack

Delegated. Chosen: plain HTML, CSS and JavaScript modules served from files next to the exe, with no Node and no build step, so the
web edition stays offline and portable like the rest of the app and lives in the same .NET repository. The host is a .NET 10 WPF window
with one WebView2 control. It uses the existing Core, Hardware, Monitoring, Diagnostics and Reporting projects through a JSON message
bridge. The WPF edition (`Mazesta.Desktop`) is untouched; the web edition is a separate executable.

## Users

Technicians at a PC service shop in Iran (the owner, Saeed Darabi, and staff) who diagnose customers' desktops: they check sensors, run
stress tests and benchmarks, tune NVIDIA GPUs, and hand the customer a report. The customer sees the screen at the counter and takes the
printed summary home.

## Product Purpose

A Persian-first diagnostics suite: live sensor monitoring, hardware tests with verdicts backed by measurements, speed benchmarks, GPU
undervolt/overclock through NVML, and reports (full HTML/PDF and a one-page A5 customer summary). Success is a technician trusting every
number on screen and a customer understanding the machine's state.

## Positioning

Honesty is the product: no fake or guessed data. A value that was not measured is shown as unavailable, never 0; a test that did not run is
never a pass; a pass is backed by measured evidence; sensor names come only from verified catalogs.

## Operating Context

Used at a workbench on the machine under test, often while a stress test or game loads it, sometimes on machines whose GPU driver is broken
(WPF software rendering exists for that). The app runs elevated (UAC). It is portable: settings, reports and history live in a `Data` folder
next to the exe. Idle cost matters: nothing animates while hidden, no timers that wake without need.

## Capabilities and Constraints

- Dashboard, Monitoring (every sensor, grouped by hardware, min/avg/max, charts), Tests (queue with durations, repeat, options),
  System Info, Benchmarks (single or queued), component pages (CPU, GPU, Storage, Network), Gaming, GPU tuning (manual, measured V/F curve,
  automatic undervolt/overclock, profiles), Windows Tools, Reports (HTML/PDF/TXT/JSON, compare, A5 customer summary), Settings, overlay.
- Persian (RTL) by default, English available. ASCII numbers for values stay Latin.
- GPU tuning is NVIDIA-only (NVML); CPU tuning is not offered. FPS, 1% low and frame time are measured for DirectX 9-12 programs through
  Windows event tracing (the overlay only); Vulkan and OpenGL frame rates are not measured and show as missing.

## Brand Commitments

- Name: Mazesta (مازستا). Logo: black wordmark in a yellow circle (`src/Mazesta.Desktop/Assets/mazesta-logo.png`, wordmark paths in
  `src/Mazesta.Reporting/MazestaLogo.cs`). Brand yellow #FDD400, used with black.
- Persian font on hand: IRANSansXFaNum (renders ASCII digits as Persian digits).

## Evidence on Hand

Real data comes from the machine at run time. There are no testimonials, customers, scores or marketing claims, and none may be invented.

## Product Principles

1. Measured or marked missing: never a placeholder number.
2. The verdict follows the evidence, and the evidence is shown next to it.
3. Right to left first, with Latin numbers kept intact.
4. Cheap when idle: the tool must not disturb the measurements it takes.
5. Portable and offline: nothing is fetched from the internet except what the technician explicitly runs, and one product card from the
   shop's own site on the dashboard (cached, shown offline from the cache, never required). Benchmark records stay on the machine.
