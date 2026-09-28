// Windows tweaks, as WinUtil lists them: essential and advanced changes are ticked and run together (or undone together), preferences are
// switches applied on the click, and the DNS resolver is one choice for every connected adapter. Each row says what the registry says now,
// read back after every change; a one-off action (a restore point, removing temporary files) has no state and no undo.
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { box } from "../groups.js";

const STAMP = { Applied: ["pass", "Tweaks_State_Applied"], NotApplied: ["none", "Tweaks_State_NotApplied"], Partial: ["warn", "Tweaks_State_Partial"] };

export function mount(el) {
  const ticked = new Set();
  const rows = new Map();   // id -> { row, check, stamp }
  const essential = h("div", { class: "tw-list" }), advanced = h("div", { class: "tw-list" }), prefs = h("div", { class: "tw-prefs" });
  const dnsList = h("div", { class: "dns-list" }), dnsMsg = h("p", { class: "msg" });
  const status = h("span", { class: "caption tw-status", "aria-live": "polite" }), log = h("ul", { class: "tw-log" });
  const run = h("button", { class: "btn go", onclick: () => go(false) }, icon("play"), t("Tweaks_Run"));
  const undo = h("button", { class: "btn", onclick: () => go(true) }, icon("refresh"), t("Tweaks_Undo"));
  const preset = (key, fn) => h("button", { type: "button", onclick: fn }, t(key));
  let state = null;

  el.append(
    h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Tweaks")), h("p", { class: "page-lede" }, t("Tweaks_Lede"))),
      h("div", { class: "seg tw-presets", role: "group", "aria-label": t("Tweaks_Presets") },
        preset("Tweaks_Preset_Standard", () => pick(state.presets.standard)), preset("Tweaks_Preset_Minimal", () => pick(state.presets.minimal)),
        preset("Tweaks_Preset_Applied", () => pick(state.tweaks.filter((x) => x.state === "Applied" && x.group !== "Preference").map((x) => x.id))),
        preset("Tweaks_Preset_Clear", () => pick([])))),
    h("div", { class: "tw-grid" },
      h("div", { class: "tw-col" },
        box({ cls: "p-tool", ico: "check", title: t("Tweaks_Essential"), sub: t("Tweaks_Essential_Sub"), i: 0, body: essential }),
        box({ cls: "p-caution", ico: "alert", title: t("Tweaks_Advanced"), sub: t("Tweaks_Advanced_Sub"), i: 1, body: advanced })),
      h("div", { class: "tw-col" },
        box({ cls: "p-win", ico: "sliders", title: t("Tweaks_Prefs"), sub: t("Tweaks_Prefs_Sub"), i: 2, body: prefs }),
        box({ kind: "Network", title: t("Dns_Title"), sub: t("Dns_Sub"), i: 3, body: [dnsList, dnsMsg] }))),
    log,
    h("div", { class: "dock" }, run, undo, status, h("span", { class: "grow" }), h("span", { class: "caption" }, t("Tweaks_Note"))));

  function pick(ids) { ticked.clear(); for (const id of ids) ticked.add(id); sync(); }
  function sync() {
    for (const [id, r] of rows) if (r.check) r.check.checked = ticked.has(id);
    const n = ticked.size;
    run.disabled = undo.disabled = !n || state?.busy;
    status.textContent = n ? t("Tweaks_Selected", fa(n)) : t("Tweaks_NothingTicked");
  }

  function stampOf(x) {
    if (x.action) return h("span", { class: "pill none" }, t("Tweaks_Action"));
    const [cls, key] = STAMP[x.state] || ["none", "Tweaks_State_Unknown"];
    return h("span", { class: `pill ${cls}` }, t(key));
  }

  function tweakRow(x) {
    const check = h("input", { type: "checkbox", class: "check", "aria-label": x.name, onchange: (e) => { e.target.checked ? ticked.add(x.id) : ticked.delete(x.id); sync(); } });
    const stamp = h("span", {}, stampOf(x));
    const row = h("label", { class: "tw-row" }, check,
      h("span", { class: "tw-name" }, x.name, x.restart ? h("span", { class: "tw-restart", title: t("Tweaks_Restart") }, icon("refresh")) : null),
      stamp, x.note ? h("span", { class: "tw-note" }, x.note) : null);
    rows.set(x.id, { row, check, stamp });
    return row;
  }

  function prefRow(x) {
    const sw = h("input", { type: "checkbox", class: "switch", "aria-label": x.name, checked: x.state === "Applied", onchange: (e) => setPref(x, e.target.checked, sw) });
    sw.indeterminate = x.state === "Partial";
    const row = h("label", { class: "tw-pref" }, sw, h("span", {}, x.name));
    rows.set(x.id, { row, sw });
    return row;
  }

  async function setPref(x, onState, sw) {
    sw.disabled = true;
    try {
      const r = await call("tweaks.pref", { id: x.id, on: onState });
      sw.checked = r.state === "Applied"; sw.indeterminate = r.state === "Partial";
      if (r.error) toast(t("Tweaks_Failed", x.name, r.error), "fail");
    } catch (e) { toast(String(e.message || e), "fail"); }
    finally { sw.disabled = false; }
  }

  async function go(back) {
    if (!ticked.size) return;
    const list = state.tweaks.filter((x) => ticked.has(x.id));
    if (!back && list.some((x) => x.group === "Advanced") && !confirm(t("Tweaks_ConfirmAdvanced"))) return;
    run.disabled = undo.disabled = true; log.replaceChildren();
    try {
      const r = await call("tweaks.run", { ids: [...ticked], undo: back });
      for (const x of r.results) {
        const row = rows.get(x.id);
        if (row?.stamp) row.stamp.replaceChildren(stampOf({ ...state.tweaks.find((y) => y.id === x.id), state: x.state }));
        log.append(h("li", { class: x.error ? "fail" : "ok" }, icon(x.error ? "x" : "check"), h("b", {}, x.name), " ", x.error ? h("span", { class: "lat" }, x.error) : x.done || t(back ? "Tweaks_Undone" : "Tweaks_Done")));
      }
      const failed = r.results.filter((x) => x.error).length;
      toast(failed ? t("Tweaks_SomeFailed", fa(failed)) : t(back ? "Tweaks_AllUndone" : "Tweaks_AllDone"), failed ? "fail" : "ok");
    } catch (e) { toast(String(e.message || e), "fail"); }
    finally { status.textContent = ""; sync(); }
  }

  function showDns(d) {
    const current = new Set(d.adapters.map((a) => a.provider));
    const choice = (id, label, servers) => h("button", { class: `dns-opt ${current.size === 1 && current.has(id) ? "on" : ""}`, type: "button", onclick: () => setDns(id) },
      h("b", {}, label), servers ? h("span", { class: "lat" }, servers.join("  ")) : h("span", {}, t("Dns_Auto_Sub")));
    dnsList.replaceChildren(
      h("div", { class: "dns-opts" }, choice("auto", t("Dns_Auto")), ...d.providers.map((p) => choice(p.id, t(`Dns_${p.id}`), p.servers))),
      d.adapters.length ? h("dl", { class: "kv" }, d.adapters.flatMap((a) => [h("dt", { class: "lat" }, a.name), h("dd", { class: "lat" }, a.servers.join("  ") || "—")]))
        : h("p", { class: "caption" }, t("Dns_NoAdapter")));
  }
  async function setDns(id) {
    dnsMsg.className = "msg"; dnsMsg.textContent = t("Tools_Working");
    try {
      const r = await call("tweaks.dns", { provider: id });
      showDns(r.dns); dnsMsg.className = `msg ${r.error ? "fail" : "ok"}`; dnsMsg.textContent = r.error || t("Dns_Done");
    } catch (e) { dnsMsg.className = "msg fail"; dnsMsg.textContent = String(e.message || e); }
  }

  function render(s) {
    state = s; rows.clear();
    essential.replaceChildren(...s.tweaks.filter((x) => x.group === "Essential").map(tweakRow));
    advanced.replaceChildren(...s.tweaks.filter((x) => x.group === "Advanced").map(tweakRow));
    prefs.replaceChildren(...s.tweaks.filter((x) => x.group === "Preference").map(prefRow));
    showDns(s.dns); sync();
  }
  call("tweaks.state").then(render);
  return on("tweakProgress", (p) => { status.textContent = t("Tweaks_Running", p.name); });
}
