// Windows and game tools, in three sections: the connection and games (the DNS resolver with its speed test, the power plan, the game switches),
// repair and clean-up (Windows' own repair tools, their real output streaming below them; Windows' own windows; hibernation and Fast Startup),
// and memory and hosts (the virtual memory, the hosts file). Every change is one
// button, is read back from Windows afterwards, and says what Windows answered; nothing runs on its own.
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { box } from "../groups.js";
import { gamingBoxes, gameBoostBox } from "./gaming.js";
import { dnsBox } from "./dns.js";
import { netfixBox } from "./netfix.js";
import { crashesBox } from "./crashes.js";

export function mount(el) {
  const gaming = gamingBoxes(1);
  // ——— Repair: sfc and DISM ———
  const btn = (cmd, key, cls = "btn") => h("button", { class: cls, "data-cmd": cmd, onclick: () => call("tools.exec", { cmd }) }, t(key));
  const repair = [btn("sfc", "Tools_Sfc", "btn primary"), btn("dismScan", "Tools_DismScan"), btn("dismRestore", "Tools_DismRestore")];
  const cancel = h("button", { class: "btn stop", onclick: () => call("tools.exec", { cmd: "cancel" }) }, icon("stop"), t("Test_Cancel"));
  const status = h("p", { class: "msg" }), bar = h("div", { class: "progress" }, h("i"));
  const consoleEl = h("pre", { class: "console", "aria-live": "off", "data-empty": t("Web_Tools_ConsoleEmpty") });

  // ——— Hibernation and Fast Startup ———
  const powerTiles = h("div", { class: "states" }), powerMsg = h("p", { class: "msg" });
  const hibOff = h("button", { class: "btn primary", onclick: () => setHibernate(false) }, t("Tools_Hib_Off"));
  const hibOn = h("button", { class: "btn", onclick: () => setHibernate(true) }, t("Tools_Hib_On"));

  // ——— Virtual memory ———
  const vmInUse = h("dl", { class: "kv" }), vmMsg = h("p", { class: "msg" }), vmPending = h("div", { class: "pending", hidden: true });
  const radio = (value, key, noteKey) => h("label", {}, h("input", { type: "radio", name: "vm", value, onchange: syncVm }), h("b", {}, t(key)), h("small", {}, t(noteKey)));
  const choice = h("div", { class: "choice", role: "radiogroup", "aria-label": t("Tools_Vm_Title") },
    radio("managed", "Tools_Vm_Managed", "Tools_Vm_Managed_Note"), radio("custom", "Tools_Vm_Custom", "Tools_Vm_Custom_Note"), radio("none", "Tools_Vm_None", "Tools_Vm_None_Note"));
  const drive = h("select", { class: "field lat", "aria-label": t("Tools_Vm_Drive") });
  const initial = h("input", { class: "field lat", inputmode: "numeric", "aria-label": t("Tools_Vm_Initial") });
  const maximum = h("input", { class: "field lat", inputmode: "numeric", "aria-label": t("Tools_Vm_Maximum") });
  const autoSize = h("input", { type: "checkbox", class: "switch", onchange: syncVm, "aria-label": t("Tools_Vm_Auto") });
  const custom = h("div", { class: "vm-custom" },
    h("label", {}, t("Tools_Vm_Drive"), drive),
    h("label", { style: { display: "flex", alignItems: "center", gap: "8px", alignSelf: "center" } }, autoSize, t("Tools_Vm_Auto")),
    h("label", {}, t("Tools_Vm_Initial"), initial), h("label", {}, t("Tools_Vm_Maximum"), maximum));
  const vmApply = h("button", { class: "btn primary", onclick: applyVm }, t("Tools_Vm_Apply"));
  const vmLimit = h("p", { class: "note" });

  // ——— Hosts ———
  const editor = h("textarea", { class: "hosts-ed", spellcheck: "false", "aria-label": t("Tools_Hosts_Title"), oninput: () => { dirty = true; hostsMsg.textContent = ""; } });
  const hostsInfo = h("span", { class: "caption lat" }), hostsMsg = h("p", { class: "msg" }), problems = h("ul", { class: "problems" });
  const saveBtn = h("button", { class: "btn primary", onclick: () => saveHosts(false) }, t("Tools_Hosts_Save"));
  const forceBtn = h("button", { class: "btn", hidden: true, onclick: () => saveHosts(true) }, t("Tools_Hosts_SaveAnyway"));
  const restoreBtn = h("button", { class: "btn quiet", onclick: restoreHosts }, t("Tools_Hosts_Restore"));
  let dirty = false;

  // Three sections, by what people come for: the connection and games first (DNS is what is asked for most), then repair and clean-up, then
  // the settings that change how Windows starts and uses memory.
  const section = (key, ...boxes) => h("section", { class: "tools-sec" }, h("h2", { class: "tools-sec-title" }, t(key)), h("div", { class: "panels two" }, boxes));
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_WindowsTools")), h("p", { class: "page-lede" }, t("Web_Tools_Note")))),
    section("Tools_Sec_Network", dnsBox(0), netfixBox(0), ...gaming.boxes, gameBoostBox(3)),
    section("Tools_Sec_Repair",
      crashesBox(3),
      box({ cls: "p-tool", ico: "win", title: t("Tools_Repair"), sub: t("Tools_Repair_Sub"), wide: true, i: 3, a: "repair",
        body: [h("p", { class: "note", style: { marginTop: 0 } }, t("Web_Tools_RepairNote")), h("div", { class: "btn-row" }, repair, h("span", { class: "grow" }), cancel), status, h("div", { style: { margin: "10px 0 14px" } }, bar), consoleEl] }),
      box({ kind: "Storage", ico: "drive", title: t("Tools_Windows"), sub: t("Tools_Windows_Sub"), i: 4, a: "cleanup",
        body: h("div", { class: "btn-row", style: { marginTop: 0 } }, btn("cleanup", "Tools_DiskCleanup"), btn("update", "Tools_WindowsUpdate")) }),
      box({ kind: "Power", title: t("Tools_Hib_Title"), sub: t("Tools_Hib_Sub"), i: 5, a: "hibernate",
        body: [powerTiles, h("p", { class: "note" }, t("Tools_Hib_Note")), h("div", { class: "btn-row" }, hibOff, hibOn), powerMsg] })),
    section("Tools_Sec_System",
      box({ kind: "Memory", title: t("Tools_Vm_Title"), sub: t("Tools_Vm_Sub"), i: 6, a: "vm",
        actions: h("button", { class: "btn quiet", onclick: () => call("tools.exec", { cmd: "pagefile" }) }, icon("popout"), t("Tools_Vm_Windows")),
        body: [vmInUse, h("div", { style: { marginTop: "14px" } }, choice), custom, vmLimit, h("div", { class: "btn-row" }, vmApply), vmMsg, vmPending] }),
      box({ cls: "p-host", ico: "net", title: t("Tools_Hosts_Title"), sub: t("Tools_Hosts_Sub"), i: 7, a: "hosts",
        actions: [h("button", { class: "btn quiet", onclick: loadHosts }, icon("refresh"), t("Tools_Hosts_Reload")), h("button", { class: "btn quiet", onclick: () => call("hosts.notepad") }, icon("doc"), t("Tools_Hosts_Notepad"))],
        body: [editor, problems, h("div", { class: "btn-row" }, saveBtn, forceBtn, restoreBtn, h("span", { class: "grow" }), hostsInfo), hostsMsg, h("p", { class: "note" }, t("Tools_Hosts_Note"))] })));

  // ——— Repair state (a session singleton on the host: a long sfc keeps running while the page is closed) ———
  let lines = 0;
  function update(s) {
    for (const b of repair) b.disabled = s.busy;
    cancel.disabled = !s.canCancel;
    status.textContent = s.status || ""; bar.firstChild.style.setProperty("--p", (s.percent || 0) / 100);
    if (s.output.length !== lines) { consoleEl.textContent = s.output.join("\n"); consoleEl.scrollTop = consoleEl.scrollHeight; lines = s.output.length; }
  }

  // ——— Hibernation ———
  const tile = (key, v) => h("div", { class: `state-tile ${v === true ? "on" : v === null || v === undefined ? "unknown" : ""}` },
    h("span", { class: "k" }, t(key)), h("span", { class: "v" }, v === true ? t("Tools_State_On") : v === false ? t("Tools_State_Off") : t("Tools_State_Default")));
  function showPower(p) {
    powerTiles.replaceChildren(tile("Tools_Hib_Hibernate", p.hibernate), tile("Tools_Hib_FastStartup", p.fastStartup));
    hibOff.disabled = p.hibernate === false; hibOn.disabled = p.hibernate === true && p.fastStartup === true;
  }
  async function setHibernate(onState) {
    hibOff.disabled = hibOn.disabled = true; powerMsg.className = "msg"; powerMsg.textContent = t("Tools_Working");
    try {
      const r = await call("sys.hibernate", { on: onState });
      showPower(r.power);
      powerMsg.className = `msg ${r.error ? "fail" : "ok"}`; powerMsg.textContent = r.error ? t("Tools_Hib_Failed", r.error) : t(onState ? "Tools_Hib_DoneOn" : "Tools_Hib_DoneOff");
    } catch (e) { powerMsg.className = "msg fail"; powerMsg.textContent = String(e.message || e); }
  }

  // ——— Virtual memory ———
  let vm = null;
  function mb(v) { return v === null || v === undefined ? t("Value_NotAvailable") : `${fa(v)} MB`; }
  function showVm(v, keepForm = false) {
    vm = v;
    const rows = [[t("Tools_PageFile_Managed"), v.managed === null || v.managed === undefined ? t("Value_NotAvailable") : t(v.managed ? "Value_Yes" : "Value_No")]];
    for (const f of v.inUse) rows.push([h("span", { class: "lat" }, f.path), `${mb(f.sizeMb)} · ${t("Tools_PageFile_Used")} ${mb(f.usedMb)}`]);
    if (!v.inUse.length) rows.push([t("Tools_PageFile"), t("Tools_PageFile_None")]);
    vmInUse.replaceChildren(...rows.flatMap(([k, x]) => [h("dt", {}, k), h("dd", {}, x)]));
    vmPending.hidden = !v.pending; vmPending.replaceChildren(icon("refresh"), t("Tools_Vm_Pending", v.pending || ""));
    vmLimit.textContent = t("Tools_Vm_Limit", fa(v.ramMb), fa(Math.max(v.ramMb * 3, 4096)));
    if (keepForm) return;
    drive.replaceChildren(...v.drives.map((d) => h("option", { value: d.name }, `${d.name} ${d.label ? `(${d.label}) ` : ""}— ${fa(Math.round(d.freeMb / 1024))} GB ${t("Tools_Vm_Free")}`)));
    // The form starts from what Windows will use after the next restart: its own setting if there is one.
    const set = v.settings[0];
    const mode = v.managed ? "managed" : v.settings.length ? "custom" : "none";
    choice.querySelector(`input[value="${mode}"]`).checked = true;
    if (set) { drive.value = set.path.slice(0, 2).toUpperCase(); autoSize.checked = !set.initialMb && !set.maximumMb; initial.value = set.initialMb || ""; maximum.value = set.maximumMb || ""; }
    syncVm();
  }
  function syncVm() {
    const mode = choice.querySelector("input:checked")?.value;
    custom.hidden = mode !== "custom";
    initial.disabled = maximum.disabled = autoSize.checked;
  }
  async function applyVm() {
    const mode = choice.querySelector("input:checked")?.value || "managed";
    vmApply.disabled = true; vmMsg.className = "msg"; vmMsg.textContent = t("Tools_Working");
    try {
      const r = await call("sys.pagefile", { mode, drive: drive.value, initial: autoSize.checked ? "0" : initial.value.trim(), maximum: autoSize.checked ? "0" : maximum.value.trim() });
      showVm(r.vm, true);
      vmMsg.className = `msg ${r.error ? "fail" : "ok"}`; vmMsg.textContent = r.error || t("Tools_Vm_Done");
    } catch (e) { vmMsg.className = "msg fail"; vmMsg.textContent = String(e.message || e); }
    finally { vmApply.disabled = false; }
  }

  // ——— Hosts ———
  const problemText = (p) => h("li", {}, t(p.problem === "address" ? "Tools_Hosts_BadAddress" : "Tools_Hosts_NoName", fa(p.line)), " ", h("code", {}, p.text));
  async function loadHosts() {
    if (dirty && !confirm(t("Tools_Hosts_Discard"))) return;
    try {
      const r = await call("hosts.read");
      editor.value = r.text; dirty = false; restoreBtn.disabled = !r.backup;
      hostsInfo.textContent = `${r.path} · ${t("Tools_Hosts_Entries", fa(r.entries))}`;
      problems.replaceChildren(...r.problems.map(problemText)); forceBtn.hidden = true; hostsMsg.textContent = "";
    } catch (e) { hostsMsg.className = "msg fail"; hostsMsg.textContent = t("Tools_Hosts_ReadFailed", String(e.message || e)); }
  }
  async function saveHosts(force) {
    saveBtn.disabled = true; hostsMsg.className = "msg"; hostsMsg.textContent = t("Tools_Working");
    try {
      const r = await call("hosts.save", { text: editor.value, force });
      if (r.saved) { dirty = false; forceBtn.hidden = true; problems.replaceChildren(); hostsMsg.className = "msg ok"; hostsMsg.textContent = t("Tools_Hosts_Saved"); toast(t("Tools_Hosts_Saved")); loadHosts(); }
      else if (r.error) { hostsMsg.className = "msg fail"; hostsMsg.textContent = r.error; }
      else { problems.replaceChildren(...r.problems.map(problemText)); forceBtn.hidden = false; hostsMsg.className = "msg fail"; hostsMsg.textContent = t("Tools_Hosts_HasProblems"); }
    } catch (e) { hostsMsg.className = "msg fail"; hostsMsg.textContent = String(e.message || e); }
    finally { saveBtn.disabled = false; }
  }
  // The previous version goes into the editor, not onto the disk: it is saved only by Save, like any other edit.
  async function restoreHosts() {
    const r = await call("hosts.backup");
    if (!r) return;
    editor.value = r.text; dirty = true; hostsMsg.className = "msg"; hostsMsg.textContent = t("Tools_Hosts_Restored");
  }

  call("tools.state").then(update);
  call("sys.state").then((s) => { showPower(s.power); showVm(s.vm); });
  loadHosts();
  const off = on("tools", update);
  return () => { off(); gaming.off(); };
}
