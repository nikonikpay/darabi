// A live tile: one part at a glance. Its label and icon, the main reading big in the part's hue (digits roll to a new value), a second reading
// beside it, a bar for a share of something (load, used space), and a foot line (the device). A reading that is missing is the hatch with words,
// never a number; a tile without a share has no bar.
import { t } from "./i18n.js";
import { fmt } from "./format.js";
import { value } from "./store.js";
import { h, icon, roll } from "./ui.js";
import { part } from "./parts.js";

// "11.8 GB" → ["11.8", "GB"]: the number rolls, the unit sits small beside it.
function split(text) { const i = text.lastIndexOf(" "); return i > 0 ? [text.slice(0, i), text.slice(i + 1)] : [text, ""]; }

export function liveTile({ kind, label, ico, main, side, share, foot, page, i = 0 }) {
  const p = part(kind), num = h("span", {}), unit = h("small", {}), sideEl = h("span", { class: "tile-side" }), bar = share ? h("i", {}) : null;
  const el = h(page ? "a" : "div", { class: `tile ${p.cls}`, href: page ? `#/${page}` : null, style: { "--i": i } },
    h("div", { class: "tile-head" }, h("span", {}, label), icon(ico || p.icon)),
    h("div", { class: "tile-main" }, h("span", { class: "tile-val" }, num, unit), sideEl),
    bar ? h("div", { class: "bar", role: "presentation" }, bar) : null,
    foot ? h("div", { class: typeof foot === "string" ? "tile-foot" : "tile-foot fa" }, foot) : null);
  function update() {
    const text = main ? fmt(value(main.id), main.unit) : null;
    if (text === null) { num.replaceChildren(h("span", { class: "hatch" }, t("Value_NotAvailable"))); num.dataset.v = ""; unit.textContent = ""; }
    else { const [n, u] = split(text); if (!num.querySelector(".roll")) num.replaceChildren(); roll(num, n); unit.textContent = u; }
    const s = typeof side === "function" ? side() : side ? fmt(value(side.id), side.unit) : null;
    sideEl.textContent = s ?? "";
    if (bar) { const x = share(); bar.style.setProperty("--p", x === null ? 0 : Math.min(1, Math.max(0, x))); }
  }
  return { el, update };
}

// The share helpers the tiles use: a percent sensor, or a used/total pair. Without both numbers there is no share (null), never 0.
export const percentOf = (s) => () => { const v = s ? value(s.id) : null; return v === null ? null : v / 100; };
export const ratioOf = (a, b) => () => { const x = a ? value(a.id) : null, y = b ? value(b.id) : null; return x === null || !y ? null : x / y; };
