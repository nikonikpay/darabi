// Small DOM helpers and the few drawn pieces: rolling numerals, the missing-value hatch, icons, the history chart.
import { t } from "./i18n.js";

export function h(tag, attrs = {}, ...kids) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v === null || v === undefined || v === false) continue;
    if (k === "class") el.className = v;
    else if (k === "style" && typeof v === "object") Object.assign(el.style, v);
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
};
export function icon(name) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24"); svg.setAttribute("fill", "none"); svg.setAttribute("stroke-width", "2");
  svg.setAttribute("stroke-linecap", "square"); svg.setAttribute("aria-hidden", "true");
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

// A sensor's recorded history as a line over the chosen window. Gaps (NaN/null) break the line. Drawn on a canvas at device pixels.
export function drawChart(canvas, series, windowSeconds, unit) {
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
  const yellow = css.getPropertyValue("--yellow").trim();
  const grad = g.createLinearGradient(0, T, 0, hgt - B); grad.addColorStop(0, "rgba(253,212,0,0.22)"); grad.addColorStop(1, "rgba(253,212,0,0)");
  let seg = [];
  const flush = () => {
    if (seg.length > 1) {
      g.beginPath(); seg.forEach(([x, y], i) => (i ? g.lineTo(x, y) : g.moveTo(x, y)));
      g.lineTo(seg[seg.length - 1][0], hgt - B); g.lineTo(seg[0][0], hgt - B); g.closePath(); g.fillStyle = grad; g.fill();
      g.beginPath(); seg.forEach(([x, y], i) => (i ? g.lineTo(x, y) : g.moveTo(x, y))); g.strokeStyle = yellow; g.lineWidth = 2; g.lineJoin = "round"; g.stroke();
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
