// The dashboard as a poster: a yellow plane with the machine's standing (last full test), its CPU and GPU temperatures at poster scale,
// then ruled columns per part. Every number is a live reading or says it is not available.
import { call } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { fmt, whole } from "../format.js";
import { topNodes, pick, value, stats, subscribe } from "../store.js";
import { h, val, roll, icon, regMarks, toast } from "../ui.js";
import { go, boot } from "../app.js";

export function mount(el) {
  const cpu = topNodes("Cpu")[0], gpus = topNodes("Gpu"), gpu = gpus.find((g) => pick(g, "GpuCoreTemp")) || gpus[0];
  const ram = topNodes("Memory").find((n) => pick(n, "RamUsed"));
  const drives = topNodes("Storage"), nets = topNodes("Network").filter((n) => !/^vEthernet/i.test(n.name));
  const cpuTemp = pick(cpu, "CpuPackageTemp", "CpuTctlTdie"), gpuTemp = pick(gpu, "GpuCoreTemp");

  const verdict = h("h2", { class: "verdict" }, t("Web_Dash_Verdict_None"));
  const machine = h("div", { class: "machine lat" }, [cpu?.name, gpu?.name].filter(Boolean).join("  ·  "));
  const giant = (labelKey, sensor) => {
    const v = h("div", { class: "val" }), r = h("div", { class: "rng" }), span = h("i", { class: "span" }), now = h("i", { class: "now" });
    return { sensor, v, r, span, now, el: h("div", { class: "giant" }, h("div", { class: "lbl" }, h("span", {}, t(labelKey)), h("span", { class: "muted lat" }, sensor ? sensor.node.name ?? "" : "")), v,
      h("div", { class: "range-rule", "aria-hidden": "true" }, span, now), r) };
  };
  const g1 = giant("Web_Dash_CpuTemp", cpuTemp && { ...cpuTemp, node: cpu }), g2 = giant("Web_Dash_GpuTemp", gpuTemp && { ...gpuTemp, node: gpu });
  const date = new Intl.DateTimeFormat(boot.rtl ? "fa-IR-u-ca-persian" : "en-GB", { weekday: "long", day: "numeric", month: "long", year: "numeric" }).format(new Date());
  const summaryBtn = h("button", { class: "btn", style: { borderColor: "var(--on-yellow)", color: "var(--on-yellow)" }, onclick: async () => {
    summaryBtn.disabled = true; toast(t("Reports_SummaryBusy"));
    try { await call("reports.exec", { cmd: "summary" }); } finally { summaryBtn.disabled = false; }
  } }, icon("doc"), t("Reports_Summary"));
  const plane = h("section", { class: "plane enter" }, regMarks(),
    verdict, machine,
    h("div", { class: "giants" }, g1.el, g2.el),
    h("div", { class: "plane-actions" }, h("button", { class: "slab", onclick: () => go("tests") }, t("Web_Dash_RunTests"), icon("arrow")), summaryBtn),
    h("div", { class: "colophon" }, h("span", {}, boot.shopName), h("span", {}, date)));

  // Ruled columns, one per part.
  const rows = [];   // [sensor, valueElement]
  const line = (label, sensor) => { const v = h("dd", { class: "num" }); rows.push([sensor, v]); return [h("dt", {}, label), v]; };
  const col = (title, page, i, ...body) => h("div", { class: "col", style: { "--i": i } },
    h("div", { class: "col-head" }, h("span", { class: "h3" }, title), page ? h("a", { href: `#/${page}` }, t("Web_Dash_More")) : null), h("dl", { class: "kv" }, body));
  const cols = h("div", { class: "cols" },
    cpu && col(t("Nav_Cpu"), "cpu", 1,
      line(t("Dashboard_Line_Package"), cpuTemp), line(t("Dashboard_Line_Clock"), pick(cpu, "CpuEffectiveClockAverage", "CpuCoreClockAverage", "CpuCoreClock")),
      line(t("Dashboard_Line_Load"), pick(cpu, "CpuTotalLoad")), line(t("Dashboard_Line_Power"), pick(cpu, "CpuPackagePower"))),
    ...gpus.map((g, i) => col(gpus.length > 1 ? `${t("Nav_Gpu")} ${fa(i + 1)}` : t("Nav_Gpu"), "gpu", 2 + i,
      line(t("Dashboard_Line_Core"), pick(g, "GpuCoreTemp")), line(t("Overlay_HotSpot"), pick(g, "GpuHotSpotTemp")), line(t("Dashboard_Line_Load"), pick(g, "GpuLoad3D", "GpuLoadD3D3D")),
      line(t("Dashboard_Line_Clock"), pick(g, "GpuCoreClock")), line(t("Dashboard_Line_Power"), pick(g, "GpuPower")), line(t("Dashboard_Line_VramUsed"), pick(g, "GpuVramUsed")))),
    ram && col(t("Dashboard_Ram"), null, 4, line(t("Dashboard_Line_Used"), pick(ram, "RamUsed")), line(t("Dashboard_Line_Free"), pick(ram, "RamFree")), line(t("Dashboard_Line_Load"), pick(ram, "RamLoad"))),
    drives.length && col(t("Nav_Storage"), "storage", 5, ...drives.map((d) => [
      h("dt", { class: "sub lat" }, d.name), line(t("Web_Dash_Temp"), pick(d, "StorageTemp")), line(t("Web_Dash_UsedSpace"), pick(d, "StorageUsedSpace"))])),
    nets.length && col(t("Nav_Network"), "network", 6, ...nets.slice(0, 3).map((n) => [
      h("dt", { class: "sub lat" }, n.name), line(t("Overlay_Down"), pick(n, "NetDownload")), line(t("Overlay_Up"), pick(n, "NetUpload"))])));

  const inv = h("dl", { class: "kv", style: { marginTop: "34px", gridTemplateColumns: "auto 1fr auto 1fr auto 1fr", columnGap: "18px" } });
  el.append(plane, cols, inv);

  function tick() {
    for (const g of [g1, g2]) {
      const s = g.sensor, v = s ? value(s.id) : null;
      roll(g.v, whole(v));
      if (v !== null && !g.v.querySelector(".unit")) g.v.append(h("span", { class: "unit" }, "°C"));
      // The session range, drawn on a 0-100 °C scale and written out; figures stay Latin (passed as text, not numbers).
      const st = s && stats.get(s.id), pct = (x) => `${Math.min(100, Math.max(0, x))}%`;
      g.r.textContent = st ? t("Web_Dash_Range", String(Math.round(st[0])), String(Math.round(st[1])), `‎${Math.round(st[2])} °C‎`) : "";
      g.span.style.left = st ? pct(st[0]) : "0"; g.span.style.width = st ? pct(st[2] - st[0]) : "0";
      g.now.hidden = v === null; if (v !== null) g.now.style.left = pct(v);
    }
    for (const [s, dd] of rows) { const text = s ? fmt(value(s.id), s.unit) : null; dd.replaceChildren(val(text)); }
  }
  tick();
  const off = subscribe(tick);

  call("reports.state").then((r) => {
    const last = r.items.find((x) => x.kind === "TestSession");
    verdict.textContent = t(`Web_Dash_Verdict_${last ? last.badge : "None"}`);
  });
  call("inventory.get").then((i) => {
    const kv = [[t("Dashboard_Inv_Board"), i.board], [t("Dashboard_Inv_Bios"), i.bios], [t("Dashboard_Inv_Os"), i.os]];
    inv.replaceChildren(...kv.map(([k, v]) => [h("dt", {}, k), h("dd", { class: "lat" }, val(v, "lat"))]));
  });
  return off;
}
