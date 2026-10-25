// The "sales and support" box under the menu: one dialog with the same ways to reach the shop's two desks as the dashboard shows.
import { t } from "./i18n.js";
import { h, icon } from "./ui.js";
import { contactLines } from "./contact.js";

let dialog = null;

// The line under a red warning (a part too hot, a drive nearly worn out, a test that failed): the customer is told they can write to Mazesta support, with the way to do it.
export function supportNote() {
  return h("p", { class: "fail-support" }, icon("phone"), h("span", {}, t("Support_Critical")),
    h("button", { class: "btn quiet", type: "button", onclick: () => import("./app.js").then((m) => openSupport(m.boot)) }, t("Fail_Contact")));
}

export function openSupport(boot) {
  if (dialog) return;
  const close = () => { dialog?.close(); dialog?.remove(); dialog = null; };
  dialog = h("dialog", { class: "upd-dialog sup-dialog", "aria-labelledby": "sup-title" },
    h("div", { class: "upd-box" },
      h("header", { class: "upd-head" }, h("span", { class: "upd-mark" }, icon("phone")),
        h("div", {}, h("h2", { class: "upd-title", id: "sup-title" }, t("Support_Title")), h("p", { class: "sup-sub" }, t("Support_Sub")))),
      contactLines(boot.contact || {}),
      h("div", { class: "btn-row" }, h("button", { class: "btn", type: "button", onclick: close }, t("Support_Close")))));
  dialog.addEventListener("close", () => { dialog?.remove(); dialog = null; });
  document.body.append(dialog); dialog.showModal();
}
