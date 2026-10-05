// A length of time as hours, minutes and seconds in three small fields; the engine still takes whole seconds, so this only composes them.
import { t, fa } from "./i18n.js";
import { h } from "./ui.js";

const DIGITS = { "۰": 0, "۱": 1, "۲": 2, "۳": 3, "۴": 4, "۵": 5, "۶": 6, "۷": 7, "۸": 8, "۹": 9, "٠": 0, "١": 1, "٢": 2, "٣": 3, "٤": 4, "٥": 5, "٦": 6, "٧": 7, "٨": 8, "٩": 9 };
const num = (s) => { const v = String(s).trim().replace(/[۰-۹٠-٩]/g, (c) => DIGITS[c]); return v === "" ? 0 : /^\d{1,6}$/.test(v) ? Number(v) : NaN; };

/** "1 h 5 min 30 s" in the page's words, for a length in whole seconds. */
export function lengthText(seconds) {
  const total = Number.parseInt(seconds, 10);
  if (!Number.isFinite(total) || total < 0) return "";
  const parts = [[Math.floor(total / 3600), "Test_Hours"], [Math.floor(total % 3600 / 60), "Test_Minutes"], [total % 60, "Test_Seconds"]].filter(([n]) => n > 0);
  return (parts.length ? parts : [[0, "Test_Seconds"]]).map(([n, k]) => `${fa(n)} ${t(k)}`).join(" ");
}

/** onChange gets the whole length in seconds as text, or the raw text "x" when a field is not a number (the engine then shows its own error). */
export function durationField(onChange) {
  const part = (key) => h("input", { class: "field lat dur-part", inputmode: "numeric", "aria-label": t(key), oninput: send });
  const hh = part("Test_Hours"), mm = part("Test_Minutes"), ss = part("Test_Seconds");
  function send() {
    const n = [num(hh.value), num(mm.value), num(ss.value)];
    onChange(n.some(Number.isNaN) ? "x" : String(n[0] * 3600 + n[1] * 60 + n[2]));
  }
  const unit = (key) => h("span", { class: "dur-unit" }, t(key));
  const el = h("span", { class: "dur" }, hh, unit("Test_Hours"), mm, unit("Test_Minutes"), ss, unit("Test_Seconds"));
  // Shows the engine's whole seconds (never over a field being typed in).
  function set(v) {
    if (el.contains(document.activeElement)) return;
    const total = Number.parseInt(v, 10);
    const [a, b, c] = Number.isFinite(total) && total >= 0 ? [Math.floor(total / 3600), Math.floor(total % 3600 / 60), total % 60] : ["", "", ""];
    for (const [box, x] of [[hh, a], [mm, b], [ss, c]]) { const s = x === "" ? "" : x === 0 && !(box === ss && a === 0 && b === 0) ? "" : String(x); if (box.value !== s) box.value = s; }
  }
  return { el, set };
}
