// Windows' own repair tools, run only on a click, with their real output streaming below; the page file settings are shown, not changed.
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon } from "../ui.js";

export function mount(el) {
  const btn = (cmd, key, cls = "btn") => h("button", { class: cls, "data-cmd": cmd, onclick: () => call("tools.exec", { cmd }) }, t(key));
  const repair = [btn("sfc", "Tools_Sfc", "btn primary"), btn("dismScan", "Tools_DismScan"), btn("dismRestore", "Tools_DismRestore")];
  const cancel = h("button", { class: "btn stop", onclick: () => call("tools.exec", { cmd: "cancel" }) }, icon("stop"), t("Test_Cancel"));
  const status = h("div", { class: "h3" }), bar = h("div", { class: "progress" }, h("i"));
  const consoleEl = h("pre", { class: "console", "aria-live": "off" });
  const pf = h("dl", { class: "kv" });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_WindowsTools")), h("p", { class: "page-lede" }, t("Tools_Note")))),
    h("div", { class: "split" },
      h("div", {}, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Tools_Repair"))),
        h("div", { class: "toolbar", style: { marginTop: "14px" } }, repair, cancel), status, h("div", { style: { margin: "10px 0 16px" } }, bar), consoleEl),
      h("div", {}, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Tools_Windows"))),
        h("div", { style: { display: "grid", gap: "8px", marginTop: "14px", justifyItems: "start" } }, btn("cleanup", "Tools_DiskCleanup"), btn("update", "Tools_WindowsUpdate"), btn("pagefile", "Tools_PageFileSettings")),
        h("div", { class: "section" }, h("div", { class: "section-head" }, h("h2", { class: "h2" }, t("Tools_PageFile"))), pf))));
  let lines = 0;
  function update(s) {
    for (const b of repair) b.disabled = s.busy;
    cancel.disabled = !s.canCancel;
    status.textContent = s.status || ""; bar.firstChild.style.setProperty("--p", (s.percent || 0) / 100);
    if (s.output.length !== lines) { consoleEl.textContent = s.output.join("\n"); consoleEl.scrollTop = consoleEl.scrollHeight; lines = s.output.length; }
    pf.replaceChildren(...s.pageFile.map((r) => [h("dt", {}, r.label), h("dd", { class: "lat" }, r.value)]));
  }
  call("tools.state").then(update);
  return on("tools", update);
}
