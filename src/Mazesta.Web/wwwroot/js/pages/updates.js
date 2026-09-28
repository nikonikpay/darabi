// Windows Update profiles, as WinUtil offers them: three plates side by side, what each one does in plain lines, and the one Windows has now
// marked. The profile is read from the registry, not remembered: a policy changed outside the app shows as "custom", never as a profile.
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon, toast } from "../ui.js";

const PROFILES = [
  { id: "Recommended", cls: "p-upd-rec", ico: "check", key: "Updates_Recommended" },
  { id: "Default", cls: "p-upd-def", ico: "win", key: "Updates_Default" },
  { id: "Disabled", cls: "p-upd-off", ico: "alert", key: "Updates_Disabled" },
];

export function mount(el) {
  const plates = h("div", { class: "upd-plates" }), custom = h("p", { class: "banner", hidden: true }, t("Updates_Custom"));
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Updates")), h("p", { class: "page-lede" }, t("Updates_Lede")))),
    custom, plates, h("p", { class: "upd-foot" }, icon("refresh"), t("Updates_Note")));

  function render(current) {
    custom.hidden = current !== "Custom";
    plates.replaceChildren(...PROFILES.map((p, i) => {
      const on = current === p.id;
      const btn = h("button", { class: `btn ${p.id === "Recommended" ? "primary" : p.id === "Disabled" ? "stop" : ""}`, disabled: on, onclick: () => apply(p, btn) }, t(`${p.key}_Apply`));
      return h("section", { class: `panel upd ${p.cls} ${on ? "current" : ""}`, style: { "--i": i } },
        h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon(p.ico)),
          h("div", { class: "ttl" }, h("h2", { class: "panel-title" }, t(p.key)), h("div", { class: "panel-sub fa" }, t(`${p.key}_Sub`))),
          on ? h("span", { class: "pill run" }, t("Updates_Current")) : null),
        h("ul", { class: "upd-points" }, t(`${p.key}_Points`).split("\n").map((line) => h("li", {}, line))),
        h("p", { class: "upd-small" }, t(`${p.key}_Foot`)),
        h("div", { class: "btn-row" }, btn));
    }));
  }
  async function apply(p, btn) {
    if (p.id === "Disabled" && !confirm(t("Updates_ConfirmDisable"))) return;
    btn.disabled = true;
    try {
      const r = await call("tweaks.update", { profile: p.id });
      render(r.update);
      toast(r.error ? t("Tweaks_Failed", t(p.key), r.error) : t("Updates_Done", t(p.key)), r.error ? "fail" : "ok");
    } catch (e) { toast(String(e.message || e), "fail"); btn.disabled = false; }
  }
  call("tweaks.state").then((s) => render(s.update));
}
