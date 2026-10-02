// Every sensor, in a boxed panel per device that wears its part's hue and folds away, grouped by kind inside, at data density. Double-click a
// row (or its chart button, or Enter) to add its chart to the stack beside the table; each chart can go to a window of its own. Filtering
// hides rows, it never hides that a sensor exists without a reading: those rows say "not available" in the hatch.
// While a test runs, its live panel sits on top and the part under test comes forward: its panels open, the others fold, its key charts join.
import { call, live } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { fmt } from "../format.js";
import { hw, value, quality, stats, subscribe, net, netRank } from "../store.js";
import { h, icon, toast } from "../ui.js";
import { part } from "../parts.js";
import { chartCard, RANGES } from "../chartcard.js";
import { runPanel } from "../testrun.js";
import { families } from "../families.js";

const KIND_ORDER = ["Temperature", "Load", "Clock", "Power", "Voltage", "Current", "Fan", "Control", "Data", "SmallData", "Throughput", "Level", "Energy", "Timespan", "Factor", "Frequency", "Timing", "Noise", "Flow", "Humidity", "Conductivity"];
const MAX_CHARTS = 10;
const FAMILY_KEY = "mazesta.sensors.openFamilies";
// Which charts are open is a per-viewer convenience: kept in the browser profile, and the page works the same without it.
const remember = (key, fallback) => { try { const v = JSON.parse(localStorage.getItem(key)); return v ?? fallback; } catch { return fallback; } };
const keep = (key, v) => { try { localStorage.setItem(key, JSON.stringify(v)); } catch { /* private profile: nothing is kept */ } };

// opts: { kinds, page, runSlot } on a part's page (only that part's sensors; the run panel of its tests goes in runSlot, on top of the page);
// none on Monitoring itself.
export function mount(el, _, opts = null) {
  const focusKinds = opts?.kinds || null, page = opts?.page || "monitoring";
  const store = `mazesta.monitor.v2.${focusKinds ? focusKinds.join("-") : "all"}`;   // v2: the first-visit charts changed
  const filter = h("input", { class: "field search", type: "search", placeholder: t("Monitoring_Search"), "aria-label": t("Monitoring_Search") });
  let windowSec = remember("mazesta.monitor.window", 600);
  const cards = new Map();   // id -> card
  const stack = h("div", { class: "chart-stack" });
  const empty = h("p", { class: "chart-empty" }, icon("chart"), h("span", {}, t("Web_Chart_Hint")));
  const ranges = h("div", { class: "chart-range", role: "group", "aria-label": t("Web_Chart_Window") }, RANGES.map((s) => h("button", { class: `btn ${s === windowSec ? "primary" : ""}`, type: "button", onclick: (e) => {
    windowSec = s; keep("mazesta.monitor.window", s);
    for (const b of ranges.children) b.classList.toggle("primary", b === e.currentTarget);
    for (const c of cards.values()) c.redraw();
  } }, t("Web_Minutes", s / 60))));
  const clearAll = h("button", { class: "btn quiet", type: "button", onclick: () => { for (const id of [...cards.keys()]) removeChart(id); } }, t("Web_Chart_ClearAll"));
  const charts = h("aside", { class: "chart-col" }, h("div", { class: "chart-bar" }, ranges, h("span", { class: "grow" }), clearAll), empty, stack);

  if (!focusKinds) el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Monitoring")), h("p", { class: "page-lede" }, t("Web_Monitoring_Lede")))));
  const groups = h("div", { class: "mon-groups" });
  const run = runPanel((kinds) => followPart(kinds), page);
  (opts?.runSlot || el).append(run.el);
  el.append(h("div", { class: "toolbar" }, filter, h("span", { class: "grow" }), focusKinds ? h("span", { class: "caption" }, t("Web_Chart_Hint")) : null), h("div", { class: "split" }, groups, charts));

  const openFamilies = new Set(remember(FAMILY_KEY, []));   // the families the viewer opened; the rest stay folded
  const cells = [];   // [sensor, current, min, avg, max, row, searchText, toggle, group]
  const bySensor = new Map();
  let i = 0;
  // The processor first, then the graphics card, then the board (its Super I/O chips follow it); every other part after them, as found.
  const RANK = { Cpu: 0, Gpu: 1, Motherboard: 2 };
  const root = (n) => { while (n.parent) { const up = hw.nodes.find((x) => x.id === n.parent); if (!up) break; n = up; } return n; };
  // Network adapters: the one the internet goes through first, then the other connected ones (an unplugged port reads all zeros).
  const nodes = hw.nodes.map((n, at) => ({ n, at, r: RANK[root(n).kind] ?? 3, w: n.kind === "Network" ? netRank(n) : 0 })).sort((a, b) => a.r - b.r || a.w - b.w || a.at - b.at).map((x) => x.n);
  for (const node of nodes) {
    if (focusKinds && !focusKinds.includes(node.kind)) continue;
    if (!node.sensors.length) continue;
    const p = part(node.kind), tbody = h("tbody");
    const table = h("table", { class: "table" }, h("thead", {}, h("tr", {},
      h("th", {}, t("Web_Col_Sensor")), h("th", { class: "n" }, t("Web_Col_Current")), h("th", { class: "n" }, t("Web_Col_Min")),
      h("th", { class: "n" }, t("Web_Col_Avg")), h("th", { class: "n" }, t("Web_Col_Max")), h("th", { class: "c" }, h("span", { class: "sr" }, t("Web_Chart"))))), tbody);
    const idle = node.kind === "Network" && net.up?.length > 0 && netRank(node) === 2;   // an adapter not connected now starts folded
    const fold = h("button", { class: "more", type: "button", "aria-expanded": String(!idle), "aria-label": node.name }, icon("chevron"));
    const group = h("section", { class: `panel group ${p.cls}${idle ? " shut" : ""}`, style: { "--i": i++ }, "data-kind": node.kind, "data-idle": idle ? "" : null },
      h("header", { class: "panel-head", onclick: () => { const shut = group.classList.toggle("shut"); fold.setAttribute("aria-expanded", String(!shut)); } },
        h("span", { class: "ico" }, icon(p.icon)),
        h("div", { class: "ttl" }, h("h2", { class: "panel-title lat" }, node.name), h("div", { class: "panel-sub fa" }, t(`Web_Kind_${node.kind}`))),
        h("span", { class: "group-count" }, t("Web_Sensors_Count", fa(node.sensors.length))), fold),
      h("div", { class: "group-body" }, h("div", {}, table)));
    groups.append(group);
    const byKind = new Map();
    for (const s of node.sensors) { if (!byKind.has(s.kind)) byKind.set(s.kind, []); byKind.get(s.kind).push({ ...s, node }); }
    const kinds = [...byKind.keys()].sort((a, b) => (KIND_ORDER.indexOf(a) + 99) % 99 - (KIND_ORDER.indexOf(b) + 99) % 99);
    for (const k of kinds) {
      tbody.append(h("tr", { class: "sec" }, h("td", { colspan: "6" }, t(`SensorKind_${k}`))));
      const sensorRow = (s, cls, lead) => {
        const c = [h("td", { class: "n" }), h("td", { class: "n" }), h("td", { class: "n" }), h("td", { class: "n" })];
        const toggle = h("button", { class: "icon-btn", type: "button", title: t("Web_Chart_Add"), "aria-label": `${t("Web_Chart_Add")}: ${s.name}`, "aria-pressed": "false",
          onclick: (e) => { e.stopPropagation(); flip(s); } }, icon("chart"));
        const row = h("tr", { class: `row ${cls}`, tabindex: "0", ondblclick: () => flip(s), onkeydown: (e) => { if (e.key === "Enter") flip(s); } },
          h("td", {}, lead, h("span", { class: "sensor-name" }, s.name)), ...c, h("td", { class: "c" }, toggle));
        tbody.append(row);
        const cell = [s, ...c, row, `${node.name} ${s.name}`.toLowerCase(), toggle, group];
        cells.push(cell); bySensor.set(s.id, cell);
        return row;
      };
      // A family of numbered sensors (each core's clock, each thread's load) folds under its head, the device's own summary where it has one.
      for (const item of families(byKind.get(k))) {
        if (item.sensor) { sensorRow(item.sensor, "", null); continue; }
        const key = `${node.name}|${k}|${item.family}`, open = openFamilies.has(key);
        const caret = h("button", { class: "fam-fold", type: "button", "aria-expanded": String(open), "aria-label": item.family, onclick: (e) => { e.stopPropagation(); fold(); } }, icon("chevron"));
        const count = h("small", { class: "fam-count num" }, `×${fa(item.members.length)}`);
        const head = item.head ? sensorRow(item.head, "fam-head", caret)
          : tbody.appendChild(h("tr", { class: "fam-head label", onclick: () => fold() }, h("td", { colspan: "6" }, caret, h("span", { class: "sensor-name" }, item.family))));
        head.querySelector("td").append(count);
        // A click anywhere on the head opens or folds it (a double click still charts a head that is a sensor).
        if (item.head) head.addEventListener("click", (e) => { if (!e.target.closest(".icon-btn")) fold(); });
        const kids = item.members.map((s) => sensorRow(s, "fam-kid", null));
        const show = (on) => { for (const r of kids) r.classList.toggle("folded", !on); caret.setAttribute("aria-expanded", String(on)); head.classList.toggle("open", on); };
        function fold() { const on = caret.getAttribute("aria-expanded") !== "true"; show(on); on ? openFamilies.add(key) : openFamilies.delete(key); keep(FAMILY_KEY, [...openFamilies]); }
        show(open);
      }
    }
  }

  function tick() {
    for (const [s, cur, mn, av, mx] of cells) {
      const v = value(s.id);
      cur.replaceChildren(v === null ? h("span", { class: "hatch", title: quality(s.id) }, t("Value_NotAvailable")) : fmt(v, s.unit));
      const st = stats.get(s.id);
      mn.textContent = st ? fmt(st[0], s.unit) : ""; av.textContent = st ? fmt(st[1], s.unit) : ""; mx.textContent = st ? fmt(st[2], s.unit) : "";
    }
    for (const c of cards.values()) { c.update(); c.refresh(); }
  }
  filter.addEventListener("input", () => {
    const q = filter.value.trim().toLowerCase();
    groups.classList.toggle("searching", !!q);   // a search shows the folded sensors that match
    for (const [, , , , , row, text] of cells) row.hidden = q && !text.includes(q);
    for (const g of groups.children) g.hidden = q && ![...g.querySelectorAll("tr.row")].some((r) => !r.hidden);
  });

  function mark(id, on) {
    const cell = bySensor.get(id); if (!cell) return;
    cell[5].classList.toggle("charted", on); cell[7].setAttribute("aria-pressed", String(on));
    cell[7].title = t(on ? "Web_Chart_Remove" : "Web_Chart_Add");
  }
  function addChart(s, quiet = false) {
    if (cards.has(s.id)) return;
    if (cards.size >= MAX_CHARTS) { if (!quiet) toast(t("Web_Chart_Max", fa(MAX_CHARTS))); return; }
    const card = chartCard(s, { kind: s.node.kind, nodeName: s.node.name, windowSec: () => windowSec,
      onRemove: () => removeChart(s.id),
      onPopout: () => live ? call("monitor.popout", { id: s.id }) : window.open(`chart.html?id=${encodeURIComponent(s.id)}`, "_blank", "width=720,height=440") });
    cards.set(s.id, card); stack.prepend(card.el); mark(s.id, true);
    card.refresh(); changed();
    if (!quiet) card.el.scrollIntoView({ block: "nearest", behavior: "smooth" });
  }
  function removeChart(id) { const c = cards.get(id); if (!c) return; c.el.remove(); cards.delete(id); mark(id, false); changed(); }
  function flip(s) { cards.has(s.id) ? removeChart(s.id) : addChart(s); }
  function changed() { empty.hidden = cards.size > 0; clearAll.hidden = cards.size < 2; keep(store, [...cards.keys()].reverse()); }

  // The charts open last time; on a first visit the processor's temperature and the graphics card's hot spot (its core temperature where the
  // card reports no hot spot), or the first sensor, so the column is never blank.
  const saved = remember(store, null);
  const byRole = (kind, ...roles) => { for (const r of roles) { const c = cells.find(([s]) => s.node.kind === kind && s.role === r); if (c) return c[0].id; } return null; };
  const first = [byRole("Cpu", "CpuPackageTemp", "CpuTctlTdie", "CpuCoreTemp"), byRole("Gpu", "GpuHotSpotTemp", "GpuCoreTemp")].filter(Boolean);
  const initial = (Array.isArray(saved) ? saved : first.length ? first.reverse() : [cells[0]?.[0].id]).map((id) => bySensor.get(id)?.[0]).filter(Boolean);
  for (const s of initial) addChart(s, true);
  changed();

  // The part a running test loads: its panels open and come into view, the others fold (only the ones this folded are reopened afterwards),
  // and its key readings are charted. kinds null: the test moved to a part the monitor has no sensors for, or following is off.
  const KEY_ROLES = {
    Cpu: [["CpuPackageTemp", "CpuTctlTdie", "CpuCoreTemp"], ["CpuTotalLoad"], ["CpuPackagePower"]],
    Gpu: [["GpuHotSpotTemp", "GpuCoreTemp"], ["GpuLoad3D", "GpuLoadD3D3D", "GpuLoadCompute"], ["GpuPower"]],
    Memory: [["RamLoad"], ["RamUsed"]], Storage: [["StorageTemp"], ["StorageWriteRate"], ["StorageReadRate"]], Network: [["NetDownload"], ["NetUpload"]],
  };
  const folded = new Set();
  function setShut(g, shut) { g.classList.toggle("shut", shut); g.querySelector(".panel-head .more")?.setAttribute("aria-expanded", String(!shut)); }
  function followPart(kinds) {
    if (!kinds) { for (const g of folded) setShut(g, false); folded.clear(); return; }
    let first = null;
    for (const g of groups.children) {
      const on = kinds.includes(g.dataset.kind) && !("idle" in g.dataset);
      if (on) { setShut(g, false); first ??= g; } else if (!g.classList.contains("shut")) { setShut(g, true); folded.add(g); }
    }
    for (const k of kinds) for (const roles of KEY_ROLES[k] || []) {
      const c = cells.find(([s]) => s.node.kind === k && roles.includes(s.role) && (k !== "Network" || !net.internet || s.node.name === net.internet))
        ?? cells.find(([s]) => s.node.kind === k && roles.includes(s.role));
      if (c) addChart(c[0], true);
    }
    if (!opts?.runSlot) first?.scrollIntoView({ block: "start", behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth" });
  }

  tick();
  const off = subscribe(tick);
  let raf = 0;
  const onResize = () => { cancelAnimationFrame(raf); raf = requestAnimationFrame(() => { for (const c of cards.values()) c.redraw(); }); };
  window.addEventListener("resize", onResize);
  const ro = new ResizeObserver(onResize); ro.observe(stack);
  return () => { off(); run.off(); window.removeEventListener("resize", onResize); ro.disconnect(); };
}
