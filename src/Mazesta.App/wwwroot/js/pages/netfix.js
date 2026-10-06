// "My internet does not connect", as one box: the check walks from the adapter to a web page and says where the connection stops (each step
// with what was measured); the repairs are ticked by the user and run in order - clear the proxy settings, put the DNS on automatic or on
// Google's, empty the DNS cache, and last the Winsock and IP reset, which needs a restart. The check is run again after the repairs.
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon } from "../ui.js";
import { box } from "../groups.js";

const STEPS = ["adapter", "gateway", "internet", "dns", "web", "proxy"];
const PILL = { Ok: "pass", Failed: "fail", Skipped: "none" };
const FIXES = ["proxy", "dns", "flush", "reset"];

export function netfixBox(i) {
  const list = h("div", { class: "nf-steps" }), verdict = h("p", { class: "msg" }), results = h("ul", { class: "problems" }), runMsg = h("p", { class: "msg" });
  const check = h("button", { class: "btn go", type: "button", onclick: () => run(false) }, icon("pulse"), t("NetFix_Check"));
  const dns = h("select", { class: "field", "aria-label": t("NetFix_Fix_dns") }, h("option", { value: "auto" }, t("NetFix_Dns_Auto")), h("option", { value: "google" }, t("NetFix_Dns_Google")));
  const ticks = new Map(FIXES.map((id) => [id, h("input", { type: "checkbox", value: id })]));
  const fixes = h("div", { class: "choice" }, FIXES.map((id) => h("label", {}, ticks.get(id), h("b", {}, t(`NetFix_Fix_${id}`), id === "dns" ? h("span", { class: "nf-dns" }, dns) : null),
    h("small", {}, t(`NetFix_Fix_${id}_Note`)))));
  const fix = h("button", { class: "btn primary", type: "button", onclick: repair }, icon("bolt"), t("NetFix_Run"));

  function show(r) {
    const by = new Map(r.checks.map((c) => [c.id, c]));
    list.replaceChildren(...STEPS.map((id, n) => {
      const c = by.get(id) || { result: "Skipped" };
      return h("div", { class: "nf-step" }, h("span", { class: "step lat" }, String(n + 1)), h("b", {}, t(`NetFix_Step_${id}`)),
        h("span", { class: `pill ${PILL[c.result]}` }, t(`NetFix_Result_${c.result}`)), c.detail ? h("span", { class: "caption lat nf-detail" }, c.detail) : null);
    }));
    verdict.className = `msg ${r.verdict === "Connected" ? "ok" : "fail"}`; verdict.textContent = t(`NetFix_Verdict_${r.verdict}`);
    for (const [id, box] of ticks) box.checked = r.suggested.includes(id);
  }
  async function run(after) {
    check.disabled = fix.disabled = true; if (!after) { verdict.className = "msg"; verdict.textContent = t("NetFix_Checking"); }
    try { show(await call("netfix.check")); }
    catch (e) { verdict.className = "msg fail"; verdict.textContent = String(e.message || e); }
    finally { check.disabled = fix.disabled = false; }
  }
  async function repair() {
    const steps = FIXES.filter((id) => ticks.get(id).checked);
    if (!steps.length) { runMsg.className = "msg fail"; runMsg.textContent = t("NetFix_NoneTicked"); return; }
    if (steps.includes("reset") && !confirm(t("NetFix_Reset_Confirm"))) return;
    check.disabled = fix.disabled = true; runMsg.className = "msg"; runMsg.textContent = t("Tools_Working"); results.replaceChildren();
    try {
      const r = await call("netfix.run", { steps, dns: dns.value });
      results.replaceChildren(...r.results.map((x) => h("li", { class: x.error ? "fail" : "" }, h("b", {}, t(`NetFix_Fix_${x.id}`)), ": ", x.error ? t("NetFix_Failed", x.error) : x.done)));
      runMsg.className = `msg ${r.results.some((x) => x.error) ? "fail" : "ok"}`; runMsg.textContent = t(r.restart ? "NetFix_Done_Restart" : "NetFix_Done");
      await run(true);
    } catch (e) { runMsg.className = "msg fail"; runMsg.textContent = String(e.message || e); check.disabled = fix.disabled = false; }
  }

  return box({ kind: "Network", title: t("NetFix_Title"), sub: t("NetFix_Sub"), i, wide: true, a: "netfix",
    body: [h("p", { class: "note", style: { marginTop: 0 } }, t("NetFix_Note")), h("div", { class: "btn-row" }, check), list, verdict,
      h("h3", { class: "dns-h" }, t("NetFix_Repairs")), fixes, h("div", { class: "btn-row" }, fix), runMsg, results] });
}
