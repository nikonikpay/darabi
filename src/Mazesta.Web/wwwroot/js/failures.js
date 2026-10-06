// A test or benchmark that broke is told in a dialog once the run is over: what broke, what it most likely points at, what to try, and the way to the
// support desk with the diagnostic file. The host decides the likely cause (FailureAdvice) from the test's part and the error text; the words are here.
import { call, on } from "./bridge.js";
import { t } from "./i18n.js";
import { h, icon, toast } from "./ui.js";
import { openSupport } from "./support.js";

let dialog = null;

export function start(boot) { on("testfail", (f) => show(f, boot)); }

function show(f, boot) {
  dialog?.remove();
  // One block per kind of cause: three memory tests that broke share one explanation.
  const kinds = [...new Set(f.items.map((i) => i.kind))];
  const list = h("ul", { class: "fail-list" }, f.items.map((i) => h("li", {}, h("b", {}, i.name), h("span", { class: "fail-out" }, i.outcome),
    i.detail ? h("code", { class: "fail-detail lat" }, i.detail) : null)), f.more ? h("li", { class: "caption" }, t("Fail_More", f.more)) : null);
  const advice = kinds.map((k) => h("section", { class: "fail-advice" },
    h("h3", {}, t("Fail_Likely")), h("p", {}, t(`Advice_${k}_Cause`)),
    h("h3", {}, t("Fail_Try")), h("ol", {}, t(`Advice_${k}_Steps`).split("|").map((s) => h("li", {}, s)))));
  const exportBtn = h("button", { class: "btn", type: "button", onclick: async (e) => {
    e.currentTarget.disabled = true;
    try { await call("diag.export"); toast(t("Web_Diag_Exported")); } catch (x) { toast(String(x.message || x), "fail"); } finally { e.currentTarget.disabled = false; }
  } }, icon("bug"), t("Web_Diag_Export"));
  const close = () => { dialog?.close(); dialog?.remove(); dialog = null; };
  dialog = h("dialog", { class: "upd-dialog fail-dialog", "aria-labelledby": "fail-title" },
    h("div", { class: "upd-box" },
      h("header", { class: "upd-head" }, h("span", { class: "upd-mark fail-mark" }, icon("bug")),
        h("div", {}, h("h2", { class: "upd-title", id: "fail-title" }, t("Fail_Title")), h("p", { class: "sup-sub" }, t("Fail_Intro")))),
      h("div", { class: "fail-body" }, list, advice, h("p", { class: "fail-support" }, icon("phone"), h("span", {}, t("Fail_Support")))),
      h("div", { class: "btn-row" },
        h("button", { class: "btn primary", type: "button", onclick: () => { close(); openSupport(boot); } }, icon("phone"), t("Fail_Contact")),
        exportBtn, h("button", { class: "btn", type: "button", onclick: close }, t("Fail_Close")))));
  dialog.addEventListener("close", () => { dialog?.remove(); dialog = null; });
  document.body.append(dialog); dialog.showModal();
}
