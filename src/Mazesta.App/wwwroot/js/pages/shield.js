// Firewall and antivirus, kept simple: two tabs. The firewall tab blocks a program (a file picked, or one of the programs running now) in both directions in the Windows firewall and
// lists what was blocked from here, each with an unblock button. The antivirus tab leaves a file or folder out of Windows Defender's scans and lists the exclusions. Below, two switches
// put the same two actions in Explorer's right-click menu. The firewall tab also lists every program blocked in Windows now (also what others blocked) and switches the firewall off and on;
// the antivirus tab stops all of Defender's guards, until the next start of the computer or for good.
//  Nothing is shown as done that Windows did not take: the host reads every change back before it answers.
import { call } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { box } from "../groups.js";

export function mount(el) {
  let tab = "fw", running = [], blocked = [], fw = null, av = null, busy = false;
  const fwStatus = h("p", { class: "sh-status", "aria-live": "polite" }), avStatus = h("p", { class: "sh-status", "aria-live": "polite" }), avNote = h("p", { class: "note", hidden: true });
  const search = h("input", { class: "field", type: "search", placeholder: t("Shield_Fw_Search"), "aria-label": t("Shield_Fw_Search"), autocomplete: "off", spellcheck: false, oninput: () => drawRunning() });
  const runningList = h("ul", { class: "sh-list sh-scroll" }), blockedList = h("ul", { class: "sh-list" }), excludedList = h("ul", { class: "sh-list" });
  const seg = h("div", { class: "seg", role: "group", "aria-label": t("Nav_Shield") },
    ["fw", "av"].map((k) => h("button", { type: "button", "data-tab": k, "aria-pressed": String(tab === k), onclick: () => show(k) }, t(k === "fw" ? "Shield_Tab_Firewall" : "Shield_Tab_Av"))));
  const menuFw = h("input", { type: "checkbox", class: "switch", "aria-label": t("Shield_Menu_Fw"), onchange: (e) => menu("firewall", e.target) });
  const menuAv = h("input", { type: "checkbox", class: "switch", "aria-label": t("Shield_Menu_Av"), onchange: (e) => menu("antivirus", e.target) });

  const pickExe = h("button", { class: "btn primary", type: "button", onclick: () => pick("exe", (p) => block(p)) }, icon("folder"), t("Shield_Fw_Pick"));
  const reload = h("button", { class: "btn", type: "button", onclick: () => { loadRunning(); loadFirewall(); } }, icon("refresh"), t("Shield_Refresh"));
  const pickFile = h("button", { class: "btn primary", type: "button", onclick: () => pick("file", (p) => exclude(p)) }, icon("doc"), t("Shield_Av_File"));
  const pickFolder = h("button", { class: "btn primary", type: "button", onclick: () => pick("folder", (p) => exclude(p)) }, icon("folder"), t("Shield_Av_Folder"));

  const fwSwitch = h("button", { class: "btn", type: "button", onclick: () => fwSwitchNow() }), fwSwitchNote = h("p", { class: "note" });
  const avSecurity = h("button", { class: "btn primary", type: "button", onclick: () => call("shield.openSecurity") }, t("Shield_Open_SecurityBtn")), avCtlNote = h("p", { class: "note" });

  const fwView = h("div", { class: "sh-view" }, fwStatus,
    box({ cls: "p-caution", ico: "stop", title: t("Shield_Fw_Ctl_Title"), sub: t("Shield_Fw_Ctl_Sub"), i: 0, a: "shield-fw-switch", body: [h("div", { class: "btn-row" }, fwSwitch), fwSwitchNote] }),
    h("div", { class: "panels two" },
      box({ cls: "p-tool", ico: "shield", title: t("Shield_Fw_Title"), sub: t("Shield_Fw_Sub"), i: 0, a: "shield-block",
        body: [h("div", { class: "btn-row" }, pickExe, reload), h("p", { class: "note" }, t("Shield_Fw_Or")), search, runningList] }),
      box({ cls: "p-caution", ico: "stop", title: t("Shield_Blocked"), sub: t("Shield_Blocked_Sub"), i: 1, a: "shield-blocked", body: blockedList })));
  const avView = h("div", { class: "sh-view", hidden: true }, avStatus, avNote,
    box({ cls: "p-caution", ico: "stop", title: t("Shield_Av_Ctl_Title"), sub: t("Shield_Av_Ctl_Sub"), i: 0, a: "shield-av-off", body: [h("div", { class: "btn-row" }, avSecurity), avCtlNote, h("p", { class: "note" }, t("Shield_Av_Ctl_Note"))] }),
    h("div", { class: "panels two" },
      box({ cls: "p-tool", ico: "shield", title: t("Shield_Av_Title"), sub: t("Shield_Av_Sub"), i: 0, a: "shield-exclude",
        body: [h("div", { class: "btn-row" }, pickFile, pickFolder), h("p", { class: "note" }, t("Shield_Av_Warn"))] }),
      box({ cls: "p-caution", ico: "check", title: t("Shield_Av_List"), sub: t("Shield_Av_List_Sub"), i: 1, a: "shield-excluded", body: excludedList })));
  const menuBox = box({ cls: "p-win", ico: "sliders", title: t("Shield_Menu_Title"), sub: t("Shield_Menu_Sub"), i: 2, a: "shield-menu",
    body: [h("label", { class: "tw-pref" }, menuFw, h("span", { class: "tw-pref-t" }, h("b", {}, t("Shield_Menu_Fw")))), h("label", { class: "tw-pref" }, menuAv, h("span", { class: "tw-pref-t" }, h("b", {}, t("Shield_Menu_Av")))),
      h("p", { class: "note" }, t("Shield_Menu_Note"))] });

  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Shield")), h("p", { class: "page-lede" }, t("Shield_Lede"))), seg), fwView, avView, menuBox);

  function show(k) { tab = k; for (const b of seg.children) b.setAttribute("aria-pressed", String(b.dataset.tab === k)); fwView.hidden = k !== "fw"; avView.hidden = k !== "av"; }

  const row = (main, sub, tail) => h("li", { class: "sh-row" }, h("span", { class: "sh-main" }, h("b", { class: "sh-name" }, main), sub ? h("span", { class: "sh-path lat", title: sub }, sub) : null), tail);
  const empty = (key) => h("li", { class: "sh-empty" }, t(key));
  const norm = (p) => String(p || "").toLowerCase();

  function drawRunning() {
    const q = search.value.trim().toLowerCase(), done = new Set(blocked.map((b) => norm(b.path)));
    const rows = running.filter((p) => !q || p.name.toLowerCase().includes(q) || p.path.toLowerCase().includes(q)).slice(0, 200).map((p) => {
      const already = done.has(norm(p.path));
      return row(p.name + (p.count > 1 ? ` ×${fa(p.count)}` : ""), p.path, already ? h("span", { class: "pill warn" }, t("Shield_Is_Blocked"))
        : h("button", { class: "btn", type: "button", disabled: busy, onclick: () => block(p.path) }, t("Shield_Block")));
    });
    runningList.replaceChildren(...(rows.length ? rows : [empty("Shield_Fw_NoRunning")]));
  }

  function drawBlocked() {
    const rows = blocked.map((b) => row(b.name, b.path, h("span", { class: "sh-tail" },
      b.ours === false ? h("span", { class: "pill none", title: t("Shield_Other_Rule") }, t("Shield_Other_Rule")) : null,
      h("span", { class: `pill ${b.inbound && b.outbound ? "pass" : "warn"}` }, t(b.inbound && b.outbound ? "Shield_Both" : b.inbound ? "Shield_In" : "Shield_Out")),
      h("button", { class: "btn", type: "button", disabled: busy, onclick: () => { if (b.ours !== false || confirm(`${t("Shield_Confirm_Other")}

${b.path}`)) unblock(b.path); } }, t("Shield_Unblock")))));
    blockedList.replaceChildren(...(rows.length ? rows : [empty("Shield_None_Blocked")]));
  }

  function drawExcluded() {
    const rows = (av?.exclusions || []).map((x) => row(x.path.split("\\").filter(Boolean).pop() || x.path, x.path, h("span", { class: "sh-tail" },
      x.exists ? h("span", { class: "pill none" }, t(x.folder ? "Shield_Kind_Folder" : "Shield_Kind_File")) : h("span", { class: "pill warn" }, t("Shield_Kind_Gone")),
      h("button", { class: "btn", type: "button", disabled: busy, onclick: () => unexclude(x.path) }, t("Shield_Unexclude")))));
    excludedList.replaceChildren(...(rows.length ? rows : [empty(av?.ok ? "Shield_Av_None" : "Shield_Av_Unknown")]));
  }

  function drawStatus() {
    if (!fw) fwStatus.replaceChildren(h("span", { class: "pill none" }, t("Shield_Fw_Unknown")));
    else {
      const off = fw.profiles.filter((p) => !p.on).map((p) => p.name);
      fwStatus.replaceChildren(off.length ? h("span", { class: "pill warn" }, t("Shield_Fw_Off", off.join("، "))) : h("span", { class: "pill pass" }, t("Shield_Fw_On")));
    }
    if (!av?.ok) avStatus.replaceChildren(h("span", { class: "pill none" }, t("Shield_Av_Unknown")));
    else avStatus.replaceChildren(!av.defender ? h("span", { class: "pill warn" }, t("Shield_Av_NoDefender")) : h("span", { class: `pill ${av.realTime ? "pass" : "warn"}` }, t(av.realTime ? "Shield_Av_On" : "Shield_Av_RealOff")));
    const fwOff = !!fw && fw.profiles.some((p) => !p.on);
    fwSwitch.hidden = !fw; fwSwitch.className = `btn ${fwOff ? "primary" : ""}`; fwSwitch.textContent = t(fwOff ? "Shield_Fw_TurnOn" : "Shield_Fw_TurnOff");
    const on = !!av?.ok && av.defender && av.stopped;
    avCtlNote.hidden = !on; avCtlNote.textContent = on ? t(av.policy || !av.restore ? "Shield_Av_Ctl_Perm" : "Shield_Av_Ctl_Pending") : "";
    const notes = [];
    if (av?.ok && av.other) notes.push(t("Shield_Av_Other", av.other));
    if (av?.ok && av.tamper) notes.push(t("Shield_Av_Tamper"));
    avNote.hidden = !notes.length; avNote.textContent = notes.join(" ");
  }

  function setBusy(on) { busy = on; for (const b of el.querySelectorAll(".sh-list button, .btn")) b.disabled = on; if (!on) { drawRunning(); drawBlocked(); drawExcluded(); } }

  async function guard(fn) {
    if (busy) return; setBusy(true);
    try { return await fn(); } catch (e) { toast(String(e.message || e), "fail"); } finally { setBusy(false); }
  }

  async function loadFirewall() { try { const r = await call("shield.firewall"); fw = r.ok ? r : null; blocked = r.ok ? r.blocked : blocked; } catch { fw = null; } drawStatus(); drawBlocked(); drawRunning(); }
  async function loadAv() { try { av = await call("shield.av"); } catch { av = null; } drawStatus(); drawExcluded(); }
  async function loadRunning() { try { running = await call("shield.processes"); } catch { running = []; } drawRunning(); }
  async function loadMenu() { try { const m = await call("shield.menu"); menuFw.checked = m.firewall; menuAv.checked = m.antivirus; } catch { /* the switches stay off */ } }

  // The reply of a change: done, or the reason, or a question (a program of Windows' own, a folder as wide as a drive) that the user answers before it is sent again with force.
  function finish(r, doneKey, name) { if (r.ok) toast(t(doneKey, name), "ok"); else toast(r.error, "fail"); return r.ok; }
  const nameOf = (p) => p.split("\\").filter(Boolean).pop() || p;

  const block = (path) => guard(async () => { await changeWith("shield.block", path, "Shield_Done_Block", nameOf(path)); await loadFirewall(); });
  const unblock = (path) => guard(async () => { await changeWith("shield.unblock", path, "Shield_Done_Unblock", nameOf(path)); await loadFirewall(); });
  const exclude = (path) => guard(async () => { await changeWith("shield.exclude", path, "Shield_Done_Exclude", nameOf(path)); await loadAv(); });
  const unexclude = (path) => guard(async () => { await changeWith("shield.unexclude", path, "Shield_Done_Unexclude", nameOf(path)); await loadAv(); });
  async function changeWith(method, path, doneKey, name) {
    let r = await call(method, { path });
    if (!r.ok && r.ask && confirm(`${r.confirm}\n\n${path}`)) r = await call(method, { path, force: true });
    finish(r, doneKey, name);
  }
  // The firewall and antivirus switches: asked first when the computer is left unguarded, then read back from Windows by the host.
  const fwSwitchNow = () => guard(async () => {
    const on = !!fw && fw.profiles.some((p) => !p.on);
    if (!on && !confirm(t("Shield_Ask_FwOff"))) return;
    const r = await call("shield.fwSwitch", { on }); if (r.ok) toast(t(on ? "Shield_Done_FwOn" : "Shield_Done_FwOff"), "ok"); else toast(r.error, "fail");
    await loadFirewall();
  });
  async function pick(kind, then) { if (busy) return; const r = await call("shield.pick", { kind }); if (r?.path) then(r.path); }

  async function menu(kind, input) {
    input.disabled = true;
    try {
      const r = await call("shield.menuSet", { kind, on: input.checked });
      if (r.error) { toast(t("Shield_Menu_Failed", r.error), "fail"); input.checked = !input.checked; } else { menuFw.checked = r.firewall; menuAv.checked = r.antivirus; }
    } catch (e) { toast(String(e.message || e), "fail"); input.checked = !input.checked; } finally { input.disabled = false; }
  }

  drawRunning(); drawBlocked(); drawExcluded(); drawStatus();
  loadMenu(); loadFirewall(); loadAv(); loadRunning();
}
