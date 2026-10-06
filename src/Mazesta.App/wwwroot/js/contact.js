// The shop's people (offices, sales, support, hours, address and the links), drawn the same on the dashboard and in the support dialog.
import { call } from "./bridge.js";
import { t } from "./i18n.js";
import { h, icon } from "./ui.js";

export function contactLines(c) {
  const chip = (key, ico, label) => h("button", { class: "chip", type: "button", onclick: () => call("app.openLink", { key }) }, icon(ico), label);
  const line = (who, number, ...chips) => h("div", { class: "line" }, h("span", { class: "who" }, who), h("span", { class: "no" }, number), chips.length ? h("div", { class: "links" }, chips) : null);
  const messengers = (desk) => [chip(`${desk}-telegram`, "send", t("Web_Contact_Telegram")), chip(`${desk}-whatsapp`, "chat", t("Web_Contact_WhatsApp")), chip("bale", "chat", t("Web_Contact_Bale"))];
  return h("div", { class: "contact" },
    c.office && line(t("Web_Contact_Office"), c.office),
    c.sales && line(t("Web_Contact_Sales"), c.sales, ...messengers("sales")),
    c.support && line(t("Web_Contact_Support"), c.support, ...messengers("support")),
    c.hours && h("p", { class: "note" }, icon("clock"), h("span", {}, t(c.hours))),
    c.address && h("p", { class: "note" }, icon("pin"), h("span", {}, t(c.address), c.postcode ? [` — ${t("Web_Contact_Postcode")} `, h("span", { class: "lat" }, c.postcode)] : null)),
    h("div", { class: "links" }, chip("site", "net", t("Dashboard_Mazesta_Site")), chip("channel-telegram", "send", t("Web_Contact_Channel")), chip("instagram", "camera", t("Web_Contact_Instagram"))));
}
