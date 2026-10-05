// The lighting page: the colours and effects of the memory, graphics card, board, fans and strips, through OpenRGB (a separate program the app runs as its server).
// A panel for the whole set (one colour, or one effect by name with its speed and brightness, for every device that has it), then a card for each device with
// its own mode, colour and speed, and, for a board, the number of LEDs of each output (an addressable header is listed with none until it is told). The makers'
// own lighting programs are stopped while the page holds the lights and started again when it lets go; the page says which. Nothing is shown as done that the
// program refused.
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon } from "../ui.js";
import { box } from "../groups.js";

const ICON = { Motherboard: "board", Memory: "ram", Graphics: "gpu", Cooler: "fan", Strip: "strip", Keyboard: "keyboard", Mouse: "mouse", Storage: "drive", Case: "case", Other: "bolt" };
const HUE = { Motherboard: "p-board", Memory: "p-ram", Graphics: "p-gpu", Cooler: "p-cpu", Storage: "p-storage", Strip: "p-net" };
const PALETTE = ["#ff0000", "#ff7a00", "#ffd400", "#00e676", "#00e5ff", "#2979ff", "#8e44ff", "#ff2bd6", "#ffffff"];

export function mount(el) {
  const ui = new Map();   // what is chosen on each card, kept while the cards are drawn again
  let state = null;
  const msg = h("p", { class: "msg" }), makers = h("p", { class: "note", hidden: true });
  const keep = h("input", { type: "checkbox", class: "switch", "aria-label": t("Rgb_Keep") });
  const start = h("button", { class: "btn primary", type: "button", onclick: () => run("rgb.start", { keepMakers: keep.checked }) }, icon("bolt"), t("Rgb_Connect"));
  const release = h("button", { class: "btn", type: "button", hidden: true, onclick: () => run("rgb.release") }, icon("stop"), t("Rgb_Release"));
  const list = h("div", { class: "panels two rgb-cards" });

  // ——— The whole set ———
  const allColor = h("input", { type: "color", class: "rgb-color", value: "#ff0000", "aria-label": t("Rgb_Color") });
  const allMode = h("select", { class: "field", "aria-label": t("Rgb_Effect") }, h("option", { value: "" }, t("Rgb_Mode_Keep")));
  const allSpeed = range("Rgb_Speed", 60), allBright = range("Rgb_Brightness", 100);
  const swatches = (setColor) => h("div", { class: "rgb-swatches", role: "group", "aria-label": t("Rgb_Color") }, PALETTE.map((c) =>
    h("button", { class: "rgb-sw", type: "button", style: { background: c }, title: c, "aria-label": c, onclick: () => setColor(c) })));
  const allBody = h("div", { class: "rgb-all-body", hidden: true },
    h("div", { class: "rgb-row" }, h("label", { class: "rgb-field" }, h("span", {}, t("Rgb_Color")), allColor), swatches((c) => { allColor.value = c; }),
      h("label", { class: "rgb-field grow" }, h("span", {}, t("Rgb_Effect")), allMode)),
    h("div", { class: "rgb-row" }, allSpeed.el, allBright.el),
    h("div", { class: "btn-row" },
      h("button", { class: "btn primary", type: "button", onclick: () => run("rgb.set", { device: -1, color: allColor.value, ...(allMode.value ? { modeName: allMode.value, speed: allSpeed.value(), brightness: allBright.value() } : {}) }) }, t("Rgb_ApplyAll")),
      h("button", { class: "btn", type: "button", onclick: () => run("rgb.set", { device: -1, color: "" }) }, t("Rgb_OffAll"))),
    h("p", { class: "note" }, t("Rgb_Effect_Note")));

  function range(key, value) {
    const input = h("input", { type: "range", class: "range", min: 0, max: 100, value, "aria-label": t(key) }), out = h("b", { class: "num" }, `${value}%`);
    input.addEventListener("input", () => { out.textContent = `${input.value}%`; });
    return { el: h("label", { class: "rgb-field grow" }, h("span", {}, t(key)), h("span", { class: "rgb-range" }, input, out)), value: () => Number(input.value), set: (v) => { input.value = v; out.textContent = `${v}%`; } };
  }

  function show(s) {
    state = s;
    start.hidden = s.connected; release.hidden = !s.connected && !(s.makers || []).length;
    msg.className = `msg ${s.error ? "fail" : ""}`; msg.textContent = s.error || (!s.found && !s.connected ? t("Rgb_NotFound") : s.connected && !s.devices.length ? t("Rgb_Empty") : "");
    const shown = s.makers?.length ? t("Rgb_Makers_Stopped", s.makers.join("، ")) : s.running?.length ? t("Rgb_Makers_Running", s.running.join("، ")) : "";
    makers.hidden = !shown; makers.textContent = shown;
    allBody.hidden = !s.devices.length;
    const was = allMode.value; allMode.replaceChildren(h("option", { value: "" }, t("Rgb_Mode_Keep")), ...s.modeNames.map((n) => h("option", { value: n }, n))); allMode.value = s.modeNames.includes(was) ? was : "";
    list.replaceChildren(...s.devices.map(card));
  }

  function card(d) {
    const mine = ui.get(d.index) || { color: d.color || "#ffffff", mode: "", speed: null, bright: null }; ui.set(d.index, mine);
    const pick = h("input", { type: "color", class: "rgb-color", value: mine.color, "aria-label": t("Rgb_Color"), oninput: (e) => { mine.color = e.target.value; } });
    const mode = h("select", { class: "field", "aria-label": t("Rgb_Mode"), onchange: () => { mine.mode = mode.value; sync(); } },
      h("option", { value: "" }, t("Rgb_Mode_Keep")), d.modes.map((m) => h("option", { value: m.index }, m.name)));
    mode.value = mine.mode;
    const speed = range("Rgb_Speed", mine.speed ?? 60), bright = range("Rgb_Brightness", mine.bright ?? 100);
    speed.el.querySelector("input").addEventListener("change", () => { mine.speed = speed.value(); }); bright.el.querySelector("input").addEventListener("change", () => { mine.bright = bright.value(); });
    const current = () => d.modes.find((m) => String(m.index) === mode.value);
    function sync() { const m = current(); pick.disabled = !!m && !m.color; speed.el.hidden = !m?.speed; bright.el.hidden = !m?.brightness; }
    sync();
    const go = () => run("rgb.set", mode.value === "" ? { device: d.index, color: pick.value } : { device: d.index, color: pick.value, mode: Number(mode.value), ...(current()?.speed ? { speed: speed.value() } : {}), ...(current()?.brightness ? { brightness: bright.value() } : {}) });
    const strip = h("div", { class: "rgb-strip", "aria-hidden": "true" }, (d.colors.length ? d.colors : []).slice(0, 40).map((c) => h("i", { style: { background: c } })));
    const zones = d.zones.filter((z) => z.resizable || z.leds > 0);
    const zoneRows = zones.length ? h("div", { class: "rgb-zones" }, h("h3", { class: "h3" }, t("Rgb_Zones")), zones.map((z) => zoneRow(d, z)),
      zones.some((z) => z.resizable && z.leds === 0) ? h("p", { class: "note" }, t("Rgb_Zone_Hint")) : null) : null;
    return box({ cls: HUE[d.kind] || "p-tool", ico: ICON[d.kind] || "bolt", title: d.name, sub: [t(`Rgb_Kind_${d.kind}`), d.vendor, d.leds ? t("Rgb_Leds", d.leds) : ""].filter(Boolean).join(" · "), extra: "rgb-card", a: "rgb",
      body: [strip, h("div", { class: "rgb-row" }, h("label", { class: "rgb-field grow" }, h("span", {}, t("Rgb_Mode")), mode), h("label", { class: "rgb-field" }, h("span", {}, t("Rgb_Color")), pick)),
        swatches((c) => { pick.value = c; mine.color = c; }), h("div", { class: "rgb-row" }, speed.el, bright.el),
        h("div", { class: "btn-row" }, h("button", { class: "btn primary", type: "button", onclick: go }, t("Rgb_Apply")), h("button", { class: "btn", type: "button", onclick: () => run("rgb.set", { device: d.index, color: "" }) }, t("Rgb_Off"))), zoneRows] });
  }

  function zoneRow(d, z) {
    if (!z.resizable) return h("div", { class: "rgb-zone" }, h("span", { class: "lat" }, z.name), h("span", { class: "rgb-zone-n" }, t("Rgb_Leds", z.leds)));
    const n = h("input", { type: "number", class: "field num", min: z.min, max: z.max, value: z.leds, inputmode: "numeric", "aria-label": `${z.name}: ${t("Rgb_Zone_Count")}` });
    const slide = h("input", { type: "range", class: "range", min: z.min, max: z.max, value: z.leds, "aria-label": z.name, oninput: () => { n.value = slide.value; } });
    n.addEventListener("input", () => { slide.value = n.value; });
    return h("div", { class: "rgb-zone resizable" }, h("span", { class: "lat" }, z.name), slide, n, h("small", {}, t("Rgb_Zone_Max", z.max)),
      h("button", { class: "btn", type: "button", onclick: () => run("rgb.zone", { device: d.index, zone: z.index, leds: Number(n.value) }) }, t("Rgb_Zone_Set")));
  }

  async function run(method, params) {
    msg.className = "msg"; msg.textContent = t("Tools_Working"); start.disabled = true;
    try { show(await call(method, params)); } catch (e) { msg.className = "msg fail"; msg.textContent = String(e.message || e); } finally { start.disabled = false; }
  }

  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Lights")), h("p", { class: "page-lede" }, t("Rgb_Page_Lede")))),
    h("div", { class: "panels two" },
      box({ cls: "p-tool", ico: "bolt", title: t("Rgb_Title"), sub: t("Rgb_Sub"), a: "rgb", i: 0,
        body: [h("p", { class: "note", style: { marginTop: 0 } }, t("Rgb_Note")), makers,
          h("label", { class: "rgb-keep" }, keep, t("Rgb_Keep")), h("div", { class: "btn-row" }, start, release), msg] }),
      box({ cls: "p-tool", ico: "sliders", title: t("Rgb_All"), sub: t("Rgb_All_Sub"), i: 1, body: [allBody] })),
    h("h2", { class: "tools-sec-title" }, t("Rgb_Devices")), list);
  run("rgb.state");
  return () => { /* the connection is the host's: it is let go by the button or when the app closes */ };
}
