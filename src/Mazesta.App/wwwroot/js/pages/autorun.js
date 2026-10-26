// What starts with Windows: the programs' own services (tick the ones to switch off; one button puts back exactly those) and the startup programs (one switch each,
// the same one Task Manager has). Both lists are searchable. Every answer is the state read back from Windows.
import { call } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { box } from "../groups.js";

export function mount(el) {
  let state = null, query = "";
  const picked = new Set();
  const msg = h("p", { class: "msg", "aria-live": "polite" });
  const svcList = h("div", { class: "autorun-list" }), startList = h("div", { class: "autorun-list" });
  const root = h("div", { class: "autorun" });
  const search = h("input", { type: "search", class: "field", placeholder: t("Autorun_Search"), "aria-label": t("Autorun_Search"), oninput: (e) => { query = e.target.value.trim().toLowerCase(); render(); } });
  const count = h("span", { class: "caption" });
  const off = h("button", { class: "btn primary", type: "button", onclick: disable }, icon("stop"), t("Autorun_Svc_Off"));
  const undo = h("button", { class: "btn", type: "button", onclick: () => act("autorun.undo", {}, "Autorun_UndoDone") }, icon("refresh"), t("Autorun_Svc_Undo"));
  const all = h("button", { class: "btn quiet", type: "button", onclick: toggleAll }, t("Autorun_SelectAll"));
  const reload = h("button", { class: "btn quiet", type: "button", onclick: () => act("autorun.state") }, icon("update"), t("Autorun_Refresh"));

  root.append(h("div", { class: "wake-bar" }, search, reload), msg,
    h("div", { class: "panels flow" },
      box({ cls: "p-tool", ico: "gear", title: t("Autorun_Svc_Title"), sub: t("Autorun_Svc_Sub"), i: 0, a: "services", wide: true,
        actions: h("div", { class: "btn-row" }, all, count),
        body: [svcList, h("div", { class: "btn-row" }, off, undo), h("p", { class: "note" }, t("Autorun_Svc_Note"))] }),
      box({ cls: "p-host", ico: "bolt", title: t("Autorun_Start_Title"), sub: t("Autorun_Start_Sub"), i: 1, a: "startup", wide: true, body: [startList, h("p", { class: "note" }, t("Autorun_Start_Note"))] })));
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Autorun")), h("p", { class: "page-lede" }, t("Autorun_Lede")))), root);

  const ok = (text) => { msg.className = "msg ok"; msg.textContent = text; };
  const fail = (text) => { msg.className = "msg fail"; msg.textContent = text; };
  async function act(method, params, doneKey = "Autorun_Done") {
    root.inert = true; msg.className = "msg"; msg.textContent = t("Tools_Working");
    try {
      const r = await call(method, params);
      show(r);
      if (r.errors?.length) fail(t("Autorun_Failed", r.errors.join(" · ")));
      else { ok(t(doneKey)); if (method !== "autorun.state") toast(t(doneKey)); }
    } catch (e) { fail(String(e.message || e)); }
    finally { root.inert = false; }
  }
  function disable() {
    if (!picked.size) { fail(t("Autorun_None_Picked")); return; }
    if (!confirm(t("Autorun_Confirm", fa(picked.size)))) return;
    const names = [...picked]; picked.clear(); act("autorun.services", { names });
  }
  const matches = (...parts) => !query || parts.some((p) => (p || "").toLowerCase().includes(query));
  const visible = () => (state?.services || []).filter((s) => matches(s.name, s.display, s.note, s.path));
  function toggleAll() { const v = visible(), every = v.every((s) => picked.has(s.name)); for (const s of v) every ? picked.delete(s.name) : picked.add(s.name); render(); }

  function render() {
    if (!state) return;
    const svcs = visible();
    svcList.replaceChildren(...(svcs.length ? svcs.map((s) => {
      const cb = h("input", { type: "checkbox", "aria-label": s.display, onchange: (e) => { e.target.checked ? picked.add(s.name) : picked.delete(s.name); count.textContent = t("Autorun_Selected", fa(picked.size)); } });
      cb.checked = picked.has(s.name);
      return h("label", { class: "wake-row" }, cb, h("span", { class: "wake-name" }, h("b", {}, s.display), h("small", {}, s.note || ""), h("small", { class: "lat" }, `${s.name} · ${s.path}`)),
        s.running ? h("span", { class: "pill ok" }, t("Autorun_Running")) : h("span", { class: "pill none" }, t("Autorun_Stopped")));
    }) : [h("p", { class: "note" }, t("Autorun_Svc_None"))]));
    count.textContent = t("Autorun_Selected", fa(picked.size));
    const apps = state.startup.filter((s) => matches(s.name, s.command));
    startList.replaceChildren(...(apps.length ? apps.map((s) => {
      const sw = h("input", { type: "checkbox", class: "switch", "aria-label": s.name, onchange: (e) => act("autorun.startup", { id: s.id, on: e.target.checked }) });
      sw.checked = s.enabled;
      return h("label", { class: "wake-row" }, h("span", { class: "wake-name" }, h("b", {}, s.name), h("small", { class: "lat" }, s.command)), sw);
    }) : [h("p", { class: "note" }, t("Autorun_Start_None"))]));
  }
  function show(s) {
    state = s; for (const n of [...picked]) if (!s.services.some((x) => x.name === n)) picked.delete(n);
    undo.disabled = !s.held.length; render();
  }

  act("autorun.state");
}
