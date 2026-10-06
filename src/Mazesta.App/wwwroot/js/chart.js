// The pop-out chart window: one sensor's card filling the window, with its own time range. The host answers only for this sensor.
import { call, on } from "./bridge.js";
import { setStrings, t } from "./i18n.js";
import { setUnits } from "./format.js";
import { subscribe } from "./store.js";
import { h } from "./ui.js";
import { chartCard, RANGES } from "./chartcard.js";

async function start() {
  const info = await call("chart.boot", { id: new URLSearchParams(location.search).get("id") });
  setStrings(info.strings, info.rtl, info.language); setUnits(info.units);
  const s = info.sensor;
  document.title = `${s.name} — ${s.node}`;
  let windowSec = 600;
  const card = chartCard(s, { kind: s.part, nodeName: s.node, windowSec: () => windowSec });
  const ranges = h("div", { class: "chart-range", role: "group", "aria-label": t("Web_Chart_Window") }, RANGES.map((sec) => h("button", { class: `btn ${sec === windowSec ? "primary" : ""}`, type: "button", onclick: (e) => {
    windowSec = sec; for (const b of ranges.children) b.classList.toggle("primary", b === e.currentTarget); card.redraw();
  } }, t("Web_Minutes", sec / 60))));
  document.getElementById("chart").append(card.el, ranges);
  subscribe(() => { card.update(); card.refresh(); });
  on("visibility", () => card.redraw());
  card.refresh();
  let raf = 0;
  new ResizeObserver(() => { cancelAnimationFrame(raf); raf = requestAnimationFrame(card.redraw); }).observe(card.el);
}
start().catch((err) => document.body.append(h("pre", { class: "console" }, String(err && err.stack || err))));
