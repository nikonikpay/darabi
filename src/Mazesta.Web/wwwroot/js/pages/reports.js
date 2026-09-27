// Reports: the one-page customer summary on its yellow plane, then every saved report in its own box with its formats, and before/after
// comparison of two ticked reports.
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon, regMarks } from "../ui.js";
import { box } from "../groups.js";

const BADGE = { Passed: "pass", Failed: "fail", Incomplete: "warn", Benchmark: "run" };

export function mount(el) {
  const make = h("button", { class: "slab", onclick: () => call("reports.exec", { cmd: "summary" }) }, t("Reports_Summary"), icon("arrow"));
  const plane = h("section", { class: "plane enter" }, regMarks(),
    h("h2", { class: "plane-head" }, t("Reports_Summary_Title")),
    h("p", { style: { maxWidth: "62ch", fontSize: "15px", fontWeight: "600", margin: "18px 0 22px" } }, t("Reports_Summary_Note")), make);
  const compare = h("button", { class: "btn primary", onclick: () => call("reports.exec", { cmd: "compare" }) }, t("Reports_Compare"));
  const list = h("div", {}), status = h("p", { class: "caption", style: { minHeight: "1.6em" } });
  const count = h("span", { class: "group-count" });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Reports")))), plane,
    h("div", { class: "panels", style: { gridTemplateColumns: "1fr" } },
      box({ kind: "System", ico: "doc", title: t("Reports_Saved"), sub: t("Reports_CompareHint"), i: 0, actions: [count, compare], body: [status, list] })));
  const act = (cmd, id, key, cls = "btn") => h("button", { class: cls, onclick: () => call("reports.exec", { cmd, id }) }, t(key));
  let shown = "";
  function update(s) {
    make.disabled = s.making; compare.disabled = !s.canCompare; status.textContent = s.status || "";
    const key = JSON.stringify(s.items.map((i) => [i.id, i.selected]));
    if (key === shown) return; shown = key;
    count.textContent = s.items.length ? t("Web_Group_Count", s.items.length, s.items.filter((i) => i.selected).length) : "";
    if (!s.items.length) { list.replaceChildren(h("p", { class: "caption" }, t("Reports_Empty"))); return; }
    list.replaceChildren(...s.items.map((r) => h("div", { class: "report" },
      h("input", { type: "checkbox", class: "check", checked: r.selected, "aria-label": t("Reports_CompareHint"), onchange: (e) => call("reports.select", { id: r.id, value: e.target.checked }) }),
      h("span", { class: `pill ${BADGE[r.badge] || "none"}` }, r.verdict), h("span", { class: "title" }, r.title),
      h("div", { class: "acts" }, act("html", r.id, "Reports_Html", "btn primary"), act("pdf", r.id, "Reports_Pdf"), act("text", r.id, "Reports_Text"), act("json", r.id, "Reports_Json"),
        act("folder", r.id, "Reports_Folder"), act("delete", r.id, "Reports_Delete", "btn stop")),
      h("span", { class: "sum" }, r.summary))));
  }
  call("reports.state").then(update);
  return on("reports", update);
}
