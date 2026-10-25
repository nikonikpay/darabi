// What the shop's site lets this copy use. Today the site answers "nothing is locked" and this file draws nothing at all; once the shop locks a menu or a tab for accounts without the
// Pro plan, the shell marks it with a lock and shows the panel below in its place (with the sign-in by mobile number when the site offers it). The host keeps the last answer,
// so a lock also holds offline. The dashboard, messages, settings, app update and help are never locked.
import { call, on } from "./bridge.js";
import { t } from "./i18n.js";
import { h, icon, toast } from "./ui.js";

const OPEN = ["dashboard", "messages", "settings", "appupdate", "help"];
export const access = { enforce: false, login: false, plan: "free", locks: [], message: "", signedIn: false, account: null };

export const isLocked = (pageId, menuKey) => access.enforce && !OPEN.includes(pageId) && (access.locks.includes(`page:${pageId}`) || access.locks.includes(`menu:${menuKey}`));

export async function startAccess(onChange) {
  Object.assign(access, await call("access.state").catch(() => ({})));
  on("access", (s) => { Object.assign(access, s); onChange(); });
}

// The panel in place of a locked page: why, what to do (sign in, or write to the shop), and the account when there is one.
export function lockedPage(boot) {
  const msg = h("p", { class: "msg" });
  const say = (text, bad) => { msg.textContent = text || ""; msg.className = `msg${bad ? " fail" : ""}`; };
  const fail = (e) => say(String(e?.message || e), true);
  const support = h("button", { class: "btn", type: "button", onclick: () => import("./support.js").then((m) => m.openSupport(boot)) }, icon("phone"), t("Fail_Contact"));
  const body = h("div", { class: "lock-body" });
  if (access.signedIn) {
    body.append(h("p", {}, t("Access_Signed", access.account || "")), h("div", { class: "btn-row" }, support,
      h("button", { class: "btn quiet", type: "button", onclick: () => call("access.logout").catch(fail) }, t("Access_Logout"))));
  } else if (access.login) {
    const mobile = h("input", { class: "field lat", type: "tel", inputmode: "numeric", dir: "ltr", maxlength: 11, placeholder: "09123456789", "aria-label": t("Access_Mobile") });
    const code = h("input", { class: "field lat", inputmode: "numeric", dir: "ltr", maxlength: 6, placeholder: "······", "aria-label": t("Access_Code"), hidden: true });
    const go = h("button", { class: "btn primary", type: "button" }, t("Access_SendCode"));
    let sent = false;
    go.onclick = async () => {
      go.disabled = true; say("");
      try {
        if (!sent) { await call("access.code", { mobile: mobile.value }); sent = true; code.hidden = false; code.focus(); go.textContent = t("Access_Verify"); say(t("Access_Sent")); }
        else { await call("access.verify", { mobile: mobile.value, code: code.value }); toast(t("Access_Done")); }
      } catch (e) { fail(e); } finally { go.disabled = false; }
    };
    body.append(h("p", {}, t("Access_Login")), h("div", { class: "lock-form" }, mobile, code, go), h("div", { class: "btn-row" }, support));
  } else body.append(h("div", { class: "btn-row" }, support));
  return h("section", { class: "lock-panel" }, h("span", { class: "lock-ico" }, icon("lock")), h("h2", {}, t("Access_Locked")),
    h("p", {}, access.message || t("Access_Locked_Text")), body, msg);
}
