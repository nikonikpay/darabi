// Reports: every saved report in its own box with its formats and its one-page summary (the highest temperatures while its tests ran), and
// before/after comparison of two ticked reports.
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { h, toast } from "../ui.js";
import { box } from "../groups.js";
import { boot } from "../app.js";

const BADGE = { Passed: "pass", Failed: "fail", Incomplete: "warn", Benchmark: "run" };

export function mount(el) {
  const compare = h("button", { class: "btn primary", "data-a": "compare", onclick: () => call("reports.exec", { cmd: "compare" }) }, t("Reports_Compare"));
  const remove = h("button", { class: "btn stop", "data-a": "delete-selected", onclick: () => call("reports.exec", { cmd: "deleteSelected" }) }, t("Reports_DeleteSelected"));
  // The company's edition lists the users' copy's reports (the installed one, or a Data folder chosen with Browse).
  const sourceLine = h("p", { class: "caption", style: { minHeight: "1.6em" } });
  const pick = (cmd) => call("reports.setSource", { cmd }).then((r) => { if (r.error) toast(r.error, "fail"); else update(r); }).catch((e) => toast(String(e.message || e), "fail"));
  const sourceBar = boot.staff ? h("div", { class: "btn-row" }, h("button", { class: "btn primary", "data-a": "browse", onclick: () => pick("browse") }, t("Reports_Source_Browse")),
    h("button", { class: "btn", onclick: () => pick("usual") }, t("Reports_Source_Usual")), h("button", { class: "btn quiet", onclick: () => pick("own") }, t("Reports_Source_Own"))) : null;
  const list = h("div", {}), status = h("p", { class: "caption", style: { minHeight: "1.6em" } });
  const count = h("span", { class: "group-count" });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Reports")))),
    h("div", { class: "panels", style: { gridTemplateColumns: "1fr" } },
      box({ kind: "System", ico: "doc", title: t("Reports_Saved"), sub: t("Reports_CompareHint"), i: 0, actions: [count, remove, compare], body: [...(boot.staff ? [sourceLine, sourceBar] : []), status, list] })));
  const act = (cmd, id, key, cls = "btn") => h("button", { class: cls, onclick: () => call("reports.exec", { cmd, id }) }, t(key));
  let shown = "", making = false;
  // The summary of one report goes to the shop's site, where colleagues print it for the serviced case.
  async function send(id, button) {
    button.disabled = true;
    try {
      const r = await call("site.report", { id });
      if (r.error) toast(r.error, "fail"); else { toast(t(r.updated ? "Site_Report_Updated" : "Site_Report_Sent"), "ok"); call("reports.state").then(update); }
    } catch (e) { toast(String(e.message || e), "fail"); }
    button.disabled = false;
  }
  // The service number and the notes on the work done, written into the report before it is sent.
  function notesEditor(r) {
    const service = h("input", { class: "field", value: r.service || "", dir: "ltr", "aria-label": t("Reports_Notes_Service"), placeholder: t("Reports_Notes_Service") });
    const notes = h("textarea", { class: "field", rows: 5, placeholder: t("Reports_Notes_Hint"), style: { width: "100%" } }); notes.value = r.notes || "";
    const save = h("button", { class: "btn primary", onclick: async () => {
      save.disabled = true;
      try { const x = await call("reports.notes", { id: r.id, service: service.value, notes: notes.value }); if (x.error) toast(x.error, "fail"); else toast(t("Reports_Notes_Saved"), "ok"); }
      catch (e) { toast(String(e.message || e), "fail"); }
      save.disabled = false;
    } }, t("Reports_Notes_Save"));
    return h("div", { class: "notes", style: { gridColumn: "1 / -1", display: "grid", gap: "8px" } }, service, notes, h("div", {}, save));
  }
  function update(s) {
    if (boot.staff && s.source) sourceLine.textContent = s.source.own ? t("Reports_Source_Own") : t("Reports_Source_Current", s.source.path);
    if (boot.staff && s.source && s.source.own && !s.source.usual && !s.items.length) sourceLine.textContent = t("Reports_Source_Missing");
    compare.disabled = !s.canCompare; remove.disabled = !s.canDelete; status.textContent = s.status || "";
    if (s.making !== making) { making = s.making; for (const b of list.querySelectorAll("button[data-summary]")) b.disabled = making; }
    const key = JSON.stringify([s.source, s.items.map((i) => [i.id, i.selected, i.site, i.service, i.notes])]);
    if (key === shown) return; shown = key;
    count.textContent = s.items.length ? t("Web_Group_Count", s.items.length, s.items.filter((i) => i.selected).length) : "";
    if (!s.items.length) { list.replaceChildren(h("p", { class: "caption" }, t("Reports_Empty"))); return; }
    list.replaceChildren(...s.items.map((r) => {
      const summary = act("summary", r.id, "Reports_Summary", "btn primary"); summary.dataset.summary = ""; summary.disabled = making;
      return h("div", { class: "report" },
        h("input", { type: "checkbox", class: "check", checked: r.selected, "aria-label": t("Reports_CompareHint"), onchange: (e) => call("reports.select", { id: r.id, value: e.target.checked }) }),
        h("span", { class: `pill ${BADGE[r.badge] || "none"}` }, r.verdict), h("span", { class: "title" }, r.title),
        h("div", { class: "acts" }, summary, act("html", r.id, "Reports_Html"), act("pdf", r.id, "Reports_Pdf"), act("text", r.id, "Reports_Text"), act("json", r.id, "Reports_Json"),
          act("folder", r.id, "Reports_Folder"),
          boot.staff ? h("button", { class: "btn", "data-a": "send-site", onclick: (e) => send(r.id, e.currentTarget) }, t("Site_Report_Send")) : null,
          boot.staff && r.site ? h("button", { class: "btn quiet", onclick: () => call("site.open", { url: r.site }) }, t("Site_Report_Open")) : null,
          act("delete", r.id, "Reports_Delete", "btn stop")),
        h("span", { class: "sum" }, r.summary), boot.staff ? h("details", { style: { gridColumn: "1 / -1" } }, h("summary", { class: "caption" }, t("Reports_Notes") + (r.service ? ` · ${r.service}` : "") + (r.notes ? " ✓" : "")), notesEditor(r)) : null);
    }));
  }
  call("reports.state").then(update);
  return on("reports", update);
}
