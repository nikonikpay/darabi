// The shop's messages to this computer: what the shop sent to every system or to this one, newest first, kept so they can be read again later.
// A new one has also come as a notification; opening the page marks the list read.
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { box } from "../groups.js";

export function mount(el) {
  const list = h("div", { class: "msg-list" });
  const render = (s) => {
    if (!s.items.length) { list.replaceChildren(h("p", { class: "caption" }, t("Msg_Empty"))); return; }
    list.replaceChildren(...s.items.map((m) => h("article", { class: `msg-item${m.read ? "" : " unread"}` },
      h("header", {}, h("b", {}, m.title || "Mazesta"), h("span", { class: "caption" }, new Date(m.at).toLocaleString(document.documentElement.lang === "fa" ? "fa-IR" : "en-GB"))),
      h("p", { style: { whiteSpace: "pre-wrap" } }, m.body),
      m.link ? h("div", {}, h("button", { class: "btn quiet", type: "button", onclick: () => call("messages.open", { id: m.id }).catch((e) => toast(String(e.message || e), "fail")) }, icon("shop"), t("Msg_Open"))) : null)));
  };
  const markAll = h("button", { class: "btn", type: "button", onclick: async () => render(await call("messages.read")) }, icon("check"), t("Msg_ReadAll"));
  el.append(box({ ico: "mail", title: t("Msg_Title"), sub: t("Msg_Sub"), actions: markAll, wide: true, body: list }));
  call("messages.state").then((s) => { render(s); if (s.unread) call("messages.read").then(render); }).catch(() => {});
  const off = on("messages", render);
  return () => off();
}
