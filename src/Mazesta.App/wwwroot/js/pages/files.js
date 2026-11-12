// The file tools of the Windows tools page: what holds a file open. Files come only from the host's own dialogs (files.pick); the host
// acts only on what was picked there. Every answer is what Windows reported; "could not check" is never shown as "nothing found".
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { box } from "../groups.js";

const lat = (s) => h("span", { class: "lat" }, s);
const pickBtn = (kind, key, then) => h("button", { class: "btn", onclick: async () => { const p = await call("files.pick", { kind }); if (p?.length) then(p); } }, icon(kind === "folder" ? "folder" : "doc"), t(key));
const shown = (paths) => paths.length === 1 ? lat(paths[0]) : t("Files_Picked_Many", paths.length);

// ——— What holds a file open (Restart Manager) ———
export function lockBox(i) {
  let paths = [];
  const picked = h("p", { class: "fl-picked" }), msg = h("p", { class: "msg", "aria-live": "polite" }), list = h("ul", { class: "fl-list" });
  const again = h("button", { class: "btn quiet", hidden: true, onclick: () => find() }, icon("refresh"), t("Files_Recheck"));
  const use = (p) => { paths = p; picked.replaceChildren(shown(p)); find(); };
  async function find() {
    list.replaceChildren(); again.hidden = false; msg.className = "msg"; msg.textContent = t("Tools_Working");
    try {
      const r = await call("locks.find", { paths });
      if (r.error) { msg.className = "msg fail"; msg.textContent = t("Files_Lock_Failed", r.error); return; }
      msg.className = `msg ${r.holders.length ? "" : "ok"}`;
      msg.textContent = (r.holders.length ? t("Files_Lock_Found", r.holders.length) : t("Files_Lock_None")) + (r.truncated ? " " + t("Files_Lock_Truncated", r.cap) : "");
      list.replaceChildren(...r.holders.map(row));
    } catch (e) { msg.className = "msg fail"; msg.textContent = String(e.message || e); }
  }
  const row = (x) => h("li", {},
    h("div", {}, h("b", {}, x.name), " ", h("span", { class: "caption" }, t(`Files_Lock_Kind_${x.kind}`), " · ", lat(`PID ${x.pid}`)), x.path ? h("small", {}, lat(x.path)) : null),
    x.canClose ? h("button", { class: "btn stop", onclick: () => close(x) }, t("Files_Lock_Close")) : h("span", { class: "caption" }, t("Files_Lock_Protected")));
  async function close(x) {
    if (!confirm(t("Files_Lock_CloseAsk", x.name))) return;
    const r = await call("locks.close", { paths, pid: x.pid, start: x.start });
    if (r.error) toast(t("Files_Lock_CloseFailed", x.name, r.error), "fail"); else toast(t("Files_Lock_Closed", x.name));
    find();
  }
  return box({ ico: "lock", cls: "p-tool", title: t("Files_Lock_Title"), sub: t("Files_Lock_Sub"), i, a: "locks",
    body: [h("div", { class: "btn-row", style: { marginTop: 0 } }, pickBtn("files", "Files_PickFile", use), pickBtn("folder", "Files_PickFolder", use), h("span", { class: "grow" }), again), picked, msg, list] });
}
