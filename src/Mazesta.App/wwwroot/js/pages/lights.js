// The lighting page: the colours and effects of the memory, graphics card, board, fans and strips, through OpenRGB (a separate program the app runs as its server).
// A panel for the whole set (one colour, a gradient of two, or a ready spectrum, with one effect by name and its speed and brightness, for every device that has it),
// then a card for each device with the same choice and its own modes, and, for a board, the number of LEDs of each output (an addressable header is listed with
// none until it is told). The makers' own lighting programs are stopped while the page holds the lights and started again when it lets go; the page says which.
// Nothing is shown as done that the program refused.
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon } from "../ui.js";
import { box } from "../groups.js";

const ICON = { Motherboard: "board", Memory: "ram", Graphics: "gpu", Cooler: "fan", Strip: "strip", Keyboard: "keyboard", Mouse: "mouse", Storage: "drive", Case: "case", Other: "bolt" };
const HUE = { Motherboard: "p-board", Memory: "p-ram", Graphics: "p-gpu", Cooler: "p-cpu", Storage: "p-storage", Strip: "p-net" };
const PALETTE = ["#ff0000", "#ff5a00", "#ffa000", "#ffd400", "#a8ff00", "#00e676", "#00e5ff", "#2979ff", "#3d2bff", "#8e44ff", "#ff2bd6", "#ffffff"];
const LOOKS = { solid: "drop", gradient: "duo", preset: "layers" };
const FX_ICON = { Static: "bulb", Breathing: "pulse", Rainbow: "rainbow", Cycle: "refresh", Flash: "bolt" };
const SPECTRUM = ["#ff0000", "#ffd400", "#00e676", "#00e5ff", "#2979ff", "#ff2bd6", "#ff0000"];

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
// Laid as the LED strips on the cards are: the first LED at the start of the line (the right in Persian).
const grad = (cs) => cs.length > 1 ? `linear-gradient(${document.dir === "rtl" ? 270 : 90}deg, ${cs.join(", ")})` : cs[0];
const look = (color) => ({ kind: "solid", a: color, b: fromHsl(toHsl(color).h + 180, 1, 0.5), edit: "a", preset: null, fx: "Static", own: "" });

// What is chosen on each card and on the whole set, kept for the app's run: going to another page and back shows the last choice, not a fresh one.
const ui = new Map();
const allSt = { ...look("#ff0000"), speed: 60, bright: 100 };

export function mount(el) {
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

  // The colour of a look (st): one colour, a gradient of two the user picks (either stop is edited by the same swatches and hue), or a spectrum (ready, or made from
  // the colour), above a strip that shows it as it will run. Colour changes update in place: a redraw would drop the hue slider out of the user's hand.
  function studio(st) {
    const cur = () => st.kind === "gradient" ? st[st.edit] : st.a;
    const presetColors = () => st.preset?.startsWith("custom:") ? st.preset.slice(7).split(",") : (state?.palettes || []).find((p) => p.id === st.preset)?.colors;
    const colors = () => st.kind === "gradient" ? [st.a, st.b] : st.kind === "preset" && st.preset ? presetColors() || [st.a] : [st.a];
    const preview = h("div", { class: "rgb-preview", role: "img" }, h("i"));
    const setColor = (c) => { if (st.kind === "gradient") st[st.edit] = c; else st.a = c; st.touched = true; update(); };
    const well = h("input", { type: "color", class: "rgb-well", "aria-label": t("Rgb_Color"), oninput: (e) => setColor(e.target.value) });
    const hex = h("b", { class: "rgb-hex lat" });
    const hue = h("input", { type: "range", class: "rgb-hue", dir: "ltr", min: 0, max: 360, value: 0, title: t("Rgb_Hue"), "aria-label": t("Rgb_Hue"), oninput: (e) => setColor(fromHsl(Number(e.target.value), 1, 0.5)) });
    const sws = PALETTE.map((c) => h("button", { class: "rgb-sw", type: "button", style: { background: c, color: c }, title: c, "aria-label": c, onclick: () => setColor(c) }));
    const picker = h("div", { class: "rgb-picker" }, h("label", { class: "rgb-wellbox" }, well, hex), h("div", { class: "rgb-pickcol" }, h("div", { class: "rgb-swatches", role: "group", "aria-label": t("Rgb_Color") }, sws), hue));
    const stop = (k) => h("button", { type: "button", class: "rgb-stop", onclick: () => { st.edit = k; update(); } }, h("i"), h("span", {}, h("small", {}, t(k === "a" ? "Rgb_First" : "Rgb_Second")), h("b", { class: "lat" })));
    const stopA = stop("a"), stopB = stop("b");
    const stops = h("div", { class: "rgb-stops" }, stopA, h("button", { type: "button", class: "rgb-swapbtn", title: t("Rgb_Swap"), "aria-label": t("Rgb_Swap"), onclick: () => { [st.a, st.b] = [st.b, st.a]; update(); } }, icon("swap")), stopB);
    const chip = (cs, label, pressed, onclick, wide) => h("button", { class: `rgb-pal${wide ? " wide" : ""}`, type: "button", "aria-pressed": String(pressed), title: label, "aria-label": label, onclick }, h("i", { style: { background: grad(cs) } }), h("span", {}, label));
    const pairs = h("div", { class: "rgb-palettes" }), presets = h("div", { class: "rgb-palettes" }), mine = h("div", { class: "rgb-palettes" });
    const body = h("div", { class: "rgb-look-body" });
    const tabs = Object.keys(LOOKS).map((k) => h("button", { type: "button", role: "radio", class: "rgb-tab", onclick: () => { st.kind = k; draw(); } }, icon(LOOKS[k]), h("span", {}, t("Rgb_Look_" + k))));
    function update() {
      const c = cur(), cs = colors();
      well.value = c; hex.textContent = c.toUpperCase();
      if (document.activeElement !== hue) hue.value = Math.round(toHsl(c).h);
      for (const b of sws) b.setAttribute("aria-pressed", String(b.title === c.toLowerCase()));
      for (const [b, k] of [[stopA, "a"], [stopB, "b"]]) { b.setAttribute("aria-pressed", String(st.edit === k)); b.firstChild.style.background = st[k]; b.querySelector("b").textContent = st[k].toUpperCase(); }
      const moving = st.fx === "Rainbow" || st.fx === "Cycle";
      preview.firstChild.style.background = grad(moving && cs.length < 2 ? SPECTRUM : moving ? [...cs, cs[0]] : cs);
      preview.dataset.fx = st.own ? "" : st.fx; preview.setAttribute("aria-label", t("Rgb_Preview"));
      pairs.replaceChildren(...harmonies(st.edit === "b" ? st.a : c).map(([id, cs2]) => [id, [cs2[0], cs2[cs2.length - 1]]]).map(([id, [x, y]]) =>
        chip([x, y], t("Rgb_Harm_" + id), st.a === x && st.b === y, () => { st.a = x; st.b = y; update(); })));
      for (const b of presets.children) b.setAttribute("aria-pressed", String(b.dataset.id === st.preset));
      mine.replaceChildren(...harmonies(st.a).map(([id, cs2]) => { const v = "custom:" + cs2.join(","); return chip(cs2, t("Rgb_Harm_" + id), st.preset === v, () => { st.preset = v; update(); }); }));
    }
    function draw() {
      tabs.forEach((b, i) => b.setAttribute("aria-checked", String(Object.keys(LOOKS)[i] === st.kind)));
      presets.replaceChildren(...(state?.palettes || []).map((p) => { const b = chip(p.colors, t("Rgb_Palette_" + p.id), st.preset === p.id, () => { st.preset = st.preset === p.id ? null : p.id; update(); }, true); b.dataset.id = p.id; return b; }));
      body.replaceChildren(...(st.kind === "solid" ? [picker]
        : st.kind === "gradient" ? [stops, picker, h("p", { class: "rgb-sub" }, t("Rgb_Suggest")), pairs, h("p", { class: "note" }, t("Rgb_Gradient_Note"))]
        : [h("p", { class: "rgb-sub" }, t("Rgb_Palettes")), presets, h("p", { class: "rgb-sub" }, t("Rgb_Harmony")), mine]));
      update();
    }
    draw();
    return {
      el: h("div", { class: "rgb-studio" }, h("div", { class: "rgb-tabs", role: "radiogroup", "aria-label": t("Rgb_ColorKind") }, tabs), preview, body),
      update, draw,
      // What the bridge is sent: the colour, and the spectrum it runs through when there is one.
      payload: () => st.kind === "gradient" ? { color: st.a, palette: `custom:${st.a},${st.b}` } : st.kind === "preset" && st.preset ? { color: colors()[0], palette: st.preset } : { color: st.a },
      set disabled(v) { this.el.classList.toggle("off", v); this.el.inert = v; },
    };
  }

  // The effects every device is offered, as tiles, and a list of the devices' own (for a card, its own modes by number; for the set, by name).
  function effects(st, own, onPick) {
    const tiles = (state?.effects || []).map((e) => h("button", { type: "button", role: "radio", class: "rgb-fx", "data-fx": e, onclick: () => { st.fx = e; st.own = ""; sel.value = ""; mark(); onPick(); } }, icon(FX_ICON[e] || "sparkle"), h("span", {}, t("Rgb_Fx_" + e))));
    const sel = h("select", { class: "field", "aria-label": t("Rgb_OwnMode"), onchange: () => { st.own = sel.value; mark(); onPick(); } }, h("option", { value: "" }, t("Rgb_OwnMode_None")), own);
    sel.value = st.own;
    const mark = () => tiles.forEach((b) => b.setAttribute("aria-checked", String(!st.own && b.dataset.fx === st.fx)));
    mark();
    return h("div", { class: "rgb-fxbox" }, h("div", { class: "rgb-fxs", role: "radiogroup", "aria-label": t("Rgb_Effect") }, tiles), own.length ? h("label", { class: "rgb-field" }, h("span", {}, t("Rgb_OwnMode")), sel) : null);
  }

  // ——— The whole set ———
  const allSpeed = range("Rgb_Speed", allSt.speed), allBright = range("Rgb_Brightness", allSt.bright);
  allSpeed.el.querySelector("input").addEventListener("change", () => { allSt.speed = allSpeed.value(); }); allBright.el.querySelector("input").addEventListener("change", () => { allSt.bright = allBright.value(); });
  const allBody = h("div", { class: "rgb-all-body", hidden: true });
  let allStudio = null;
  function drawAll(s) {
    allStudio = studio(allSt); paced(allStudio, allSpeed);
    const sync = () => { allSpeed.el.hidden = allBright.el.hidden = !allSt.own && allSt.fx === "Static"; allStudio.update(); };
    allBody.replaceChildren(h("p", { class: "rgb-sub" }, t("Rgb_Effect")), effects(allSt, s.modeNames.map((n) => h("option", { value: n }, n)), sync), allStudio.el,
      h("div", { class: "rgb-row" }, allSpeed.el, allBright.el),
      h("div", { class: "btn-row" },
        h("button", { class: "btn primary", type: "button", onclick: () => run("rgb.set", { device: -1, ...allStudio.payload(), modeName: allSt.own || allSt.fx, ...(allSt.own || allSt.fx !== "Static" ? { speed: allSpeed.value(), brightness: allBright.value() } : {}) }) }, icon("check"), t("Rgb_ApplyAll")),
        h("button", { class: "btn", type: "button", onclick: () => run("rgb.set", { device: -1, color: "" }) }, t("Rgb_OffAll"))),
      h("p", { class: "note" }, t("Rgb_Effect_Note")));
    sync();
  }
  // The preview runs at the speed chosen (the devices' own speeds differ, so it is only a likeness).
  function paced(s, sp) { const set = () => s.el.style.setProperty("--rgb-dur", `${(6 - sp.value() / 22).toFixed(2)}s`); sp.el.querySelector("input").addEventListener("input", set); set(); }

  function range(key, value) {
    const input = h("input", { type: "range", class: "range", min: 0, max: 100, value, "aria-label": t(key) }), out = h("b", { class: "num" }, `${value}%`);
    input.addEventListener("input", () => { out.textContent = `${input.value}%`; });
    return { el: h("label", { class: "rgb-field grow" }, h("span", {}, t(key)), h("span", { class: "rgb-range" }, input, out)), value: () => Number(input.value), set: (v) => { input.value = v; out.textContent = `${v}%`; } };
  }

  function show(s) {
    const first = !state; state = s; powerEnd.value = s.powerEnd || "leave";
    start.hidden = s.connected || s.connecting; release.hidden = !s.connected && !(s.makers || []).length; dark.hidden = !s.connected; dark.lastChild.textContent = t(s.dark ? "Rgb_Dark_On" : "Rgb_Dark_Off");
    clearTimeout(poll); if (s.connecting && alive) poll = setTimeout(() => alive && run("rgb.state", undefined, true), 2000);   // the first search runs on its own: ask again until it is done
    msg.className = `msg ${s.error ? "fail" : ""}`; msg.textContent = s.error || (s.connecting ? t("Rgb_Connecting") : !s.found && !s.connected ? t("Rgb_NotFound") : s.connected && !s.devices.length ? t("Rgb_Empty") : "");
    const shown = s.makers?.length ? t("Rgb_Makers_Stopped", s.makers.join("، ")) : s.running?.length ? t("Rgb_Makers_Running", s.running.join("، ")) : "";
    makers.hidden = !shown; makers.textContent = shown;
    const had = !allBody.hidden; allBody.hidden = !s.devices.length;
    if (first || !had) drawAll(s);   // drawn once the devices are there, then kept (so a slider in use is not taken away)
    list.replaceChildren(...s.devices.map(card));
    if (!s.error && s.report) msg.textContent = reportText(s.report);
  }

  function reportText(r) {
    const name = (s) => (state?.effects || []).includes(s) ? t("Rgb_Fx_" + s) : s;
    const parts = [r.native + r.simulated ? t("Rgb_Report", name(r.effect), r.native, r.simulated) : t("Rgb_Report_Still", name(r.effect), r.still)];
    if (r.skipped?.length) parts.push(t("Rgb_Report_Skipped", r.skipped.join("، ")));
    return r.effect ? parts.join(" ") : "";
  }

  function card(d) {
    const key = `${d.index}:${d.name}`;   // the name too: after a new search the numbers can belong to other devices
    const mine = ui.get(key) || { ...look(lit(d) || "#ff0000"), speed: null, bright: null, touched: false }; ui.set(key, mine);
    if (!mine.touched) mine.a = lit(d) || mine.a;   // follows the lights until the user picks a colour of their own
    const speed = range("Rgb_Speed", mine.speed ?? 60), bright = range("Rgb_Brightness", mine.bright ?? 100);
    speed.el.querySelector("input").addEventListener("change", () => { mine.speed = speed.value(); }); bright.el.querySelector("input").addEventListener("change", () => { mine.bright = bright.value(); });
    const studioC = studio(mine); paced(studioC, speed);
    const ownMode = () => mine.own === "" ? null : d.modes.find((m) => String(m.index) === mine.own);
    // What the chosen look can be given: an effect of ours takes all three; a device's own mode what it says it takes.
    const takes = () => ownMode() ?? { color: true, speed: mine.fx !== "Static", brightness: mine.fx !== "Static" };
    function sync() { const m = takes(); studioC.disabled = !m.color; speed.el.hidden = !m.speed; bright.el.hidden = !m.brightness; studioC.update(); }
    const fxs = effects(mine, d.modes.map((m) => h("option", { value: String(m.index) }, m.name)), sync);
    sync();
    const go = () => { const m = takes(); run("rgb.set", { device: d.index, ...(m.color ? studioC.payload() : {}), ...(mine.own !== "" ? { mode: Number(mine.own) } : { modeName: mine.fx }), ...(m.speed ? { speed: speed.value() } : {}), ...(m.brightness ? { brightness: bright.value() } : {}) }); };
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
      body: [strip, d.perLed ? h("p", { class: "note rgb-led-hint" }, t("Rgb_Led_Hint")) : null, h("p", { class: "rgb-sub" }, t("Rgb_Effect")), fxs, studioC.el, h("div", { class: "rgb-row" }, speed.el, bright.el),
        d.kind === "Graphics" && d.leds <= 1 ? h("p", { class: "note" }, t("Rgb_Gpu_OneZone")) : null,
        h("div", { class: "btn-row" }, h("button", { class: "btn primary", type: "button", onclick: go }, icon("check"), t("Rgb_Apply")), h("button", { class: "btn", type: "button", onclick: () => run("rgb.set", { device: d.index, color: "" }) }, t("Rgb_Off"))), zoneRows] });
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
