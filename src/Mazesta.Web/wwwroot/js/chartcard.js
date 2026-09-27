// One sensor's chart as a card: its name, part and kind, the reading now and the session's min/avg/max, and its history drawn in the part's
// hue. Used by the monitoring page (a stack of them) and by the pop-out window (one, filling the window).
import { call } from "./bridge.js";
import { t } from "./i18n.js";
import { fmt } from "./format.js";
import { value, stats } from "./store.js";
import { h, val, icon, drawChart } from "./ui.js";
import { part, hueOf } from "./parts.js";

export const RANGES = [60, 300, 600, 900];

export function chartCard(s, { kind, nodeName, windowSec, onRemove = null, onPopout = null }) {
  const hue = hueOf(kind);
  const now = h("div", { class: "cc-now" }), mm = h("div", { class: "cc-mm" }), canvas = h("canvas", { class: "chart", role: "img", "aria-label": `${s.name} — ${nodeName}` });
  const btn = (ico, label, fn) => h("button", { class: "icon-btn", type: "button", title: label, "aria-label": label, onclick: fn }, icon(ico));
  const el = h("article", { class: `chart-card ${part(kind).cls}` },
    h("header", { class: "cc-head" },
      h("div", { class: "cc-ttl" }, h("div", { class: "cc-name lat" }, s.name), h("div", { class: "cc-sub" }, h("span", { class: "lat" }, nodeName), " · ", t(`SensorKind_${s.kind}`))),
      now,
      h("div", { class: "cc-acts" }, onPopout ? btn("popout", t("Web_Chart_Popout"), onPopout) : null, onRemove ? btn("x", t("Web_Chart_Remove"), onRemove) : null)),
    canvas, mm);
  let series = null;
  const redraw = () => { if (series) drawChart(canvas, series, windowSec(), s.unit, hue); };
  const refresh = async () => { series = await call("history.get", { id: s.id }); redraw(); };
  const update = () => {
    now.replaceChildren(val(fmt(value(s.id), s.unit), "num"));
    const st = stats.get(s.id);
    mm.replaceChildren(...(st ? [["Web_Col_Min", st[0]], ["Web_Col_Avg", st[1]], ["Web_Col_Max", st[2]]].map(([k, v]) => h("span", {}, h("b", {}, t(k)), h("span", { class: "num" }, fmt(v, s.unit)))) : []));
  };
  update();
  return { el, id: s.id, refresh, redraw, update };
}
