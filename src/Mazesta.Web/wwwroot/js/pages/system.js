// The machine's inventory as ruled columns: every field the system reported, and "not available" where it reported nothing. The specifications
// can be saved whole as HTML, PDF or JSON.
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { h, val, toast } from "../ui.js";

// A specification card: the rows that matter, the rest folded under "more", a table (a module's speed profiles, the one in use marked) and a note.
const latin = (s) => /[a-z]/i.test(s) && !/[؀-ۿ]/.test(s);
function rows(list) {
  return h("dl", { class: "kv" }, list.map((r) => [h("dt", { class: latin(r.label) ? "lat" : "" }, r.label),
    h("dd", { class: /[؀-ۿ]/.test(r.value) ? "" : "lat", style: { textAlign: /[؀-ۿ]/.test(r.value) ? null : "left" } }, r.value)]));
}
export function card(c, i) {
  const main = c.rows.filter((r) => !r.more), more = c.rows.filter((r) => r.more);
  return h("div", { class: "col spec-card", style: { "--i": i } }, h("div", { class: "col-head" }, h("span", { class: "h3" }, c.title)),
    main.length ? rows(main) : null,
    c.table ? h("div", { class: "spec-table-wrap" }, h("table", { class: "table spec-table" },
      h("thead", {}, h("tr", {}, c.table.headers.map((x) => h("th", {}, x)))),
      h("tbody", {}, c.table.rows.map((r, k) => h("tr", { class: k === c.table.highlight ? "on" : "" }, r.map((x, j) => h("td", { class: j ? "lat" : "lat nm" }, x))))))) : null,
    more.length ? h("details", { class: "spec-more" }, h("summary", {}, t("Spec_More")), rows(more)) : null,
    c.note ? h("p", { class: "caption spec-note" }, c.note) : null);
}

export function section(s, i) {
  return h("div", { class: "col", style: { "--i": i } }, h("div", { class: "col-head" }, h("span", { class: "h3" }, s.title)),
    h("dl", { class: "kv" }, s.rows.map((r) => [h("dt", { class: /[a-z]/i.test(r.label) && !/[؀-ۿ]/.test(r.label) ? "lat" : "" }, r.label),
      h("dd", { class: "lat", style: { textAlign: "left" } }, val(r.value === t("Value_NotAvailable") ? null : r.value, "lat"))])));
}

export function mount(el) {
  const body = h("div", { class: "masonry" }, h("p", { class: "page-lede" }, t("Web_Loading")));
  const buttons = ["html", "pdf", "json"].map((format) => h("button", { class: format === "pdf" ? "btn primary" : "btn", type: "button", onclick: () => save(format) },
    t(`System_Export_${format[0].toUpperCase()}${format.slice(1)}`)));
  async function save(format) {
    for (const b of buttons) b.disabled = true;
    if (format === "pdf") toast(t("System_Export_Busy"));
    try { const r = await call("specs.export", { format }); if (r?.error) toast(r.error, "fail"); }
    finally { for (const b of buttons) b.disabled = false; }
  }
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_SystemInfo")), h("p", { class: "page-lede" }, t("Web_System_Lede"))),
    h("div", { class: "export", role: "group", "aria-label": t("System_Export") }, h("span", { class: "caption" }, t("System_Export")), buttons)), body);
  call("specs.get").then((r) => {
    body.replaceChildren(...r.cards.map(card));
    if (r.errors?.length) body.append(h("div", { class: "col" }, h("div", { class: "col-head" }, h("span", { class: "h3" }, t("Web_System_Errors"))), h("p", { class: "caption lat" }, r.errors.join("\n"))));
  });
}
