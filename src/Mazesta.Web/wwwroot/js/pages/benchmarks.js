// Benchmarks: numbers only, no score and no verdict. Ticked rows run one after another; a single row can run on its own.
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon } from "../ui.js";
import { setField } from "./tests.js";

// The list, optionally only one part's benchmarks (the component pages reuse it).
export function benchList(component = null) {
  const wrap = h("div", {});
  const runSel = h("button", { class: "btn go", onclick: () => call("bench.exec", { cmd: "runSelected" }) }, icon("play"), t("Bench_RunSelected"));
  const cancel = h("button", { class: "btn stop", onclick: () => call("bench.exec", { cmd: "cancel" }) }, icon("stop"), t("Bench_Cancel"));
  const queue = h("span", { class: "pill run", hidden: true });
  const list = h("div", { class: "queue" });
  wrap.append(list, h("div", { class: "dock" }, runSel, cancel, queue, h("span", { class: "grow" }),
    h("button", { class: "btn quiet", onclick: () => call("bench.exec", { cmd: "selectAll" }) }, t("Test_SelectAll")),
    h("button", { class: "btn quiet", onclick: () => call("bench.exec", { cmd: "clear" }) }, t("Test_ClearSelection"))));

  const rows = new Map();
  function build(s) {
    s.rows.filter((r) => !component || r.component === component).forEach((r, i) => {
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
      const row = h("div", { class: "q-row", style: { "--i": i } },
        h("span", { class: "step" }, fa(String(i + 1).padStart(2, "0"))), check, h("span", { class: "name" }, r.name),
        h("div", { class: "ctrls" }, h("label", {}, t("Bench_Duration"), dur, t("Test_Seconds")), run),
        opts.length ? h("div", { class: "extra" }, opts.map((x) => x.el)) : null,
        h("div", { class: "state" }, bar, status), metrics, detail);
      list.append(row);
      rows.set(r.id, { row, check, dur, run, opts, bar, status, metrics, detail, last: "" });
    });
  }
  function update(s) {
    if (!rows.size) build(s);
    runSel.disabled = !s.canRunSelected; cancel.disabled = !s.running;
    queue.hidden = !s.queue; queue.textContent = s.queue || "";
    for (const r of s.rows) {
      const x = rows.get(r.id); if (!x) continue;
      x.check.checked = r.selected; setField(x.dur, r.duration); x.run.disabled = s.running;
      for (const o of x.opts) { const cur = r.options.find((y) => y.key === o.o.key); if (cur) setField(o.input, cur.value); }
      x.bar.firstChild.style.setProperty("--p", r.percent / 100);
      x.status.textContent = r.status || "";
      x.row.classList.toggle("active", r.active);
      const key = JSON.stringify(r.metrics);
      if (key !== x.last) { x.last = key; x.metrics.replaceChildren(...r.metrics.map((m) => h("div", { class: "metric" }, h("div", { class: "v" }, m.value), h("div", { class: "n" }, m.name)))); }
      x.detail.hidden = !r.detail; x.detail.textContent = r.detail || "";
    }
  }
  call("bench.state").then(update);
  return { el: wrap, off: on("bench", update) };
}

export function mount(el) {
  const list = benchList();
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Benchmarks")), h("p", { class: "page-lede" }, t("Bench_Note")))), list.el);
  return list.off;
}
