// The machine's inventory as ruled columns: every field the system reported, and "not available" where it reported nothing.
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { h, val } from "../ui.js";

export function section(s, i) {
  return h("div", { class: "col", style: { "--i": i } }, h("div", { class: "col-head" }, h("span", { class: "h3" }, s.title)),
    h("dl", { class: "kv" }, s.rows.map((r) => [h("dt", { class: /[a-z]/i.test(r.label) && !/[؀-ۿ]/.test(r.label) ? "lat" : "" }, r.label),
      h("dd", { class: "lat", style: { textAlign: "left" } }, val(r.value === t("Value_NotAvailable") ? null : r.value, "lat"))])));
}

export function mount(el) {
  const body = h("div", { class: "masonry" }, h("p", { class: "page-lede" }, t("Web_Loading")));
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_SystemInfo")), h("p", { class: "page-lede" }, t("Web_System_Lede")))), body);
  call("inventory.get").then((inv) => {
    body.replaceChildren(...inv.sections.map(section));
    if (inv.errors?.length) body.append(h("div", { class: "col" }, h("div", { class: "col-head" }, h("span", { class: "h3" }, t("Web_System_Errors"))), h("p", { class: "caption lat" }, inv.errors.join("\n"))));
  });
}
