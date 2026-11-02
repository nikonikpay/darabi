// The BitLocker box on the storage page: each drive letter's encryption state, and - when one can lock - the warning about the recovery key before a
// Windows reinstall or a BIOS update, where to check the key is saved, and how to turn BitLocker off. It only reads; the user turns it off themselves.
import { call } from "./bridge.js";
import { t } from "./i18n.js";
import { h, icon } from "./ui.js";

const PILL = { On: "warn", Suspended: "warn", WaitingForActivation: "warn", Encrypting: "warn", Decrypting: "run", Off: "pass", Unknown: "none" };
const NOTE = { Suspended: "BitLocker_Note_Suspended", WaitingForActivation: "BitLocker_Note_Waiting", Encrypting: "BitLocker_Note_Encrypting", Decrypting: "BitLocker_Note_Decrypting" };

export function bitlockerPanel() {
  const body = h("div", {}, h("p", { class: "caption" }, t("Spec_Loading")));
  const el = h("section", { class: "panel bl-panel", "data-a": "bitlocker" },
    h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("lock")),
      h("div", { class: "ttl" }, h("h2", { class: "panel-title" }, t("BitLocker_Title")), h("div", { class: "panel-sub fa" }, t("BitLocker_Sub")))), body);
  const steps = (key) => h("ol", { class: "bl-steps" }, t(key).split("|").map((s) => h("li", {}, s)));
  call("bitlocker.get").then((r) => {
    if (!r.known) { body.replaceChildren(h("p", { class: "chk-text" }, t("BitLocker_NotKnown"))); return; }
    const locking = r.volumes.filter((v) => v.canLock);
    const rows = h("div", { class: "bl-vols" }, r.volumes.map((v) => h("div", { class: "bl-vol" },
      h("code", { class: "lat" }, v.letter), h("span", { class: `pill ${PILL[v.state]}` }, t("BitLocker_State_" + v.state)),
      NOTE[v.state] ? h("span", { class: "caption" }, t(NOTE[v.state])) : null,
      v.canLock && v.recovery === false ? h("span", { class: "bl-bad" }, t("BitLocker_NoRecovery")) : null)));
    if (locking.length === 0) { body.replaceChildren(rows, h("p", { class: "chk-text" }, t(r.volumes.some((v) => v.state === "Unknown") ? "BitLocker_SomeUnknown" : "BitLocker_None"))); return; }
    body.replaceChildren(rows,
      h("p", { class: "bl-warn" }, t("BitLocker_Warn", locking.map((v) => v.letter).join(" "))),
      h("details", { class: "bl-more", open: "" }, h("summary", {}, t("BitLocker_Key_Title")), steps("BitLocker_Key_Steps"),
        h("div", { class: "btn-row" }, h("button", { class: "btn", type: "button", onclick: () => call("app.openLink", { key: "recoverykey" }) }, icon("popout"), t("BitLocker_OpenKeys")))),
      h("details", { class: "bl-more" }, h("summary", {}, t("BitLocker_Off_Title")), h("p", { class: "chk-text" }, t("BitLocker_Off_Who")), steps("BitLocker_Off_Steps"),
        h("p", { class: "bl-warn" }, t("BitLocker_Off_Warn"))));
  }).catch((e) => body.replaceChildren(h("p", { class: "ai-err" }, String(e.message || e))));
  return el;
}
