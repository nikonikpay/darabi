// Small DOM helpers and the few drawn pieces: rolling numerals, the missing-value hatch, icons, the history chart.
import { t } from "./i18n.js";

export function h(tag, attrs = {}, ...kids) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v === null || v === undefined || v === false) continue;
    if (k === "class") el.className = v;
    // Custom properties (--i, --p) need setProperty: assigning them to the style object is silently ignored.
    else if (k === "style" && typeof v === "object") for (const [p, x] of Object.entries(v)) { if (p.startsWith("--")) el.style.setProperty(p, x); else el.style[p] = x; }
    else if (k.startsWith("on")) el.addEventListener(k.slice(2), v);
    else if (k === "html") el.innerHTML = v;
    else if (v === true) el.setAttribute(k, "");
    else el.setAttribute(k, v);
  }
  for (const kid of kids.flat(Infinity)) if (kid !== null && kid !== undefined && kid !== false) el.append(kid instanceof Node ? kid : document.createTextNode(String(kid)));
  return el;
}

export function clear(el) { while (el.firstChild) el.firstChild.remove(); return el; }

// A Latin value; a missing one is the hatch with the words, never a number.
export function val(text, cls = "num") {
  return text === null || text === undefined ? h("span", { class: "hatch", title: t("Value_NotAvailable") }, t("Value_NotAvailable")) : h("span", { class: cls }, text);
}

const ICONS = {
  arrow: "M19 12H5m6-6-6 6 6 6",
  play: "M7 5v14l11-7z",
  stop: "M6 6h12v12H6z",
  doc: "M7 3h7l5 5v13H7zM14 3v5h5",
  folder: "M3 7h6l2 2h10v10H3z",
  eye: "M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12zm10 3a3 3 0 1 0 0-6 3 3 0 0 0 0 6z",
  x: "M6 6l12 12M18 6 6 18",
  pause: "M8 5v14M16 5v14",
  cpu: "M7 7h10v10H7zM10 10h4v4h-4zM9 3v4M15 3v4M9 17v4M15 17v4M3 9h4M3 15h4M17 9h4M17 15h4",
  gpu: "M2 6h20v11H2zM5 17v3M9 17v3M9.5 11.5a2.5 2.5 0 1 0 5 0 2.5 2.5 0 1 0-5 0M17 9.5h2M17 13.5h2",
  ram: "M3 7h18v9H3zM6 16v3M10 16v3M14 16v3M18 16v3M7 10h2v3H7zM11 10h2v3h-2zM15 10h2v3h-2z",
  drive: "M3 13h18v6H3zM3 13l3-8h12l3 8M17 16h.01M7 16h5",
  net: "M12 3a9 9 0 1 0 0 18 9 9 0 1 0 0-18M3 12h18M12 3c3.5 3.5 3.5 14.5 0 18M12 3c-3.5 3.5-3.5 14.5 0 18",
  board: "M4 4h16v16H4zM8 8h5v5H8zM16 8v2M16 13v2M8 16h8",
  win: "M3 5h18v11H3zM8 20h8M12 16v4",
  bolt: "M13 2 4 14h7l-1 8 9-12h-7z",
  chevron: "M6 9l6 6 6-6",
  chart: "M4 4v16h16M7 15l4-5 3 3 5-6",
  popout: "M14 4h6v6M20 4l-8 8M18 14v6H4V6h6",
  shop: "M5 8h14l-1 12H6zM9 8V6a3 3 0 0 1 6 0v2",
  phone: "M5 4h4l2 5-2.5 1.5a11 11 0 0 0 5 5L15 13l5 2v4a2 2 0 0 1-2 2A16 16 0 0 1 3 6a2 2 0 0 1 2-2",
  chat: "M4 5h16v11H9l-5 4z",
  send: "M21 3 3 10.5l7 3 3 7.5zM10 13.5 21 3",
  mail: "M3 6h18v12H3zM3 6l9 7 9-7",
  clock: "M12 3a9 9 0 1 0 0 18 9 9 0 1 0 0-18M12 7v5l3 3",
  pin: "M12 21s-7-6.5-7-12a7 7 0 0 1 14 0c0 5.5-7 12-7 12zM12 11.5a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5",
  refresh: "M20 11a8 8 0 0 0-14.9-3M4 4v4h4M4 13a8 8 0 0 0 14.9 3M20 20v-4h-4",
  camera: "M4 4h16v16H4zM12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8M16.5 7.5h.01",
  bug: "M8 8a4 4 0 0 1 8 0v8a4 4 0 0 1-8 0zM8 12H3M21 12h-5M5 6l3 2M19 6l-3 2M5 19l3-3M19 19l-3-3",
  overlay: "M3 5h18v14H3zM6 8h6v4H6z",
  gamepad: "M7 8h10a5 5 0 0 1 5 5v1.5a2.5 2.5 0 0 1-4.6 1.4L16 14H8l-1.4 1.9A2.5 2.5 0 0 1 2 14.5V13a5 5 0 0 1 5-5zM8 10.5v3M6.5 12h3M15.5 11h.01M17.5 13h.01",
  check: "M5 12.5l4.5 4.5L19 7.5",
  trophy: "M8 4h8v5a4 4 0 0 1-8 0zM8 6H4v2a4 4 0 0 0 4 4M16 6h4v2a4 4 0 0 1-4 4M12 13v4M8 21h8M10 17h4v4h-4z",
  home: "M4 10.5 12 4l8 6.5V20h-5v-6H9v6H4z",
  pulse: "M3 12h4l2.5-6 4 12 2.5-6H21",
  flask: "M9 3h6M10 3v6L4.5 18.5A1.7 1.7 0 0 0 6 21h12a1.7 1.7 0 0 0 1.5-2.5L14 9V3M7 15h10",
  sliders: "M4 6h10M18 6h2M4 12h4M12 12h8M4 18h12M20 18h0M14 4v4M8 10v4M16 16v4",
  gear: "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z",
  menu: "M4 5h16v14H4zM15 5v14",
  temp: "M14 14.8V5a2 2 0 0 0-4 0v9.8a4 4 0 1 0 4 0zM12 9v7",
};
export function icon(name) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24"); svg.setAttribute("fill", "none"); svg.setAttribute("stroke-width", "2");
  svg.setAttribute("stroke-linecap", "round"); svg.setAttribute("stroke-linejoin", "round"); svg.setAttribute("aria-hidden", "true");
  const p = document.createElementNS("http://www.w3.org/2000/svg", "path"); p.setAttribute("d", ICONS[name] || ""); svg.append(p);
  return svg;
}

// Registration marks: the print crosses at a plane's corners.
export function regMarks() {
  return ["tl", "tr", "bl", "br"].map((c) => {
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("viewBox", "0 0 18 18"); svg.setAttribute("class", `reg ${c}`); svg.setAttribute("aria-hidden", "true");
    svg.innerHTML = '<circle cx="9" cy="9" r="4.5" fill="none"/><path d="M9 0v18M0 9h18"/>';
    return svg;
  });
}

// Rolling numerals: a digit column per character that slides to its value. Only a changed digit moves, so a steady reading stays still.
export function roll(el, text) {
  if (text === null || text === undefined) { el.dataset.v = ""; clear(el); el.append(t("Value_NotAvailable")); el.classList.add("missing"); return; }
  el.classList.remove("missing");
  const chars = String(text).split("");
  let box = el.querySelector(".roll");
  if (!box || box.childElementCount !== chars.length || el.dataset.v === "") { clear(el); box = h("span", { class: "roll" }); el.prepend(box); build(box, chars); }
  [...box.children].forEach((d, i) => {
    const c = chars[i];
    if (/\d/.test(c)) { if (!d.classList.contains("d")) return; d.firstChild.style.transform = `translateY(calc(-${+c} * var(--dh)))`; }
    else d.textContent = c;
  });
  el.dataset.v = text;
}
function build(box, chars) {
  for (const c of chars) {
    if (/\d/.test(c)) {
      const strip = h("span", {}, ...Array.from({ length: 10 }, (_, n) => h("b", {}, n)));
      strip.style.transform = "translateY(0)";
      box.append(h("span", { class: "d" }, strip));
    } else box.append(h("span", { class: "s" }, c));
  }
}

export function toast(text, kind = "") {
  const el = h("div", { class: `toast ${kind}` }, text);
  document.getElementById("toasts").append(el);
  setTimeout(() => el.remove(), 7000);
}

// A sensor's recorded history as a line over the chosen window, in its part's hue. Gaps (NaN/null) break the line. Drawn on a canvas at device pixels.
export function drawChart(canvas, series, windowSeconds, unit, color) {
  const dpr = window.devicePixelRatio || 1, w = canvas.clientWidth, hgt = canvas.clientHeight;
  canvas.width = w * dpr; canvas.height = hgt * dpr;
  const g = canvas.getContext("2d"); g.scale(dpr, dpr); g.clearRect(0, 0, w, hgt);
  const css = getComputedStyle(document.documentElement);
  const start = series.now - windowSeconds;
  const pts = []; for (let i = 0; i < series.sec.length; i++) if (series.sec[i] >= start) pts.push([series.sec[i], series.val[i]]);
  const vals = pts.map((p) => p[1]).filter((v) => v !== null);
  if (!vals.length) {
    g.fillStyle = css.getPropertyValue("--paper-3"); g.font = "600 14px Vazirmatn"; g.textAlign = "center";
    g.fillText(t("Value_NotAvailable"), w / 2, hgt / 2); return;
  }
  let lo = Math.min(...vals), hi = Math.max(...vals); if (hi - lo < 1e-6) { lo -= 1; hi += 1; }
  const pad = (hi - lo) * 0.12; lo -= pad; hi += pad;
  const L = 46, R = 8, T = 10, B = 20;
  const X = (s) => L + ((s - start) / windowSeconds) * (w - L - R), Y = (v) => T + (1 - (v - lo) / (hi - lo)) * (hgt - T - B);
  g.strokeStyle = css.getPropertyValue("--rule"); g.lineWidth = 1; g.fillStyle = css.getPropertyValue("--paper-3"); g.font = "500 11px Archivo"; g.textAlign = "right";
  for (let i = 0; i <= 4; i++) {
    const v = lo + ((hi - lo) * i) / 4, y = Y(v);
    g.beginPath(); g.moveTo(L, y); g.lineTo(w - R, y); g.stroke();
    g.fillText(v.toFixed(Math.abs(hi - lo) < 10 ? 1 : 0), L - 8, y + 4);
  }
  const line = (color || css.getPropertyValue("--yellow")).trim(), [r, gr, b] = rgb(line);
  const grad = g.createLinearGradient(0, T, 0, hgt - B); grad.addColorStop(0, `rgba(${r},${gr},${b},0.24)`); grad.addColorStop(1, `rgba(${r},${gr},${b},0)`);
  let seg = [];
  const flush = () => {
    if (seg.length > 1) {
      g.beginPath(); seg.forEach(([x, y], i) => (i ? g.lineTo(x, y) : g.moveTo(x, y)));
      g.lineTo(seg[seg.length - 1][0], hgt - B); g.lineTo(seg[0][0], hgt - B); g.closePath(); g.fillStyle = grad; g.fill();
      g.beginPath(); seg.forEach(([x, y], i) => (i ? g.lineTo(x, y) : g.moveTo(x, y))); g.strokeStyle = line; g.lineWidth = 2; g.lineJoin = "round"; g.stroke();
    }
    seg = [];
  };
  let prev = null;
  for (const [s, v] of pts) {
    if (v === null || (prev !== null && s - prev > 8)) flush();
    if (v !== null) seg.push([X(s), Y(v)]);
    prev = s;
  }
  flush();
}

function rgb(hex) {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex); if (!m) return [253, 212, 0];
  const n = parseInt(m[1], 16); return [n >> 16, (n >> 8) & 255, n & 255];
}
