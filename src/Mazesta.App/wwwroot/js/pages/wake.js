// What wakes the computer from sleep, one switch each: the devices Windows lets wake it (keyboard, mouse, USB, network cards), the wake timers of
// the power plan, Windows' maintenance, scheduled tasks that wake it and Wake-on-LAN; one button turns them all off, one puts back exactly what it
// turned off. Every answer is the state read back from Windows; a switch that Windows refused goes back to what Windows says and shows its error.
import { call } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { box } from "../groups.js";

const KINDS = [["input", "Wake_Kind_Input"], ["network", "Wake_Kind_Network"], ["usb", "Wake_Kind_Usb"], ["other", "Wake_Kind_Other"]];
const TIMER_CHOICES = [[1, "Wake_Timers_On"], [2, "Wake_Timers_Important"], [0, "Wake_Timers_Off"]];
const TIMER_WORD = { 0: "Wake_Timers_Off", 1: "Wake_Timers_On", 2: "Wake_Timers_Important" };

export function mount(el) {
  let state = null, showAll = false;
  const msg = h("p", { class: "msg", "aria-live": "polite" });
  const tiles = h("div", { class: "wake-tiles" });
  const devices = h("div"), timers = h("div"), net = h("div"), last = h("div");
  const allOff = h("button", { class: "btn primary", onclick: turnAllOff }, icon("bolt"), t("Wake_AllOff"));
  const undo = h("button", { class: "btn", onclick: () => act("wake.undo") }, icon("refresh"), t("Wake_Undo"));
  const reload = h("button", { class: "btn quiet", onclick: () => act("wake.state") }, icon("update"), t("Wake_Refresh"));
  const root = h("div", { class: "wake" });

  root.append(h("div", { class: "wake-bar" }, allOff, undo, reload), msg, tiles,
    h("div", { class: "panels flow" },
      box({ kind: "Power", ico: "keyboard", title: t("Wake_Dev_Title"), sub: t("Wake_Dev_Sub"), i: 0, a: "devices", wide: true,
        actions: h("label", { class: "wake-all" }, h("input", { type: "checkbox", class: "switch", onchange: (e) => { showAll = e.target.checked; renderDevices(); } }), t("Wake_ShowAll")),
        body: [devices, h("p", { class: "note" }, t("Wake_Dev_Note"))] }),
      box({ kind: "Power", ico: "clock", title: t("Wake_Timers_Title"), sub: t("Wake_Timers_Sub"), i: 1, a: "timers", body: timers }),
      box({ cls: "p-host", ico: "net", title: t("Wake_Net_Title"), sub: t("Wake_Net_Sub"), i: 2, a: "wol", body: [net, h("p", { class: "note" }, t("Wake_Net_Note"))] }),
      box({ cls: "p-tool", ico: "pulse", title: t("Wake_Last_Title"), sub: t("Wake_Last_Sub"), i: 3, a: "lastwake", body: [last, h("p", { class: "note" }, t("Wake_Shutdown_Note"))] })));
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Wake")), h("p", { class: "page-lede" }, t("Wake_Lede")))), root);

  const ok = (text) => { msg.className = "msg ok"; msg.textContent = text; };
  const fail = (text) => { msg.className = "msg fail"; msg.textContent = text; };
  async function act(method, params, doneKey = "Wake_Done") {
    root.inert = true; msg.className = "msg"; msg.textContent = t("Tools_Working");
    try {
      const r = await call(method, params);
      show(r);
      if (r.error) fail(t("Wake_Failed", r.error));
      else if (r.errors?.length) fail(t("Wake_Partial", fa(r.errors.length), r.errors.join(" · ")));
      else { ok(t(doneKey)); if (method !== "wake.state") toast(t(doneKey)); }
    } catch (e) { fail(String(e.message || e)); }
    finally { root.inert = false; }
  }
  function turnAllOff() { if (confirm(t("Wake_AllOff_Confirm"))) act("wake.all", {}, "Wake_AllDone"); }

  // A switch row: the name (and a second line), anything extra, and the switch. The change is asked of Windows, then the page shows what Windows says.
  function row(name, sub, checked, onChange, extra, disabled = false) {
    const sw = h("input", { type: "checkbox", class: "switch", "aria-label": name, onchange: (e) => onChange(e.target.checked) });
    sw.checked = !!checked; sw.disabled = disabled;
    return h("label", { class: "wake-row" }, h("span", { class: "wake-name" }, h("b", {}, name), sub ? h("small", {}, sub) : null), extra || null, sw);
  }
  const onOff = (v) => v === true ? t("Tools_State_On") : v === false ? t("Tools_State_Off") : t("Tools_State_Default");
  const tile = (key, value, on) => h("div", { class: `state-tile ${on ? "on" : ""}` }, h("span", { class: "k" }, t(key)), h("span", { class: "v" }, value));

  function renderDevices() {
    if (!state) return;
    if (state.readError) { devices.replaceChildren(h("p", { class: "msg fail" }, t("Wake_Dev_Unread", state.readError))); return; }
    const list = state.devices.filter((d) => showAll || d.armed), out = [];
    for (const [kind, key] of KINDS) {
      const ds = list.filter((d) => d.kind === kind);
      if (!ds.length) continue;
      out.push(h("h3", { class: "wake-kind" }, t(key), h("span", { class: "caption" }, `${fa(ds.filter((d) => d.armed).length)} / ${fa(ds.length)}`)));
      for (const d of ds) out.push(row(d.name, null, d.armed, (v) => act("wake.device", { name: d.name, on: v }), h("span", { class: `pill ${d.armed ? "warn" : "none"}` }, t(d.armed ? "Wake_Armed" : "Wake_Unarmed"))));
    }
    devices.replaceChildren(...(out.length ? out : [h("p", { class: "note" }, t("Wake_Dev_None"))]));
  }
  function renderTimers() {
    const { ac, dc } = state.timers, same = ac !== null && ac === dc;
    const choices = h("div", { class: "choice", role: "radiogroup", "aria-label": t("Wake_Timers_Setting") },
      TIMER_CHOICES.map(([v, key]) => { const r = h("input", { type: "radio", name: "wake-timers", onchange: () => act("wake.timers", { value: String(v) }) }); r.checked = same && ac === v; return h("label", {}, r, h("b", {}, t(key))); }));
    const tasks = state.tasks.length ? state.tasks.map((x) => h("div", { class: "wake-row" }, h("span", { class: "wake-name" }, h("b", {}, x.name), h("small", { class: "lat" }, x.path)),
      h("button", { class: "btn quiet", onclick: () => act("wake.task", { path: x.path, name: x.name }) }, t("Wake_Tasks_Off")))) : [h("p", { class: "note" }, t("Wake_Tasks_None"))];
    const active = h("details", { class: "wake-active" }, h("summary", {}, t("Wake_Active")), h("pre", { class: "console", "data-empty": t("Wake_Last_None") }, state.active.join("\n")));
    timers.replaceChildren(h("h3", { class: "wake-kind" }, t("Wake_Timers_Setting")), choices,
      same ? "" : h("p", { class: "note" }, t("Wake_Timers_Mixed")), h("p", { class: "note" }, t("Wake_Timers_Note")),
      row(t("Wake_Maint"), t("Wake_Maint_Note"), state.maintenance !== 0, (v) => act("wake.maintenance", { on: v })),
      h("h3", { class: "wake-kind" }, t("Wake_Tasks")), ...tasks, active);
  }
  function renderNet() {
    net.replaceChildren(...(state.nics.length ? state.nics.map((n) => {
      const known = n.magic !== null || n.pattern !== null;
      return row(n.name, n.description, n.magic === true || n.pattern === true, (v) => act("wake.nic", { name: n.name, on: v }),
        known ? h("span", { class: "wake-pills" }, h("span", { class: "pill none" }, `${t("Wake_Net_Magic")}: ${onOff(n.magic)}`), h("span", { class: "pill none" }, `${t("Wake_Net_Pattern")}: ${onOff(n.pattern)}`)) : h("small", {}, t("Wake_Net_Unknown")), !known);
    }) : [h("p", { class: "note" }, t("Wake_Net_None"))]));
  }
  function show(s) {
    state = s;
    const armed = s.devices.filter((d) => d.armed).length, wol = s.nics.filter((n) => n.magic === true || n.pattern === true).length;
    const timerOn = s.timers.ac !== 0 || s.timers.dc !== 0;
    tiles.replaceChildren(
      tile("Wake_Tile_Devices", fa(armed), armed > 0),
      tile("Wake_Tile_Timers", s.timers.ac === s.timers.dc && TIMER_WORD[s.timers.ac] ? t(TIMER_WORD[s.timers.ac]) : t("Wake_Timers_Mixed_Short"), timerOn),
      tile("Wake_Tile_Maint", onOff(s.maintenance !== 0), s.maintenance !== 0), tile("Wake_Tile_Tasks", fa(s.tasks.length), s.tasks.length > 0), tile("Wake_Tile_Wol", fa(wol), wol > 0));
    undo.disabled = !s.canUndo;
    renderDevices(); renderTimers(); renderNet();
    last.replaceChildren(s.last.length ? h("pre", { class: "console" }, s.last.join("\n")) : h("p", { class: "note" }, t("Wake_Last_None")));
  }

  act("wake.state");
}
