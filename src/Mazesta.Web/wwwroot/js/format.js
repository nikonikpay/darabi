// Readings formatted the way the WPF edition formats them (Units.FormatWithSymbol): Latin digits, the unit's symbol, never a made-up value.
let symbols = {};
export function setUnits(map) { symbols = map || {}; }

const DECIMALS = { Volt: 3, Celsius: 1, Watt: 1, Ampere: 1, Gigabyte: 1, Ratio: 1, Nanoseconds: 1 };

export function fmt(value, unit) {
  if (value === null || value === undefined || Number.isNaN(value)) return null;
  if (unit === "BytesPerSecond") {
    const p = ["B/s", "KB/s", "MB/s", "GB/s"]; let v = value, i = 0;
    while (v >= 1000 && i < p.length - 1) { v /= 1000; i++; }
    return `${v.toFixed(i === 0 ? 0 : 1)} ${p[i]}`;
  }
  if (unit === "MegaHertz" && value >= 1000) return `${(value / 1000).toFixed(2)} GHz`;
  const s = symbols[unit] ?? "";
  const n = value.toFixed(DECIMALS[unit] ?? 0);
  return s ? `${n} ${s}` : n;
}

// Short forms for big numerals: whole numbers, the unit apart.
export function whole(value) { return value === null || value === undefined || Number.isNaN(value) ? null : Math.round(value); }
