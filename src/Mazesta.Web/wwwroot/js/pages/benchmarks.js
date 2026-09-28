// Benchmarks: numbers only, no score and no verdict, in a folding panel per part (CPU apart from GPU apart from storage …). Ticked rows run one
// after another; a single row can run on its own. Each row shows the best result kept on this system, and after a run how the new one
// compares with it: only a better run replaces the record (the host keeps it, per system, in Data/benchmarks).
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon } from "../ui.js";
import { setField } from "./tests.js";
import { groupPanel, byPart } from "../groups.js";

// The list, optionally only one part's benchmarks (the component pages reuse it).
export function benchList(component = null) {
  const wrap = h("div", {});
  const runSel = h("button", { class: "btn go", onclick: () => call("bench.exec", { cmd: "runSelected" }) }, icon("play"), t("Bench_RunSelected"));
  const cancel = h("button", { class: "btn stop", onclick: () => call("bench.exec", { cmd: "cancel" }) }, icon("stop"), t("Bench_Cancel"));
  const queue = h("span", { class: "pill run", hidden: true });
  const list = h("div", { class: "groups" });
  wrap.append(list, h("div", { class: "dock" }, runSel, cancel, queue, h("span", { class: "grow" }),
    h("button", { class: "btn quiet", onclick: () => call("bench.exec", { cmd: "selectAll" }) }, t("Test_SelectAll")),
    h("button", { class: "btn quiet", onclick: () => call("bench.exec", { cmd: "clear" }) }, t("Test_ClearSelection"))));

  const rows = new Map(), groups = [];
  function build(s) {
    const mine = s.rows.filter((r) => !component || r.component === component), index = new Map(mine.map((r, i) => [r.id, i]));
    let gi = 0;
    for (const [kind, members] of byPart(mine, (r) => r.component)) {
      const g = groupPanel("bench", kind, gi++, (on) => { for (const r of members) call("bench.set", { id: r.id, field: "selected", value: on }); });
      groups.push({ g, ids: members.map((r) => r.id) }); list.append(g.el);
      for (const r of members) addRow(r, index.get(r.id), g.body);
    }
  }
  function addRow(r, i, into) {
    const set = (field, value, extra = {}) => call("bench.set", { id: r.id, field, value, ...extra });
    const check = h("input", { type: "checkbox", class: "check", "aria-label": r.name, onchange: (e) => set("selected", e.target.checked) });
    const dur = h("input", { class: "field lat short", inputmode: "numeric", oninput: (e) => set("duration", e.target.value) });
    const run = h("button", { class: "btn", onclick: () => call("bench.exec", { cmd: "run", id: r.id }) }, t("Bench_Run"));
    const opts = r.options.map((o) => {
      const input = o.choices
        ? h("select", { class: "field", onchange: (e) => set("option", e.target.value, { key: o.key }) }, o.choices.map((c) => h("option", { value: c.value }, c.label)))
        : h("input", { class: "field lat", style: { width: "110px" }, oninput: (e) => set("option", e.target.value, { key: o.key }) });
      return { o, input, el: h("label", {}, o.label, input) };
    });
    const bar = h("div", { class: "progress" }, h("i")), status = h("span", { class: "caption" }), metrics = h("div", { class: "metrics" }), detail = h("div", { class: "detail", hidden: true });
    const rec = h("div", { class: "rec" }), unavailable = h("div", { class: "unavailable", hidden: true });
    const row = h("div", { class: "q-row", style: { "--i": i } },
      h("span", { class: "step" }, fa(String(i + 1).padStart(2, "0"))), check, h("span", { class: "name" }, r.name),
      h("div", { class: "ctrls" }, h("label", {}, t("Bench_Duration"), dur, t("Test_Seconds")), run),
      opts.length ? h("div", { class: "extra" }, opts.map((x) => x.el)) : null,
      h("div", { class: "state" }, bar, status), unavailable, metrics, rec, detail);
    into.append(row);
    rows.set(r.id, { row, check, dur, run, opts, bar, status, metrics, rec, detail, unavailable, last: "", lastRec: "" });
  }
  function update(s) {
    if (!rows.size) build(s);
    runSel.disabled = !s.canRunSelected; cancel.disabled = !s.running;
    queue.hidden = !s.queue; queue.textContent = s.queue || "";
    for (const r of s.rows) {
      const x = rows.get(r.id); if (!x) continue;
      x.check.checked = r.selected; x.check.disabled = !!r.unavailable; setField(x.dur, r.duration); x.run.disabled = s.running || !!r.unavailable;
      x.row.classList.toggle("off", !!r.unavailable); x.unavailable.hidden = !r.unavailable; x.unavailable.textContent = r.unavailable || "";
      for (const o of x.opts) { const cur = r.options.find((y) => y.key === o.o.key); if (cur) setField(o.input, cur.value); }
      x.bar.firstChild.style.setProperty("--p", r.percent / 100);
      x.status.textContent = r.status || "";
      x.row.classList.toggle("active", r.active);
      const key = JSON.stringify(r.metrics);
      if (key !== x.last) { x.last = key; x.metrics.replaceChildren(...r.metrics.map((m) => h("div", { class: "metric" }, h("div", { class: "v" }, m.value), h("div", { class: "n" }, m.name)))); }
      x.detail.hidden = !r.detail; x.detail.textContent = r.detail || "";
      const recKey = JSON.stringify([r.best, r.compared]);
      if (recKey !== x.lastRec) { x.lastRec = recKey; x.rec.replaceChildren(...record(r)); }
    }
    const byId = new Map(s.rows.map((r) => [r.id, r]));
    for (const { g, ids } of groups) {
      const rs = ids.map((id) => byId.get(id)).filter(Boolean);
      g.sync(rs.length, rs.filter((r) => r.selected).length, rs.some((r) => r.active));
    }
  }
  call("bench.state").then(update);
  return { el: wrap, off: on("bench", update) };
}

// The record line: the best result kept on this system; after a run, the run against it. A better run is saved, a lower one is shown and
// dropped. The change is written as a signed percentage in the part's hue for a record, plain for a lower run (never pass/fail colours).
function record(r) {
  const c = r.compared, b = r.best;
  const cell = (label, m, extra = null) => h("div", { class: "rec-cell" }, h("span", { class: "k" }, label), h("span", { class: "v num" }, m.value), h("span", { class: "d" }, m.name, extra ? " · " : "", extra ? h("span", { class: "lat" }, extra) : null));
  if (c) {
    const pct = c.change === null || c.change === undefined ? null : `${c.change > 0 ? "+" : c.change < 0 ? "−" : ""}${Math.abs(c.change).toFixed(1)}%`;
    const verdict = !c.previous ? "Web_Bench_FirstRecord" : c.saved ? "Web_Bench_NewRecord" : c.change === 0 ? "Web_Bench_Equal" : "Web_Bench_Lower";
    return [h("div", { class: `rec-grid ${c.saved ? "up" : "down"}` },
      cell(t("Web_Bench_ThisRun"), c.now), c.previous ? cell(t("Web_Bench_Best"), c.previous, c.previous.at) : null,
      h("div", { class: "rec-verdict" }, c.saved ? icon("trophy") : null, pct ? h("span", { class: "num pct" }, pct) : null, h("span", {}, t(verdict))))];
  }
  if (b) return [h("div", { class: "rec-grid" }, cell(t("Web_Bench_Best"), b, b.at))];
  return [h("p", { class: "rec-none" }, t("Web_Bench_NoRecord"))];
}

export function mount(el) {
  const list = benchList();
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Benchmarks")), h("p", { class: "page-lede" }, t("Bench_Note")))), list.el);
  return list.off;
}
