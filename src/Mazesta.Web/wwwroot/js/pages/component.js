// One part of the machine per page, all in the part's own hue: its band with the numbers that matter for it, its specification, its sensors and its benchmarks.
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { topNodes, pick, subscribe } from "../store.js";
import { h } from "../ui.js";
import { liveTile, percentOf } from "../tiles.js";
import { section } from "./system.js";
import { part } from "../parts.js";
import { benchList } from "./benchmarks.js";
import { mount as sensors } from "./monitoring.js";

const FIGURES = {
  Cpu: [["Overlay_Temp", ["CpuPackageTemp", "CpuTctlTdie"]], ["Overlay_Load", ["CpuTotalLoad"]], ["Overlay_Clock", ["CpuEffectiveClockAverage", "CpuCoreClockAverage", "CpuCoreClock"]], ["Overlay_Power", ["CpuPackagePower"]]],
  Gpu: [["Overlay_Temp", ["GpuCoreTemp"]], ["Overlay_Load", ["GpuLoad3D", "GpuLoadD3D3D"]], ["Overlay_Clock", ["GpuCoreClock"]], ["Overlay_Power", ["GpuPower"]]],
  Storage: [["Overlay_Temp", ["StorageTemp"]], ["Web_Dash_UsedSpace", ["StorageUsedSpace"]], ["Web_Read", ["StorageReadRate"]], ["Web_Write", ["StorageWriteRate"]]],
  Network: [["Overlay_Down", ["NetDownload"]], ["Overlay_Up", ["NetUpload"]], ["Overlay_Load", ["NetUtilization"]]],
};
const NAV = { Cpu: "Nav_Cpu", Gpu: "Nav_Gpu", Storage: "Nav_Storage", Network: "Nav_Network" };

export function mount(el, kind) {
  const nodes = topNodes(kind).filter((n) => kind !== "Network" || !/^vEthernet/i.test(n.name));
  el.classList.add(part(kind).cls, "part-page");
  // The part's card: its device names, then a live tile per figure that matters for it (a share of something draws its bar).
  const tiles = nodes.slice(0, 3).flatMap((n, ni) => FIGURES[kind].map(([key, roles], fi) => {
    const s = pick(n, ...roles);
    return liveTile({ kind, label: t(key), main: s, share: s && s.unit === "Percent" ? percentOf(s) : null, foot: nodes.length > 1 ? n.name : null, i: ni * 4 + fi });
  }));
  const band = h("section", { class: "plane part-plane enter" },
    h("h2", { class: "plane-head lat" }, nodes.map((n) => n.name).join("  ·  ") || t(NAV[kind])),
    h("div", { class: "tiles compact" }, tiles.map((x) => x.el)));
  const specs = h("div", { class: "cols spec", style: { marginTop: "4px" } });
  const sensorBox = h("div", {});
  const bench = benchList(kind);
  el.append(band,
    h("div", { class: "section" }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Web_Spec"))), specs),
    h("div", { class: "section" }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Nav_Monitoring"))), sensorBox),
    h("div", { class: "section" }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Nav_Benchmarks"))), bench.el));
  const offSensors = sensors(sensorBox, null, [kind]);

  function tick() { for (const x of tiles) x.update(); }
  tick();
  const off = subscribe(tick);
  call("inventory.get").then((inv) => specs.replaceChildren(...(inv.components[kind] || []).map(section)));
  return () => { off(); offSensors?.(); bench.off(); };
}
