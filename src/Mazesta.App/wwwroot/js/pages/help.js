// Help: every page and the boxes on it, in the side bar's order, each with what it does and a button that opens it. The search filters as you
// type, over the names and the explanations. Every page of app.js must have an entry here (ResourceParityTests checks it), so a new feature
// comes with its help. [page, box mark or null, title key]; the explanation is "Help_" + the title key. A null page is a part of the app
// that is not a page (the assistant, the tray).
import { t } from "../i18n.js";
import { h, icon } from "../ui.js";
import { FAMILIES, go } from "../app.js";
import { norm, mark } from "../search.js";

const TOPICS = [
  ["dashboard", null, "Nav_Dashboard"],
  ["monitoring", null, "Nav_Monitoring"], ["system", null, "Nav_SystemInfo"], ["cpu", null, "Nav_Cpu"], ["gpu", null, "Nav_Gpu"], ["ram", null, "Dashboard_Ram"],
  ["storage", null, "Nav_Storage"], ["network", null, "Nav_Network"],
  ["tests", null, "Nav_Tests"], ["benchmarks", null, "Nav_Benchmarks"], ["checkup", null, "Nav_Checkup"], ["checks", null, "Nav_Checks"],
  ["apps", null, "Nav_Apps"], ["ai", null, "Nav_Ai"],
  ["play", "power", "Gaming_PowerPlan"], ["play", "gamemode", "Gaming_GameMode"], ["play", "gameboost", "GameBoost_Title"], ["games", null, "Nav_Games"], ["overlay", null, "Nav_Overlay"],
  ["tuning", null, "Nav_Tuning"], ["tuning", "curve", "Tuning_Curve_Title"], ["tuning", "profiles", "Tuning_Profiles"], ["tuning", "rules", "Tuning_Rules_Title"],
  ["tuning", "scene", "Tuning_SceneTest_Title"], ["tuning", "memory", "Tuning_Memory"],
  ["lights", null, "Nav_Lights"], ["fans", null, "Nav_Fans"],
  ["tools", "dns", "Dns_Title"], ["tools", "netfix", "NetFix_Title"], ["tools", "crashes", "Bsod_Title"], ["tools", "repair", "Tools_Repair"], ["tools", "cleanup", "Tools_Windows"],
  ["tools", "hibernate", "Tools_Hib_Title"], ["tools", "vm", "Tools_Vm_Title"], ["tools", "hosts", "Tools_Hosts_Title"],
  ["tweaks", null, "Nav_Tweaks"], ["shield", null, "Nav_Shield"], ["updates", null, "Nav_Updates"], ["drivers", null, "Nav_Drivers"],
  ["reports", null, "Nav_Reports"], ["messages", null, "Nav_Messages"], ["settings", null, "Nav_Settings"], ["appupdate", null, "Nav_AppUpdate"],
  ["help", null, "Nav_Help"], [null, null, "Nav_Assistant"], [null, null, "Help_Tray_Title"], [null, null, "Help_Keys_Title"],
];

export function mount(el) {
  const input = h("input", { class: "field help-q", type: "search", placeholder: t("Help_Search"), "aria-label": t("Help_Search"), autocomplete: "off", spellcheck: false });
  const list = h("div", { class: "help-list" }), none = h("p", { class: "caption", hidden: true }, t("Nav_Search_None"));
  const family = (page) => FAMILIES.find((f) => f.pages.includes(page));
  const items = TOPICS.map(([page, target, key]) => {
    const f = page ? family(page) : null;
    const el = h("article", { class: "help-item" },
      h("div", { class: "help-head" }, h("h3", {}, t(key)), f ? h("small", { class: "caption" }, t(f.key)) : null, h("span", { class: "grow" }),
        page && page !== "help" ? h("button", { class: "btn quiet", onclick: () => { go(page); if (target) mark(target); } }, icon("arrow"), t("Help_Open")) : null),
      h("p", {}, t(`Help_${key}`)));
    return { el, text: norm(`${t(key)} ${t(`Help_${key}`)} ${f ? t(f.key) : ""}`) };
  });
  list.append(...items.map((x) => x.el));
  input.addEventListener("input", () => {
    const words = norm(input.value).split(" ").filter(Boolean);
    let shown = 0;
    for (const x of items) { const ok = words.every((w) => x.text.includes(w)); x.el.hidden = !ok; shown += ok; }
    none.hidden = shown > 0;
  });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Help")), h("p", { class: "page-lede" }, t("Help_Lede")))),
    h("div", { class: "help-search" }, icon("search"), input), none, list);
  setTimeout(() => input.focus(), 0);
  return () => {};
}
