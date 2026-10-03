// The test queue, in a folding panel per part (CPU, memory, storage, GPU …) so a long list stays readable: every test is a numbered step,
// ticked to run (one by one or a whole group), with its own length, repeat and options. The engine keeps running when the page is left; the
// page only mirrors it. A test that did not run is never shown as passed.
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon } from "../ui.js";
import { partOfId } from "../parts.js";
import { groupPanel, byPart } from "../groups.js";
import { pageOfRun } from "../testrun.js";

export const OUTCOME = { Passed: "pass", Failed: "fail", Cancelled: "warn", Unsupported: "warn", Error: "warn", Inconclusive: "warn", Running: "run", NotRun: "none" };

// Keeps a field's value unless the technician is typing in it.
export function setField(el, v) { if (document.activeElement !== el && el.value !== (v ?? "")) el.value = v ?? ""; }

export function mount(el) {
  const list = h("div", { class: "groups" });
  const notice = h("div", { class: "banner", hidden: true });
  const blocked = h("div", { class: "banner", role: "status", hidden: true });
  // Ready-made selections: each picks its tests and their lengths; the note says what it covers and what it leaves out.
  const profiles = h("div", { class: "profiles", role: "group", "aria-label": t("Profile_Title") }), profileNote = h("p", { class: "caption profile-note", hidden: true });
  // Start goes to the live monitor as soon as the queue is really running; a row with a bad field keeps the page here, where its error shows.
  let toMonitor = false;
  const start = h("button", { class: "btn go", "data-a": "start", onclick: () => { toMonitor = true; call("tests.exec", { cmd: "start" }).finally(() => { toMonitor = false; }); } }, icon("play"), t("Test_Start"));
  const watch = h("a", { class: "btn", href: "#/monitoring", hidden: true }, icon("pulse"), t("Web_Run_Live"));   // points at the running test's page
  const cancel = h("button", { class: "btn stop", onclick: () => call("tests.exec", { cmd: "cancel" }) }, icon("stop"), t("Test_Cancel"));
  // Side by side: the processor's, the memory's and the graphics card's tests load the machine together, as real work does.
  const togetherBox = h("input", { type: "checkbox", class: "switch", "aria-label": t("Test_Together"), onchange: (e) => call("tests.exec", { cmd: "together", value: e.target.checked }) });
  const together = h("label", { class: "run-follow", title: t("Test_Together_Hint") }, togetherBox, t("Test_Together"));
  el.append(
    h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Tests")), h("p", { class: "page-lede" }, t("Web_Tests_Lede")))),
    notice, blocked, h("div", { class: "profile-bar" }, h("span", { class: "caption" }, t("Profile_Title")), profiles), profileNote, list,
    h("div", { class: "dock" }, start, cancel, watch, together, h("span", { class: "grow" }),
      h("button", { class: "btn quiet", onclick: () => call("tests.exec", { cmd: "selectAll" }) }, t("Test_SelectAll")),
      h("button", { class: "btn quiet", onclick: () => call("tests.exec", { cmd: "clear" }) }, t("Test_ClearSelection"))));

  const rows = new Map(), groups = [];
  function build(s) {
    const index = new Map(s.rows.map((r, i) => [r.id, i]));
    let gi = 0;
    for (const [kind, members] of byPart(s.rows, (r) => partOfId(r.id))) {
      const g = groupPanel("tests", kind, gi++, (on) => { for (const r of members) call("tests.set", { id: r.id, field: "selected", value: on }); });
      groups.push({ g, ids: members.map((r) => r.id) }); list.append(g.el);
      for (const r of members) addRow(s, r, index.get(r.id), g.body);
    }
  }
  function addRow(s, r, i, into) {
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
    const unavailable = h("div", { class: "unavailable", hidden: true }), advice = h("div", { class: "advice", hidden: true });
    const row = h("div", { class: "q-row", style: { "--i": i } },
      h("span", { class: "step" }, fa(String(i + 1).padStart(2, "0"))), check, h("span", { class: "name" }, r.name),
      h("div", { class: "ctrls" }, h("label", {}, dur, t("Test_Seconds")), rep, cnt),
      opts.length ? h("div", { class: "extra" }, opts.map((x) => x.el)) : null,
      h("div", { class: "state" }, bar, h("span", {}, status, " ", errs), pill), unavailable, error, detail, advice);
    into.append(row);
    rows.set(r.id, { row, check, dur, rep, cnt, opts, bar, status, pill, error, detail, errs, unavailable, advice });
  }
  function update(s) {
    if (!rows.size) build(s);
    if (!profiles.childElementCount) profiles.append(...s.profiles.map((p) => h("button", { class: "btn quiet", type: "button", onclick: () => call("tests.exec", { cmd: "profile", id: p.id }) }, p.name)));
    for (const b of profiles.children) b.disabled = s.running;
    profileNote.hidden = !s.profileNote; profileNote.textContent = s.profileNote || "";
    start.disabled = !s.canStart; cancel.disabled = !s.running; watch.hidden = !s.running;
    togetherBox.checked = !!s.together; togetherBox.disabled = s.running;
    const livePage = s.current ? pageOfRun(s.current) : "monitoring";
    watch.href = `#/${livePage}`;
    // The tested part's page once the first test is under way (its page is known only then).
    if (toMonitor && s.running && s.current && s.current.outcome === "Running") {   // a test really under way, never the last session's last one toMonitor = false; location.hash = `#/${livePage}`; return; }
    notice.hidden = !s.incomplete;
    blocked.hidden = !s.blocked; blocked.textContent = s.blocked || "";
    if (s.incomplete) notice.replaceChildren(h("span", { class: "grow" }, s.incomplete), h("button", { class: "btn", onclick: () => call("tests.exec", { cmd: "dismissIncomplete" }) }, t("Test_IncompleteSession_Dismiss")));
    for (const r of s.rows) {
      const x = rows.get(r.id); if (!x) continue;
      x.check.checked = r.selected; x.check.disabled = !!r.unavailable; setField(x.dur, r.duration); x.rep.value = r.repeat;
      // A test this machine cannot run is shown with the reason, never offered: it could only end Unsupported.
      x.row.classList.toggle("off", !!r.unavailable); x.unavailable.hidden = !r.unavailable; x.unavailable.textContent = r.unavailable || "";
      setField(x.cnt, r.count); x.cnt.hidden = r.repeat !== "Count";
      for (const o of x.opts) { const cur = r.options.find((y) => y.key === o.o.key); if (cur) setField(o.input, cur.value); }
      x.bar.firstChild.style.setProperty("--p", r.percent);
      x.status.textContent = r.status || ""; x.errs.textContent = r.errors || "";
      x.pill.className = `pill ${OUTCOME[r.outcome] || "none"}`; x.pill.textContent = r.outcomeText;
      x.row.classList.toggle("active", r.outcome === "Running");
      x.error.hidden = !r.error; x.error.textContent = r.error || "";
      x.detail.hidden = !r.detail; x.detail.textContent = r.detail || "";
      x.advice.hidden = !r.advice; x.advice.textContent = r.advice || "";
    }
    const byId = new Map(s.rows.map((r) => [r.id, r]));
    for (const { g, ids } of groups) {
      const rs = ids.map((id) => byId.get(id)).filter(Boolean);
      g.sync(rs.length, rs.filter((r) => r.selected).length, rs.some((r) => r.outcome === "Running"));
    }
  }
  call("tests.state").then(update);
  return on("tests", update);
}
