// Every sensor, grouped by hardware then by kind, at data density. A row selects its history on the right. Filtering hides rows, it never
// hides that a sensor exists without a reading: those rows say "not available" in the hatch.
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { fmt } from "../format.js";
import { hw, value, quality, stats, subscribe } from "../store.js";
import { h, val, drawChart } from "../ui.js";

const KIND_ORDER = ["Temperature", "Load", "Clock", "Power", "Voltage", "Current", "Fan", "Control", "Data", "SmallData", "Throughput", "Level", "Energy", "Timespan", "Factor", "Frequency", "Timing", "Noise", "Flow", "Humidity", "Conductivity"];

export function mount(el, _, focusKinds = null) {
  const filter = h("input", { class: "field search", type: "search", placeholder: t("Monitoring_Search"), "aria-label": t("Monitoring_Search") });
  const tbody = h("tbody");
  const table = h("table", { class: "table" }, h("thead", {}, h("tr", {},
    h("th", {}, t("Web_Col_Sensor")), h("th", { class: "n" }, t("Web_Col_Current")), h("th", { class: "n" }, t("Web_Col_Min")),
    h("th", { class: "n" }, t("Web_Col_Avg")), h("th", { class: "n" }, t("Web_Col_Max")))), tbody);

  const big = h("div", { class: "big" }), chartTitle = h("div", { class: "h3 sensor-name" }), chartSub = h("div", { class: "caption" });
  const canvas = h("canvas", { class: "chart" });
  let selected = null, windowSec = 600, series = null;
  const ranges = h("div", { class: "chart-range" }, [60, 300, 600, 900].map((s) => h("button", { class: `btn ${s === windowSec ? "primary" : ""}`, onclick: (e) => {
    windowSec = s; for (const b of ranges.children) b.classList.toggle("primary", b === e.currentTarget); redraw();
  } }, t("Web_Minutes", s / 60))));
  const chart = h("aside", { class: "chart-card" }, chartTitle, chartSub, big, canvas, ranges);

  if (!focusKinds) el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Monitoring")), h("p", { class: "page-lede" }, t("Web_Monitoring_Lede")))));
  el.append(h("div", { class: "toolbar" }, filter, h("span", { class: "grow" })), h("div", { class: "split" }, h("div", {}, table), chart));

  const cells = [];   // [sensor, current, min, avg, max, row, searchText]
  for (const node of hw.nodes) {
    if (focusKinds && !focusKinds.includes(node.kind)) continue;
    if (!node.sensors.length) continue;
    const grp = h("tr", { class: "grp" }, h("td", { colspan: "5" }, h("span", { class: "gname" }, node.name), h("span", { class: "gkind" }, t(`Web_Kind_${node.kind}`))));
    tbody.append(grp);
    const byKind = new Map();
    for (const s of node.sensors) { if (!byKind.has(s.kind)) byKind.set(s.kind, []); byKind.get(s.kind).push({ ...s, node }); }
    const kinds = [...byKind.keys()].sort((a, b) => (KIND_ORDER.indexOf(a) + 99) % 99 - (KIND_ORDER.indexOf(b) + 99) % 99);
    for (const k of kinds) {
      tbody.append(h("tr", { class: "sec", "data-node": node.id }, h("td", { colspan: "5" }, t(`SensorKind_${k}`))));
      for (const s of byKind.get(k)) {
        const c = [h("td", { class: "n" }), h("td", { class: "n" }), h("td", { class: "n" }), h("td", { class: "n" })];
        const row = h("tr", { class: "row", tabindex: "0", onclick: () => select(s, row), onkeydown: (e) => { if (e.key === "Enter") select(s, row); } },
          h("td", {}, h("span", { class: "sensor-name" }, s.name)), ...c);
        tbody.append(row);
        cells.push([s, ...c, row, `${node.name} ${s.name}`.toLowerCase()]);
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
    if (selected) { big.replaceChildren(val(fmt(value(selected.id), selected.unit), "")); refresh(); }
  }
  filter.addEventListener("input", () => {
    const q = filter.value.trim().toLowerCase();
    for (const [, , , , , row, text] of cells) row.hidden = q && !text.includes(q);
  });

  async function refresh() { if (!selected) return; series = await call("history.get", { id: selected.id }); redraw(); }
  function redraw() { if (series && selected) drawChart(canvas, series, windowSec, selected.unit); }
  function select(s, row) {
    selected = s;
    for (const [, , , , , r] of cells) r.classList.toggle("on", r === row);
    chartTitle.textContent = s.name; chartSub.textContent = s.node.name;
    big.replaceChildren(val(fmt(value(s.id), s.unit), ""));
    refresh();
  }
  chart.hidden = false;
  if (cells.length) select(cells[0][0], cells[0][5]);
  tick();
  const off = subscribe(tick);
  const onResize = () => redraw(); window.addEventListener("resize", onResize);
  return () => { off(); window.removeEventListener("resize", onResize); };
}
