// Settings, validated and saved by the same rules as the WPF edition (one config file). Language and render mode apply after a restart. The
// overlay has its own page; here is the diagnostic log a technician brings back from a machine the app does not fully know.
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { setField } from "./tests.js";

export function mount(el) {
  const set = (field, value) => call("settings.set", { field, value });
  const f = {};
  const input = (field, cls = "field lat short") => (f[field] = h("input", { class: cls, oninput: (e) => set(field, e.target.value) }));
  const select = (field) => (f[field] = h("select", { class: "field", onchange: (e) => set(field, e.target.value) }));
  const row = (label, control) => [h("dt", {}, label), h("dd", { style: { textAlign: "left" } }, control)];
  const message = h("p", { class: "h3", style: { minHeight: "1.4em", marginTop: "16px" } });
  const trayStatus = h("p", { class: "caption" });
  const findings = h("ul", { class: "findings" }), logs = h("span", { class: "lat caption" });
  const exportBtn = h("button", { class: "btn primary", onclick: async () => {
    exportBtn.disabled = true;
    try { await call("diag.export"); toast(t("Web_Diag_Exported")); } catch (e) { toast(String(e.message || e), "fail"); } finally { exportBtn.disabled = false; }
  } }, icon("bug"), t("Web_Diag_Export"));
  const enableTray = h("button", { class: "btn go", onclick: () => call("settings.exec", { cmd: "enableTray" }) }, t("Settings_Tray_Enable"));
  const disableTray = h("button", { class: "btn", onclick: () => call("settings.exec", { cmd: "disableTray" }) }, t("Settings_Tray_Disable"));
  const folder = h("span", { class: "lat caption" }), version = h("span", { class: "lat caption" });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Settings")))),
    h("div", { class: "cols", style: { marginTop: 0 } },
      h("div", { class: "col" }, h("div", { class: "col-head" }, h("span", { class: "h3" }, t("Web_Settings_General"))),
        h("dl", { class: "kv" }, row(t("Settings_Language"), select("language")), row(t("Settings_FastInterval"), input("interval")), row(t("Settings_StorageInterval"), input("storageInterval")),
          row(t("Settings_ShopName"), input("shopName", "field")), row(t("Settings_RenderMode"), select("renderMode"))),
        h("div", { class: "toolbar", style: { marginTop: "18px" } }, h("button", { class: "btn primary", onclick: () => call("settings.exec", { cmd: "save" }) }, t("Settings_Save"))), message),
      h("div", { class: "col" }, h("div", { class: "col-head" }, h("span", { class: "h3" }, t("Web_Diag_Title"))),
        h("p", { class: "caption" }, t("Web_Diag_Note")), findings,
        h("div", { class: "toolbar", style: { marginTop: "14px" } }, exportBtn), logs),
      h("div", { class: "col" }, h("div", { class: "col-head" }, h("span", { class: "h3" }, t("Settings_Tray_Section"))),
        h("dl", { class: "kv" }, row(t("Settings_Tray_FirstCheck"), input("trayFirst")), row(t("Settings_Tray_Idle"), input("trayIdle")), row(t("Settings_Tray_Watch"), input("trayWatch")), row(t("Settings_Tray_Health"), input("trayHealth"))),
        h("div", { class: "toolbar", style: { marginTop: "14px" } }, enableTray, disableTray), trayStatus),
      h("div", { class: "col" }, h("div", { class: "col-head" }, h("span", { class: "h3" }, t("Settings_DataFolder"))), folder,
        h("div", { class: "toolbar", style: { marginTop: "14px" } }, h("button", { class: "btn", onclick: () => call("settings.exec", { cmd: "openFolder" }) }, t("Settings_OpenFolder"))), version)));
  let built = false;
  function update(s) {
    if (!built) {
      f.language.replaceChildren(...s.languages.map((l) => h("option", { value: l }, l === "fa" ? "فارسی" : "English")));
      f.renderMode.replaceChildren(...s.renderModes.map((m) => h("option", { value: m }, m)));
      built = true;
    }
    f.language.value = s.language; f.renderMode.value = s.renderMode;
    for (const k of ["interval", "storageInterval", "shopName", "trayFirst", "trayIdle", "trayWatch", "trayHealth"]) setField(f[k], s[k]);
    message.textContent = s.message || ""; trayStatus.textContent = s.trayStatus || "";
    enableTray.disabled = !s.canEnableTray; disableTray.disabled = !s.canDisableTray;
    folder.textContent = s.dataFolder; version.textContent = `${s.mode} · v${s.version}`;
  }
  call("settings.state").then(update);
  call("diag.state").then((d) => {
    if (!d) return;
    findings.replaceChildren(...(d.findings.length ? d.findings.slice(0, 12).map((x) => h("li", { class: "lat" }, x)) : [h("li", { class: "ok" }, t("Web_Diag_None"))]));
    if (d.findings.length > 12) findings.append(h("li", { class: "caption" }, t("Web_Diag_More", d.findings.length - 12)));
    logs.textContent = d.logs;
  });
  return on("settings", update);
}
