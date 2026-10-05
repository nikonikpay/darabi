// A release found on the site is announced once a run, with what changed in it, and can be installed from there with one click: the app downloads and
// checks it, closes, puts it in place and opens again by itself. "Later" leaves it to the update page (the settings entry stays marked).
import { call, on } from "./bridge.js";
import { t, fa } from "./i18n.js";
import { h, icon } from "./ui.js";
import { notesView } from "./releasenotes.js";

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
  const title = h("h2", { class: "upd-title", id: "upd-title" }), versions = h("div", { class: "upd-versions" }), facts = h("p", { class: "upd-facts" });
  const notes = h("div", { class: "upd-notes" });
  const pct = h("span", { class: "num upd-pct" }), what = h("span", { class: "upd-what" }), bar = h("div", { class: "progress" }, h("i"));
  const work = h("div", { class: "upd-work", hidden: true }, h("div", { class: "upd-work-line" }, what, pct), bar);
  const fail = h("p", { class: "au-status fail", hidden: true });
  const go = h("button", { class: "btn primary", type: "button", onclick: () => run() }, icon("update"), t("AppUpd_UpdateNow"));
  const later = h("button", { class: "btn", type: "button", onclick: () => close() }, t("AppUpd_Notice_Later"));
  dialog = h("dialog", { class: "upd-dialog", "aria-labelledby": "upd-title" },
    h("div", { class: "upd-box" },
      h("header", { class: "upd-head" }, h("span", { class: "upd-mark" }, icon("update")), h("div", {}, title, versions)),
      facts, h("h3", { class: "upd-sub" }, t("AppUpd_Notes")), notes, work, fail,
      h("p", { class: "upd-restart" }, icon("refresh"), h("span", {}, t("AppUpd_Notice_Restart"))),
      h("div", { class: "btn-row" }, go, later)));
  dialog.addEventListener("cancel", (e) => { if (current?.state === "Downloading" || current?.state === "Installing") e.preventDefault(); else close(); });
  document.body.append(dialog); dialog.showModal();
  dialog._parts = { title, versions, facts, notes, work, what, pct, bar, fail, go, later };
  paint(s);
  async function run() { try { paint(await call("upd.now")); } catch (e) { paint({ ...current, state: "Failed", error: String(e.message || e) }); } }
}

function close() { dialog?.close(); dialog?.remove(); dialog = null; }

function paint(s) {
  if (!dialog || !s?.latest) return;
  current = s; const { title, versions, facts, notes, work, what, pct, bar, fail, go, later } = dialog._parts;
  title.textContent = t("AppUpd_Notice_Title", s.latest.version);
  versions.replaceChildren(h("span", { class: "lat" }, `v${s.current}`), icon("arrow"), h("b", { class: "lat" }, `v${s.latest.version}`));
  facts.textContent = t("AppUpd_Size", fa((s.latest.size / 1048576).toFixed(1)), s.latest.date);
  const key = s.latest.version + "|" + s.latest.notes;
  if (notes.dataset.key !== key) { notes.dataset.key = key; notes.replaceChildren(notesView(s.latest.notes)); }
  const downloading = s.state === "Downloading", installing = s.state === "Ready" || s.state === "Installing", working = downloading || installing;
  work.hidden = !working;
  what.textContent = downloading ? t("AppUpd_Step_Download") : t("AppUpd_Installing");
  pct.textContent = downloading ? fa(Math.round((s.progress || 0) * 100)) + "%" : "";
  work.classList.toggle("busy", installing);
  bar.firstChild.style.setProperty("--p", downloading ? s.progress || 0 : 1);
  fail.hidden = s.state !== "Failed"; fail.textContent = s.state === "Failed" ? t("AppUpd_Failed", s.error || "") : "";
  go.disabled = later.disabled = working;
  if (s.state === "Failed") { go.disabled = false; later.disabled = false; }
}
