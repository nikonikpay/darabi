// Blue screens, as one box of the Windows tools page: the stops Windows kept a record of (dump files and its own log), newest first, each
// with its code, Microsoft's name for it, its parameters and what they say, and the usual causes of that code to check. It only reads; an
// empty list says what was looked at, not that the computer never crashed.
import { call } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon } from "../ui.js";
import { box } from "../groups.js";

export function crashesBox(i) {
  const list = h("div", { class: "cr-list" }), msg = h("p", { class: "msg" }), foot = h("p", { class: "note" }), power = h("p", { class: "note" });
  const read = h("button", { class: "btn go", type: "button", onclick: load }, icon("refresh"), t("Bsod_Read"));

  function row(c) {
    const causes = c.causes.map((x, n) => h("li", {}, h("b", {}, `${fa(n + 1)}. ${x.title}`), h("small", {}, x.check)));
    return h("details", { class: "cr-item" },
      h("summary", {}, h("span", { class: "lat cr-code" }, c.code), h("b", { class: "lat" }, c.name || t("Bsod_Unknown")),
        h("span", { class: "caption" }, c.atIsRestart ? t("Bsod_AtRestart", c.at) : c.at)),
      c.notes.length ? h("ul", { class: "cr-notes" }, c.notes.map((n) => h("li", {}, n))) : null,
      causes.length ? [h("h4", {}, t("Bsod_Causes")), h("ol", { class: "cr-causes" }, causes)] : h("p", { class: "note" }, t("Bsod_NoCauses")),
      c.parameters ? h("dl", { class: "kv" }, h("dt", {}, t("Bsod_Parameters")), h("dd", { class: "lat" }, c.parameters.join("  ")),
        c.parametersMean ? [h("dt", {}, t("Bsod_ParametersMean")), h("dd", {}, c.parametersMean)] : null) : null,
      // The question goes to the assistant's column as if typed there; the assistant reads these same records before it answers.
      h("div", { class: "btn-row" }, h("button", { class: "btn quiet", type: "button", "data-a": "crash-ask",
        onclick: () => window.dispatchEvent(new CustomEvent("assistant:ask", { detail: t("Bsod_Ask_Text", c.code, c.name || t("Bsod_Unknown"), c.at) })) }, icon("chat"), t("Bsod_Ask"))),
      h("p", { class: "caption" }, [c.dump ? t("Bsod_Dump", c.dump) : t("Bsod_NoDump"), c.uptimeMinutes != null ? t("Bsod_Uptime", fa(c.uptimeMinutes)) : null].filter(Boolean).join(" · ")));
  }

  async function load() {
    read.disabled = true; msg.className = "msg"; msg.textContent = t("Tools_Working");
    try {
      const r = await call("crashes.read");
      list.replaceChildren(...r.crashes.map(row));
      if (list.firstChild) list.firstChild.open = true;
      msg.className = `msg ${r.total ? "fail" : "ok"}`; msg.textContent = r.total ? t("Bsod_Found", fa(r.total)) : t("Bsod_None");
      const looked = [r.dumpsReadable ? t("Bsod_Looked_Dumps", fa(r.dumpFiles)) : t("Bsod_Looked_NoDumps"), r.logReadable ? (r.logSince ? t("Bsod_Looked_Log", r.logSince) : t("Bsod_Looked_LogEmpty")) : t("Bsod_Looked_NoLog")];
      foot.textContent = looked.join(" ");
      power.hidden = !r.powerLosses; power.textContent = r.powerLosses ? t("Bsod_PowerLoss", fa(r.powerLosses), r.lastPowerLoss) : "";
    } catch (e) { msg.className = "msg fail"; msg.textContent = String(e.message || e); }
    finally { read.disabled = false; }
  }

  return box({ cls: "p-tool", ico: "win", title: t("Bsod_Title"), sub: t("Bsod_Sub"), i, wide: true, a: "crashes",
    body: [h("p", { class: "note", style: { marginTop: 0 } }, t("Bsod_Note")), h("div", { class: "btn-row" }, read), msg, list, power, foot] });
}
