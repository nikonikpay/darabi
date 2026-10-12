// When a test session ends, one window says how it went, part by part (processor, memory, graphics card, drives): a plain verdict, the figures
// behind it (tests passed, the hottest reading, the load reached) and what the diagnosis found, folded. The words are the page's; the host
// judges (CheckupService) and sends the result once. The report's summary is one button away.
import { call, on } from "./bridge.js";
import { t, fa } from "./i18n.js";
import { h, icon, toast } from "./ui.js";
import { part } from "./parts.js";
import { findingCard, bySeverity } from "./pages/checkup.js";

const LEVEL = { Good: "pass", Attention: "warn", Problem: "fail" };
let dialog = null;

export function start() { on("testdone", (s) => whenFree(() => show(s))); }

// A failure window (failures.js) goes first: this one opens when it is closed.
function whenFree(go) {
  const fail = document.querySelector("dialog.fail-dialog[open]");
  if (fail) fail.addEventListener("close", go, { once: true }); else go();
}

function card(p) {
  const pt = part(p.part), lvl = LEVEL[p.level] || "warn";
  const figs = [
    [t("Done_Tests"), `${fa(p.passed)} / ${fa(p.total)}`],
    p.tempMax != null ? [t("Done_TempMax"), `${fa(Math.round(p.tempMax))} °C`] : null,
    p.load != null ? [t("Done_Load"), `${fa(Math.round(p.load))}%`] : null,
    p.errors > 0 ? [t("Done_Errors"), fa(p.errors)] : null,
  ].filter(Boolean);
  const notGood = p.findings.filter((f) => f.level === "Problem" || f.level === "Attention");
  const good = p.findings.filter((f) => f.level === "Good");
  return h("section", { class: `done-part ${pt.cls} ${lvl}` },
    h("header", { class: "done-head" }, h("span", { class: "ico" }, icon(pt.icon)),
      h("div", { class: "grow" }, h("h3", {}, t(pt.key)), p.name ? h("small", { class: "lat" }, p.name) : null),
      h("span", { class: `pill ${lvl}` }, t(`Done_Level_${p.level}`))),
    h("p", { class: "done-say" }, t(p.say)),
    h("div", { class: "done-figs" }, figs.map(([n, v]) => h("div", {}, h("b", { class: "num" }, v), h("span", {}, n)))),
    // What needs a look, in words; what was checked and found right, as short ticks.
    notGood.length ? h("ul", { class: "done-list" }, bySeverity(notGood).map((f) => h("li", { class: LEVEL[f.level] }, icon("alert"), h("span", {}, f.title)))) : null,
    good.length ? h("ul", { class: "done-list ok" }, good.map((f) => h("li", { class: "pass" }, icon("check"), h("span", {}, f.title)))) : null,
    p.failed > 0 ? h("ul", { class: "done-list" }, p.tests.filter((x) => x.outcome === "Failed" || x.outcome === "Error").map((x) => h("li", { class: "fail" }, icon("x"), h("span", {}, `${x.name}: ${x.outcomeText}`)))) : null,
    p.findings.length ? h("details", { class: "done-more" }, h("summary", {}, t("Done_Details")), bySeverity(p.findings).map(findingCard)) : null);
}

export function show(s) {
  dialog?.remove();
  const close = () => { dialog?.close(); dialog?.remove(); dialog = null; };
  // The summary of the report this session was saved as: the newest report.
  const summary = h("button", { class: "btn", type: "button", onclick: async (e) => {
    e.currentTarget.disabled = true;
    try {
      const r = await call("reports.state");
      const newest = [...(r.items || [])].sort((a, b) => b.created - a.created)[0];
      if (!newest) { toast(t("Done_NoReport"), "fail"); return; }
      close(); location.hash = "#/reports";
      await call("reports.exec", { cmd: "summary", id: newest.id });
    } catch (x) { toast(String(x.message || x), "fail"); } finally { if (dialog) e.currentTarget.disabled = false; }
  } }, icon("doc"), t("Done_Summary"));
  const lvl = LEVEL[s.level] || "warn";
  dialog = h("dialog", { class: "upd-dialog done-dialog", "aria-labelledby": "done-title" },
    h("div", { class: "upd-box" },
      h("header", { class: "upd-head" }, h("span", { class: `upd-mark done-mark ${lvl}` }, icon(s.level === "Good" ? "check" : "alert")),
        h("div", {}, h("h2", { class: "upd-title", id: "done-title" }, t("Done_Title")), h("p", { class: "sup-sub" }, t(`Done_Overall_${s.level}`)))),
      h("div", { class: "done-body" }, s.parts.map(card)),
      h("div", { class: "btn-row" }, h("button", { class: "btn primary", type: "button", onclick: close }, icon("check"), t("Done_Close")), summary)));
  dialog.addEventListener("close", () => { dialog?.remove(); dialog = null; });
  document.body.append(dialog); dialog.showModal();
}
