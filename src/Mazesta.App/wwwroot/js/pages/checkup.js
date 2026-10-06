// The diagnosis: whether this machine works as it should, said in words so nobody has to compare numbers themselves. It is made from the Tests
// page's own tests - a short sample of each part, run from here - each with how it ended and what the sensors said meanwhile, and from the
// setup (memory, power plan, drive links and health) as it is now. A benchmark run of this session is judged too, further down. The host does
// the judging; this page only shows it, the findings that need action first.
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

const OUTCOME = { Passed: "pass", Failed: "fail", Cancelled: "warn", Unsupported: "warn", Error: "warn", Inconclusive: "warn", Running: "run", NotRun: "none" };

export function mount(el) {
  const run = h("button", { class: "btn go", "data-a": "run", onclick: async () => { run.disabled = true; if (!(await call("checkup.run"))) run.disabled = false; } }, icon("play"), t("Checkup_Run"));
  const hint = h("span", { class: "caption" }, t("Checkup_RunHint"));
  const live = h("a", { class: "btn", href: "#/tests", hidden: true }, icon("flask"), t("Checkup_Watch"));
  const summary = h("div", { class: "checkup-sum" });
  const setup = h("div", { class: "findings" }, h("p", { class: "page-lede" }, t("Checkup_Loading")));
  const tests = h("div", { class: "findings checkup-runs" }), runs = h("div", { class: "findings checkup-runs" }), runsSection = h("div", { class: "section", hidden: true }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Checkup_Runs"))), runs);
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Checkup")), h("p", { class: "page-lede" }, t("Checkup_Lede"))),
    h("div", { class: "checkup-run" }, run, live, hint)),
    summary,
    h("div", { class: "section" }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Checkup_Tests"))), tests),
    h("div", { class: "section" }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Checkup_Setup"))), setup),
    runsSection);

  let setupList = [], state = null;
  function paintSummary() {
    const all = [...setupList, ...(state?.tests || []).flatMap((r) => r.findings), ...(state?.runs || []).flatMap((r) => r.findings)];
    // A test that failed is a problem by itself, whatever the sensors said; one that did not finish is something to look at, never a pass.
    const failed = (state?.tests || []).filter((r) => r.outcome === "Failed").length, open = (state?.tests || []).filter((r) => !["Passed", "Failed"].includes(r.outcome)).length;
    const problems = all.filter((f) => f.level === "Problem").length + failed, looks = all.filter((f) => f.level === "Attention").length + open;
    const [cls, text] = problems ? ["fail", t("Checkup_Summary_Problem", fa(problems))] : looks ? ["warn", t("Checkup_Summary_Attention", fa(looks))] : ["pass", t("Checkup_Summary_Good")];
    summary.replaceChildren(all.length || state?.tests?.length ? h("span", { class: `pill ${cls}` }, text) : "");
    if (problems && looks) summary.append(h("span", { class: "pill warn" }, t("Checkup_Summary_Attention", fa(looks))));
  }
  function paint(s) {
    state = s; run.disabled = !!s.running; live.hidden = !s.own;
    hint.textContent = s.own ? t("Checkup_Running") : t("Checkup_RunHint", fa(s.minutes), (s.plan || []).join("، "));
    tests.replaceChildren(...(s.tests.length ? s.tests.map((r) => h("div", { class: "checkup-run-block" },
      h("div", { class: "col-head" }, h("span", { class: "h3" }, r.name), h("span", { class: `pill ${OUTCOME[r.outcome] || "none"}` }, r.outcomeText), h("span", { class: "caption lat" }, r.at)),
      ...bySeverity(r.findings).map(findingCard))) : [h("p", { class: "rec-none" }, t("Checkup_NoTests"))]));
    runsSection.hidden = !s.runs.length;
    runs.replaceChildren(...s.runs.map((r) => h("div", { class: "checkup-run-block" },
      h("div", { class: "col-head" }, h("span", { class: "h3" }, r.name), h("span", { class: "caption lat" }, r.at)),
      ...(r.findings.length ? bySeverity(r.findings).map(findingCard) : [h("p", { class: "rec-none" }, t("Checkup_Nothing"))]))));
    paintSummary();
  }
  call("checkup.state").then(paint);
  call("checkup.setup").then((list) => { setupList = list || []; setup.replaceChildren(...bySeverity(setupList).map(findingCard)); paintSummary(); })
    .catch(() => setup.replaceChildren());
  return on("checkup", paint);
}
