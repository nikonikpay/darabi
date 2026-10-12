// Settings, validated and saved by the same rules as the WPF edition (one config file). Language and render mode apply after a restart. The
// overlay has its own page; here is the diagnostic log a technician brings back from a machine the app does not fully know.
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { setField } from "./tests.js";
import { box } from "../groups.js";
import { boot } from "../app.js";

export function mount(el) {
  const set = (field, value) => call("settings.set", { field, value });
  const f = {};
  const input = (field, cls = "field lat short") => (f[field] = h("input", { class: cls, oninput: (e) => set(field, e.target.value) }));
  const select = (field) => (f[field] = h("select", { class: "field", onchange: (e) => set(field, e.target.value) }));
  const row = (label, control) => [h("dt", {}, label), h("dd", { style: { textAlign: "left" } }, control)];
  const message = h("p", { class: "msg" }), trayMessage = h("p", { class: "msg" });
  const trayStatus = h("span", {});
  const findings = h("ul", { class: "findings" }), notes = h("ul", { class: "findings notes" }), notesHead = h("p", { class: "note", hidden: true }, h("b", {}, t("Web_Diag_NotesTitle")), " ", t("Web_Diag_NotesNote"));
  const logs = h("span", { class: "lat caption" });
  const exportBtn = h("button", { class: "btn primary", onclick: async () => {
    exportBtn.disabled = true;
    try { const r = await call("diag.export"); toast(t(r?.sent ? "Web_Diag_ExportedSent" : "Web_Diag_Exported")); } catch (e) { toast(String(e.message || e), "fail"); } finally { exportBtn.disabled = false; }
  } }, icon("bug"), t("Web_Diag_Export"));
  const enableTray = h("button", { class: "btn go", onclick: () => call("settings.exec", { cmd: "enableTray" }) }, t("Settings_Tray_Enable"));
  const disableTray = h("button", { class: "btn", onclick: () => call("settings.exec", { cmd: "disableTray" }) }, t("Settings_Tray_Disable"));
  // Anonymous usage statistics: on by default, said first in plain words, off with one switch.
  const usage = h("input", { type: "checkbox", class: "switch", "aria-label": t("Settings_Usage_Switch"), onchange: (e) => set("usageReport", e.target.checked) });
  const usageLog = h("span", { class: "lat caption" });
  const folder = h("span", { class: "lat caption" }), version = h("span", { class: "lat caption" });
  // The users' edition: the name their shared benchmark results go under (empty: the one name every unnamed user gets). Mazesta's own
  // copies share under the company's name, so they have no such field.
  // The overlay's global shortcuts: click a box and press the keys wanted (Alt, Ctrl or Win with a letter, digit or F-key). The app and the tray both hold them.
  const keyBoxes = {};
  const KEYS = [["toggle", "Settings_Keys_Toggle"], ["move", "Settings_Keys_Move"], ["layout", "Settings_Keys_Layout"], ["preset", "Settings_Keys_Preset"]];
  const DEFAULT_KEYS = { toggle: "Alt+O", move: "Alt+M", layout: "Alt+U", preset: "Alt+P" };
  const keyName = (e) => /^Key[A-Z]$/.test(e.code) ? e.code[3] : /^Digit\d$/.test(e.code) ? e.code[5] : /^F\d{1,2}$/.test(e.code) ? e.code : ["Insert", "Delete", "Home", "End", "PageUp", "PageDown"].includes(e.code) ? e.code : null;
  const setKey = (name, value) => call("overlay.set", { field: "key." + name, value }).then(() => call("overlay.state").then(showKeys)).catch((x) => { toast(String(x.message || x), "fail"); call("overlay.state").then(showKeys); });
  for (const [name] of KEYS) keyBoxes[name] = h("input", { class: "field lat key-capture", readonly: "", "aria-label": name, onkeydown: (e) => {
    if (e.key === "Tab") return;
    e.preventDefault();
    const key = keyName(e); if (!key) return;
    setKey(name, [e.ctrlKey && "Ctrl", e.altKey && "Alt", e.shiftKey && "Shift", e.metaKey && "Win"].filter(Boolean).concat(key).join("+"));
  } });
  const showKeys = (s) => { if (s?.keys) for (const [name] of KEYS) if (document.activeElement !== keyBoxes[name] || keyBoxes[name].value !== s.keys[name]) keyBoxes[name].value = s.keys[name]; };
  const nameRow = boot.staff ? [] : row(t("Settings_DisplayName"), [input("displayName", "field"), h("small", { class: "caption", style: { display: "block", textAlign: "start" } }, t("Settings_DisplayName_Hint"))]);
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Settings")))),
    h("div", { class: "panels flow", style: { marginTop: 0 } },
      box({ kind: "Cpu", ico: "win", title: t("Web_Settings_General"), sub: t("Web_Settings_General_Sub"), i: 0,
        body: [h("dl", { class: "kv" }, row(t("Settings_Language"), select("language")), row(t("Settings_FastInterval"), input("interval")), row(t("Settings_StorageInterval"), input("storageInterval")),
          nameRow, row(t("Settings_RenderMode"), select("renderMode"))),
        h("div", { class: "btn-row" }, h("button", { class: "btn primary", onclick: () => call("settings.exec", { cmd: "save" }) }, t("Settings_Save"))), message] }),
      // The background monitor: its state first, then when it checks, then what it warns about; one Save for the whole box.
      box({ kind: "Memory", ico: "clock", title: t("Settings_Tray_Section"), sub: t("Web_Settings_Tray_Sub"), i: 1,
        body: [h("h3", { class: "h3 set-group" }, t("Settings_Tray_GroupState")), h("p", { class: "note" }, trayStatus), h("div", { class: "btn-row" }, enableTray, disableTray),
          h("h3", { class: "h3 set-group" }, t("Settings_Tray_GroupSchedule")),
          h("dl", { class: "kv" }, row(t("Settings_Tray_FirstCheck"), input("trayFirst")), row(t("Settings_Tray_Idle"), input("trayIdle")), row(t("Settings_Tray_Watch"), input("trayWatch")), row(t("Settings_Tray_Health"), input("trayHealth"))),
          h("h3", { class: "h3 set-group" }, t("Settings_Tray_GroupTemp")), h("p", { class: "caption set-note" }, t("Settings_Tray_TempNote")),
          h("dl", { class: "kv" }, row(t("Settings_Tray_CpuAlert"), input("trayCpuAlert")), row(t("Settings_Tray_GpuAlert"), input("trayGpuAlert")), row(t("Settings_Tray_GpuHotSpot"), input("trayGpuHotSpot")), row(t("Settings_Tray_SsdAlert"), input("traySsd")), row(t("Settings_Tray_Hold"), input("trayHold"))),
          h("h3", { class: "h3 set-group" }, t("Settings_Tray_GroupRam")), h("p", { class: "caption set-note" }, t("Settings_Tray_RamNote")),
          h("dl", { class: "kv" }, row(t("Settings_Tray_RamAlert"), input("trayRamAlert")), row(t("Settings_Tray_RamHold"), input("trayRamHold")), row(t("Settings_Tray_RamCheck"), input("trayRamCheck"))),
          h("div", { class: "btn-row" }, h("button", { class: "btn primary", onclick: () => call("settings.exec", { cmd: "save" }) }, t("Settings_Save"))), trayMessage] }),
      // One box for what the app can tell about this machine: the statistics it sends by itself (anonymous, one switch) and the troubleshooting file you export by hand.
      box({ kind: "Storage", ico: "bug", title: t("Settings_Report_Title"), sub: t("Settings_Report_Sub"), i: 2,
        body: [h("p", { class: "caption", style: { maxWidth: "90ch", marginTop: 0 } }, t("Settings_Report_Diff")),
          h("h3", { class: "h3", style: { fontSize: "16px", fontWeight: 900 } }, t("Settings_Usage_Title")),
          h("p", { class: "caption", style: { maxWidth: "90ch" } }, t("Settings_Usage_Note")), h("p", { class: "caption", style: { maxWidth: "90ch" } }, t("Settings_Usage_Sent")),
          h("label", { class: "ov-show" }, usage, h("span", {}, h("b", {}, t("Settings_Usage_Switch")))),
          h("div", { class: "btn-row" }, h("button", { class: "btn", onclick: () => call("settings.exec", { cmd: "openUsageLog" }).catch((e) => toast(String(e.message || e), "fail")) }, t("Settings_Usage_Open"))), usageLog,
          h("h3", { class: "h3", style: { fontSize: "16px", fontWeight: 900, marginTop: "26px" } }, t("Web_Diag_Title")),
          h("p", { class: "caption", style: { maxWidth: "90ch" } }, t("Settings_Report_Button")),
          ...(boot.staff ? [h("p", { class: "caption" }, t("Web_Diag_Note")), findings, notesHead, notes, h("div", { class: "btn-row" }, exportBtn), logs]
            : [h("p", { class: "caption" }, t("Web_Diag_Note_Client")), h("div", { class: "btn-row" }, exportBtn)])] }),
      box({ kind: "Gpu", ico: "clock", title: t("Settings_Keys_Title"), sub: t("Settings_Keys_Sub"), i: 3,
        body: [h("p", { class: "caption set-note" }, t("Settings_Keys_Note")),
          h("dl", { class: "kv" }, ...KEYS.map(([name, label]) => row(t(label), keyBoxes[name]))),
          h("div", { class: "btn-row" }, h("button", { class: "btn", onclick: async () => { for (const [name] of KEYS) { try { await call("overlay.set", { field: "key." + name, value: DEFAULT_KEYS[name] }); } catch { /* one that is taken stays; the rest still go back */ } } call("overlay.state").then(showKeys); } }, t("Settings_Keys_Reset")))] }),
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
    for (const k of ["interval", "storageInterval", "displayName", "trayFirst", "trayIdle", "trayWatch", "trayHealth", "trayCpuAlert", "trayGpuAlert", "trayGpuHotSpot", "traySsd", "trayHold", "trayRamAlert", "trayRamHold", "trayRamCheck"]) if (f[k]) setField(f[k], s[k]);
    message.textContent = s.message || ""; trayMessage.textContent = s.message || ""; trayStatus.textContent = s.trayStatus || "";
    enableTray.disabled = !s.canEnableTray; disableTray.disabled = !s.canDisableTray;
    usage.checked = !!s.usageReport; usageLog.textContent = s.usageLog || "";
    folder.textContent = s.dataFolder; version.textContent = `${s.mode} · v${s.version}`;
  }
  call("settings.state").then(update); call("overlay.state").then(showKeys);
  call("diag.state").then((d) => {
    if (!d) return;
    findings.replaceChildren(...(d.findings.length ? d.findings.slice(0, 12).map((x) => h("li", { class: "lat" }, x)) : [h("li", { class: "ok" }, t(d.polled ? "Web_Diag_None" : "Web_Diag_Waiting"))]));
    if (d.findings.length > 12) findings.append(h("li", { class: "caption" }, t("Web_Diag_More", d.findings.length - 12)));
    notesHead.hidden = !d.notes?.length;
    notes.replaceChildren(...(d.notes || []).map((x) => h("li", { class: "lat" }, x)));
    logs.textContent = d.logs;
  });
  const offKeys = on("overlayState", showKeys), offSettings = on("settings", update);
  return () => { offKeys?.(); offSettings?.(); };
}
