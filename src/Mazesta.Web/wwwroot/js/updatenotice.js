// A release found on the site is announced once a run, with what changed in it, and can be installed from there with one click: the app downloads and
// checks it, closes, puts it in place and opens again by itself. "Later" leaves it to the update page (the settings entry stays marked).
import { call, on } from "./bridge.js";
import { t, fa } from "./i18n.js";
import { h, icon } from "./ui.js";

const told = new Set();   // the versions already announced in this run
let dialog = null, current = null;

export function start(boot) {
  if (boot.staff) return;   // the company's own copy is not replaced by the users' release
  const consider = (s) => {
    if (dialog) { paint(s); return; }
    if (!s || s.state !== "Available" || !s.latest || !s.canInstall || told.has(s.latest.version)) return;
    told.add(s.latest.version); open(s);
  };
  call("upd.state").then(consider).catch(() => {});
  on("upd", consider);
}

function open(s) {
  const title = h("h2", { class: "upd-title" }), meta = h("p", { class: "caption" }), list = h("ul", { class: "upd-notes" });
  const bar = h("div", { class: "progress", hidden: true }, h("i")), status = h("p", { class: "au-status", hidden: true });
  const go = h("button", { class: "btn primary", type: "button", onclick: () => run() }, icon("update"), t("AppUpd_UpdateNow"));
  const later = h("button", { class: "btn", type: "button", onclick: () => close() }, t("AppUpd_Notice_Later"));
  dialog = h("dialog", { class: "upd-dialog", "aria-labelledby": "upd-title" },
    h("div", { class: "upd-box" }, title, meta, h("h3", { class: "h3" }, t("AppUpd_Notes")), list, bar, status, h("p", { class: "note" }, t("AppUpd_Notice_Restart")), h("div", { class: "btn-row" }, go, later)));
  title.id = "upd-title";
  dialog.addEventListener("cancel", (e) => { if (current?.state === "Downloading" || current?.state === "Installing") e.preventDefault(); else close(); });
  document.body.append(dialog); dialog.showModal();
  dialog._parts = { title, meta, list, bar, status, go, later };
  paint(s);
  async function run() { try { paint(await call("upd.now")); } catch (e) { paint({ ...current, state: "Failed", error: String(e.message || e) }); } }
}

function close() { dialog?.close(); dialog?.remove(); dialog = null; }

function paint(s) {
  if (!dialog || !s?.latest) return;
  current = s; const { title, meta, list, bar, status, go, later } = dialog._parts;
  title.textContent = t("AppUpd_Notice_Title", s.latest.version);
  meta.textContent = t("AppUpd_Size", fa((s.latest.size / 1048576).toFixed(1)), s.latest.date) + " · v" + s.current + " → v" + s.latest.version;
  const lines = (s.latest.notes || "").split("\n").map((l) => l.replace(/^[-•*]\s*/, "").trim()).filter(Boolean);
  list.replaceChildren(...(lines.length ? lines.map((l) => h("li", {}, l)) : [h("li", { class: "dim" }, t("AppUpd_NoNotes"))]));
  const working = s.state === "Downloading" || s.state === "Ready" || s.state === "Installing";
  bar.hidden = s.state !== "Downloading"; bar.firstChild.style.setProperty("--p", s.progress || 0);
  status.hidden = !(working || s.state === "Failed");
  status.className = `au-status ${s.state === "Failed" ? "fail" : "go"}`;
  status.textContent = s.state === "Downloading" ? t("AppUpd_Downloading", fa(Math.round((s.progress || 0) * 100))) : s.state === "Installing" || s.state === "Ready" ? t("AppUpd_Installing")
    : s.state === "Failed" ? t("AppUpd_Failed", s.error || "") : "";
  go.disabled = later.disabled = working;
  go.hidden = false; if (s.state === "Failed") { go.disabled = false; later.disabled = false; }
}
