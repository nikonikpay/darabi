// Gaming, as boxes on the Windows tools page: Windows' power plans (switchable, reversible) in the power hue, and the two switches Windows keeps
// in its own settings (Game Mode, hardware-accelerated GPU scheduling) in the GPU's, shown as Windows has them and changed in Windows' own window.
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon } from "../ui.js";
import { box } from "../groups.js";

export function gamingBoxes(i) {
  const plans = h("div", { class: "plans" }), status = h("p", { class: "msg" });
  const ultimate = h("button", { class: "btn", hidden: true, onclick: () => call("gaming.exec", { cmd: "ultimate" }) }, icon("bolt"), t("Gaming_Ultimate"));
  const tiles = h("div", { class: "states" });
  const boxes = [
    box({ kind: "Power", title: t("Gaming_PowerPlan"), sub: t("Gaming_PowerPlan_Sub"), i, body: [plans, h("div", { class: "btn-row" }, ultimate), status] }),
    box({ kind: "Gpu", ico: "gamepad", title: t("Gaming_GameMode"), sub: t("Gaming_Switches_Sub"), i: i + 1,
      body: [tiles, h("p", { class: "note" }, t("Gaming_Switches_Note")),
        h("div", { class: "btn-row" },
          h("button", { class: "btn", onclick: () => call("gaming.exec", { cmd: "gameMode" }) }, icon("popout"), t("Gaming_OpenSettings"), " · ", t("Gaming_GameMode")),
          h("button", { class: "btn", onclick: () => call("gaming.exec", { cmd: "graphics" }) }, icon("popout"), t("Gaming_OpenSettings"), " · HAGS"))] })];
  // Game Mode and HAGS come as text from the host ("on", "off", or Windows' default when the registry does not say).
  const tile = (key, v) => h("div", { class: `state-tile ${v === t("Gaming_On") ? "on" : v === t("Gaming_Off") ? "" : "unknown"}` }, h("span", { class: "k" }, t(key)), h("span", { class: "v" }, v));
  function update(s) {
    status.textContent = s.status || ""; ultimate.hidden = !!s.hasUltimate;
    tiles.replaceChildren(tile("Gaming_GameMode", s.gameMode), tile("Gaming_Hags", s.gpuScheduling));
    plans.replaceChildren(...(s.plans.length ? s.plans.map((p) => h("div", { class: `plan ${p.active ? "on" : ""}` },
      h("span", { class: "name" }, p.name),
      p.active ? h("span", { class: "pill run" }, icon("check"), " ", t("Gaming_Active")) : h("button", { class: "btn", onclick: () => call("gaming.exec", { cmd: "activate", index: String(p.index) }) }, t("Gaming_Activate"))))
      : [h("p", { class: "caption" }, t("Gaming_NoPlans"))]));
  }
  call("gaming.state").then(update);
  return { boxes, off: on("gaming", update) };
}
