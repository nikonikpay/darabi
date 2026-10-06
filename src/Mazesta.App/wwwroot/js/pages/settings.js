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
  // The users' edition: the name their shared benchmark results go under (empty: the one name every unnamed user gets). Mazesta's own
  // copies share under the company's name, so they have no such field.
  const nameRow = boot.staff ? [] : row(t("Settings_DisplayName"), [input("displayName", "field"), h("small", { class: "caption", style: { display: "block", textAlign: "start" } }, t("Settings_DisplayName_Hint"))]);
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Settings")))),
    h("div", { class: "panels flow", style: { marginTop: 0 } },
      box({ kind: "Cpu", ico: "win", title: t("Web_Settings_General"), sub: t("Web_Settings_General_Sub"), i: 0,
        body: [h("dl", { class: "kv" }, row(t("Settings_Language"), select("language")), row(t("Settings_FastInterval"), input("interval")), row(t("Settings_StorageInterval"), input("storageInterval")),
          nameRow, row(t("Settings_RenderMode"), select("renderMode"))),
        h("div", { class: "btn-row" }, h("button", { class: "btn primary", onclick: () => call("settings.exec", { cmd: "save" }) }, t("Settings_Save"))), message] }),
      box({ kind: "Storage", ico: "bug", title: t("Web_Diag_Title"), sub: t("Web_Diag_Sub"), i: 1,
        body: boot.staff ? [h("p", { class: "caption" }, t("Web_Diag_Note")), findings, notesHead, notes, h("div", { class: "btn-row" }, exportBtn), logs]
          : [h("p", { class: "caption" }, t("Web_Diag_Note_Client")), h("div", { class: "btn-row" }, exportBtn)] }),
      box({ kind: "Memory", ico: "clock", title: t("Settings_Tray_Section"), sub: t("Web_Settings_Tray_Sub"), i: 2,
        body: [h("dl", { class: "kv" }, row(t("Settings_Tray_FirstCheck"), input("trayFirst")), row(t("Settings_Tray_Idle"), input("trayIdle")), row(t("Settings_Tray_Watch"), input("trayWatch")), row(t("Settings_Tray_Health"), input("trayHealth")), row(t("Settings_Tray_CpuAlert"), input("trayCpuAlert")), row(t("Settings_Tray_GpuAlert"), input("trayGpuAlert"))),
        h("div", { class: "btn-row" }, enableTray, disableTray), h("p", { class: "note" }, trayStatus)] }),
      box({ kind: "Motherboard", ico: "folder", title: t("Settings_DataFolder"), i: 3,
        body: [folder, h("div", { class: "btn-row" }, h("button", { class: "btn", onclick: () => call("settings.exec", { cmd: "openFolder" }) }, t("Settings_OpenFolder"))), h("p", { class: "note" }, version)] })));
  let built = false;
  function update(s) {
    if (!built) {
      f.language.replaceChildren(...s.languages.map((l) => h("option", { value: l }, l === "fa" ? "فارسی" : "English")));
      f.renderMode.replaceChildren(...s.renderModes.map((m) => h("option", { value: m }, m)));
      built = true;
    }
    f.language.value = s.language; f.renderMode.value = s.renderMode;
    for (const k of ["interval", "storageInterval", "displayName", "trayFirst", "trayIdle", "trayWatch", "trayHealth", "trayCpuAlert", "trayGpuAlert"]) if (f[k]) setField(f[k], s[k]);
    message.textContent = s.message || ""; trayStatus.textContent = s.trayStatus || "";
    enableTray.disabled = !s.canEnableTray; disableTray.disabled = !s.canDisableTray;
    folder.textContent = s.dataFolder; version.textContent = `${s.mode} · v${s.version}`;
  }
  call("settings.state").then(update);
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
