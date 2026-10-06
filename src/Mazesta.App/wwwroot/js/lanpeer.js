// The LAN partner switch on the network page: while it is on, this computer answers the LAN test another computer runs against it.
import { call } from "./bridge.js";
import { t } from "./i18n.js";
import { h, icon, toast } from "./ui.js";

export function lanPanel() {
  const body = h("div", { class: "lan-body" }), el = h("section", { class: "panel lan-panel" },
    h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("net")),
      h("div", { class: "ttl" }, h("h2", { class: "panel-title" }, t("Lan_Title")), h("div", { class: "panel-sub fa" }, t("Lan_Sub")))), body);
  let timer = 0;
  function render(s) {
    const toggle = h("button", { class: `btn ${s.listening ? "stop" : "go"}`, onclick: () => call("lan.set", { on: !s.listening }).then(render).catch((e) => toast(String(e.message || e), "fail")) },
      icon(s.listening ? "stop" : "play"), t(s.listening ? "Lan_Stop" : "Lan_Start"));
    body.replaceChildren(...[
      h("p", { class: "chk-text" }, t("Lan_Text")),
      h("div", { class: "btn-row" }, toggle, s.listening ? h("span", { class: "pill run" }, t("Lan_Listening", s.port, s.sessions)) : null),
      s.listening ? h("div", { class: "lan-addr" }, h("span", { class: "caption" }, t("Lan_Addresses")), ...s.addresses.map((a) => h("code", { class: "lat" }, a))) : null,
      s.error ? h("p", { class: "ai-err" }, s.error) : null].filter(Boolean));
    clearTimeout(timer);
    if (s.listening) timer = setTimeout(() => call("lan.state").then(render).catch(() => {}), 3000);
  }
  call("lan.state").then(render);
  return { el, off: () => clearTimeout(timer) };
}
