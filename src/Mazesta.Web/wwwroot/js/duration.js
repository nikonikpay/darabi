// A length of time as hours, minutes and seconds in three small fields, each with its picture, and arrows above and below to make it longer or shorter
// (also the arrow keys and the wheel over the field); the engine still takes whole seconds, so this only composes them.
import { t, fa } from "./i18n.js";
import { h, icon } from "./ui.js";

const DIGITS = { "۰": 0, "۱": 1, "۲": 2, "۳": 3, "۴": 4, "۵": 5, "۶": 6, "۷": 7, "۸": 8, "۹": 9, "٠": 0, "١": 1, "٢": 2, "٣": 3, "٤": 4, "٥": 5, "٦": 6, "٧": 7, "٨": 8, "٩": 9 };
const num = (s) => { const v = String(s).trim().replace(/[۰-۹٠-٩]/g, (c) => DIGITS[c]); return v === "" ? 0 : /^\d{1,6}$/.test(v) ? Number(v) : NaN; };

/** "1 h 5 min 30 s" in the page's words, for a length in whole seconds. */
export function lengthText(seconds) {
  const total = Number.parseInt(seconds, 10);
  if (!Number.isFinite(total) || total < 0) return "";
  const parts = [[Math.floor(total / 3600), "Test_Hours"], [Math.floor(total % 3600 / 60), "Test_Minutes"], [total % 60, "Test_Seconds"]].filter(([n]) => n > 0);
  return (parts.length ? parts : [[0, "Test_Seconds"]]).map(([n, k]) => `${fa(n)} ${t(k)}`).join(" ");
}

const MAX = 359999;   // 99 h 59 min 59 s: what three fields of two digits can say

/** onChange gets the whole length in seconds as text, or the raw text "x" when a field is not a number (the engine then shows its own error). */
export function durationField(onChange) {
  const input = (key) => h("input", { class: "field lat dur-part", inputmode: "numeric", "aria-label": t(key), oninput: send });
  const hh = input("Test_Hours"), mm = input("Test_Minutes"), ss = input("Test_Seconds");
  const total = () => { const n = [num(hh.value), num(mm.value), num(ss.value)]; return n.some(Number.isNaN) ? NaN : n[0] * 3600 + n[1] * 60 + n[2]; };
  function send() { const v = total(); onChange(Number.isNaN(v) ? "x" : String(v)); }
  // An arrow adds or takes one unit of its field to the whole length (59 seconds and one more is a minute), never below nothing.
  function step(unit, dir) {
    const v = total(); if (Number.isNaN(v)) return;
    fill(Math.min(MAX, Math.max(0, v + dir * unit))); send();
  }
  function part(box, key, pic, unit) {
    const arrow = (dir, name, label) => h("button", { class: "dur-arrow", type: "button", tabindex: "-1", title: t(key), "aria-label": `${label}: ${t(key)}`, onclick: () => step(unit, dir) }, icon(name));
    box.addEventListener("keydown", (e) => { if (e.key === "ArrowUp" || e.key === "ArrowDown") { e.preventDefault(); step(unit, e.key === "ArrowUp" ? 1 : -1); } });
    box.addEventListener("wheel", (e) => { if (document.activeElement !== box) return; e.preventDefault(); step(unit, e.deltaY < 0 ? 1 : -1); }, { passive: false });
    return h("span", { class: "dur-part-box", title: t(key) },
      h("span", { class: "dur-pic" }, icon(pic), h("small", {}, t(key))),
      arrow(1, "chevronup", "+"), box, arrow(-1, "chevron", "−"));
  }
  const el = h("span", { class: "dur" }, part(hh, "Test_Hours", "hourglass", 3600), part(mm, "Test_Minutes", "clock", 60), part(ss, "Test_Seconds", "stopwatch", 1));
  function fill(total) {
    const [a, b, c] = Number.isFinite(total) && total >= 0 ? [Math.floor(total / 3600), Math.floor(total % 3600 / 60), total % 60] : ["", "", ""];
    for (const [box, x] of [[hh, a], [mm, b], [ss, c]]) { const s = x === "" ? "" : x === 0 && !(box === ss && a === 0 && b === 0) ? "" : String(x); if (box.value !== s) box.value = s; }
  }
  // Shows the engine's whole seconds (never over a field being typed in).
  function set(v) {
    if (el.contains(document.activeElement)) return;
    fill(Number.parseInt(v, 10));
  }
  return { el, set };
}
