// Fan control of the board's outputs. Each output has three ways: automatic (the board's own control), a fixed duty, or a curve of temperature against duty
// that the app follows while it runs, drawn and dragged like the board makers' own fan programs. The curve takes its temperature from the processor, the
// graphics card or the hotter of the two; a ready-made one (silent, standard, performance, full speed) is a click. The page says what is held, the floor a
// duty never goes below, and that the board gets its fans back when the app closes.
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon } from "../ui.js";
import { box } from "../groups.js";

const NS = "http://www.w3.org/2000/svg";
const s = (tag, attrs = {}, ...kids) => { const e = document.createElementNS(NS, tag); for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, v); e.append(...kids.map((k) => (k instanceof Node ? k : document.createTextNode(String(k))))); return e; };
const T0 = 20, T1 = 100, MAXPTS = 8;

// The editable curve: points of (temperature, duty) on a grid; drag a point, double-click the line to add one, double-click a point to take it out.
function curveEditor({ onChange }) {
  const svg = s("svg", { class: "curve fan-curve", role: "img" });
  let pts = [], floor = 20, ceil = 100, now = null, drag = -1, readOnly = false;
  const W = () => svg.clientWidth || 640, H = () => 260, L = 40, R = 14, TT = 24, B = 28;
  const X = (temp) => L + ((temp - T0) / (T1 - T0)) * (W() - L - R), Y = (p) => H() - B - (p / 100) * (H() - TT - B);
  const tempAt = (x) => Math.round(T0 + ((x - L) / (W() - L - R)) * (T1 - T0)), dutyAt = (y) => Math.round(((H() - B - y) / (H() - TT - B)) * 100);
  const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
  const rel = (e) => { const r = svg.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };

  function draw() {
    svg.replaceChildren(); svg.setAttribute("viewBox", `0 0 ${W()} ${H()}`);
    const grid = s("g", { class: "grid" }), axis = s("g", { class: "axis" });
    for (let p = 0; p <= 100; p += 25) { grid.append(s("line", { x1: L, x2: W() - R, y1: Y(p), y2: Y(p) })); axis.append(s("text", { x: L - 6, y: Y(p) + 4, "text-anchor": "end" }, `${p}%`)); }
    for (let c = T0; c <= T1; c += 10) { grid.append(s("line", { x1: X(c), x2: X(c), y1: TT, y2: H() - B })); axis.append(s("text", { x: X(c), y: H() - B + 16, "text-anchor": "middle" }, `${c}°`)); }
    svg.append(grid, axis, s("rect", { class: "fan-floor", x: L, y: Y(floor), width: W() - L - R, height: Math.max(0, H() - B - Y(floor)) }));
    if (!pts.length) return;
    const area = [[X(T0), Y(0)], [X(T0), Y(pts[0][1])], ...pts.map((p) => [X(p[0]), Y(p[1])]), [X(T1), Y(pts[pts.length - 1][1])], [X(T1), Y(0)]];
    svg.append(s("polygon", { class: "fan-area", points: area.map((a) => a.join(",")).join(" ") }),
      s("polyline", { class: "tuned", points: area.slice(1, -1).map((a) => a.join(",")).join(" ") }));
    if (now !== null && now >= T0 && now <= T1) {
      const duty = interpolate(pts, now);
      svg.append(s("line", { class: "fan-now", x1: X(now), x2: X(now), y1: TT, y2: H() - B }), s("circle", { class: "livept", cx: X(now), cy: Y(duty), r: 4.5 }), s("circle", { class: "livering", cx: X(now), cy: Y(duty), r: 8 }));
    }
    pts.forEach((p, i) => {
      const c = s("circle", { class: `pt ${i === drag ? "hot" : ""}`, cx: X(p[0]), cy: Y(p[1]), r: 5.5, tabindex: readOnly ? -1 : 0, "aria-label": `${p[0]}° → ${p[1]}%` });
      if (!readOnly) {
        c.addEventListener("pointerdown", (e) => { drag = i; svg.setPointerCapture(e.pointerId); e.preventDefault(); draw(); });
        c.addEventListener("dblclick", (e) => { e.stopPropagation(); if (pts.length > 2) { pts.splice(i, 1); draw(); onChange(pts, true); } });
        c.addEventListener("keydown", (e) => {
          const dy = e.key === "ArrowUp" ? 1 : e.key === "ArrowDown" ? -1 : 0, dx = e.key === "ArrowRight" ? 1 : e.key === "ArrowLeft" ? -1 : 0; if (!dx && !dy) return;
          e.preventDefault(); move(i, p[0] + dx, p[1] + dy); onChange(pts, true); svg.querySelectorAll(".pt")[i]?.focus();
        });
      }
      svg.append(c, s("text", { class: "fan-tag", x: X(p[0]), y: Y(p[1]) - 11, "text-anchor": "middle" }, `${p[1]}%`));
    });
  }
  const interpolate = (a, temp) => { if (temp <= a[0][0]) return a[0][1]; for (let i = 1; i < a.length; i++) if (temp <= a[i][0]) { const [x0, y0] = a[i - 1], [x1, y1] = a[i]; return x1 <= x0 ? y1 : y0 + ((y1 - y0) * (temp - x0)) / (x1 - x0); } return a[a.length - 1][1]; };
  // A point stays between its neighbours in temperature and never below the one before in duty, so the curve never dips as it gets hotter.
  function move(i, temp, duty) {
    const lo = i > 0 ? pts[i - 1][0] + 1 : T0, hi = i < pts.length - 1 ? pts[i + 1][0] - 1 : T1;
    const dlo = i > 0 ? pts[i - 1][1] : floor, dhi = i < pts.length - 1 ? pts[i + 1][1] : ceil;
    pts[i] = [clamp(Math.round(temp), lo, hi), clamp(Math.round(duty), Math.max(floor, dlo), Math.min(ceil, dhi))];
    draw();
  }
  svg.addEventListener("pointermove", (e) => { if (drag < 0) return; const [x, y] = rel(e); move(drag, tempAt(x), dutyAt(y)); onChange(pts, false); });
  const up = () => { if (drag < 0) return; drag = -1; draw(); onChange(pts, true); };
  svg.addEventListener("pointerup", up); svg.addEventListener("pointercancel", up);
  svg.addEventListener("dblclick", (e) => {
    if (readOnly || pts.length >= MAXPTS) return; const [x, y] = rel(e), temp = tempAt(x); if (pts.some((p) => Math.abs(p[0] - temp) < 3)) return;
    const next = [...pts, [temp, clamp(dutyAt(y), floor, ceil)]].sort((a, b) => a[0] - b[0]);
    for (let i = 1; i < next.length; i++) next[i][1] = Math.max(next[i][1], next[i - 1][1]);
    pts = next; draw(); onChange(pts, true);
  });
  new ResizeObserver(() => draw()).observe(svg);
  return { svg, set(points, f, c) { pts = points.map((p) => [p[0], p[1]]); floor = f; ceil = c; draw(); }, get: () => pts.map((p) => [...p]), live(temp) { now = temp; draw(); }, readOnly(v) { readOnly = v; svg.classList.toggle("ro", v); draw(); } };
}

export function mount(el) {
  const cards = new Map();   // channel id → its card, kept so the live values change without drawing the editors again
  let guard = 85, floor = 20, timer = 0, saveTimer = 0, last = null;
  const msg = h("p", { class: "msg" }), grid = h("div", { class: "panels two fan-cards" }), banner = h("div", { class: "banner", hidden: true });
  const temps = h("p", { class: "caption" });
  const resetAll = h("button", { class: "btn", type: "button", hidden: true, onclick: () => act("fans.reset") }, icon("refresh"), t("Fans_Reset"));

  const act = async (method, params) => {
    try { render(await call(method, params)); msg.textContent = ""; msg.className = "msg"; } catch (e) { msg.className = "msg fail"; msg.textContent = String(e.message || e); }
  };

  function build(c) {
    const mine = { mode: c.mode };
    const name = h("b", { class: "lat" }, c.name), live = h("span", { class: "fan-live" });
    const duty = h("span", { class: "fan-duty num" }), rpm = h("span", { class: "fan-rpm num" });
    const gauge = h("i"), bar = h("div", { class: "progress fan-bar" }, gauge);
    const modes = h("div", { class: "seg", role: "group", "aria-label": t("Fans_Mode") }, ["auto", "manual", "curve"].map((m) =>
      h("button", { type: "button", "data-mode": m, "aria-pressed": String(c.mode === m), onclick: () => choose(m) }, t(`Fans_Mode_${m}`))));
    const editor = curveEditor({ onChange: (points, final) => { clearTimeout(saveTimer); if (final) send(); else saveTimer = setTimeout(send, 400); } });
    editor.set(c.points, c.min, c.max);
    const source = h("select", { class: "field", "aria-label": t("Fans_Source"), onchange: () => send() }, ["cpu", "gpu", "max"].map((v) => h("option", { value: v }, t(`Fans_Src_${v}`))));
    source.value = c.source;
    const fixed = h("input", { type: "range", class: "range", min: c.min, max: 100, value: Math.max(c.min, Math.round(c.manual)), "aria-label": t("Fans_Duty") });
    const fixedOut = h("b", { class: "num" }, `${fixed.value}%`);
    fixed.addEventListener("input", () => { fixedOut.textContent = `${fixed.value}%`; });
    fixed.addEventListener("change", () => act("fans.set", { id: c.id, mode: "manual", percent: Number(fixed.value) }));
    const presets = h("div", { class: "fan-presets", role: "group", "aria-label": t("Fans_Presets") }, ["silent", "standard", "performance", "full"].map((p) =>
      h("button", { class: "btn", type: "button", onclick: () => { editor.set(last.presets[p].map((a) => a), floor, 100); source.value = source.value; send(); } }, t(`Fans_Preset_${p}`))));
    const curveBox = h("div", { class: "fan-curve-box" }, h("div", { class: "rgb-row" }, h("label", { class: "rgb-field" }, h("span", {}, t("Fans_Source")), source), presets), editor.svg, h("p", { class: "note" }, t("Fans_Curve_Hint")));
    const fixedBox = h("div", { class: "fan-fixed" }, h("span", {}, t("Fans_Duty")), fixed, fixedOut);
    const held = h("small", { class: "fan-held" });
    function send() { act("fans.set", { id: c.id, mode: "curve", source: source.value, points: editor.get() }); }
    function choose(m) {
      mine.mode = m; sync();
      if (m === "auto") act("fans.set", { id: c.id, mode: "auto" });
      else if (m === "manual") act("fans.set", { id: c.id, mode: "manual", percent: Number(fixed.value) });
      else send();
    }
    function sync() { modes.querySelectorAll("button").forEach((b) => b.setAttribute("aria-pressed", String(b.dataset.mode === mine.mode))); curveBox.hidden = mine.mode !== "curve"; fixedBox.hidden = mine.mode !== "manual"; }
    sync();
    const el = box({ cls: "p-cpu", ico: "fan", title: c.name, sub: c.part, extra: "fan-card", a: "fans",
      body: [h("div", { class: "fan-top" }, h("div", { class: "fan-read" }, duty, rpm), bar), modes, fixedBox, curveBox, held] });
    return { el, name, duty, rpm, gauge, held, editor, mine, source, sync, modes };
  }

  function render(s) {
    last = s; guard = s.guard; floor = s.floor;
    banner.hidden = s.supported; banner.textContent = s.supported ? "" : t("Fans_Unsupported");
    resetAll.hidden = !s.supported;
    temps.textContent = s.supported ? [s.cpu != null ? `${t("Fans_Src_cpu")} ${Math.round(s.cpu)}°` : "", s.gpu != null ? `${t("Fans_Src_gpu")} ${Math.round(s.gpu)}°` : ""].filter(Boolean).join("  ·  ") : "";
    const ids = new Set(s.channels.map((c) => c.id));
    for (const [id, c] of cards) if (!ids.has(id)) { c.el.remove(); cards.delete(id); }
    for (const ch of s.channels) {
      let card = cards.get(ch.id);
      if (!card) { card = build(ch); cards.set(ch.id, card); grid.append(card.el); }
      else if (card.mine.mode !== ch.mode) { card.mine.mode = ch.mode; card.sync(); }
      card.duty.textContent = ch.percent != null ? `${Math.round(ch.percent)}%` : "—"; card.rpm.textContent = ch.rpm != null ? `${Math.round(ch.rpm)} ${t("Fans_Rpm")}` : "";
      card.gauge.style.width = `${Math.min(100, ch.percent ?? 0)}%`; card.held.textContent = ch.held ? t("Fans_Held") : "";
      const temp = ch.source === "gpu" ? s.gpu : ch.source === "max" ? (s.cpu != null ? Math.max(s.cpu, s.gpu ?? s.cpu) : s.gpu) : s.cpu;
      card.editor.live(ch.mode === "curve" ? temp : null);
    }
  }

  const poll = () => { if (document.hidden) return; call("fans.state").then((s) => { if (!dragging()) render(s); }).catch(() => {}); };
  const dragging = () => !!el.querySelector(".fan-curve .pt.hot");

  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Fans")), h("p", { class: "page-lede" }, t("Fans_Lede")))),
    banner, h("div", { class: "btn-row", style: { marginTop: 0 } }, resetAll, h("span", { class: "grow" }), temps), msg, grid, h("p", { class: "note" }, t("Fans_Note")));
  call("fans.state").then(render).catch((e) => { msg.className = "msg fail"; msg.textContent = String(e.message || e); });
  timer = setInterval(poll, 2000);
  return () => { clearInterval(timer); clearTimeout(saveTimer); };
}
