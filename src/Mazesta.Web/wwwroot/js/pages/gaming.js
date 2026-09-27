// Windows' power plans (switchable, reversible) and the two gaming switches Windows keeps in its own settings.
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { h } from "../ui.js";

export function mount(el) {
  const plans = h("div", { class: "queue" }), status = h("p", { class: "caption" });
  const kv = h("dl", { class: "kv" });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Gaming")), h("p", { class: "page-lede" }, t("Gaming_Note")))),
    h("div", { class: "cols", style: { marginTop: 0 } },
      h("div", { class: "col", style: { gridColumn: "span 2" } }, h("div", { class: "col-head" }, h("span", { class: "h3" }, t("Gaming_PowerPlan"))), plans, status),
      h("div", { class: "col" }, h("div", { class: "col-head" }, h("span", { class: "h3" }, t("Gaming_GameMode"))), kv,
        h("div", { style: { display: "flex", gap: "8px", marginTop: "14px", flexWrap: "wrap" } },
          h("button", { class: "btn", onclick: () => call("gaming.exec", { cmd: "gameMode" }) }, t("Gaming_OpenSettings"), " · ", t("Gaming_GameMode")),
          h("button", { class: "btn", onclick: () => call("gaming.exec", { cmd: "graphics" }) }, t("Gaming_OpenSettings"), " · HAGS")))));
  function update(s) {
    status.textContent = s.status || "";
    kv.replaceChildren(h("dt", {}, t("Gaming_GameMode")), h("dd", {}, s.gameMode), h("dt", {}, t("Gaming_Hags")), h("dd", {}, s.gpuScheduling));
    plans.replaceChildren(...(s.plans.length ? s.plans.map((p) => h("div", { class: "q-row", style: { gridTemplateColumns: "1fr auto", padding: "14px 0" } },
      h("span", { class: "name lat", style: { textAlign: "right" } }, p.name),
      p.active ? h("span", { class: "pill pass" }, t("Gaming_Active")) : h("button", { class: "btn", onclick: () => call("gaming.exec", { cmd: "activate", index: String(p.index) }) }, t("Gaming_Activate"))))
      : [h("p", { class: "caption" }, t("Gaming_NoPlans"))]));
  }
  call("gaming.state").then(update);
  return on("gaming", update);
}
