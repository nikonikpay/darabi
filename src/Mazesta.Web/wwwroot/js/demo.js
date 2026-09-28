// Browser preview only (the app never loads this file and it is not shipped): a stand-in host with demo readings shaped like a real machine,
// so the interface can be designed and checked in a plain browser. The band says "demo data" on every page while it is in use.
let strings = null, emitRef = null, started = false;
const T0 = Date.now();

const node = (id, kind, name, sensors, parent = null) => ({ id, kind, name, vendor: "Unknown", parent, sensors: sensors.map(([p, n, k, u, r]) => ({ id: `${id}#${p}`, name: n, kind: k, unit: u, role: r })) });
const HW = [
  node("amdcpu/0", "Cpu", "AMD Ryzen 9 3950X", [
    ["temperature/2", "Core (Tctl/Tdie)", "Temperature", "Celsius", "CpuTctlTdie"], ["temperature/3", "CCD1 (Tdie)", "Temperature", "Celsius", "CpuCcdTemp"], ["temperature/4", "CCD2 (Tdie)", "Temperature", "Celsius", "CpuCcdTemp"],
    ["load/0", "CPU Total", "Load", "Percent", "CpuTotalLoad"], ["clock/avg", "Cores (Average)", "Clock", "MegaHertz", "CpuCoreClockAverage"], ["clock/eff", "Cores (Average Effective)", "Clock", "MegaHertz", "CpuEffectiveClockAverage"],
    ["power/0", "Package", "Power", "Watt", "CpuPackagePower"], ["voltage/0", "Core (SVI2 TFN)", "Voltage", "Volt", "CpuVcore"]]),
  node("gpu-nvidia/0", "Gpu", "NVIDIA GeForce RTX 3090", [
    ["temperature/0", "GPU Core", "Temperature", "Celsius", "GpuCoreTemp"], ["temperature/2", "GPU Hot Spot", "Temperature", "Celsius", "GpuHotSpotTemp"], ["temperature/3", "GPU Memory Junction", "Temperature", "Celsius", "GpuVramTemp"],
    ["load/0", "GPU Core", "Load", "Percent", "GpuLoad3D"], ["clock/0", "GPU Core", "Clock", "MegaHertz", "GpuCoreClock"], ["clock/4", "GPU Memory", "Clock", "MegaHertz", "GpuMemoryClock"],
    ["power/0", "GPU Package", "Power", "Watt", "GpuPower"], ["voltage/0", "GPU Core", "Voltage", "Volt", "GpuVoltage"], ["control/1", "GPU Fan 1", "Control", "Percent", "GpuFanPercent"],
    ["fan/1", "GPU Fan 1", "Fan", "Rpm", "GpuFanRpm"], ["smalldata/1", "GPU Memory Used", "SmallData", "Megabyte", "GpuVramUsed"], ["smalldata/2", "GPU Memory Total", "SmallData", "Megabyte", "GpuVramTotal"]]),
  node("memory/ram", "Memory", "Total Memory", [["data/0", "Memory Used", "Data", "Gigabyte", "RamUsed"], ["data/1", "Memory Available", "Data", "Gigabyte", "RamFree"], ["load/0", "Memory", "Load", "Percent", "RamLoad"]]),
  node("lpc/nct6798d", "Motherboard", "Nuvoton NCT6798D", [["temperature/0", "CPU Socket", "Temperature", "Celsius", "BoardTemp"], ["temperature/1", "Chipset", "Temperature", "Celsius", "ChipsetTemp"],
    ["fan/0", "CPU Fan", "Fan", "Rpm", "BoardFan"], ["fan/1", "Chassis Fan #1", "Fan", "Rpm", "BoardFan"], ["voltage/0", "+12V", "Voltage", "Volt", "BoardVoltage"], ["voltage/9", "Vcore", "Voltage", "Volt", "None"]]),
  node("nvme/0", "Storage", "Samsung SSD 980 PRO 1TB", [["temperature/0", "Temperature", "Temperature", "Celsius", "StorageTemp"], ["load/0", "Used Space", "Load", "Percent", "StorageUsedSpace"],
    ["throughput/0", "Read Rate", "Throughput", "BytesPerSecond", "StorageReadRate"], ["throughput/1", "Write Rate", "Throughput", "BytesPerSecond", "StorageWriteRate"]]),
  node("hdd/1", "Storage", "WDC WD20PURZ-85GU6Y0", [["temperature/0", "Temperature", "Temperature", "Celsius", "StorageTemp"], ["load/0", "Used Space", "Load", "Percent", "StorageUsedSpace"]]),
  node("nic/eth", "Network", "Ethernet", [["throughput/0", "Download Speed", "Throughput", "BytesPerSecond", "NetDownload"], ["throughput/1", "Upload Speed", "Throughput", "BytesPerSecond", "NetUpload"], ["load/0", "Network Utilization", "Load", "Percent", "NetUtilization"]]),
];
const BASE = {
  "Core (Tctl/Tdie)": 52, "CCD1 (Tdie)": 47, "CCD2 (Tdie)": 45, "CPU Total": 13, "Cores (Average)": 3780, "Cores (Average Effective)": 1612, Package: 71, "Core (SVI2 TFN)": 1.2,
  "GPU Core": 49, "GPU Hot Spot": 63, "GPU Memory Junction": 68, "GPU Memory": 9752, "GPU Package": 124, "GPU Fan 1": 30, "GPU Memory Used": 2560, "GPU Memory Total": 24576,
  "Memory Used": 18.5, "Memory Available": 45.4, Memory: 29, "CPU Socket": 44, Chipset: 58, "CPU Fan": 1310, "Chassis Fan #1": 820, "+12V": 12.1, Vcore: null,
  Temperature: 41, "Used Space": 63, "Read Rate": 1200000, "Write Rate": 380000, "Download Speed": 30400000, "Upload Speed": 204800, "Network Utilization": 3,
};
const hist = new Map(), st = new Map();
function reading(n, s, k) {
  let b = BASE[s.name]; if (s.name === "GPU Core" && s.kind === "Clock") b = 1695; if (s.name === "GPU Core" && s.kind === "Load") b = 11; if (s.name === "GPU Core" && s.kind === "Voltage") b = 0.744;
  if (s.name === "GPU Fan 1" && s.kind === "Fan") b = 1302; if (n.id === "hdd/1" && s.name === "Temperature") b = 36;
  if (b === null || b === undefined) return null;
  const wave = Math.sin(k / 7 + s.id.length) * 0.08 + (Math.random() - 0.5) * 0.04;
  return s.unit === "Volt" ? +(b * (1 + wave / 6)).toFixed(3) : b * (1 + wave);
}
function snapshot() {
  const k = Math.round((Date.now() - T0) / 2000), r = [], s = [];
  for (const n of HW) for (const x of n.sensors) {
    const v = reading(n, x, k); r.push([x.id, v, v === null ? "Missing" : "Ok"]);
    if (!hist.has(x.id)) hist.set(x.id, []);
    const sec = Math.round((Date.now() - T0) / 1000) + 600; hist.get(x.id).push([sec, v]);
    if (v !== null) { const a = st.get(x.id) || [v, v, v, 0]; a[0] = Math.min(a[0], v); a[2] = Math.max(a[2], v); a[3]++; a[1] += (v - a[1]) / a[3]; st.set(x.id, a); s.push([x.id, a[0], a[1], a[2]]); }
  }
  emitRef("snapshot", { t: Date.now(), r, s });
}
function seed() {   // ten minutes of history so the chart has something to draw
  for (const n of HW) for (const x of n.sensors) { const arr = []; for (let i = 0; i < 300; i++) { arr.push([i * 2, reading(n, x, i - 300)]); } hist.set(x.id, arr); }
}

// Windows tools (browser preview only): a page file Windows manages on C:, a short hosts file.
const demoVm = () => ({ ramMb: 65536, managed: true, pending: null, inUse: [{ path: "C:\\pagefile.sys", sizeMb: 9728, usedMb: 412, peakMb: 1880 }], settings: [],
  drives: [{ name: "C:", label: "", freeMb: 312000, totalMb: 952000 }, { name: "D:", label: "Data", freeMb: 1210000, totalMb: 1907000 }] });
const DEMO_HOSTS = "# Copyright (c) 1993-2009 Microsoft Corp.\n#\n# This is a sample HOSTS file used by Microsoft TCP/IP for Windows.\n\n127.0.0.1       localhost\n::1             localhost\n";
const TESTS = ["Test_Cpu_Matrix", "Test_Memory_Pattern", "Test_Storage_Sequential", "Test_Storage_Random4k", "Test_Network_Latency", "Test_Gpu_Steady", "Test_Gpu_Variable", "Test_Gpu_Pulse", "Test_Gpu_Vram", "Test_Gpu_Render", "Test_Power_Combined", "Test_Windows_Sfc", "Test_Windows_Dism", "Test_Storage_Smart"];
const OUT = ["Passed", "Passed", "Running", "NotRun"];
const tests = () => ({
  running: true, canStart: false, incomplete: null,
  repeatModes: ["Once", "Count", "Unlimited"].map((m) => ({ value: m, label: strings[`Test_Repeat_${m}`] })),
  rows: TESTS.map((k, i) => ({ id: k, name: strings[k], selected: i < 6, duration: "60", repeat: "Once", count: "1",
    options: i === 1 ? [{ key: "mb", label: strings.Test_Option_MemoryMb, kind: "Integer", value: "0", choices: null }] : i === 2 ? [{ key: "drive", label: strings.Test_Option_Drive, kind: "Choice", value: "C:\\", choices: [{ value: "C:\\", label: "C:\\ (Samsung SSD 980 PRO)" }, { value: "D:\\", label: "D:\\ (WDC WD20PURZ)" }] }] : [],
    error: null, outcome: i < 3 ? OUT[i] : "NotRun", outcomeText: strings[`Test_Outcome_${i < 3 ? OUT[i] : "NotRun"}`], percent: i < 2 ? 1 : i === 2 ? 0.46 : 0,
    status: i === 2 ? strings.Test_Status_Running : "", errors: null, detail: i === 0 ? "matrix 256x256 FP64 on 32 threads; measured CPU load avg 99.6 % (min 98.1); package 142 W max; Tctl 81.4 °C max" : null })),
});
const BENCH = [["Bench_Cpu_Single", "Cpu"], ["Bench_Cpu_Multi", "Cpu"], ["Bench_Memory", "Memory"], ["Bench_Storage", "Storage"], ["Bench_Gpu_D3D", "Gpu"], ["Bench_Gpu_SceneD3D", "Gpu"], ["Bench_Gpu_Rt", "Gpu"], ["Bench_Gpu_SceneRt", "Gpu"], ["Bench_Gpu_Ai", "Gpu"], ["Bench_Net_Internet", "Network"]];
const bench = () => ({ running: false, queue: "", canRunSelected: true,
  rows: BENCH.map(([k, c], i) => ({ id: k, name: strings[k], component: c, selected: i === 0 || i === 3, duration: "60", percent: i < 2 ? 100 : 0, status: i < 2 ? strings.Bench_Status_CompletedAt.replace("{0}", "01:40") : "", active: false, detail: null, options: [],
    metrics: i === 0 ? [{ name: strings.Bench_Cpu_Gflops, value: "21.40 GFLOPS" }, { name: strings.Bench_Cpu_ClockPeak, value: "4650 MHz" }, { name: strings.Bench_Cpu_TempMax, value: "71.0 °C" }]
      : i === 1 ? [{ name: strings.Bench_Cpu_Gflops, value: "412 GFLOPS" }, { name: strings.Bench_Cpu_PerThread, value: "12.9 GFLOPS" }, { name: strings.Bench_Cpu_Power, value: "142 W" }] : [],
    best: i === 4 ? { name: strings.Bench_Gpu_Fps, value: "318 FPS", at: "1405/07/02 21:14" } : null,
    compared: i === 0 ? { now: { name: strings.Bench_Cpu_Gflops, value: "21.40 GFLOPS", at: "1405/07/06 14:20" }, previous: { name: strings.Bench_Cpu_Gflops, value: "20.70 GFLOPS", at: "1405/07/01 11:02" }, change: 3.38, saved: true }
      : i === 1 ? { now: { name: strings.Bench_Cpu_Gflops, value: "412 GFLOPS", at: "1405/07/06 14:22" }, previous: { name: strings.Bench_Cpu_Gflops, value: "421 GFLOPS", at: "1405/07/01 11:05" }, change: -2.14, saved: false } : null })) });
const CURVE = [[1020, 0.725], [1080, 0.725], [1140, 0.731], [1200, 0.737], [1260, 0.75], [1320, 0.762], [1380, 0.775], [1440, 0.787], [1500, 0.8], [1560, 0.818], [1620, 0.837], [1680, 0.856], [1740, 0.875], [1800, 0.9], [1860, 0.931], [1920, 0.962], [1965, 0.993], [1995, 1.025]];
const form = { core: "165", memory: "0", lockClock: true, maxClock: "1905", setPower: false, power: "350", manualFan: false, fan: "60", profileName: "", coreValue: 165, capValue: 1905 };
const tuning = () => ({
  hasDevice: true, unavailable: "", devices: ["NVIDIA GeForce RTX 3090"], device: 0, name: "NVIDIA GeForce RTX 3090",
  ranges: "آفست هسته ‎-1000..1000 MHz‎   ·   آفست حافظه ‎-2000..6000 MHz‎   ·   توان ‎100..365 W‎ (پیش‌فرض ‎350 W‎)   ·   ۲ فن، ‎30..100 %‎", otherGpus: "", status: "",
  live: [["Tuning_Live_Core", "1695 MHz"], ["Tuning_Live_Memory", "9751 MHz"], ["Tuning_Live_Voltage", "0.743 V"], ["Tuning_Live_Temperature", "55 °C"], ["Tuning_Live_Power", "136 W"], ["Tuning_Live_Fan", "30 %"]].map(([k, v]) => ({ label: strings[k], value: v })),
  liveClock: 1695, liveVolt: 0.743, form,
  limits: { hasCore: true, hasMemory: true, hasPower: true, hasFan: true, coreMin: -1000, coreMax: 1000, memoryMin: -2000, memoryMax: 6000, powerMin: 100, powerMax: 365, fanMin: 30, fanMax: 100, clockMax: 3100, clockMin: 300 },
  curve: CURVE.map(([clock, volt]) => ({ clock, volt })), curveInfo: "۱۸ نقطهٔ اندازه‌گیری‌شده · ‎0.725–1.025 V‎ · تا ‎1995 MHz‎ · اندازه‌گیری ‎2026-09-27 01:36‎",
  curveEstimate: "برآورد از نقاط اندازه‌گیری‌شده: در ‎1905 MHz‎ حدود ‎0.875 V‎ (کارخانه: ‎0.954 V‎)", curveStatus: "",
  busy: false, percent: 0, stepTitle: "", stepSettings: "", stepLoad: "", result: "آندرولت پیدا شد: همان فرکانس و کارایی حالت کارخانه با توان یا دمای کمتر.\nکارخانه: ‎1950 MHz · 348 W · 71 °C · 12174 Gop/s‎  ←  تنظیم‌شده: ‎1905 MHz · 291 W · 64 °C · 12020 Gop/s‎",
  log: [{ step: 1, kind: "اندازه‌گیری", settings: "حالت کارخانه", result: "1950 MHz · 348 W · 71 °C · 12174 Gop/s", clean: true, problem: null },
    { step: 2, kind: "آزمایش", settings: "هسته ‎+90 MHz‎ · سقف ‎1950 MHz‎", result: "1950 MHz · 331 W · 69 °C · 12160 Gop/s", clean: true, problem: null },
    { step: 3, kind: "آزمایش", settings: "هسته ‎+240 MHz‎ · سقف ‎1950 MHz‎", result: "1950 MHz · 302 W · 66 °C · 12011 Gop/s", clean: false, problem: "نتیجهٔ غلط" }],
  profiles: [{ index: 0, name: "آندرولت 2026-09-27 01:40", kind: "Undervolt", kindText: "آندرولت", created: "2026-09-27 01:40", summary: "هسته ‎+165 MHz‎ · سقف ‎1905 MHz‎", evidence: "کارخانه: ‎1950 MHz · 348 W · 71 °C‎  ←  تنظیم‌شده: ‎1905 MHz · 291 W · 64 °C‎" },
    { index: 1, name: "بازی شب", kind: "Manual", kindText: "دستی", created: "2026-09-26 22:10", summary: "هسته ‎+90 MHz‎ · حافظه ‎+500 MHz‎ · توان ‎330 W‎ · فن ‎65 %‎", evidence: "" }],
  memory: [{ label: "DIMM_A2 · CMK64GX4M2E3200C16", value: "تنظیم‌شده ‎3200 MT/s‎ · ماژول طبق SMBIOS ‎3200 MT/s‎" }, { label: "DIMM_B2 · CMK64GX4M2E3200C16", value: "تنظیم‌شده ‎3200 MT/s‎ · ماژول طبق SMBIOS ‎3200 MT/s‎" }],
});
const reports = () => ({ status: "", making: false, canCompare: false,
  items: [{ id: "a1", title: "1405/07/05  01:12", badge: "Passed", verdict: strings.Reports_Verdict_Passed, summary: "۶ آزمون · ۶ موفق · ۰ ناموفق · ۰ انجام‌نشده", selected: false, kind: "TestSession" },
    { id: "b2", title: "1405/07/04  18:40", badge: "Benchmark", verdict: strings.Reports_Verdict_Benchmark, summary: "پردازنده — تک‌رشته · ذخیره‌سازی (ترتیبی و تصادفی، بدون کش)", selected: false, kind: "Benchmark" },
    { id: "c3", title: "1405/07/02  11:05", badge: "Failed", verdict: strings.Reports_Verdict_Failed, summary: "۱۴ آزمون · ۱۲ موفق · ۱ ناموفق · ۱ انجام‌نشده", selected: false, kind: "TestSession" }] });
const sections = () => [
  { title: strings.Tuning_Cpu ?? "پردازنده", rows: [{ label: "Name", value: "AMD Ryzen 9 3950X 16-Core Processor" }, { label: "Cores / threads", value: "16 / 32" }, { label: "Socket", value: "AM4" }] },
  { title: "کارت گرافیک", rows: [{ label: "Name", value: "NVIDIA GeForce RTX 3090" }, { label: "Driver", value: "32.0.15.6094" }, { label: "VRAM", value: "24 GB" }] },
  { title: "مادربرد و بایوس", rows: [{ label: "Board", value: "ASUSTeK ROG STRIX X570-E GAMING" }, { label: "BIOS", value: "4602 (2023-07-11)" }] },
  { title: "ذخیره‌سازی", rows: [{ label: "Samsung SSD 980 PRO 1TB", value: "NVMe · 1000 GB · Healthy" }, { label: "WDC WD20PURZ-85GU6Y0", value: "SATA · 2000 GB · Healthy" }] },
  { title: "سیستم‌عامل", rows: [{ label: "OS", value: "Microsoft Windows 11 Pro 10.0.26200" }, { label: "Architecture", value: "64-bit" }] },
];

// Tweaks as the host lists them (ids and groups from TweakCatalog), with a made-up registry state for the preview.
const TWEAKS = [["restorePoint", "Essential", "RestorePoint", 1], ["tempFiles", "Essential", "TempFiles", 1], ["activityHistory", "Essential", "ActivityHistory"], ["consumerFeatures", "Essential", "ConsumerFeatures"],
  ["telemetry", "Essential", "Telemetry"], ["location", "Essential", "Location"], ["deliveryOptimization", "Essential", "DeliveryOptimization"], ["widgets", "Essential", "Widgets"], ["gameDvr", "Essential", "GameDvr"],
  ["endTask", "Essential", "EndTask"], ["wpbt", "Essential", "Wpbt"], ["backgroundApps", "Advanced", "BackgroundApps"], ["classicMenu", "Advanced", "ClassicMenu"], ["copilot", "Advanced", "Copilot"],
  ["storageSense", "Advanced", "StorageSense"], ["visualFx", "Advanced", "VisualFx"], ["ipv4First", "Advanced", "Ipv4First"], ["teredo", "Advanced", "Teredo"], ["utcClock", "Advanced", "UtcClock"]];
const PREFS = ["DarkTheme", "BingSearch", "FileExtensions", "HiddenFiles", "MouseAcceleration", "StickyKeys", "TaskbarCenter", "TaskbarSearch", "TaskView", "Snapping", "GameMode", "VerboseLogon", "BsodDetails", "LongPaths"];
const tweakState = new Map([["telemetry", "Applied"], ["consumerFeatures", "Applied"], ["gameDvr", "Partial"], ["DarkTheme", "Applied"], ["FileExtensions", "Applied"], ["TaskbarCenter", "Applied"], ["Snapping", "Applied"], ["GameMode", "Applied"]]);
let demoUpdate = "Default";
const tweaks = () => ({ busy: false, update: demoUpdate, presets: { standard: ["restorePoint", "tempFiles", "activityHistory", "consumerFeatures", "telemetry", "location", "deliveryOptimization", "widgets", "gameDvr", "endTask", "wpbt"], minimal: ["restorePoint", "consumerFeatures", "telemetry", "wpbt"] },
  tweaks: [...TWEAKS.map(([id, group, k, action]) => ({ id, group, name: strings[`Tweak_${k}`], note: strings[`Tweak_${k}_Note`], state: action ? "Unknown" : id === "teredo" ? "Unknown" : tweakState.get(id) || "NotApplied", action: !!action, canUndo: !action, restart: ["widgets", "wpbt", "classicMenu", "copilot", "visualFx", "ipv4First", "utcClock"].includes(id) })),
    ...PREFS.map((k) => ({ id: k, group: "Preference", name: strings[`Pref_${k}`], note: "", state: tweakState.get(k) || "NotApplied", action: false, canUndo: true, restart: false }))],
  dns: { providers: [["cloudflare", "1.1.1.1", "1.0.0.1"], ["google", "8.8.8.8", "8.8.4.4"], ["quad9", "9.9.9.9", "149.112.112.112"], ["shecan", "178.22.122.100", "185.51.200.2"], ["electro", "78.157.42.100", "78.157.42.101"], ["403", "10.202.10.202", "10.202.10.102"]].map(([id, ...servers]) => ({ id, servers })),
    adapters: [{ name: "Ethernet", servers: ["192.168.1.1"], provider: "auto" }] } });

import { overlay, frames } from "./demo-overlay.js";

export async function call(m, p, emit) {
  emitRef = emit;
  strings ??= await (await fetch("js/demo-strings.json")).json();
  if (!started) { started = true; seed(); setTimeout(snapshot, 50); setInterval(() => { snapshot(); frames(emit); }, 2000); }
  if (m.startsWith("overlay.")) return overlay(m, p, HW, strings, emit);
  switch (m) {
    case "app.boot": return { language: "fa", rtl: true, strings, version: "demo", shopName: "مازستا", serviceNumber: "S-1405-0042", interval: 2, paused: false,
      contact: { sales: "09197588700", support: "09197588701", office: "021-41139", fax: "88867214 - 88867207", email: "info@dfmrendering.com", hours: "Contact_Hours", address: "Contact_Address", postcode: "1571837738" },
      provider: { state: "Ready", text: strings.Status_Provider_Ready.replace("{0}", "۴۱۲"), count: 412 }, banner: null,
      units: { Celsius: "°C", MegaHertz: "MHz", Percent: "%", Volt: "V", Ampere: "A", Watt: "W", WattHour: "Wh", Rpm: "RPM", Gigabyte: "GB", Megabyte: "MB", BytesPerSecond: "B/s", Seconds: "s", Hertz: "Hz", None: "" } };
    case "app.hardware": return HW;
    case "app.navReady": return true;
    case "diag.state": return { findings: ["Motherboard 'ASUS ROG STRIX X570-E GAMING': 1 sensor(s) without a role: 'Temperature #7'", "Storage 'WDC WD20PURZ-85GU6Y0': no used space sensor"],
      notes: ["Storage 'Samsung Portable SSD T7': 'Composite Temperature' reads 0 all the time: the device does not report it (a drive behind a USB bridge gives no temperature)"], polled: true, logs: "D:\\Mazesta-Test\\Data\\logs" };
    case "sys.state": return { power: { hibernate: true, fastStartup: true }, vm: demoVm(), hosts: { path: "C:\\Windows\\System32\\drivers\\etc\\hosts", backup: true } };
    case "sys.hibernate": await new Promise((r) => setTimeout(r, 500)); return { power: { hibernate: p.on, fastStartup: p.on }, error: null };
    case "sys.pagefile": await new Promise((r) => setTimeout(r, 500)); return p.mode === "custom" && +p.maximum > 196608 ? { error: strings.Tools_Vm_Error_Max.replace("{0}", "196608"), vm: demoVm() } : { vm: { ...demoVm(), pending: p.mode === "custom" ? `${p.drive} ${p.initial}–${p.maximum} MB` : strings.Tools_Vm_Managed } };
    case "hosts.read": return { path: "C:\\Windows\\System32\\drivers\\etc\\hosts", text: DEMO_HOSTS, entries: 2, problems: [], backup: true };
    case "hosts.save": await new Promise((r) => setTimeout(r, 300)); return /^[^#\s]+\s*$/m.test(p.text) && !p.force ? { saved: false, problems: [{ line: 3, text: "10.0.0.5", problem: "name" }] } : { saved: true, problems: [] };
    case "hosts.backup": return { text: DEMO_HOSTS };
    case "chart.boot": { const n = HW.find((x) => x.sensors.some((y) => y.id === p.id)) || HW[0], x = n.sensors.find((y) => y.id === p.id) || n.sensors[0];
      return { language: "fa", rtl: true, strings, units: { Celsius: "°C", MegaHertz: "MHz", Percent: "%", Volt: "V", Watt: "W", Rpm: "RPM", Gigabyte: "GB", Megabyte: "MB", BytesPerSecond: "B/s", None: "" },
        sensor: { ...x, node: n.name, part: n.kind } }; }
    case "shop.product": await new Promise((r) => setTimeout(r, 700));
      return { id: 1, title: "نمونهٔ محصول فروشگاه (دادهٔ نمایشی)", summary: "در برنامه، یک محصول تصادفی از سایت dfmrendering.com اینجا نمایش داده می‌شود: نام، خلاصهٔ توضیحات به‌صورت متن ساده و تصویر محصول.", link: "https://www.dfmrendering.com/shop/", image: null };
    case "history.get": { const h = hist.get(p.id) || []; const now = Math.round((Date.now() - T0) / 1000) + 600; return { sec: h.map((x) => x[0]), val: h.map((x) => x[1]), now }; }
    case "inventory.get": return { sections: sections(), components: { Cpu: sections().slice(0, 1), Gpu: sections().slice(1, 2), Storage: sections().slice(3, 4), Network: [] },
      cpu: "AMD Ryzen 9 3950X", gpus: ["NVIDIA GeForce RTX 3090"], board: "ASUSTeK COMPUTER INC. ROG STRIX X570-E GAMING", bios: "4602", os: "Microsoft Windows 11 Pro", errors: [] };
    case "tweaks.state": return tweaks();
    case "tweaks.pref": tweakState.set(p.id, p.on ? "Applied" : "NotApplied"); return { error: null, state: tweakState.get(p.id) };
    case "tweaks.run": await new Promise((r) => setTimeout(r, 900));
      return { results: p.ids.map((id) => { const x = TWEAKS.find((y) => y[0] === id); if (!x[3]) tweakState.set(id, p.undo ? "NotApplied" : "Applied");
        return { id, name: strings[`Tweak_${x[2]}`], error: id === "teredo" ? "netsh.exe interface teredo set state disabled: exit code 1" : null, done: id === "tempFiles" ? strings.Tweak_TempFiles_Done.replace("{0}", "1284").replace("{1}", "912").replace("{2}", "37") : null, state: x[3] ? "Unknown" : tweakState.get(id) }; }) };
    case "tweaks.update": await new Promise((r) => setTimeout(r, 600)); demoUpdate = p.profile; return { error: null, update: demoUpdate };
    case "tweaks.dns": await new Promise((r) => setTimeout(r, 600)); { const d = tweaks().dns; d.adapters[0].provider = p.provider; d.adapters[0].servers = p.provider === "auto" ? ["192.168.1.1"] : d.providers.find((x) => x.id === p.provider).servers; return { error: null, dns: d }; }
    case "tests.state": return tests();
    case "bench.state": return bench();
    case "tuning.state": return tuning();
    case "tuning.set": if (p.field === "curve") { form.core = p.core; form.coreValue = +p.core; form.capValue = +p.cap; form.maxClock = p.cap; } return null;
    case "reports.state": return reports();
    case "tools.state": return { busy: false, status: strings.Tools_Result_Healthy, percent: 100, canCancel: false,
      output: ["Beginning system scan.  This process will take some time.", "", "Beginning verification phase of system scan.", "Verification 100% complete.", "", "Windows Resource Protection did not find any integrity violations."],
      pageFile: [{ label: strings.Tools_PageFile_Managed, value: "C:\\pagefile.sys" }, { label: strings.Tools_PageFile_Size, value: "16384 MB" }, { label: strings.Tools_PageFile_Used, value: "120 MB" }] };
    case "gaming.state": return { status: "", gameMode: strings.Gaming_On, gpuScheduling: strings.Gaming_On, plans: [{ index: 0, name: "Balanced", active: false }, { index: 1, name: "High performance", active: false }, { index: 2, name: "AMD Ryzen™ High Performance", active: true }] };
    case "settings.state": return { language: "fa", languages: ["en", "fa"], renderMode: "software", renderModes: ["auto", "software"], interval: "2", storageInterval: "900", shopName: "مازستا", message: "",
      trayFirst: "20", trayIdle: "10", trayWatch: "30", trayHealth: "30", trayStatus: "Tray: اجرا نمی‌شود · اجرا با ورود به ویندوز: خیر", canEnableTray: true, canDisableTray: false, dataFolder: "D:\\Mazesta-Test\\Data", mode: strings.Settings_Mode_Portable, version: "demo",
      overlayVisible: false, overlayCorner: "TopLeft", overlayCorners: ["TopLeft", "TopRight", "BottomLeft", "BottomRight"].map((c) => ({ value: c, label: strings[`Overlay_Corner_${c}`] })), hotkey: "Ctrl+Shift+O" };
    default: return null;
  }
}
