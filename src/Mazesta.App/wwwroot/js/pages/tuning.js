// GPU tuning through NVIDIA's NVML, driven by the same view model as the WPF edition: range checks, crash journal, measured curve scan and the
// automatic search all live on the host. The curve editor here draws the measured stock curve and the curve the form gives, and turns a drag
// into offset + cap exactly as the WPF editor does; NVML cannot move single points, and nothing here pretends it can.
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon } from "../ui.js";
import { setField } from "./tests.js";
import { box } from "../groups.js";

const SVG = "http://www.w3.org/2000/svg";
const s = (tag, attrs = {}, ...kids) => { const e = document.createElementNS(SVG, tag); for (const [k, v] of Object.entries(attrs)) if (v !== null && v !== undefined) e.setAttribute(k, v); e.append(...kids); return e; };

// The measured-curve maths of Mazesta.Core.Tuning.VfCurve, for drawing while dragging; the host re-checks everything on Apply.
function sorted(points) { return [...points].sort((a, b) => a.clock - b.clock || a.volt - b.volt); }
function clockAt(stock, v) {
  const p = sorted(stock); if (!p.length || v < p[0].volt || v > p[p.length - 1].volt) return null;
  let best = null;
  for (let i = 1; i < p.length; i++) {
    const a = p[i - 1], b = p[i]; if (v < a.volt || v > b.volt) continue;
    const c = b.volt - a.volt < 1e-9 ? b.clock : a.clock + ((v - a.volt) / (b.volt - a.volt)) * (b.clock - a.clock);
    best = Math.max(best ?? c, c);
  }
  return best ?? p[0].clock;
}
function pin(stock, volt, target, bin = 15) { const c = clockAt(stock, volt); return c === null ? null : { offset: Math.round((target - c) / bin) * bin, cap: target }; }

function curveEditor(onChange) {
  const svg = s("svg", { class: "curve", role: "img" });
  let st = null, drag = null, frozen = null, hover = -1;
  const W = () => svg.clientWidth || 800, H = () => svg.clientHeight || 380, L = 54, R = 16, T = 26, B = 30;

  function axes(stock) {
    if (frozen) return frozen;
    let v0 = Math.min(...stock.map((p) => p.volt)), v1 = Math.max(...stock.map((p) => p.volt));
    let c0 = Math.min(...stock.map((p) => p.clock)) + Math.min(0, st.offset), c1 = Math.max(Math.max(...stock.map((p) => p.clock)) + Math.max(0, st.offset), st.cap || 0);
    if (st.liveClock != null) { c0 = Math.min(c0, st.liveClock); c1 = Math.max(c1, st.liveClock); }
    if (st.liveVolt != null) { v0 = Math.min(v0, st.liveVolt); v1 = Math.max(v1, st.liveVolt); }
    return { v0: Math.floor((v0 - 0.02) / 0.05) * 0.05, v1: Math.ceil((v1 + 0.02) / 0.05) * 0.05, c0: Math.floor((c0 - 60) / 100) * 100, c1: Math.ceil((c1 + 60) / 100) * 100 };
  }
  const X = (a, v) => L + ((v - a.v0) / (a.v1 - a.v0)) * (W() - L - R);
  const Y = (a, c) => H() - B - ((c - a.c0) / (a.c1 - a.c0)) * (H() - T - B);
  const clockOfY = (a, y) => a.c0 + ((H() - B - y) / (H() - T - B)) * (a.c1 - a.c0);
  const ordered = () => [...st.stock].sort((a, b) => a.volt - b.volt || a.clock - b.clock);
  const shaped = () => ordered().map((p) => ({ volt: p.volt, clock: Math.min(p.clock + st.offset, st.cap > 0 ? st.cap : Infinity) }));

  function draw() {
    svg.replaceChildren(); svg.setAttribute("viewBox", `0 0 ${W()} ${H()}`);
    if (!st || !st.stock || st.stock.length < 2) { svg.append(s("text", { class: "empty", x: W() / 2, y: H() / 2, "text-anchor": "middle" }, t("Tuning_Curve_Empty"))); return; }
    const a = axes(st.stock), grid = s("g", { class: "grid" }), axis = s("g", { class: "axis" });
    const step = a.c1 - a.c0 > 1200 ? 200 : 100;
    for (let c = a.c0; c <= a.c1 + 0.1; c += step) { const y = Y(a, c); grid.append(s("line", { x1: L, x2: W() - R, y1: y, y2: y })); axis.append(s("text", { x: L - 8, y: y + 4, "text-anchor": "end" }, String(c))); }
    for (let v = a.v0; v <= a.v1 + 1e-9; v += 0.05) { const x = X(a, v); grid.append(s("line", { x1: x, x2: x, y1: T, y2: H() - B })); axis.append(s("text", { x, y: H() - B + 18, "text-anchor": "middle" }, v.toFixed(2) + (v + 0.05 > a.v1 + 1e-9 ? " V" : ""))); }
    axis.append(s("text", { x: L - 8, y: 12, "text-anchor": "end" }, "MHz"));
    svg.append(grid, axis);
    const line = (pts, cls) => s("polyline", { class: cls, points: pts.map((p) => `${X(a, p.volt).toFixed(1)},${Y(a, p.clock).toFixed(1)}`).join(" ") });
    const sh = shaped();
    svg.append(line(ordered(), "stock"), line(sh, "tuned"));
    if (st.cap > 0) {
      const y = Y(a, st.cap);
      svg.append(s("line", { class: "cap", x1: L, x2: W() - R, y1: y, y2: y }), s("line", { class: "cap-hit", "data-cap": "1", x1: L, x2: W() - R, y1: y, y2: y }),
        s("g", { class: "cap-tag" }, s("rect", { x: L + 6, y: y - 24, width: 98, height: 20 }), s("text", { x: L + 55, y: y - 10, "text-anchor": "middle" }, `CAP ${st.cap} MHz`)));
    }
    sh.forEach((p, i) => svg.append(s("circle", { class: `pt ${i === hover || (drag?.kind === "pt" && drag.i === i) ? "hot" : ""}`, cx: X(a, p.volt), cy: Y(a, p.clock), r: 4.5, "data-i": i })));
    if (st.liveClock != null && st.liveVolt != null) {
      const x = X(a, st.liveVolt), y = Y(a, st.liveClock);
      svg.append(s("circle", { class: "livering", cx: x, cy: y, r: 9 }), s("circle", { class: "livept", cx: x, cy: y, r: 4 }));
    }
    const tipI = drag?.kind === "pt" ? drag.i : hover;
    if (tipI >= 0 && sh[tipI]) {
      const p = sh[tipI], x = Math.min(Math.max(X(a, p.volt) - 80, L), W() - R - 160), y = Math.max(T, Y(a, p.clock) - 36);
      svg.append(s("g", { class: "tip" }, s("rect", { x, y, width: 160, height: 22 }), s("text", { x: x + 80, y: y + 15, "text-anchor": "middle" }, `${p.volt.toFixed(3)} V  ·  ${Math.round(p.clock)} MHz`)));
    }
  }

  svg.addEventListener("pointerdown", (e) => {
    if (!st || st.locked || !st.stock || st.stock.length < 2) return;
    frozen = axes(st.stock);
    const target = e.target;
    if (e.ctrlKey) drag = { kind: "curve", y0: e.offsetY, off0: st.offset };
    else if (target.dataset.i !== undefined) drag = { kind: "pt", i: +target.dataset.i };
    else if (target.dataset.cap) drag = { kind: "cap" };
    else { frozen = null; return; }
    svg.setPointerCapture(e.pointerId); draw();
  });
  svg.addEventListener("pointermove", (e) => {
    if (!drag) {
      const i = e.target.dataset?.i !== undefined ? +e.target.dataset.i : -1;
      if (i !== hover) { hover = i; draw(); }
      return;
    }
    const a = frozen, clock = Math.round(clockOfY(a, e.offsetY) / 15) * 15;
    const lim = st.limits;
    if (drag.kind === "cap") st.cap = Math.min(Math.max(clock, lim.clockMin), lim.clockMax);
    else if (drag.kind === "curve") st.offset = Math.min(Math.max(drag.off0 + Math.round((clockOfY(a, e.offsetY) - clockOfY(a, drag.y0)) / 15) * 15, lim.coreMin), lim.coreMax);
    else { const p = pin(st.stock, ordered()[drag.i].volt, Math.min(Math.max(clock, lim.clockMin), lim.clockMax)); if (p) { st.offset = Math.min(Math.max(p.offset, lim.coreMin), lim.coreMax); st.cap = p.cap; } }
    draw(); onChange(st.offset, st.cap, false);
  });
  const end = () => { if (!drag) return; drag = null; frozen = null; draw(); onChange(st.offset, st.cap, true); };
  svg.addEventListener("pointerup", end); svg.addEventListener("lostpointercapture", end);
  svg.addEventListener("pointerleave", () => { if (!drag && hover !== -1) { hover = -1; draw(); } });
  new ResizeObserver(draw).observe(svg);
  return { el: svg, set(next) { if (drag) { st.liveClock = next.liveClock; st.liveVolt = next.liveVolt; return; } st = next; draw(); } };
}

// Nothing that changes the card's clocks, voltage curve or power starts before the owner has read what it can cost and said yes: the
// app keeps to the range the driver allows, but no range makes every card safe. An automatic search asks every time (it takes the card to
// the edge of stability on purpose); settings applied by hand ask once in a session of the app, and say that nothing has tested them.
let manualAccepted = false;
function acceptRisk(kind) {
  if (kind === "manual" && manualAccepted) return Promise.resolve(true);
  return new Promise((resolve) => {
    let yes = false;
    const go = h("button", { class: "btn primary", type: "button", disabled: true, onclick: () => { yes = true; d.close(); } }, t("Tuning_Risk_Continue"));
    const agree = h("input", { type: "checkbox", class: "check", onchange: (e) => { go.disabled = !e.target.checked; } });
    const d = h("dialog", { class: "sheet risk" },
      h("header", { class: "sheet-head" }, h("span", { class: "risk-ico" }, icon("alert")), h("div", { class: "ttl" }, h("h2", { class: "panel-title" }, t("Tuning_Risk_Title")))),
      h("div", { class: "sheet-body" },
        h("p", {}, t("Tuning_Risk_Text")),
        h("ul", { class: "risk-list" }, ["Tuning_Risk_Point_Range", "Tuning_Risk_Point_Crash", "Tuning_Risk_Point_Damage", "Tuning_Risk_Point_Temporary"].map((k) => h("li", {}, t(k)))),
        h("p", { class: "risk-kind" }, t(kind === "manual" ? "Tuning_Risk_Manual" : "Tuning_Risk_Auto")),
        h("label", { class: "risk-agree" }, agree, t("Tuning_Risk_Agree")),
        h("div", { class: "btn-row" }, go, h("button", { class: "btn", type: "button", onclick: () => d.close() }, t("Tuning_Risk_Cancel")))));
    d.addEventListener("close", () => { d.remove(); if (yes && kind === "manual") manualAccepted = true; resolve(yes); });
    document.body.append(d); d.showModal();
  });
}

export function mount(el) {
  const set = (field, value, extra = {}) => call("tuning.set", { field, value, ...extra });
  const exec = (cmd, extra = {}) => call("tuning.exec", { cmd, ...extra });
  const risky = (kind, cmd, extra = {}) => acceptRisk(kind).then((yes) => { if (yes) exec(cmd, extra); });
  const unavailable = h("div", { class: "banner", hidden: true });
  const name = h("span", { class: "lat" }), device = h("select", { class: "field", hidden: true, onchange: (e) => set("device", e.target.value) });
  const live = h("div", { class: "live" }), ranges = h("p", { class: "caption", style: { marginTop: "10px" } }), others = h("p", { class: "caption" });
  let sendTimer = 0;
  const editor = curveEditor((offset, cap, final) => {
    clearTimeout(sendTimer);
    const send = () => set("curve", "", { core: String(offset), cap: String(cap) });
    if (final) send(); else sendTimer = setTimeout(send, 90);
  });
  const curveInfo = h("p", { class: "caption", style: { whiteSpace: "pre-line" } }), estimate = h("div", { class: "estimate", hidden: true }), curveStatus = h("p", { class: "h3", style: { marginTop: "12px" } });
  const scan = h("button", { class: "btn", title: t("Tuning_Curve_Scan_Hint"), onclick: () => exec("scanCurve") }, t("Tuning_Curve_Scan"));

  // Manual controls: a slider and an exact field per setting, and a switch for the settings that are off unless turned on.
  const f = {};
  function slider(key, labelKey, unit, sw = null, stepVal = 1) {
    const range = h("input", { type: "range", class: "range", step: String(stepVal), oninput: (e) => { fill(range); set(key, e.target.value); } });
    const box = h("input", { class: "field lat short", oninput: (e) => set(key, e.target.value) });
    const toggle = sw ? h("input", { type: "checkbox", class: "switch", onchange: (e) => set(sw, e.target.checked) }) : null;
    f[key] = { range, box, toggle, sw };
    return h("div", { class: "slider-row" },
      h("div", { class: "top" }, h("label", {}, toggle, t(labelKey)), h("span", {}, box, h("span", { class: "unit" }, unit))), range);
  }
  const fill = (r) => r.style.setProperty("--fill", `${((r.value - r.min) / Math.max(1, r.max - r.min)) * 100}%`);
  const manual = h("div", { class: "manual" },
    h("h3", { class: "h3", style: { fontSize: "17px", fontWeight: 900 } }, t("Tuning_Manual")),
    h("p", { class: "caption" }, t("Tuning_Manual_Note")),
    slider("core", "Tuning_Label_CoreOffset", "MHz", null, 15), slider("memory", "Tuning_Label_MemoryOffset", "MHz", null, 50),
    slider("maxClock", "Tuning_Label_MaxClock", "MHz", "lockClock", 15), slider("power", "Tuning_Label_PowerLimit", "W", "setPower"), slider("fan", "Tuning_Label_Fan", "%", "manualFan"),
    h("div", { class: "toolbar", style: { marginTop: "18px" } },
      h("button", { class: "btn primary", id: "tApply", onclick: () => exec("apply") }, t("Tuning_Apply")), h("button", { class: "btn stop", id: "tReset", onclick: () => exec("reset") }, t("Tuning_Reset"))),
    h("div", { class: "toolbar" }, (f.profileName = h("input", { class: "field", placeholder: t("Tuning_ProfileNameHint"), style: { flex: 1 }, oninput: (e) => set("profileName", e.target.value) })),
      h("button", { class: "btn", onclick: () => exec("saveProfile") }, t("Tuning_SaveProfile"))),
    h("p", { class: "h3", id: "tStatus" }));

  const running = h("div", { hidden: true, style: { marginTop: "18px" } });
  const result = h("p", { class: "h3", style: { whiteSpace: "pre-line", marginTop: "16px" } });
  const log = h("div", {}), profiles = h("div", {}), memory = h("dl", { class: "kv" });
  const autoU = h("button", { class: "btn go", "data-a": "autoundervolt", onclick: () => risky("auto", "autoUndervolt") }, t("Tuning_AutoUndervolt"));
  const autoP = h("button", { class: "btn primary", "data-a": "autooverclockplus", onclick: () => risky("auto", "autoOverclockPlus") }, t("Tuning_AutoOverclockPlus"));
  const autoO = h("button", { class: "btn primary", "data-a": "autooverclock", onclick: () => risky("auto", "autoOverclock") }, t("Tuning_AutoOverclock"));
  const sceneBtn = h("button", { class: "btn", title: t("Tuning_SceneTest_Hint"), onclick: () => exec("sceneTest") }, t("Tuning_SceneTest"));
  const sceneClear = h("button", { class: "btn", onclick: () => exec("clearScene") }, t("Tuning_SceneTest_Clear"));
  // How long the scene test runs and whether it draws the ray-traced picture; both are the view model's, so a reopened page shows what was set.
  const sceneSecs = h("input", { class: "field num", type: "number", min: 20, max: 600, step: 10, style: { width: "84px" }, "aria-label": t("Tuning_SceneTest_Seconds"), onchange: (e) => set("sceneSeconds", e.target.value) });
  const sceneRt = h("input", { type: "checkbox", class: "switch", onchange: (e) => set("sceneRt", e.target.checked) });
  const sceneOpts = h("div", { class: "toolbar" }, h("label", {}, t("Tuning_SceneTest_Seconds"), " ", sceneSecs), h("label", { title: t("Tuning_SceneTest_RtHint") }, sceneRt, " ", t("Test_Option_RayTracing")));
  const sceneList = h("div", {});
  // Automatic profiles: which saved profile goes with a game and with each listed program (the tray follows them).
  const rule = (op, extra = {}) => call("tuning.rules", { op, ...extra });
  const gameSel = h("select", { class: "field", onchange: (e) => rule("game", { profile: e.target.value }) });
  const rulesList = h("div", {}), programSel = h("select", { class: "field", hidden: true }), programFind = h("input", { class: "field", placeholder: t("Tuning_Rules_Search"), hidden: true });
  const exeBox = h("input", { class: "field lat", placeholder: "Lumion.exe", style: { maxWidth: "220px" } });
  let programs = [], profileNames = [];
  const fillPrograms = () => {
    const q = programFind.value.trim().toLowerCase();
    programSel.replaceChildren(...programs.filter((p) => !q || p.name.toLowerCase().includes(q) || p.exe.toLowerCase().includes(q)).map((p) => h("option", { value: p.exe, "data-name": p.name }, `${p.name}  ·  ${p.exe}`)));
  };
  programFind.addEventListener("input", fillPrograms);
  const loadPrograms = h("button", { class: "btn", onclick: (e) => {
    const b = e.currentTarget; b.disabled = true; b.textContent = t("Tuning_Rules_Loading");
    call("tuning.programs").then((list) => { programs = list; fillPrograms(); programSel.hidden = programFind.hidden = false; })
      .finally(() => { b.disabled = false; b.textContent = t("Tuning_Rules_Pick"); });
  } }, t("Tuning_Rules_Pick"));
  const addRule = (exe, name) => { if (exe && profileNames.length) { rule("add", { exe, name: name || exe, profile: profileNames[0] }); exeBox.value = ""; } };
  const addPicked = h("button", { class: "btn primary", onclick: () => { const o = programSel.selectedOptions[0]; if (o) addRule(o.value, o.dataset.name); } }, t("Tuning_Rules_Add"));
  const addTyped = h("button", { class: "btn", onclick: () => addRule(exeBox.value.trim(), "") }, t("Tuning_Rules_Add"));
  const cancel = h("button", { class: "btn stop", onclick: () => exec("cancel") }, icon("stop"), t("Tuning_Cancel"));

  el.append(
    h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Tuning")), h("p", { class: "page-lede" }, t("Tuning_Note")))),
    unavailable,
    h("div", { class: "banner risk-banner", role: "note", "data-a": "tuning-risk" }, h("span", { class: "risk-ico" }, icon("alert")), h("div", { class: "grow" }, h("b", {}, t("Tuning_Risk_Banner_Title")), h("p", {}, t("Tuning_Risk_Banner")))),
    h("div", { class: "has-device panels", style: { gridTemplateColumns: "1fr", marginTop: 0 } },
      box({ kind: "Gpu", title: name, sub: "NVIDIA", i: 0, a: "fan", actions: device, body: [live, ranges, others] }),
      box({ kind: "Gpu", ico: "chart", title: t("Tuning_Curve_Title"), sub: t("Tuning_Curve_Sub"), i: 1, a: "curve", actions: scan,
        body: h("div", { class: "tune", style: { marginTop: 0 } },
          h("div", { class: "curve-wrap" },
            curveInfo, editor.el,
            h("div", { class: "legend" }, h("span", {}, h("i", { style: { background: "var(--paper-3)" } }), t("Tuning_Curve_LegendStock")),
              h("span", {}, h("i", { style: { background: "var(--hue)", height: "3px" } }), t("Tuning_Curve_LegendTuned")),
              h("span", {}, h("i", { style: { background: "var(--paper)", width: "9px", height: "9px" } }), t("Tuning_Curve_LegendLive"))),
            h("p", { class: "caption", style: { marginTop: "8px", maxWidth: "80ch" } }, t("Tuning_Curve_Hint")), estimate, curveStatus),
          manual) }),
      box({ kind: "Power", ico: "bolt", title: t("Tuning_Auto"), sub: t("Tuning_Auto_Sub"), i: 2, actions: cancel,
        body: [h("p", { class: "caption", style: { maxWidth: "90ch", marginTop: 0 } }, t("Tuning_Auto_Note")),
          h("div", { class: "auto-pair" },
            h("div", {}, h("div", { class: "h3" }, t("Tuning_AutoUndervolt_Title")), h("p", { class: "caption" }, t("Tuning_AutoUndervolt_Desc")), autoU),
            h("div", {}, h("div", { class: "h3", style: { color: "var(--hue)" } }, t("Tuning_AutoOverclock_Title")), h("p", { class: "caption" }, t("Tuning_AutoOverclock_Desc")), autoO),
            h("div", {}, h("div", { class: "h3", style: { color: "var(--hue)" } }, t("Tuning_AutoOverclockPlus_Title")), h("p", { class: "caption" }, t("Tuning_AutoOverclockPlus_Desc")), autoP)),
          running, result, log] }),
      box({ kind: "Gpu", ico: "chart", title: t("Tuning_SceneTest_Title"), sub: t("Tuning_SceneTest_Sub"), i: 3, a: "scene", actions: h("div", { class: "toolbar" }, sceneOpts, sceneBtn, sceneClear), body: [sceneList] }),
      box({ kind: "Power", ico: "bolt", title: t("Tuning_Rules_Title"), sub: t("Tuning_Rules_Sub"), i: 3, a: "rules",
        body: [h("p", { class: "caption", style: { maxWidth: "90ch", marginTop: 0 } }, t("Tuning_Rules_Note")),
          h("div", { class: "toolbar" }, h("b", {}, t("Tuning_Rules_Game")), gameSel),
          h("div", { class: "h3", style: { marginTop: "14px" } }, t("Tuning_Rules_Apps")), rulesList,
          h("div", { class: "toolbar", style: { marginTop: "10px", flexWrap: "wrap" } }, loadPrograms, programFind, programSel, addPicked),
          h("div", { class: "toolbar", style: { flexWrap: "wrap" } }, h("span", { class: "caption" }, t("Tuning_Rules_Exe")), exeBox, addTyped)] }),
      box({ kind: "System", ico: "doc", title: t("Tuning_Profiles"), sub: t("Tuning_Profiles_Note"), i: 3, a: "profiles", body: [h("p", { class: "caption", style: { maxWidth: "90ch" } }, t("Tuning_Legend")), profiles] })),
    h("div", { class: "panels", style: { gridTemplateColumns: "1fr" } },
      box({ kind: "Memory", title: t("Tuning_Memory"), sub: t("Tuning_Memory_Sub"), i: 4, a: "memory",
        actions: h("button", { class: "btn stop", onclick: () => exec("firmware") }, t("Tuning_RestartToFirmware")),
        body: [memory, h("p", { class: "note" }, t("Tuning_Memory_Note"))] })));

  let shownLog = -1, shownProfiles = "", shownRules = "";
  function update(x) {
    unavailable.hidden = !x.unavailable; unavailable.textContent = x.unavailable || "";
    el.querySelector(".has-device").hidden = !x.hasDevice;
    name.textContent = x.name || ""; ranges.textContent = x.ranges || ""; others.textContent = x.otherGpus || "";
    device.hidden = x.devices.length < 2;
    if (device.options.length !== x.devices.length) device.replaceChildren(...x.devices.map((d, i) => h("option", { value: i }, d)));
    device.value = x.device;
    live.replaceChildren(...x.live.map((tile) => h("div", {}, h("div", { class: "caption" }, tile.label),
      h("div", { class: `v ${tile.value === t("Value_NotAvailable") ? "missing" : ""}` }, tile.value))));
    const L = x.limits, form = x.form;
    const conf = { core: [L.coreMin, L.coreMax, form.core, L.hasCore], memory: [L.memoryMin, L.memoryMax, form.memory, L.hasMemory],
      maxClock: [L.clockMin, L.clockMax, form.maxClock, true], power: [L.powerMin, L.powerMax, form.power, L.hasPower], fan: [L.fanMin, L.fanMax, form.fan, L.hasFan] };
    for (const [k, [min, max, v, has]] of Object.entries(conf)) {
      const c = f[k]; c.range.min = min; c.range.max = max;
      if (document.activeElement !== c.range) c.range.value = v; fill(c.range); setField(c.box, v);
      const on = c.sw ? form[c.sw] : true;
      if (c.toggle) { c.toggle.checked = on; c.toggle.disabled = !has; }
      c.range.disabled = c.box.disabled = !has || !on || x.busy;
    }
    setField(f.profileName, form.profileName);
    el.querySelector("#tStatus").textContent = x.status || "";
    el.querySelector("#tApply").disabled = el.querySelector("#tReset").disabled = x.busy;
    editor.set({ stock: x.curve, offset: form.coreValue, cap: form.capValue, liveClock: x.liveClock, liveVolt: x.liveVolt, limits: L, locked: x.busy });
    // The chart draws the core's curve only; what a profile sets besides it (memory, power) is said beside it, so a profile that moves only those is not mistaken for stock.
    const extra = [form.memory ? `${t("Tuning_Label_MemoryOffset")} ${form.memory > 0 ? "+" : ""}${form.memory} MHz` : null, form.setPower ? `${t("Tuning_Label_PowerLimit")} ${form.power} W` : null].filter(Boolean).join(" · ");
    curveInfo.textContent = [x.curveInfo, extra].filter(Boolean).join("\n"); estimate.hidden = !x.curveEstimate; estimate.textContent = x.curveEstimate || ""; curveStatus.textContent = x.curveStatus || "";
    scan.disabled = autoU.disabled = autoO.disabled = autoP.disabled = x.busy;   // (Overclock Plus too: a second search started mid-run would fight the first over the card) cancel.disabled = !x.busy;
    running.hidden = !x.busy;
    running.replaceChildren(h("div", { class: "toolbar" }, h("span", { class: "h3" }, x.stepTitle || x.curveStatus || ""), x.stepLoad ? h("span", { class: "pill run" }, x.stepLoad) : null, h("span", { class: "caption" }, x.stepSettings || "")),
      h("div", { class: "progress" }, h("i", { style: { "--p": (x.percent || 0) / 100 } })));
    result.textContent = x.result || "";
    if (x.log.length !== shownLog) {
      shownLog = x.log.length;
      log.replaceChildren(...x.log.map((l) => h("div", { class: "logline" }, h("span", { class: "st" }, fa(l.step)),
        h("span", {}, h("b", {}, l.kind), "  ", h("span", { class: "caption" }, l.settings)),
        h("span", { class: `pill ${l.clean ? "pass" : "fail"}` }, l.clean ? t("Tuning_Log_Clean") : l.problem), h("span", { class: "res" }, l.result))));
    }
    sceneBtn.disabled = x.busy; sceneClear.hidden = !(x.sceneTests || []).length;
    sceneSecs.disabled = sceneRt.disabled = x.busy; if (document.activeElement !== sceneSecs) sceneSecs.value = x.sceneSeconds; sceneRt.checked = !!x.sceneRt;
    sceneList.replaceChildren(...((x.sceneTests || []).length ? x.sceneTests.map((r, i) => h("div", { class: "logline" }, h("span", { class: "st" }, fa(i + 1)),
      h("span", {}, h("b", {}, r.settings), "  ", h("span", { class: "caption lat" }, r.result)), h("span", { class: `pill ${r.clean ? "pass" : "fail"}` }, r.clean ? (r.change || t("Tuning_SceneTest_First")) : r.problem)))
      : [h("p", { class: "caption" }, t("Tuning_SceneTest_Empty"))]));
    // The rules' pickers are rebuilt only when what they show changes: this runs every second, and a list rebuilt under an open dropdown would close it.
    profileNames = [...new Set(x.profiles.map((p) => p.name))];
    const rk = JSON.stringify([x.rules, profileNames]);
    if (rk !== shownRules) {
      shownRules = rk;
      const opts = (cur) => [h("option", { value: "" }, t("Tuning_Rules_None")), ...profileNames.map((n) => h("option", { value: n }, n))].map((o) => { o.selected = o.value === cur; return o; });
      gameSel.replaceChildren(...opts(x.rules.game)); gameSel.value = x.rules.game;
      rulesList.replaceChildren(...(x.rules.apps.length ? x.rules.apps.map((a) => {
        const sel = h("select", { class: "field", onchange: (e) => rule("profile", { exe: a.exe, profile: e.target.value }) }, ...profileNames.map((n) => h("option", { value: n }, n)));
        if (!profileNames.includes(a.profile)) sel.prepend(h("option", { value: a.profile }, a.profile)); sel.value = a.profile;
        return h("div", { class: "toolbar", style: { padding: "4px 0" } }, h("b", {}, a.name), h("span", { class: "caption lat" }, a.exe), sel,
          h("button", { class: "btn stop", onclick: () => rule("remove", { exe: a.exe }) }, t("Tuning_Delete")));
      }) : [h("p", { class: "caption" }, t("Tuning_Rules_Empty"))]));
    }
    const pk = JSON.stringify(x.profiles);
    if (pk !== shownProfiles) {
      shownProfiles = pk;
      profiles.replaceChildren(...(x.profiles.length ? x.profiles.map((p) => h("div", { class: `report ${p.startup ? "active" : ""}`, style: { gridTemplateColumns: "auto 1fr auto" } },
        h("span", { class: `pill ${p.kind === "Manual" ? "none" : "run"}` }, p.kindText),
        h("span", {}, h("b", {}, p.name), "  ", h("span", { class: "caption lat" }, p.created), p.startup ? [" ", h("span", { class: "pill pass", title: t("Tuning_Startup_Hint") }, t("Tuning_Startup"))] : null),
        h("div", { class: "acts", style: { gridColumn: 3, gridRow: "1 / 3" } },
          h("button", { class: "btn", title: t("Tuning_LoadProfile_Hint"), onclick: () => exec("loadProfile", { index: String(p.index) }) }, t("Tuning_LoadProfile")),
          h("button", { class: "btn primary", onclick: () => exec("applyProfile", { index: String(p.index) }) }, t("Tuning_Apply")),
          h("button", { class: "btn stop", onclick: () => exec("deleteProfile", { index: String(p.index) }) }, t("Tuning_Delete"))),
        h("span", { class: "sum", style: { gridColumn: 2 } }, p.summary, p.evidence ? h("br") : null, p.evidence)))
        : [h("p", { class: "caption" }, t("Tuning_Profiles_Empty"))]));
    }
    memory.replaceChildren(...x.memory.flatMap((m) => [h("dt", { class: "lat" }, m.label), h("dd", {}, m.value)]));
  }
  call("tuning.visible", { value: true });
  call("tuning.state").then(update);
  const off = on("tuning", update);
  return () => { off(); call("tuning.visible", { value: false }); };
}
