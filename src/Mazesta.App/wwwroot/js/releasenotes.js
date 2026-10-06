// What a release changed, as the notes file of the release writes it: one short line per item, marked at its start with + (new), ~ (improved) or ! (fixed);
// a line with no mark (or the old - • *) is listed as it is. Shown grouped under a small heading each, so a long list stays easy to scan.
import { t, fa } from "./i18n.js";
import { h, icon } from "./ui.js";

const KINDS = [["+", "new", "sparkle", "AppUpd_Group_New"], ["~", "better", "sliders", "AppUpd_Group_Better"], ["!", "fix", "wrench", "AppUpd_Group_Fix"]];

/** The lines of a notes text, each {kind, text}; kind is "new", "better", "fix" or "" for an unmarked line. */
export function parseNotes(text) {
  return String(text || "").split("\n").map((l) => l.trim()).filter(Boolean).map((l) => {
    const k = KINDS.find(([m]) => l.startsWith(m));
    return { kind: k ? k[1] : "", text: l.replace(/^[-•*+~!]\s*/, "") };
  }).filter((n) => n.text);
}

/** The notes as a framed list, or the "no notes" line when there are none. */
export function notesView(text) {
  const items = parseNotes(text);
  if (!items.length) return h("div", { class: "rn" }, h("p", { class: "rn-none" }, t("AppUpd_NoNotes")));
  const groups = KINDS.map(([, kind, ico, key]) => [kind, ico, key, items.filter((n) => n.kind === kind)]).filter(([, , , list]) => list.length);
  const rest = items.filter((n) => !n.kind);
  return h("div", { class: "rn" },
    groups.map(([kind, ico, key, list]) => h("section", { class: `rn-group ${kind}` },
      h("h4", { class: "rn-head" }, h("span", { class: "rn-badge" }, icon(ico)), h("span", {}, t(key)), h("span", { class: "rn-count num" }, fa(list.length))),
      h("ul", { class: "rn-list" }, list.map((n) => h("li", {}, n.text))))),
    rest.length ? h("ul", { class: "rn-list plain" }, rest.map((n) => h("li", {}, n.text))) : null);
}
