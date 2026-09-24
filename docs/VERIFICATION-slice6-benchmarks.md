# Slice 6 — benchmark suite: verification

## What changed
- **Results are kept.** Every completed benchmark run is saved as its own report (verdict `Benchmark`: numbers and the sensors over the
  run, no pass/fail) and appears on the Reports page; the latest of each is still added to the next test report. The Benchmarks page
  view model lives for the whole session, so a run keeps going and its numbers stay when you leave the page and come back.
- **CPU** is two benchmarks, single thread and all threads, 60 s each by default (memory and the GPU benchmarks too; storage stays 20 s).
- **Storage** is measured the way CrystalDiskMark does: SEQ1M Q8T1 write and read, RND4K Q32T1 and Q1T1 read (+ latency), RND4K Q32T1
  write, with overlapped unbuffered I/O. The file is written once before timing; the drive rests 5 s after that and 5 s between the write
  and read phases. The old reads-slower-than-writes result came from queue depth 1 (a write is acknowledged from the drive's cache, a
  read waits for the flash) and from reading straight after writing.
- **GPU**, three separate benchmarks on raw Direct3D 12 (Vortice) on the adapter ComputeSharp picked:
  - Direct3D 12 rendering: 4096 lit spheres (5.2 M triangles/frame) at 2560x1440 off screen → FPS and triangle rate; 16 blended
    full-screen layers → pixel fill rate. The last frame is read back and must show the scene.
  - Ray tracing: DXR 1.1 inline ray tracing, 2305 instances, camera + shadow + up to 3 bounces at 2560x1440 → FPS and rays/s; the rays
    of one frame are counted on the GPU. GPUs below ray-tracing tier 1.1 are Unsupported.
  - AI: DirectML (the copy in Windows) 4096³ matrix multiply at three precision levels - FP32, FP16, INT8 - as AI benchmarks
    (UL Procyon) report them. A precision the GPU/DirectML cannot run is left out and named.
  - Shaders are HLSL in `src/Mazesta.Diagnostics.Gpu/Shaders`, compiled to DXIL by `tools/compile-gpu-shaders.ps1` (Windows SDK dxc);
    the `.cso` files are committed and embedded, so the build does not need the SDK.

## Measured on the owner's machine (2026-09-24, RTX 3090, Ryzen 9 3950X), short runs
- GPU (3 s runs, `GpuBenchmarkHardwareTests`): Direct3D 1985 FPS / 10.4 Gtri/s / 197 Gpixel/s (scene covers 55 % of the frame);
  ray tracing 1323 FPS / 11.7 Grays/s (2.4 rays/pixel, DXR tier 1.2); AI FP32 15.1 TFLOPS, FP16 107 TFLOPS, INT8 12.2 TOPS.
  INT8 is slower than FP16 here: DirectML's integer matmul is not accelerated on this driver, which is what an INT8 DirectML app would get.
- Storage (20 s, 1 GiB): MSI M390 NVMe (D:) SEQ1M Q8 write 1374-3253 MB/s, read 2178-3456 MB/s (consumer SLC cache varies run to run;
  the second run matches the drive's rated 3000/3300 MB/s), RND4K Q32 read ~250 K IOPS, Q1 ~10-11.7 K IOPS (86-101 µs);
  WD HDDs (E:, F:) ~110-124 MB/s both ways, ~140 IOPS at Q1.
- The frames of both GPU benchmarks were dumped once and looked at (sphere field; ray-traced spheres on a checkered ground).

## Not verified
- The Benchmarks and Reports pages on screen (the app elevates; checked only by the STA view-load test).
- A full 60 s run of each benchmark from the app, and a saved benchmark report opened in the browser/PDF.
- A PCIe 5.0 drive (none here). Depth 8 of 1 MiB is what CrystalDiskMark uses to reach ~14 GB/s on such drives; RND4K Q32 may be limited
  by the .NET async I/O path somewhere above ~250 K IOPS on very fast drives.
- GPUs other than the RTX 3090 (AMD/Intel, pre-DXR cards → Unsupported path only by reasoning).
- The storage benchmark writing to the root of C: needs administrator rights (the app runs elevated; the test run did not).
