// One part of the machine per page, all in the part's own hue: its band with the numbers that matter for it, its specification and its sensors
// (with the live panel of a test running on it). Benchmarks have their own page.
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { topNodes, pick, subscribe } from "../store.js";
import { h } from "../ui.js";
import { liveTile, percentOf } from "../tiles.js";
import { card, cachedNote } from "./system.js";
import { part } from "../parts.js";
import { mount as sensors } from "./monitoring.js";
import { lanPanel } from "../lanpeer.js";

const FIGURES = {
  Cpu: [["Overlay_Temp", ["CpuPackageTemp", "CpuTctlTdie"]], ["Overlay_Load", ["CpuTotalLoad"]], ["Overlay_Clock", ["CpuEffectiveClockAverage", "CpuCoreClockAverage", "CpuCoreClock"]], ["Overlay_Power", ["CpuPackagePower"]]],
  Gpu: [["Overlay_Temp", ["GpuCoreTemp"]], ["Overlay_Load", ["GpuLoad3D", "GpuLoadD3D3D"]], ["Overlay_Clock", ["GpuCoreClock"]], ["Overlay_Power", ["GpuPower"]]],
  Storage: [["Overlay_Temp", ["StorageTemp"]], ["Web_Dash_UsedSpace", ["StorageUsedSpace"]], ["Web_Read", ["StorageReadRate"]], ["Web_Write", ["StorageWriteRate"]]],
  Network: [["Overlay_Down", ["NetDownload"]], ["Overlay_Up", ["NetUpload"]], ["Overlay_Load", ["NetUtilization"]]],
  Memory: [["Overlay_Load", ["RamLoad"]], ["Web_Ram_Used", ["RamUsed"]], ["Web_Ram_Free", ["RamFree"]], ["Overlay_Temp", ["DimmTemp"]]],
};
const NAV = { Cpu: "Nav_Cpu", Gpu: "Nav_Gpu", Storage: "Nav_Storage", Network: "Nav_Network", Memory: "Dashboard_Ram" };
const PAGE = { Cpu: "cpu", Gpu: "gpu", Storage: "storage", Network: "network", Memory: "ram" };

export function mount(el, kind) {
  // A part's band shows the devices that report one of its figures (for memory: the total, not the page file's "virtual memory").
  const nodes = topNodes(kind).filter((n) => (kind !== "Network" || !/^vEthernet/i.test(n.name)) && FIGURES[kind].some(([, roles]) => pick(n, ...roles)));
  el.classList.add(part(kind).cls, "part-page");
  // The part's card: its device names, then a live tile per figure that matters for it (a share of something draws its bar).
  const tiles = nodes.slice(0, 3).flatMap((n, ni) => FIGURES[kind].filter(([, roles]) => kind !== "Memory" || pick(n, ...roles)).map(([key, roles], fi) => {
    const s = pick(n, ...roles);
    return liveTile({ kind, label: t(key), main: s, share: s && s.unit === "Percent" ? percentOf(s) : null, foot: nodes.length > 1 ? n.name : null, i: ni * 4 + fi });
  }));
  const band = h("section", { class: "plane part-plane enter" },
    h("h2", { class: "plane-head lat" }, kind === "Memory" ? t(NAV[kind]) : nodes.map((n) => n.name).join("  ·  ") || t(NAV[kind])),
    h("div", { class: "tiles compact" }, tiles.map((x) => x.el)));
  const specs = h("div", { class: "cols spec", style: { marginTop: "4px" } });
  const sensorBox = h("div", {}), runSlot = h("div", {});
  const lan = kind === "Network" ? lanPanel() : null;
  el.append(runSlot, band, lan?.el ?? "",
    h("div", { class: "section" }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Web_Spec"))), specs),
    h("div", { class: "section" }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Nav_Monitoring"))), sensorBox));
  const offSensors = sensors(sensorBox, null, { kinds: [kind], page: PAGE[kind], runSlot });

  function tick() { for (const x of tiles) x.update(); }
  tick();
  const off = subscribe(tick);
  specs.append(h("p", { class: "page-lede" }, t("Spec_Loading")));
  // The last start's read of the same parts draws at once (said so), and is replaced when this start's own read is done.
  let cached = false;
  const load = () => call("specs.get", { kind }).then((r) => { cached = r.cached; specs.replaceChildren(...(r.cached ? [cachedNote()] : []), ...r.cards.map(card)); });
  load();
  const offFresh = on("hardwareFresh", () => { if (cached) load(); });
  return () => { off(); offFresh(); offSensors?.(); lan?.off(); };
}
