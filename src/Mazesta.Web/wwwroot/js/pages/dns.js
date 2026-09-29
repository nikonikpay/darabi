// The DNS resolver as one box: one choice for every connected adapter, and what each adapter uses now, read back after the change.
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { h } from "../ui.js";
import { box } from "../groups.js";

export function dnsBox(i) {
  const dnsList = h("div", { class: "dns-list" }), dnsMsg = h("p", { class: "msg" });
  function showDns(d) {
    const current = new Set(d.adapters.map((a) => a.provider));
    const choice = (id, label, servers) => h("button", { class: `dns-opt ${current.size === 1 && current.has(id) ? "on" : ""}`, type: "button", onclick: () => setDns(id) },
      h("b", {}, label), servers ? h("span", { class: "lat" }, servers.join("  ")) : h("span", {}, t("Dns_Auto_Sub")));
    dnsList.replaceChildren(
      h("div", { class: "dns-opts" }, choice("auto", t("Dns_Auto")), ...d.providers.map((p) => choice(p.id, t(`Dns_${p.id}`), p.servers))),
      d.adapters.length ? h("dl", { class: "kv" }, d.adapters.flatMap((a) => [h("dt", { class: "lat" }, a.name), h("dd", { class: "lat" }, a.servers.join("  ") || "—")]))
        : h("p", { class: "caption" }, t("Dns_NoAdapter")));
  }
  async function setDns(id) {
    dnsMsg.className = "msg"; dnsMsg.textContent = t("Tools_Working");
    try {
      const r = await call("tweaks.dns", { provider: id });
      showDns(r.dns); dnsMsg.className = `msg ${r.error ? "fail" : "ok"}`; dnsMsg.textContent = r.error || t("Dns_Done");
    } catch (e) { dnsMsg.className = "msg fail"; dnsMsg.textContent = String(e.message || e); }
  }
  call("dns.state").then(showDns);
  return box({ kind: "Network", title: t("Dns_Title"), sub: t("Dns_Sub"), i, body: [dnsList, dnsMsg] });
}
