// A folding group of queue rows for one part (tests and benchmarks): a panel in the part's hue with a tick for the whole group, a count, and
// the rows inside. Whether a group is folded is a per-viewer convenience kept in the browser profile.
import { t } from "./i18n.js";
import { h, icon } from "./ui.js";
import { part } from "./parts.js";

const shutKey = (page) => `mazesta.groups.${page}`;
function shutSet(page) { try { return new Set(JSON.parse(localStorage.getItem(shutKey(page))) || []); } catch { return new Set(); } }
function keepShut(page, set) { try { localStorage.setItem(shutKey(page), JSON.stringify([...set])); } catch { /* nothing kept */ } }

export function groupPanel(page, kind, i, onTick) {
  const p = part(kind), title = t(p.key), shut = shutSet(page);
  const count = h("span", { class: "group-count" });
  const all = h("input", { type: "checkbox", class: "check", title: t("Web_Group_SelectAll"), "aria-label": `${t("Web_Group_SelectAll")}: ${title}`,
    onclick: (e) => e.stopPropagation(), onchange: (e) => onTick(e.target.checked) });
  const fold = h("button", { class: "more", type: "button", "aria-expanded": "true", "aria-label": title }, icon("chevron"));
  const body = h("div", { class: "queue" });
  const el = h("section", { class: `panel group ${p.cls}`, style: { "--i": i } },
    h("header", { class: "panel-head", onclick: () => setShut(!el.classList.contains("shut"), true) },
      all, h("span", { class: "ico" }, icon(p.icon)), h("div", { class: "ttl" }, h("h2", { class: "panel-title" }, title)), count, fold),
    h("div", { class: "group-body" }, h("div", {}, body)));
  function setShut(v, remember) {
    el.classList.toggle("shut", v); fold.setAttribute("aria-expanded", String(!v));
    if (remember) { const s = shutSet(page); v ? s.add(kind) : s.delete(kind); keepShut(page, s); }
  }
  setShut(shut.has(kind), false);
  // The tick and the count follow the rows: ticked when all are, half when some are.
  function sync(total, selected, running) {
    all.checked = total > 0 && selected === total; all.indeterminate = selected > 0 && selected < total;
    count.textContent = t("Web_Group_Count", total, selected);
    el.classList.toggle("running", running);
    if (running) setShut(false, false);   // the row that runs is always in view
  }
  return { el, body, sync };
}

// Rows grouped by part, the groups in the order their first row appears (so the numbering still reads top to bottom as the run order).
export function byPart(rows, kindOf) {
  const groups = new Map();
  for (const r of rows) { const k = kindOf(r); if (!groups.has(k)) groups.set(k, []); groups.get(k).push(r); }
  return groups;
}
