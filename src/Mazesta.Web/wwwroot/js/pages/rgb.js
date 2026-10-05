// Light colours of the memory, graphics card, board and what plugs into it, through OpenRGB (a separate program the app runs as its server).
// One colour for everything or one device at a time, with the device's own modes (static, rainbow, breathing...) when it has them; "off" is a
// colour of black or the device's own off mode. A device that takes no colour says so; nothing is shown as done that the program refused.
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon } from "../ui.js";
import { box } from "../groups.js";

export function rgbBox(i) {
  const list = h("div", { class: "rgb-list" }), msg = h("p", { class: "msg" });
  const color = h("input", { type: "color", class: "rgb-color", value: "#ff0000", "aria-label": t("Rgb_Color") });
  const start = h("button", { class: "btn go", type: "button", onclick: () => run("rgb.start") }, icon("bolt"), t("Rgb_Connect"));
  const apply = h("button", { class: "btn primary", type: "button", onclick: () => set({ device: -1, color: color.value }) }, t("Rgb_ApplyAll"));
  const off = h("button", { class: "btn", type: "button", onclick: () => set({ device: -1, color: "" }) }, t("Rgb_OffAll"));
  const all = h("div", { class: "btn-row" }, h("label", { class: "rgb-all" }, t("Rgb_All"), color), apply, off);

  function show(s) {
    all.hidden = !s.devices.length;
    msg.className = `msg ${s.error ? "fail" : ""}`; msg.textContent = s.error || (!s.found && !s.connected ? t("Rgb_NotFound") : s.connected && !s.devices.length ? t("Rgb_Empty") : "");
    list.replaceChildren(...s.devices.map(card));
  }
  function card(d) {
    const pick = h("input", { type: "color", class: "rgb-color", value: d.color || "#ffffff", "aria-label": t("Rgb_Color") });
    const mode = h("select", { class: "field", "aria-label": t("Rgb_Mode") }, h("option", { value: "" }, t("Rgb_Mode_Keep")), d.modes.map((m) => h("option", { value: m.index }, m.name)));
    const go = () => set(mode.value === "" ? { device: d.index, color: pick.value } : { device: d.index, color: pick.value, mode: Number(mode.value) });
    return h("div", { class: "rgb-dev" },
      h("div", { class: "rgb-name" }, h("b", { class: "lat" }, d.name), h("small", {}, [t(`Rgb_Kind_${d.kind}`), d.vendor, d.leds ? t("Rgb_Leds", d.leds) : ""].filter(Boolean).join(" · "))),
      mode, pick, h("button", { class: "btn", type: "button", onclick: go }, t("Rgb_Apply")),
      h("button", { class: "btn quiet", type: "button", onclick: () => set({ device: d.index, color: "" }) }, t("Rgb_Off")));
  }
  async function run(method, params) {
    start.disabled = true; msg.className = "msg"; msg.textContent = t("Tools_Working");
    try { show(await call(method, params)); } catch (e) { msg.className = "msg fail"; msg.textContent = String(e.message || e); } finally { start.disabled = false; }
  }
  const set = (p) => run("rgb.set", p);
  run("rgb.state");

  return box({ kind: "Motherboard", ico: "bolt", title: t("Rgb_Title"), sub: t("Rgb_Sub"), i, wide: true, a: "rgb",
    body: [h("p", { class: "note", style: { marginTop: 0 } }, t("Rgb_Note")), h("div", { class: "btn-row" }, start), all, list, msg] });
}
