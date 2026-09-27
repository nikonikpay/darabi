// Settings, validated and saved by the same rules as the WPF edition (one config file). Language and render mode apply after a restart.
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { h } from "../ui.js";
import { setField } from "./tests.js";

export function mount(el) {
  const set = (field, value) => call("settings.set", { field, value });
  const f = {};
  const input = (field, cls = "field lat short") => (f[field] = h("input", { class: cls, oninput: (e) => set(field, e.target.value) }));
  const select = (field) => (f[field] = h("select", { class: "field", onchange: (e) => set(field, e.target.value) }));
  const row = (label, control) => [h("dt", {}, label), h("dd", { style: { textAlign: "left" } }, control)];
  const message = h("p", { class: "h3", style: { minHeight: "1.4em", marginTop: "16px" } });
  const trayStatus = h("p", { class: "caption" }), ovl = h("input", { type: "checkbox", class: "switch", onchange: (e) => call("settings.set", { field: "overlayVisible", value: e.target.checked }) });
  const enableTray = h("button", { class: "btn go", onclick: () => call("settings.exec", { cmd: "enableTray" }) }, t("Settings_Tray_Enable"));
  const disableTray = h("button", { class: "btn", onclick: () => call("settings.exec", { cmd: "disableTray" }) }, t("Settings_Tray_Disable"));
  const folder = h("span", { class: "lat caption" }), version = h("span", { class: "lat caption" }), hotkey = h("span", { class: "pill run lat" });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Settings")))),
    h("div", { class: "cols", style: { marginTop: 0 } },
      h("div", { class: "col" }, h("div", { class: "col-head" }, h("span", { class: "h3" }, t("Web_Settings_General"))),
        h("dl", { class: "kv" }, row(t("Settings_Language"), select("language")), row(t("Settings_FastInterval"), input("interval")), row(t("Settings_StorageInterval"), input("storageInterval")),
          row(t("Settings_ShopName"), input("shopName", "field")), row(t("Settings_RenderMode"), select("renderMode"))),
        h("div", { class: "toolbar", style: { marginTop: "18px" } }, h("button", { class: "btn primary", onclick: () => call("settings.exec", { cmd: "save" }) }, t("Settings_Save"))), message),
      h("div", { class: "col" }, h("div", { class: "col-head" }, h("span", { class: "h3" }, t("Overlay_Title")), ovl),
        h("p", { class: "caption" }, t("Overlay_Note")), h("dl", { class: "kv" }, row(t("Overlay_Corner"), select("overlayCorner")), row(t("Web_Shortcut"), hotkey))),
      h("div", { class: "col" }, h("div", { class: "col-head" }, h("span", { class: "h3" }, t("Settings_Tray_Section"))),
        h("dl", { class: "kv" }, row(t("Settings_Tray_FirstCheck"), input("trayFirst")), row(t("Settings_Tray_Idle"), input("trayIdle")), row(t("Settings_Tray_Watch"), input("trayWatch"))),
        h("div", { class: "toolbar", style: { marginTop: "14px" } }, enableTray, disableTray), trayStatus),
      h("div", { class: "col" }, h("div", { class: "col-head" }, h("span", { class: "h3" }, t("Settings_DataFolder"))), folder,
        h("div", { class: "toolbar", style: { marginTop: "14px" } }, h("button", { class: "btn", onclick: () => call("settings.exec", { cmd: "openFolder" }) }, t("Settings_OpenFolder"))), version)));
  let built = false;
  function update(s) {
    if (!built) {
      f.language.replaceChildren(...s.languages.map((l) => h("option", { value: l }, l === "fa" ? "فارسی" : "English")));
      f.renderMode.replaceChildren(...s.renderModes.map((m) => h("option", { value: m }, m)));
      f.overlayCorner.replaceChildren(...s.overlayCorners.map((c) => h("option", { value: c.value }, c.label)));
      built = true;
    }
    f.language.value = s.language; f.renderMode.value = s.renderMode; f.overlayCorner.value = s.overlayCorner;
    for (const k of ["interval", "storageInterval", "shopName", "trayFirst", "trayIdle", "trayWatch"]) setField(f[k], s[k]);
    message.textContent = s.message || ""; trayStatus.textContent = s.trayStatus || ""; ovl.checked = s.overlayVisible;
    enableTray.disabled = !s.canEnableTray; disableTray.disabled = !s.canDisableTray;
    folder.textContent = s.dataFolder; version.textContent = `${s.mode} · v${s.version}`; hotkey.textContent = s.hotkey;
  }
  call("settings.state").then(update);
  return on("settings", update);
}
