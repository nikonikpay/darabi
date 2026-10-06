// Gaming, as boxes on the Windows tools page: Windows' power plans (switchable, reversible) in the power hue, and the two switches Windows keeps
// in its own settings (Game Mode, hardware-accelerated GPU scheduling) in the GPU's, shown as Windows has them and changed in Windows' own window.
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon } from "../ui.js";
import { box } from "../groups.js";

// Game mode: background services stopped while it is on, each put back as it was when it is switched off.
export function gameBoostBox(i) {
  const gameSwitch = h("input", { type: "checkbox", class: "switch", "aria-label": t("GameBoost_Switch"), onchange: (e) => switchGame(e.target.checked) });
  const gameList = h("div", { class: "gb-list" }), gameMsg = h("p", { class: "msg" }), gameResults = h("ul", { class: "problems" });
  function showGame(s) {
    gameSwitch.checked = s.on; gameSwitch.disabled = s.busy;
    const held = new Set(s.held);
    const row = (x) => h("label", { class: `gb-row ${x.present ? "" : "off"}` },
      h("input", { type: "checkbox", class: "check", checked: x.chosen, disabled: s.on || !x.present, onchange: (e) => call("gameboost.tick", { name: x.name, on: e.target.checked }) }),
      h("span", { class: "gb-name" }, h("b", {}, x.display || x.name), h("small", { class: "lat" }, x.name), h("small", {}, x.note)),
      h("span", { class: `pill ${!x.present ? "none" : x.running ? "run" : "none"}` }, t(!x.present ? "GameBoost_State_Absent" : held.has(x.name) ? "GameBoost_State_Held" : x.running ? "GameBoost_State_Running" : x.disabled ? "GameBoost_State_Disabled" : "GameBoost_State_Stopped")));
    const group = (g) => [h("h3", { class: "dns-h" }, t(`GameBoost_Group_${g}`)), ...s.services.filter((x) => x.group === g).map(row)];
    gameList.replaceChildren(...group("Network"), ...group("Background"));
    gameResults.replaceChildren(...s.results.filter((r) => r.error).map((r) => h("li", { class: "fail" }, h("b", { class: "lat" }, r.name), ": ", r.error)));
  }
  async function switchGame(onState) {
    gameSwitch.disabled = true; gameMsg.className = "msg"; gameMsg.textContent = t("Tools_Working");
    try {
      const s = await call("gameboost.switch", { on: onState });
      showGame(s);
      const failed = s.results.filter((r) => r.error).length, changed = s.results.filter((r) => r.changed).length;
      gameMsg.className = `msg ${failed ? "fail" : "ok"}`; gameMsg.textContent = t(onState ? "GameBoost_Done_On" : "GameBoost_Done_Off", changed, failed);
    } catch (e) { gameMsg.className = "msg fail"; gameMsg.textContent = String(e.message || e); call("gameboost.state").then(showGame); }
  }
  call("gameboost.state").then(showGame).catch(() => {});
  return box({ kind: "Gaming", ico: "gamepad", title: t("GameBoost_Title"), sub: t("GameBoost_Sub"), i, wide: true, a: "gameboost",
    body: [h("p", { class: "note", style: { marginTop: 0 } }, t("GameBoost_Note")), h("label", { class: "ov-show" }, gameSwitch, h("span", {}, h("b", {}, t("GameBoost_Switch")), h("small", {}, t("GameBoost_Switch_Sub")))),
      gameMsg, gameResults, gameList] });
}

export function gamingBoxes(i) {
  const plans = h("div", { class: "plans" }), status = h("p", { class: "msg" });
  const ultimate = h("button", { class: "btn", hidden: true, onclick: () => call("gaming.exec", { cmd: "ultimate" }) }, icon("bolt"), t("Gaming_Ultimate"));
  const tiles = h("div", { class: "states" });
  const boxes = [
    box({ kind: "Power", title: t("Gaming_PowerPlan"), sub: t("Gaming_PowerPlan_Sub"), i, a: "power", body: [plans, h("div", { class: "btn-row" }, ultimate), status] }),
    box({ kind: "Gpu", ico: "gamepad", title: t("Gaming_GameMode"), sub: t("Gaming_Switches_Sub"), i: i + 1, a: "gamemode",
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
