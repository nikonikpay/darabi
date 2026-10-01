// The DNS resolver as one box: what each connected adapter uses now; DNS Jumper's test (every resolver timed on this connection, the fastest
// that answered every lookup marked, and used only when asked); and the choice by hand. Every change is read back from Windows afterwards.
import { call } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon } from "../ui.js";
import { box } from "../groups.js";

const name = (id) => (id === "current" ? t("Dns_Modem") : id === "auto" ? t("Dns_Auto") : t(`Dns_${id}`));

export function dnsBox(i) {
  const inUse = h("div", { class: "dns-now" }), opts = h("div", { class: "dns-opts" }), dnsMsg = h("p", { class: "msg" });
  const results = h("div", { class: "dns-results" }), testMsg = h("p", { class: "msg" });
  const test = h("button", { class: "btn", type: "button", onclick: () => bench(false) }, icon("pulse"), t("Dns_Test"));
  const auto = h("button", { class: "btn go", type: "button", onclick: () => bench(true) }, icon("bolt"), t("Dns_TestApply"));
  let state = null;

  function showDns(d) {
    state = d;
    const current = new Set(d.adapters.map((a) => a.provider));
    const choice = (id, servers) => h("button", { class: `dns-opt ${current.size === 1 && current.has(id) ? "on" : ""}`, type: "button", onclick: () => setDns(id) },
      h("b", {}, name(id)), servers ? h("span", { class: "lat" }, servers.join("  ")) : h("span", {}, t("Dns_Auto_Sub")));
    opts.replaceChildren(choice("auto"), ...d.providers.map((p) => choice(p.id, p.servers)));
    inUse.replaceChildren(...(d.adapters.length ? d.adapters.map((a) => h("div", { class: "dns-adapter" },
      h("span", { class: "caption lat" }, a.name), h("b", {}, name(a.provider === "auto" ? "current" : a.provider)), h("span", { class: "lat" }, a.servers.join("  ") || "—")))
      : [h("p", { class: "caption" }, t("Dns_NoAdapter"))]));
  }
  async function setDns(id, done) {
    dnsMsg.className = "msg"; dnsMsg.textContent = t("Tools_Working");
    try {
      const r = await call("tweaks.dns", { provider: id });
      showDns(r.dns); dnsMsg.className = `msg ${r.error ? "fail" : "ok"}`; dnsMsg.textContent = r.error || done || t("Dns_Done");
    } catch (e) { dnsMsg.className = "msg fail"; dnsMsg.textContent = String(e.message || e); }
  }

  // The test: every resolver at once, a few seconds; the table is drawn from what came back, the fastest reliable one first.
  async function bench(apply) {
    test.disabled = auto.disabled = true; testMsg.className = "msg"; testMsg.textContent = t("Dns_Testing"); results.replaceChildren();
    try {
      const r = await call("dns.bench");
      const slowest = Math.max(...r.scores.filter((x) => x.ms != null).map((x) => x.ms), 1);
      results.replaceChildren(h("table", { class: "dns-table" }, h("tbody", {}, r.scores.map((x) => h("tr", { class: `${x.provider === r.best ? "best" : ""} ${x.reliable ? "" : "miss"}` },
        h("th", {}, name(x.provider), x.provider === r.best ? h("span", { class: "pill pass" }, t("Dns_Fastest")) : null),
        h("td", { class: "lat caption" }, x.server),
        h("td", { class: "dns-bar" }, x.ms != null ? h("i", { style: { "--p": x.ms / slowest } }) : null),
        h("td", { class: "num" }, x.ms != null ? `${fa(x.ms)} ms` : "—"),
        h("td", { class: "caption" }, x.reliable ? "" : t("Dns_Missed", fa(x.answered), fa(x.asked))),
        h("td", {}, x.provider !== "current" && x.reliable ? h("button", { class: "btn quiet", type: "button", onclick: () => setDns(x.provider) }, t("Dns_Use")) : null))))));
      const best = r.scores.find((x) => x.provider === r.best);
      if (!best) { testMsg.className = "msg fail"; testMsg.textContent = t("Dns_None"); return; }
      const already = r.best === "current" || r.inUse.length === 1 && r.inUse[0] === r.best;
      testMsg.className = "msg ok"; testMsg.textContent = already ? t("Dns_AlreadyBest", name(r.best), fa(best.ms)) : t("Dns_Best", name(r.best), fa(best.ms));
      if (apply && !already) await setDns(r.best, t("Dns_Applied", name(r.best), fa(best.ms)));
    } catch (e) { testMsg.className = "msg fail"; testMsg.textContent = String(e.message || e); }
    finally { test.disabled = auto.disabled = false; }
  }

  call("dns.state").then(showDns);
  return box({ kind: "Network", title: t("Dns_Title"), sub: t("Dns_Sub"), i, wide: true, a: "dns",
    body: [h("h3", { class: "dns-h" }, t("Dns_Current")), inUse,
      h("h3", { class: "dns-h" }, t("Dns_Jumper")), h("p", { class: "note", style: { marginTop: 0 } }, t("Dns_Test_Note")), h("div", { class: "btn-row" }, auto, test), testMsg, results,
      h("h3", { class: "dns-h" }, t("Dns_Choose")), opts, dnsMsg] });
}
