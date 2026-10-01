// The checkup: whether this machine works as it should, said in words so nobody has to compare numbers themselves. The setup (memory, power
// plan, drive links) as it is now, and every CPU and GPU benchmark run of this session judged against what its parts report themselves and
// against other systems with the same parts. The host does the judging; this page only shows it, the findings that need action first.
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon } from "../ui.js";

const PILL = { Good: "pass", Note: "none", Attention: "warn", Problem: "fail" };
const RANK = { Problem: 0, Attention: 1, Note: 2, Good: 3 };
export const bySeverity = (list) => [...list].sort((a, b) => RANK[a.level] - RANK[b.level]);

// One finding: its level as a stamp, its title, what it means and what to do, the line its measurements point to, and the numbers themselves.
export function findingCard(f) {
  return h("div", { class: `finding lv-${f.level}` },
    h("div", { class: "finding-head" }, h("span", { class: `pill ${PILL[f.level] || "none"}` }, f.levelName), h("b", {}, f.title), f.subject ? h("span", { class: "caption lat" }, f.subject) : null),
    h("p", { class: "finding-text" }, f.text),
    f.hint ? h("p", { class: "finding-hint" }, f.hint) : null,
    f.measures.length ? h("dl", { class: "finding-m" }, f.measures.flatMap((m) => [h("dt", {}, m.name), h("dd", { class: "num" }, m.value)])) : null,
    f.source ? h("p", { class: "caption finding-src", title: f.source }, t("Check_Source", new URL(f.source).host)) : null);
}

export function mount(el) {
  const run = h("button", { class: "btn go", "data-a": "run", onclick: async () => { run.disabled = true; if (!(await call("checkup.run"))) run.disabled = false; } }, icon("play"), t("Checkup_Run"));
  const summary = h("div", { class: "checkup-sum" });
  const setup = h("div", { class: "findings" }, h("p", { class: "page-lede" }, t("Checkup_Loading")));
  const runs = h("div", { class: "findings" });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Checkup")), h("p", { class: "page-lede" }, t("Checkup_Lede"))),
    h("div", { class: "checkup-run" }, run, h("span", { class: "caption" }, t("Checkup_RunHint")))),
    summary,
    h("div", { class: "section" }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Checkup_Setup"))), setup),
    h("div", { class: "section" }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Checkup_Runs"))), runs));

  let setupList = [], state = null;
  function paintSummary() {
    const all = [...setupList, ...(state?.runs || []).flatMap((r) => r.findings)];
    const problems = all.filter((f) => f.level === "Problem").length, looks = all.filter((f) => f.level === "Attention").length;
    const [cls, text] = problems ? ["fail", t("Checkup_Summary_Problem", fa(problems))] : looks ? ["warn", t("Checkup_Summary_Attention", fa(looks))] : ["pass", t("Checkup_Summary_Good")];
    summary.replaceChildren(all.length ? h("span", { class: `pill ${cls}` }, text) : "");
    if (problems && looks) summary.append(h("span", { class: "pill warn" }, t("Checkup_Summary_Attention", fa(looks))));
  }
  function paint(s) {
    state = s; run.disabled = !!s.running;
    runs.replaceChildren(...(s.runs.length ? s.runs.map((r) => h("div", { class: "checkup-run-block" },
      h("div", { class: "col-head" }, h("span", { class: "h3" }, r.name), h("span", { class: "caption lat" }, r.at)),
      ...(r.findings.length ? bySeverity(r.findings).map(findingCard) : [h("p", { class: "rec-none" }, t("Checkup_Nothing"))]))) : [h("p", { class: "rec-none" }, t("Checkup_NoRuns"))]));
    paintSummary();
  }
  call("checkup.state").then(paint);
  call("checkup.setup").then((list) => { setupList = list || []; setup.replaceChildren(...bySeverity(setupList).map(findingCard)); paintSummary(); })
    .catch(() => setup.replaceChildren());
  return on("checkup", paint);
}
