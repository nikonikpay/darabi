// Browser preview only (not shipped): the overlay page's host answers, from the demo hardware. The catalog mirrors Core's OverlayCatalog.
const CATALOG = [
  ["fps", "Gaming", "Overlay_Fps", []], ["low1", "Gaming", "Overlay_Low1", []], ["frametime", "Gaming", "Overlay_FrameTime", []],
  ["fps.avg", "Gaming", "Overlay_FpsAvg", []], ["fps.min", "Gaming", "Overlay_FpsMin", []], ["fps.max", "Gaming", "Overlay_FpsMax", []],
  ["gpu.temp", "Gpu", "Overlay_Temp", ["GpuCoreTemp"]], ["gpu.hotspot", "Gpu", "Overlay_HotSpot", ["GpuHotSpotTemp"]], ["gpu.vramtemp", "Gpu", "Overlay_VramTemp", ["GpuVramTemp"]],
  ["gpu.load", "Gpu", "Overlay_Load", ["GpuLoad3D", "GpuLoadD3D3D"]], ["gpu.clock", "Gpu", "Overlay_Clock", ["GpuCoreClock"]], ["gpu.memclock", "Gpu", "Overlay_VramClock", ["GpuMemoryClock"]],
  ["gpu.power", "Gpu", "Overlay_Power", ["GpuPower"]], ["gpu.voltage", "Gpu", "Overlay_Voltage", ["GpuVoltage"]], ["gpu.fan", "Gpu", "Overlay_Fan", ["GpuFanPercent"]],
  ["gpu.fanrpm", "Gpu", "Overlay_FanRpm", ["GpuFanRpm"]], ["gpu.vram", "Gpu", "Overlay_Vram", ["GpuVramUsed"]],
  ["cpu.temp", "Cpu", "Overlay_Temp", ["CpuPackageTemp", "CpuTctlTdie"]], ["cpu.hotcore", "Cpu", "Overlay_HotCore", ["CpuCoreTemp", "CpuCcdTemp"], "Max"], ["cpu.load", "Cpu", "Overlay_Load", ["CpuTotalLoad"]],
  ["cpu.maxthread", "Cpu", "Overlay_MaxThread", ["CpuThreadLoad"], "Max"], ["cpu.clock", "Cpu", "Overlay_Clock", ["CpuEffectiveClockAverage", "CpuCoreClockAverage", "CpuCoreClock"]],
  ["cpu.maxclock", "Cpu", "Overlay_MaxClock", ["CpuEffectiveClock", "CpuCoreClock"], "Max"], ["cpu.power", "Cpu", "Overlay_Power", ["CpuPackagePower"]], ["cpu.voltage", "Cpu", "Overlay_Voltage", ["CpuVcore"]],
  ["cpu.fan", "Cpu", "Overlay_FanRpm", ["CpuFan"]],
  ["ram.used", "Memory", "Overlay_Used", ["RamUsed"]], ["ram.load", "Memory", "Overlay_Load", ["RamLoad"]], ["ram.temp", "Memory", "Overlay_Temp", ["DimmTemp"], "Max"],
  ["storage.temp", "Storage", "Overlay_HotDrive", ["StorageTemp"], "Max"], ["storage.read", "Storage", "Overlay_Read", ["StorageReadRate"], "Sum"], ["storage.write", "Storage", "Overlay_Write", ["StorageWriteRate"], "Sum"],
  ["storage.activity", "Storage", "Overlay_Activity", ["StorageTotalActivity"], "Max"],
  ["net.down", "Network", "Overlay_Down", ["NetDownload"], "Sum"], ["net.up", "Network", "Overlay_Up", ["NetUpload"], "Sum"],
  ["net.ping", "Network", "Overlay_Ping", []], ["net.loss", "Network", "Overlay_Loss", []], ["net.jitter", "Network", "Overlay_Jitter", []],
];
const PRESETS = {
  game: "fps:c low1 fps.avg fps.min fps.max frametime:c gpu.temp gpu.load gpu.clock gpu.vram gpu.power cpu.temp cpu.load cpu.maxthread ram.used net.ping net.loss net.jitter net.down net.up",
  render: "cpu.load:c cpu.temp:c cpu.clock cpu.power gpu.load:c gpu.temp gpu.power gpu.vram gpu.vramtemp ram.used:c ram.load storage.read storage.write storage.activity",
  troubleshoot: "cpu.load cpu.temp:c cpu.hotcore cpu.clock cpu.maxclock cpu.power cpu.voltage cpu.fan gpu.load gpu.temp:c gpu.hotspot gpu.vram gpu.vramtemp gpu.clock gpu.power gpu.voltage gpu.fanrpm ram.load storage.temp storage.read storage.write storage.activity",
};
// The items with a fixed 0-100 top (percentages and temperatures), as FixedMax in the catalog.
const HUNDRED = new Set(["gpu.temp", "gpu.hotspot", "gpu.vramtemp", "gpu.load", "gpu.fan", "cpu.temp", "cpu.hotcore", "cpu.load", "cpu.maxthread", "ram.load", "ram.temp", "storage.temp", "storage.activity", "net.loss"]);
const parse = (spec) => spec.split(" ").map((s) => ({ id: s.replace(":c", ""), chart: s.endsWith(":c") }));
let chosen = parse(PRESETS.game), preset = "game", visible = false, corner = "TopLeft", opacity = 0.9, scale = 1, layout = "list", pingTarget = "8.8.8.8";
const PER_DRIVE = ["storage.read", "storage.write", "storage.temp", "storage.activity"];
// Each drive's own items, as OverlayCatalog.ForDrives makes them: "base@device", the base item read on that drive only.
function drives(hw) {
  return hw.filter((n) => !n.parent && n.kind === "Storage").flatMap((n) => PER_DRIVE.map((b) => { const c = CATALOG.find((x) => x[0] === b); return [`${b}@${n.id}`, c[1], c[2], c[3], "First", n]; }));
}

function resolve(hw, [, part, , roles, agg = "First", device]) {
  const nodes = device ? [device] : hw.filter((n) => !n.parent && n.kind === part && !/^vEthernet/i.test(n.name));
  if (!roles.length) return [];
  if (agg === "First") { for (const n of nodes) for (const r of roles) { const s = n.sensors.find((x) => x.role === r); if (s) return [s.id]; } return []; }
  for (const r of roles) { const all = nodes.flatMap((n) => n.sensors.filter((x) => x.role === r)); if (all.length) return all.map((s) => s.id); }
  return [];
}

export function overlay(m, p, hw, strings, emit) {
  const state = () => ({ visible, corner, opacity, scale, preset, layout, hotkey: "Ctrl+Shift+O", frameProblem: null, pingTarget,
    corners: ["TopLeft", "TopRight", "BottomLeft", "BottomRight"].map((c) => ({ value: c, label: strings[`Overlay_Corner_${c}`] })),
    presets: Object.entries(PRESETS).map(([id, spec]) => ({ id, count: parse(spec).length })), order: chosen.map((c) => c.id),
    items: [...CATALOG, ...drives(hw)].map((c) => { const sensors = resolve(hw, c), ch = chosen.find((x) => x.id === c[0]);
      return { id: c[0], part: c[1], label: strings[c[2]] ?? c[2], frame: !c[3].length, available: !c[3].length || sensors.length > 0, on: !!ch, chart: ch?.chart ?? false, aggregate: c[4] || "First", max: HUNDRED.has(c[0].split("@")[0]) ? 100 : null, sensors,
        device: c[5]?.id ?? null, deviceName: c[5]?.name ?? null }; }) });
  const changed = () => { setTimeout(() => emit("overlayState", state()), 30); return null; };
  switch (m) {
    case "overlay.state": return state();
    case "overlay.set":
      if (p.field === "visible") visible = p.value; if (p.field === "corner") corner = p.value; if (p.field === "opacity") opacity = p.value; if (p.field === "scale") scale = p.value; if (p.field === "layout") layout = p.value; if (p.field === "pingTarget") pingTarget = p.value || "8.8.8.8";
      return changed();
    case "overlay.preset": chosen = parse(PRESETS[p.id]); preset = p.id; return changed();
    case "overlay.order": { const next = p.ids.map((id) => chosen.find((c) => c.id === id)).filter(Boolean); chosen = [...next, ...chosen.filter((c) => !next.includes(c))]; return changed(); }
    case "overlay.item": {
      const i = chosen.findIndex((c) => c.id === p.id);
      if (!p.on) { if (i >= 0) chosen.splice(i, 1); } else if (i >= 0) chosen[i] = { id: p.id, chart: p.chart }; else chosen.push({ id: p.id, chart: p.chart });
      preset = "custom"; return changed();
    }
  }
  return undefined;
}

// A game in front at about 140 FPS with the odd hitch, while the overlay is on; the session's average, lowest and highest as the host keeps them.
const session = { n: 0, sum: 0, min: Infinity, max: 0 };
export function frames(emit) {
  if (!visible || !chosen.some((c) => c.id.startsWith("fps") || ["low1", "frametime", "net.ping", "net.loss", "net.jitter"].includes(c.id))) return;
  // A scene that swings and now and then stutters, so the preview's trace has something to show.
  const fps = 128 + 16 * Math.sin(Date.now() / 9000) + Math.random() * 8 - (Math.random() < 0.12 ? 40 : 0);
  session.n++; session.sum += fps; session.min = Math.min(session.min, fps); session.max = Math.max(session.max, fps);
  emit("overlayFrames", { fps, low1: 96 + Math.random() * 6, frametime: 1000 / fps, app: "Cyberpunk2077", avg: session.sum / session.n, min: session.min, max: session.max,
    ping: 38 + Math.random() * 9, loss: Math.random() < 0.2 ? 3.3 : 0, jitter: 2 + Math.random() * 3 });
}
