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

// Colours that go together, worked out from the one the user picked (hue turned on the wheel): next to it, across from it, a triad, and shades of it.
function toHsl(hex) {
  const n = parseInt(hex.slice(1), 16), r = (n >> 16) / 255, g = ((n >> 8) & 255) / 255, b = (n & 255) / 255, mx = Math.max(r, g, b), mn = Math.min(r, g, b), l = (mx + mn) / 2, d = mx - mn;
  let hh = 0; if (d) hh = mx === r ? ((g - b) / d + (g < b ? 6 : 0)) : mx === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return { h: hh * 60, s: d ? d / (1 - Math.abs(2 * l - 1)) : 0, l };
}
function fromHsl(h, s, l) {
  h = ((h % 360) + 360) % 360; const k = (n) => (n + h / 30) % 12, a = s * Math.min(l, 1 - l), f = (n) => l - a * Math.max(-1, Math.min(k(n) - 3, Math.min(9 - k(n), 1)));
  return "#" + [f(0), f(8), f(4)].map((x) => Math.round(x * 255).toString(16).padStart(2, "0")).join("");
}
function harmonies(hex) {
  const { h, s, l } = toHsl(hex), bright = Math.max(s, 0.85), mid = Math.min(Math.max(l, 0.4), 0.6);
  return [["analogous", [fromHsl(h - 35, bright, mid), hex, fromHsl(h + 35, bright, mid)]], ["complement", [hex, fromHsl(h + 180, bright, mid)]],
    ["triad", [hex, fromHsl(h + 120, bright, mid), fromHsl(h + 240, bright, mid)]], ["mono", [fromHsl(h, bright, 0.3), fromHsl(h, bright, 0.5), fromHsl(h, bright, 0.75)]]];
}
// The first LED that is lit: what the card's colour starts as (a card that began dark must not offer black, which would switch the part off).
const lit = (d) => (d.colors || []).find((c) => /^#[0-9a-f]{6}$/i.test(c) && c.toLowerCase() !== "#000000");

export function mount(el) {
  const ui = new Map();   // what is chosen on each card, kept while the cards are drawn again
  let state = null;
  const msg = h("p", { class: "msg" }), makers = h("p", { class: "note", hidden: true });
  const keep = h("input", { type: "checkbox", class: "switch", "aria-label": t("Rgb_Keep") });
  // What the lights do at sleep and shutdown (the background monitor carries it out).
  const powerEnd = h("select", { class: "field", "aria-label": t("Rgb_PowerEnd"), onchange: (e) => run("rgb.powerEnd", { value: e.target.value }) }, ...["leave", "off", "on"].map((v) => h("option", { value: v }, t("Rgb_PowerEnd_" + v))));
  const start = h("button", { class: "btn primary", type: "button", onclick: () => run("rgb.start", { keepMakers: keep.checked }) }, icon("bolt"), t("Rgb_Connect"));
  const release = h("button", { class: "btn", type: "button", hidden: true, onclick: () => run("rgb.release") }, icon("stop"), t("Rgb_Release"));
  const dark = h("button", { class: "btn", type: "button", hidden: true, onclick: () => run("rgb.dark", { on: !state?.dark }) }, icon("stop"), t("Rgb_Dark_Off"));
  const list = h("div", { class: "panels two rgb-cards" });
  let alive = true, poll = 0;

  // ——— The whole set ———
  const allColor = h("input", { type: "color", class: "rgb-color", value: "#ff0000", "aria-label": t("Rgb_Color") });
  const allMode = h("select", { class: "field", "aria-label": t("Rgb_Effect") }, h("option", { value: "" }, t("Rgb_Mode_Keep")));
  const allSpeed = range("Rgb_Speed", 60), allBright = range("Rgb_Brightness", 100);
  // The colour spectra: a click picks one for the next apply (a second click on the same one lets it go). Laid along a strip it is a gradient; an effect that moves goes through its colours.
  let palette = null; const paletteBtns = [];
  const paletteChips = (setPalette) => h("div", { class: "rgb-palettes", role: "group", "aria-label": t("Rgb_Palettes") }, (state?.palettes || []).map((p) => {
    const b = h("button", { class: "rgb-pal", type: "button", style: { background: `linear-gradient(90deg, ${p.colors.join(", ")})` }, title: t("Rgb_Palette_" + p.id), "aria-label": t("Rgb_Palette_" + p.id), "aria-pressed": String(palette === p.id),
      onclick: () => { palette = palette === p.id ? null : p.id; setPalette(palette); for (const x of paletteBtns) x.setAttribute("aria-pressed", String(x.dataset.id === palette)); allHarmony?.draw(); } });
    b.dataset.id = p.id; paletteBtns.push(b); return b;
  }));
  const effectOptions = (s) => [h("option", { value: "" }, t("Rgb_Mode_Keep")), ...(s.effects || []).map((e) => h("option", { value: e }, t("Rgb_Fx_" + e))),
    s.modeNames.length ? h("optgroup", { label: t("Rgb_Fx_Own") }, s.modeNames.map((n) => h("option", { value: n }, n))) : null];
  const swatches = (setColor) => h("div", { class: "rgb-swatches", role: "group", "aria-label": t("Rgb_Color") }, PALETTE.map((c) =>
    h("button", { class: "rgb-sw", type: "button", style: { background: c }, title: c, "aria-label": c, onclick: () => setColor(c) })),
    h("input", { type: "range", class: "rgb-hue", min: 0, max: 360, value: 0, title: t("Rgb_Hue"), "aria-label": t("Rgb_Hue"), oninput: (e) => setColor(fromHsl(Number(e.target.value), 1, 0.5)) }));
  // Matching colours for the colour chosen now: a click makes them the spectrum for the next apply (a second click lets it go). Drawn again when the colour changes.
  const harmony = (getColor, getPalette, setPalette) => {
    const row = h("div", { class: "rgb-palettes rgb-harmony", role: "group", "aria-label": t("Rgb_Harmony") });
    const draw = () => row.replaceChildren(h("span", { class: "rgb-pal-label" }, t("Rgb_Harmony")), ...harmonies(getColor()).map(([id, colors]) => {
      const value = "custom:" + colors.join(","), b = h("button", { class: "rgb-pal", type: "button", style: { background: colors.length > 1 ? `linear-gradient(90deg, ${colors.join(", ")})` : colors[0] }, title: t("Rgb_Harm_" + id), "aria-label": t("Rgb_Harm_" + id), "aria-pressed": String(getPalette() === value),
        onclick: () => { setPalette(getPalette() === value ? null : value); draw(); } }); return b; }));
    draw(); return { el: row, draw };
  };
  const animate = () => { const box = h("input", { type: "checkbox", class: "switch", "aria-label": t("Rgb_Animate") }); return { box, el: h("label", { class: "rgb-keep", title: t("Rgb_Animate_Note") }, box, t("Rgb_Animate")) }; };
  const allPalettes = h("div", { class: "rgb-pal-wrap" }), allAnim = animate();
  const allHarmony = harmony(() => allColor.value, () => palette, (v) => { palette = v; for (const x of paletteBtns) x.setAttribute("aria-pressed", String(x.dataset.id === palette)); });
  allColor.addEventListener("input", () => allHarmony.draw());
  const allBody = h("div", { class: "rgb-all-body", hidden: true },
    h("div", { class: "rgb-row" }, h("label", { class: "rgb-field" }, h("span", {}, t("Rgb_Color")), allColor), swatches((c) => { allColor.value = c; allHarmony.draw(); }),
      h("label", { class: "rgb-field grow" }, h("span", {}, t("Rgb_Effect")), allMode)),
    h("div", { class: "rgb-row" }, allSpeed.el, allBright.el),
    allPalettes, allHarmony.el, allAnim.el,
    h("div", { class: "btn-row" },
      h("button", { class: "btn primary", type: "button", onclick: () => { const fx = allMode.value || (allAnim.box.checked ? (palette ? "Rainbow" : "Breathing") : ""); run("rgb.set", { device: -1, color: allColor.value, ...(palette ? { palette } : {}), ...(fx ? { modeName: fx, speed: allSpeed.value(), brightness: allBright.value() } : {}) }); } }, t("Rgb_ApplyAll")),
      h("button", { class: "btn", type: "button", onclick: () => run("rgb.set", { device: -1, color: "" }) }, t("Rgb_OffAll"))),
    h("p", { class: "note" }, t("Rgb_Effect_Note")));

  function range(key, value) {
    const input = h("input", { type: "range", class: "range", min: 0, max: 100, value, "aria-label": t(key) }), out = h("b", { class: "num" }, `${value}%`);
    input.addEventListener("input", () => { out.textContent = `${input.value}%`; });
    return { el: h("label", { class: "rgb-field grow" }, h("span", {}, t(key)), h("span", { class: "rgb-range" }, input, out)), value: () => Number(input.value), set: (v) => { input.value = v; out.textContent = `${v}%`; } };
  }

  function show(s) {
    state = s; powerEnd.value = s.powerEnd || "leave";
    start.hidden = s.connected || s.connecting; release.hidden = !s.connected && !(s.makers || []).length; dark.hidden = !s.connected; dark.lastChild.textContent = t(s.dark ? "Rgb_Dark_On" : "Rgb_Dark_Off");
    clearTimeout(poll); if (s.connecting && alive) poll = setTimeout(() => alive && run("rgb.state", undefined, true), 2000);   // the first search runs on its own: ask again until it is done
    msg.className = `msg ${s.error ? "fail" : ""}`; msg.textContent = s.error || (s.connecting ? t("Rgb_Connecting") : !s.found && !s.connected ? t("Rgb_NotFound") : s.connected && !s.devices.length ? t("Rgb_Empty") : "");
    const shown = s.makers?.length ? t("Rgb_Makers_Stopped", s.makers.join("، ")) : s.running?.length ? t("Rgb_Makers_Running", s.running.join("، ")) : "";
    makers.hidden = !shown; makers.textContent = shown;
    allBody.hidden = !s.devices.length;
    const was = allMode.value; allMode.replaceChildren(...effectOptions(s).filter(Boolean)); allMode.value = [...allMode.querySelectorAll("option")].some((o) => o.value === was) ? was : "";
    paletteBtns.length = 0; allPalettes.replaceChildren(h("span", { class: "rgb-pal-label" }, t("Rgb_Palettes")), paletteChips(() => {}));
    list.replaceChildren(...s.devices.map(card));
    if (!s.error && s.report) msg.textContent = reportText(s.report);
  }

  function reportText(r) {
    const name = (s) => (state?.effects || []).includes(s) ? t("Rgb_Fx_" + s) : s;
    const parts = [t("Rgb_Report", name(r.effect), r.native, r.simulated)];
    if (r.skipped?.length) parts.push(t("Rgb_Report_Skipped", r.skipped.join("، ")));
    return r.effect ? parts.join(" ") : "";
  }

  function card(d) {
    const mine = ui.get(d.index) || { color: lit(d) || "#ff0000", mode: "", speed: null, bright: null, touched: false }; ui.set(d.index, mine);
    if (!mine.touched) mine.color = lit(d) || mine.color;   // follows the lights until the user picks a colour of their own
    const pick = h("input", { type: "color", class: "rgb-color", value: mine.color, "aria-label": t("Rgb_Color"), oninput: (e) => { mine.color = e.target.value; mine.touched = true; harm.draw(); } });
    const mode = h("select", { class: "field", "aria-label": t("Rgb_Mode"), onchange: () => { mine.mode = mode.value; sync(); } },
      h("option", { value: "" }, t("Rgb_Mode_Keep")), d.modes.map((m) => h("option", { value: m.index }, m.name)),
      h("optgroup", { label: t("Rgb_Fx_Group") }, (state?.effects || []).map((e) => h("option", { value: "fx:" + e }, t("Rgb_Fx_" + e)))));
    mode.value = mine.mode;
    const speed = range("Rgb_Speed", mine.speed ?? 60), bright = range("Rgb_Brightness", mine.bright ?? 100);
    speed.el.querySelector("input").addEventListener("change", () => { mine.speed = speed.value(); }); bright.el.querySelector("input").addEventListener("change", () => { mine.bright = bright.value(); });
    const fx = () => mode.value.startsWith("fx:") ? mode.value.slice(3) : "";
    const current = () => fx() ? { color: true, speed: true, brightness: true } : d.modes.find((m) => String(m.index) === mode.value);
    function sync() { const m = current(); pick.disabled = !!m && !m.color; speed.el.hidden = !m?.speed; bright.el.hidden = !m?.brightness; }
    sync();
    let own = null;   // this card's spectrum
    const harm = harmony(() => pick.value, () => own, (v) => { own = v; for (const x of chips.children) x.setAttribute("aria-pressed", String(x.dataset.id === own)); }), anim = animate();
    const go = () => run("rgb.set", mode.value === "" ? { device: d.index, color: pick.value, ...(own ? { palette: own } : {}), ...(anim.box.checked ? { modeName: own ? "Rainbow" : "Breathing" } : {}) }
      : { device: d.index, color: pick.value, ...(own ? { palette: own } : {}), ...(fx() ? { modeName: fx() } : { mode: Number(mode.value) }), ...(current()?.speed ? { speed: speed.value() } : {}), ...(current()?.brightness ? { brightness: bright.value() } : {}) });
    const chips = h("div", { class: "rgb-palettes", role: "group", "aria-label": t("Rgb_Palettes") }, (state?.palettes || []).map((p) => {
      const b = h("button", { class: "rgb-pal", type: "button", style: { background: `linear-gradient(90deg, ${p.colors.join(", ")})` }, title: t("Rgb_Palette_" + p.id), "aria-label": t("Rgb_Palette_" + p.id), "aria-pressed": "false",
        onclick: () => { own = own === p.id ? null : p.id; for (const x of chips.children) x.setAttribute("aria-pressed", String(x.dataset.id === own)); harm.draw(); } });
      b.dataset.id = p.id; return b;
    }));
    // The LEDs as they are lit. Where the device takes a colour for each LED, an LED is a button: it opens the colour picker for that one alone.
    const one = h("input", { type: "color", class: "rgb-one", tabindex: "-1", "aria-hidden": "true" }); let oneLed = -1;
    one.addEventListener("change", () => { if (oneLed >= 0) run("rgb.led", { device: d.index, led: oneLed, color: one.value }); });
    const strip = d.perLed
      ? h("div", { class: "rgb-strip pick", role: "group", "aria-label": t("Rgb_Led_Hint") }, d.colors.map((c, i) => h("button", { type: "button", class: "rgb-led", style: { background: c, color: c }, title: t("Rgb_Led", i + 1), "aria-label": t("Rgb_Led", i + 1),
          onclick: () => { oneLed = i; one.value = /^#[0-9a-f]{6}$/i.test(c) ? c : "#ffffff"; if (one.showPicker) one.showPicker(); else one.click(); } })), one)
      : h("div", { class: "rgb-strip", "aria-hidden": "true" }, d.colors.slice(0, 40).map((c) => h("i", { style: { background: c } })));
    const zones = d.zones.filter((z) => z.resizable || z.leds > 0);
    const zoneRows = zones.length ? h("div", { class: "rgb-zones" }, h("h3", { class: "h3" }, t("Rgb_Zones")), zones.map((z) => zoneRow(d, z)),
      zones.some((z) => z.resizable && z.leds === 0) ? h("p", { class: "note" }, t("Rgb_Zone_Hint")) : null) : null;
    return box({ cls: HUE[d.kind] || "p-tool", ico: ICON[d.kind] || "bolt", title: d.name, sub: [t(`Rgb_Kind_${d.kind}`), d.vendor, d.leds ? t("Rgb_Leds", d.leds) : ""].filter(Boolean).join(" · "), extra: "rgb-card", a: "rgb",
      body: [strip, d.perLed ? h("p", { class: "note rgb-led-hint" }, t("Rgb_Led_Hint")) : null, h("div", { class: "rgb-row" }, h("label", { class: "rgb-field grow" }, h("span", {}, t("Rgb_Mode")), mode), h("label", { class: "rgb-field" }, h("span", {}, t("Rgb_Color")), pick)),
        swatches((c) => { pick.value = c; mine.color = c; mine.touched = true; harm.draw(); }), chips, harm.el, anim.el, h("div", { class: "rgb-row" }, speed.el, bright.el),
        d.kind === "Graphics" && d.leds <= 1 ? h("p", { class: "note" }, t("Rgb_Gpu_OneZone")) : null,
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
          h("label", { class: "rgb-keep" }, keep, t("Rgb_Keep")), h("label", { class: "rgb-field" }, h("span", {}, t("Rgb_PowerEnd")), powerEnd), h("p", { class: "caption", style: { marginTop: 0 } }, t("Rgb_PowerEnd_Note")), h("div", { class: "btn-row" }, start, dark, release), msg] }),
      box({ cls: "p-tool", ico: "sliders", title: t("Rgb_All"), sub: t("Rgb_All_Sub"), i: 1, body: [allBody] })),
    h("h2", { class: "tools-sec-title" }, t("Rgb_Devices")), list);
  run("rgb.state");
  return () => { alive = false; clearTimeout(poll); /* the connection is the host's: it is let go by the button or when the app closes */ };
}
