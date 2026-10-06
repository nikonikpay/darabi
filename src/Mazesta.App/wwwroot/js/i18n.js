// The app's own translations (the same table the WPF edition uses), sent by the host at boot.
let table = {};
export let rtl = true;
export let lang = "fa";

export function setStrings(strings, isRtl, language) {
  table = strings || {};
  rtl = isRtl; lang = language;
  document.documentElement.lang = language;
  document.documentElement.dir = isRtl ? "rtl" : "ltr";
}

// A string by key with {0}-style arguments. Persian prose gets Persian digits in its arguments; values set with .lat/.num never pass here.
export function t(key, ...args) {
  const s = table[key] ?? key;
  return args.length ? s.replace(/\{(\d+)\}/g, (_, i) => prose(args[+i] ?? "")) : s;
}

export function has(key) { return key in table; }

const FA = "۰۱۲۳۴۵۶۷۸۹";
export function fa(value) { return String(value).replace(/[0-9]/g, (d) => FA[d]); }
export function prose(value) { return rtl && typeof value === "number" ? fa(value) : String(value); }
