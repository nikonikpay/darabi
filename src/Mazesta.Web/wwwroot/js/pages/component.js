// One part of the machine per page: its yellow band with the numbers that matter for it, its specification, its sensors and its benchmarks.
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { fmt } from "../format.js";
import { topNodes, pick, value, subscribe } from "../store.js";
import { h, val, regMarks } from "../ui.js";
import { section } from "./system.js";
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
  const cells = [];
  const band = h("section", { class: "plane enter", style: { paddingBlock: "28px 30px" } }, regMarks(),
    h("h2", { class: "plane-head lat" }, nodes.map((n) => n.name).join("  ·  ") || t(NAV[kind])),
    nodes.slice(0, 3).map((n) => h("div", { class: "giants", style: { gridTemplateColumns: `repeat(${FIGURES[kind].length}, minmax(0,1fr))`, marginTop: "20px" } },
      FIGURES[kind].map(([key, roles]) => {
        const s = pick(n, ...roles), v = h("div", { class: "val", style: { fontSize: "clamp(40px, 5.2vw, 88px)" } });
        cells.push([s, v]);
        return h("div", { class: "giant" }, h("div", { class: "lbl" }, h("span", {}, t(key)), nodes.length > 1 ? h("span", { class: "muted lat" }, n.name) : null), v);
      }))));
  const specs = h("div", { class: "cols spec", style: { marginTop: "4px" } });
  const sensorBox = h("div", {});
  const bench = benchList(kind);
  el.append(band,
    h("div", { class: "section" }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Web_Spec"))), specs),
    h("div", { class: "section" }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Nav_Monitoring"))), sensorBox),
    h("div", { class: "section" }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Nav_Benchmarks"))), bench.el));
  const offSensors = sensors(sensorBox, null, [kind]);

  function tick() {
    for (const [s, v] of cells) {
      const text = s ? fmt(value(s.id), s.unit) : null;
      v.replaceChildren(val(text, ""));
      v.classList.toggle("missing", text === null);
    }
  }
  tick();
  const off = subscribe(tick);
  call("inventory.get").then((inv) => specs.replaceChildren(...(inv.components[kind] || []).map(section)));
  return () => { off(); offSensors?.(); bench.off(); };
}
