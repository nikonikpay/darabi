// Reports: every saved report in its own box with its formats and its one-page summary (the highest temperatures while its tests ran), and
// before/after comparison of two ticked reports.
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { h } from "../ui.js";
import { box } from "../groups.js";

const BADGE = { Passed: "pass", Failed: "fail", Incomplete: "warn", Benchmark: "run" };

export function mount(el) {
  const compare = h("button", { class: "btn primary", onclick: () => call("reports.exec", { cmd: "compare" }) }, t("Reports_Compare"));
  const list = h("div", {}), status = h("p", { class: "caption", style: { minHeight: "1.6em" } });
  const count = h("span", { class: "group-count" });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Reports")))),
    h("div", { class: "panels", style: { gridTemplateColumns: "1fr" } },
      box({ kind: "System", ico: "doc", title: t("Reports_Saved"), sub: t("Reports_CompareHint"), i: 0, actions: [count, compare], body: [status, list] })));
  const act = (cmd, id, key, cls = "btn") => h("button", { class: cls, onclick: () => call("reports.exec", { cmd, id }) }, t(key));
  let shown = "", making = false;
  function update(s) {
    compare.disabled = !s.canCompare; status.textContent = s.status || "";
    if (s.making !== making) { making = s.making; for (const b of list.querySelectorAll("button[data-summary]")) b.disabled = making; }
    const key = JSON.stringify(s.items.map((i) => [i.id, i.selected]));
    if (key === shown) return; shown = key;
    count.textContent = s.items.length ? t("Web_Group_Count", s.items.length, s.items.filter((i) => i.selected).length) : "";
    if (!s.items.length) { list.replaceChildren(h("p", { class: "caption" }, t("Reports_Empty"))); return; }
    list.replaceChildren(...s.items.map((r) => {
      const summary = act("summary", r.id, "Reports_Summary", "btn primary"); summary.dataset.summary = ""; summary.disabled = making;
      return h("div", { class: "report" },
        h("input", { type: "checkbox", class: "check", checked: r.selected, "aria-label": t("Reports_CompareHint"), onchange: (e) => call("reports.select", { id: r.id, value: e.target.checked }) }),
        h("span", { class: `pill ${BADGE[r.badge] || "none"}` }, r.verdict), h("span", { class: "title" }, r.title),
        h("div", { class: "acts" }, summary, act("html", r.id, "Reports_Html"), act("pdf", r.id, "Reports_Pdf"), act("text", r.id, "Reports_Text"), act("json", r.id, "Reports_Json"),
          act("folder", r.id, "Reports_Folder"), act("delete", r.id, "Reports_Delete", "btn stop")),
        h("span", { class: "sum" }, r.summary));
    }));
  }
  call("reports.state").then(update);
  return on("reports", update);
}
