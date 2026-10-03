// Settings, validated and saved by the same rules as the WPF edition (one config file). Language and render mode apply after a restart. The
// overlay has its own page; here is the diagnostic log a technician brings back from a machine the app does not fully know.
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { setField } from "./tests.js";
import { box } from "../groups.js";

export function mount(el) {
  const set = (field, value) => call("settings.set", { field, value });
  const f = {};
  const input = (field, cls = "field lat short") => (f[field] = h("input", { class: cls, oninput: (e) => set(field, e.target.value) }));
  const select = (field) => (f[field] = h("select", { class: "field", onchange: (e) => set(field, e.target.value) }));
  const row = (label, control) => [h("dt", {}, label), h("dd", { style: { textAlign: "left" } }, control)];
  const message = h("p", { class: "msg" });
  const trayStatus = h("span", {});
  const findings = h("ul", { class: "findings" }), notes = h("ul", { class: "findings notes" }), notesHead = h("p", { class: "note", hidden: true }, h("b", {}, t("Web_Diag_NotesTitle")), " ", t("Web_Diag_NotesNote"));
  const logs = h("span", { class: "lat caption" });
  const exportBtn = h("button", { class: "btn primary", onclick: async () => {
    exportBtn.disabled = true;
    try { await call("diag.export"); toast(t("Web_Diag_Exported")); } catch (e) { toast(String(e.message || e), "fail"); } finally { exportBtn.disabled = false; }
  } }, icon("bug"), t("Web_Diag_Export"));
  const enableTray = h("button", { class: "btn go", onclick: () => call("settings.exec", { cmd: "enableTray" }) }, t("Settings_Tray_Enable"));
  const disableTray = h("button", { class: "btn", onclick: () => call("settings.exec", { cmd: "disableTray" }) }, t("Settings_Tray_Disable"));
  const folder = h("span", { class: "lat caption" }), version = h("span", { class: "lat caption" });
  // ——— Game mode: background services stopped while it is on, each put back as it was when it is switched off ———
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
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Settings")))),
    h("div", { class: "panels flow", style: { marginTop: 0 } },
      box({ kind: "Cpu", ico: "win", title: t("Web_Settings_General"), sub: t("Web_Settings_General_Sub"), i: 0,
        body: [h("dl", { class: "kv" }, row(t("Settings_Language"), select("language")), row(t("Settings_FastInterval"), input("interval")), row(t("Settings_StorageInterval"), input("storageInterval")),
          row(t("Settings_ShopName"), input("shopName", "field")), row(t("Settings_RenderMode"), select("renderMode"))),
        h("div", { class: "btn-row" }, h("button", { class: "btn primary", onclick: () => call("settings.exec", { cmd: "save" }) }, t("Settings_Save"))), message] }),
      box({ kind: "Storage", ico: "bug", title: t("Web_Diag_Title"), sub: t("Web_Diag_Sub"), i: 1,
        body: [h("p", { class: "caption" }, t("Web_Diag_Note")), findings, notesHead, notes, h("div", { class: "btn-row" }, exportBtn), logs] }),
      box({ kind: "Memory", ico: "clock", title: t("Settings_Tray_Section"), sub: t("Web_Settings_Tray_Sub"), i: 2,
        body: [h("dl", { class: "kv" }, row(t("Settings_Tray_FirstCheck"), input("trayFirst")), row(t("Settings_Tray_Idle"), input("trayIdle")), row(t("Settings_Tray_Watch"), input("trayWatch")), row(t("Settings_Tray_Health"), input("trayHealth")), row(t("Settings_Tray_CpuAlert"), input("trayCpuAlert")), row(t("Settings_Tray_GpuAlert"), input("trayGpuAlert"))),
        h("div", { class: "btn-row" }, enableTray, disableTray), h("p", { class: "note" }, trayStatus)] }),
      box({ kind: "Gaming", title: t("GameBoost_Title"), sub: t("GameBoost_Sub"), i: 3, a: "gameboost",
        body: [h("p", { class: "note", style: { marginTop: 0 } }, t("GameBoost_Note")), h("label", { class: "ov-show" }, gameSwitch, h("span", {}, h("b", {}, t("GameBoost_Switch")), h("small", {}, t("GameBoost_Switch_Sub")))),
          gameMsg, gameResults, gameList] }),
      box({ kind: "Motherboard", ico: "folder", title: t("Settings_DataFolder"), i: 4,
        body: [folder, h("div", { class: "btn-row" }, h("button", { class: "btn", onclick: () => call("settings.exec", { cmd: "openFolder" }) }, t("Settings_OpenFolder"))), h("p", { class: "note" }, version)] })));
  let built = false;
  function update(s) {
    if (!built) {
      f.language.replaceChildren(...s.languages.map((l) => h("option", { value: l }, l === "fa" ? "فارسی" : "English")));
      f.renderMode.replaceChildren(...s.renderModes.map((m) => h("option", { value: m }, m)));
      built = true;
    }
    f.language.value = s.language; f.renderMode.value = s.renderMode;
    for (const k of ["interval", "storageInterval", "shopName", "trayFirst", "trayIdle", "trayWatch", "trayHealth", "trayCpuAlert", "trayGpuAlert"]) setField(f[k], s[k]);
    message.textContent = s.message || ""; trayStatus.textContent = s.trayStatus || "";
    enableTray.disabled = !s.canEnableTray; disableTray.disabled = !s.canDisableTray;
    folder.textContent = s.dataFolder; version.textContent = `${s.mode} · v${s.version}`;
  }
  call("settings.state").then(update);
  call("gameboost.state").then(showGame).catch(() => {});
  call("diag.state").then((d) => {
    if (!d) return;
    findings.replaceChildren(...(d.findings.length ? d.findings.slice(0, 12).map((x) => h("li", { class: "lat" }, x)) : [h("li", { class: "ok" }, t(d.polled ? "Web_Diag_None" : "Web_Diag_Waiting"))]));
    if (d.findings.length > 12) findings.append(h("li", { class: "caption" }, t("Web_Diag_More", d.findings.length - 12)));
    notesHead.hidden = !d.notes?.length;
    notes.replaceChildren(...(d.notes || []).map((x) => h("li", { class: "lat" }, x)));
    logs.textContent = d.logs;
  });
  return on("settings", update);
}
