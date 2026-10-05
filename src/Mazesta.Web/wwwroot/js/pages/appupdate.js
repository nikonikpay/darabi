// The app's own updates, as other programs have them: the installed version, a check against the shop's site, then download and install with one
// button each (the host checks the shop's signature and every file's hash; Data is never touched). Below, the benchmark comparison lists, which
// the host fetches on its own a little after start-up; the button fetches them now.
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { box } from "../groups.js";
import { boot } from "../app.js";
import { notesView } from "../releasenotes.js";

export function mount(el) {
  const version = h("span", { class: "num au-ver" }), status = h("p", { class: "au-status" }), checked = h("p", { class: "caption" });
  const bar = h("div", { class: "progress", hidden: true }, h("i")), notes = h("div", { class: "au-notes", hidden: true });
  const check = h("button", { class: "btn", onclick: () => run("upd.check") }, icon("refresh"), t("AppUpd_Check"));
  // One button: download, check, install, and the app closes and opens again by itself.
  const now = h("button", { class: "btn primary", hidden: true, onclick: () => run("upd.now") }, icon("update"), t("AppUpd_UpdateNow"));
  const restart = h("p", { class: "note", hidden: true }, t("AppUpd_Notice_Restart"));
  const lists = h("p", { class: "au-status" }), published = h("p", { class: "caption" });
  const sync = h("button", { class: "btn", onclick: () => run("upd.data") }, icon("refresh"), t("AppUpd_Data_Sync"));
  // The link to the shop's site: the key this copy sends with, and what the site said to it.
  const keyField = h("input", { class: "field lat", type: "password", autocomplete: "off", spellcheck: false, style: { minWidth: "260px" }, "aria-label": t("Site_Key"), placeholder: "mz_…" });
  const siteState = h("p", { class: "au-status" });
  const saveKey = h("button", { class: "btn primary", onclick: () => link("site.key", { value: keyField.value }) }, t("Site_Key_Save"));
  const checkSite = h("button", { class: "btn", onclick: () => link("site.check") }, icon("refresh"), t("Site_Check"));
  // Without typing the key: the site's dashboard opens in the browser, a manager signed in there approves this copy, the key comes by itself.
  const pair = h("button", { class: "btn go", "data-a": "site-pair", onclick: () => link("site.pair", { cmd: pairing ? "cancel" : "start" }) }, icon("net"), t("Site_Pair_Start"));
  const pairState = h("p", { class: "au-status go", hidden: true });
  let pairing = false;
  async function link(method, args) {
    try { showSite(await call(method, args)); keyField.value = ""; } catch (e) { toast(String(e.message || e), "fail"); }
  }
  function showSite(s) {
    if (!s) return;
    saveKey.disabled = checkSite.disabled = s.busy;
    pairing = !!s.pairCode; pair.lastChild.textContent = t(pairing ? "Site_Pair_Cancel" : "Site_Pair_Start");
    pairState.hidden = !pairing; pairState.replaceChildren(...(pairing ? [t("Site_Pair_Waiting"), " ", h("b", { class: "lat" }, s.pairCode)] : []));
    keyField.placeholder = s.hasKey ? "••••••••" : "mz_…";
    const st = s.status;
    siteState.classList.toggle("fail", !!s.error || st?.key === "wrong"); siteState.classList.toggle("go", st?.key === "ok");
    siteState.textContent = s.error ? s.error : !st ? t("Site_State_Unknown")
      : st.key === "ok" ? [t("Site_State_Ok", st.version, fa(st.reports ?? 0), fa(st.runs ?? 0)), st.pending ? t("Site_State_Pending", fa(st.pending)) : ""].filter(Boolean).join(" ")
      : st.key === "wrong" ? t("Site_State_Wrong") : t("Site_State_NoKey", st.version);
  }
  let last = null;

  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_AppUpdate")), h("p", { class: "page-lede" }, t("AppUpd_Lede")))),
    h("div", { class: "panels flow", style: { marginTop: 0 } },
      box({ kind: "Cpu", ico: "update", title: t("AppUpd_Version"), sub: h("span", {}, "v", version), i: 0,
        body: [status, bar, notes, h("div", { class: "btn-row" }, now, check), restart, checked, boot.staff ? h("p", { class: "note" }, t("AppUpd_StaffNote")) : null] }),
      box({ kind: "Gpu", ico: "trophy", title: t("AppUpd_Data_Title"), sub: t("AppUpd_Data_Sub"), i: 1,
        body: [lists, published, h("p", { class: "note" }, t("AppUpd_Data_Note")), h("div", { class: "btn-row" }, sync)] }),
      boot.staff ? box({ kind: "Storage", ico: "net", title: t("Site_Title"), sub: t("Site_Sub"), i: 3, a: "site",
        body: [siteState, h("label", { class: "caption", style: { display: "block" } }, t("Site_Key"), " · ", t("Site_Key_Hint")),
          h("div", { class: "btn-row" }, pair), pairState, h("p", { class: "note" }, t("Site_Pair_Note")),
          h("div", { class: "btn-row" }, keyField, saveKey, checkSite), h("p", { class: "note" }, t("Site_Note"))] }) : null));

  async function run(method) {
    try { render(await call(method)); } catch (e) { toast(String(e.message || e), "fail"); }
  }
  function render(s) {
    if (!s) return;
    last = s;
    const busy = s.state === "Checking" || s.state === "Downloading" || s.state === "Installing";
    version.textContent = s.current;
    const v = s.latest?.version || "";
    status.textContent = {
      Idle: t("AppUpd_Idle"), Checking: t("AppUpd_Checking"), UpToDate: t("AppUpd_UpToDate"), Available: t("AppUpd_Available", v),
      Downloading: t("AppUpd_Downloading", fa(Math.round(s.progress * 100))), Ready: t("AppUpd_Ready", v), Installing: t("AppUpd_Installing"),
      Failed: t("AppUpd_Failed", s.error || ""),
    }[s.state] || "";
    status.classList.toggle("fail", s.state === "Failed"); status.classList.toggle("go", s.state === "Available" || s.state === "Ready");
    bar.hidden = s.state !== "Downloading"; bar.firstChild.style.setProperty("--p", s.progress);
    const offer = s.latest && ["Available", "Downloading", "Ready"].includes(s.state);
    notes.hidden = !offer;
    if (offer) notes.replaceChildren(h("div", { class: "k" }, t("AppUpd_Notes"), " · ", h("span", { class: "lat" }, `v${v}`), " · ", t("AppUpd_Size", fa((s.latest.size / 1048576).toFixed(1)), s.latest.date)), notesView(s.latest.notes));
    check.disabled = busy || s.dataBusy; sync.disabled = busy || s.dataBusy;
    now.hidden = !["Available", "Ready", "Downloading", "Installing"].includes(s.state) || !s.canInstall; now.disabled = s.state === "Downloading" || s.state === "Installing"; restart.hidden = now.hidden;
    checked.textContent = s.checkedAt ? t("AppUpd_Checked", s.checkedAt) : "";
    lists.textContent = s.data.lists ? t("AppUpd_Data_Lists", fa(s.data.lists)) : t("AppUpd_Data_None");
    published.textContent = [s.data.published ? t("AppUpd_Data_Published", s.data.published) : "", s.data.syncedAt ? t("AppUpd_Data_Synced", s.data.syncedAt, fa(s.data.downloaded)) : ""].filter(Boolean).join(" · ");
  }
  call("upd.state").then(render);
  if (boot.staff) call("site.state").then(showSite).then(() => call("site.check")).then(showSite).catch(() => {});
  const offSite = on("site", showSite), offUpd = on("upd", render);
  return () => { offSite(); offUpd(); };
}
