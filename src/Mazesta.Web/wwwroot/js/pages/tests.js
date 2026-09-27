// The test queue: every test is a numbered step in the margin, ticked to run, with its own length, repeat and options. The engine keeps
// running when the page is left; the page only mirrors it. A test that did not run is never shown as passed.
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon } from "../ui.js";

export const OUTCOME = { Passed: "pass", Failed: "fail", Cancelled: "warn", Unsupported: "warn", Running: "run", NotRun: "none" };

// Keeps a field's value unless the technician is typing in it.
export function setField(el, v) { if (document.activeElement !== el && el.value !== (v ?? "")) el.value = v ?? ""; }

export function mount(el) {
  const list = h("div", { class: "queue" });
  const notice = h("div", { class: "banner", hidden: true });
  const start = h("button", { class: "btn go", onclick: () => call("tests.exec", { cmd: "start" }) }, icon("play"), t("Test_Start"));
  const cancel = h("button", { class: "btn stop", onclick: () => call("tests.exec", { cmd: "cancel" }) }, icon("stop"), t("Test_Cancel"));
  el.append(
    h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Tests")), h("p", { class: "page-lede" }, t("Web_Tests_Lede")))),
    notice, list,
    h("div", { class: "dock" }, start, cancel, h("span", { class: "grow" }),
      h("button", { class: "btn quiet", onclick: () => call("tests.exec", { cmd: "selectAll" }) }, t("Test_SelectAll")),
      h("button", { class: "btn quiet", onclick: () => call("tests.exec", { cmd: "clear" }) }, t("Test_ClearSelection"))));

  const rows = new Map();
  function build(s) {
    s.rows.forEach((r, i) => {
      const set = (field, value, extra = {}) => call("tests.set", { id: r.id, field, value, ...extra });
      const check = h("input", { type: "checkbox", class: "check", "aria-label": r.name, onchange: (e) => set("selected", e.target.checked) });
      const dur = h("input", { class: "field lat short", inputmode: "numeric", "aria-label": t("Test_Seconds"), oninput: (e) => set("duration", e.target.value) });
      const rep = h("select", { class: "field", onchange: (e) => set("repeat", e.target.value) }, s.repeatModes.map((m) => h("option", { value: m.value }, m.label)));
      const cnt = h("input", { class: "field lat short", inputmode: "numeric", oninput: (e) => set("count", e.target.value) });
      const opts = r.options.map((o) => {
        const input = o.choices
          ? h("select", { class: "field", onchange: (e) => set("option", e.target.value, { key: o.key }) }, o.choices.map((c) => h("option", { value: c.value }, c.label)))
          : h("input", { class: "field lat", style: { width: "110px" }, oninput: (e) => set("option", e.target.value, { key: o.key }) });
        return { o, input, el: h("label", {}, o.label, input) };
      });
      const bar = h("div", { class: "progress" }, h("i")), status = h("span", { class: "caption" }), pill = h("span", { class: "pill none" });
      const error = h("div", { class: "error", hidden: true }), detail = h("div", { class: "detail", hidden: true }), errs = h("span", { class: "caption lat" });
      const row = h("div", { class: "q-row", style: { "--i": i } },
        h("span", { class: "step" }, fa(String(i + 1).padStart(2, "0"))), check, h("span", { class: "name" }, r.name),
        h("div", { class: "ctrls" }, h("label", {}, dur, t("Test_Seconds")), rep, cnt),
        opts.length ? h("div", { class: "extra" }, opts.map((x) => x.el)) : null,
        h("div", { class: "state" }, bar, h("span", {}, status, " ", errs), pill), error, detail);
      list.append(row);
      rows.set(r.id, { row, check, dur, rep, cnt, opts, bar, status, pill, error, detail, errs });
    });
  }
  function update(s) {
    if (!rows.size) build(s);
    start.disabled = !s.canStart; cancel.disabled = !s.running;
    notice.hidden = !s.incomplete;
    if (s.incomplete) notice.replaceChildren(h("span", { class: "grow" }, s.incomplete), h("button", { class: "btn", onclick: () => call("tests.exec", { cmd: "dismissIncomplete" }) }, t("Test_IncompleteSession_Dismiss")));
    for (const r of s.rows) {
      const x = rows.get(r.id); if (!x) continue;
      x.check.checked = r.selected; setField(x.dur, r.duration); x.rep.value = r.repeat;
      setField(x.cnt, r.count); x.cnt.hidden = r.repeat !== "Count";
      for (const o of x.opts) { const cur = r.options.find((y) => y.key === o.o.key); if (cur) setField(o.input, cur.value); }
      x.bar.firstChild.style.setProperty("--p", r.percent);
      x.status.textContent = r.status || ""; x.errs.textContent = r.errors || "";
      x.pill.className = `pill ${OUTCOME[r.outcome] || "none"}`; x.pill.textContent = r.outcomeText;
      x.row.classList.toggle("active", r.outcome === "Running");
      x.error.hidden = !r.error; x.error.textContent = r.error || "";
      x.detail.hidden = !r.detail; x.detail.textContent = r.detail || "";
    }
  }
  call("tests.state").then(update);
  return on("tests", update);
}
