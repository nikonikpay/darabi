// Help: the app explained in the side bar's sections (FAMILIES of app.js), each with its own intro and, under it, a topic for every page and
// important box: what it does (Help_<key>) and how to use it, step by step (HelpHow_<key>, steps split by "|"). A rail of the sections jumps
// to one; the search filters the topics as you type and opens the ones that match. Every page of app.js must have a topic here
// (ResourceParityTests checks it, with all three strings), so a new feature comes with its help. [page, box mark or null, title key]; a null
// page is a part of the app that is not a page (the assistant, the tray) and sits under the help section.
import { t } from "../i18n.js";
import { h, icon } from "../ui.js";
import { FAMILIES, TAB_ICON, go } from "../app.js";
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
// The boxes' own icons and each section's colour (the part colours of the rest of the app).
const BOX_ICON = { power: "bolt", gamemode: "gamepad", gameboost: "sparkle", curve: "chart", profiles: "layers", rules: "clock", scene: "play", memory: "ram",
  dns: "net", netfix: "net", crashes: "alert", repair: "wrench", cleanup: "win", hibernate: "hourglass", vm: "drive", hosts: "doc" };
const LOOSE_ICON = { Nav_Assistant: "chat", Help_Tray_Title: "pin", Help_Keys_Title: "keyboard", Nav_Dashboard: "home", Nav_Messages: "mail", Nav_Reports: "doc" };
const HUE = { Nav_Group_Hardware: "--c-cpu", Nav_Group_Tests: "--pass", Nav_Group_Apps: "--c-ram", Nav_Group_Gaming: "--c-gpu", Nav_Tuning: "--c-power",
  Nav_Group_Lights: "--c-board", Nav_Group_Windows: "--c-net", Nav_Reports: "--c-storage" };

export function mount(el) {
  const input = h("input", { class: "field help-q", type: "search", placeholder: t("Help_Search"), "aria-label": t("Help_Search"), autocomplete: "off", spellcheck: false });
  const none = h("p", { class: "caption", hidden: true }, t("Nav_Search_None"));
  const helpFamily = FAMILIES.find((f) => f.pages.includes("help"));
  const sections = FAMILIES.map((f) => {
    const topics = TOPICS.filter(([page]) => (page ? f.pages.includes(page) : f === helpFamily)).map(([page, target, key]) => {
      const steps = t(`HelpHow_${key}`).split("|");
      const node = h("details", { class: "help-topic" },
        h("summary", {}, h("span", { class: "help-ti" }, icon((target && BOX_ICON[target]) || LOOSE_ICON[key] || TAB_ICON[page] || f.icon)),
          h("span", { class: "help-tt" }, h("b", {}, t(key)), h("small", {}, t(`Help_${key}`))), icon("chevron")),
        h("div", { class: "help-body" }, h("p", {}, t(`Help_${key}`)),
          h("h4", {}, t("Help_How")), h("ol", { class: "help-steps" }, ...steps.map((s) => h("li", {}, s))),
          page && page !== "help" ? h("button", { class: "btn", onclick: () => { go(page); if (target) mark(target); } }, icon("arrow"), t("Help_Open")) : null));
      return { node, text: norm(`${t(key)} ${t(`Help_${key}`)} ${steps.join(" ")} ${t(f.key)}`) };
    });
    const node = h("section", { class: "help-sec", style: { "--hue": `var(${HUE[f.key] || "--yellow"})` } },
      h("header", { class: "help-sec-head" }, h("div", { class: "help-emblem", "aria-hidden": "true" }, icon(f.icon)),
        h("div", {}, h("h2", {}, t(f.key)), h("p", {}, t(`HelpFam_${f.key}`)))),
      ...topics.map((x) => x.node));
    const link = h("button", { class: "help-rail-a", style: { "--hue": `var(${HUE[f.key] || "--yellow"})` }, onclick: () => node.scrollIntoView({ behavior: "smooth", block: "start" }) },
      icon(f.icon), h("span", {}, t(f.key)), h("small", { class: "num" }, String(topics.length)));
    return { node, link, topics };
  });
  input.addEventListener("input", () => {
    const words = norm(input.value).split(" ").filter(Boolean);
    let shown = 0;
    for (const s of sections) {
      let here = 0;
      for (const x of s.topics) { const ok = words.every((w) => x.text.includes(w)); x.node.hidden = !ok; x.node.open = ok && words.length > 0; here += ok; }
      s.node.hidden = s.link.hidden = here === 0; shown += here;
    }
    none.hidden = shown > 0;
  });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Help")), h("p", { class: "page-lede" }, t("Help_Lede")))),
    h("div", { class: "help" },
      h("nav", { class: "help-rail", "aria-label": t("Help_Sections") }, ...sections.map((s) => s.link)),
      h("div", { class: "help-main" }, h("div", { class: "help-search" }, icon("search"), input), none, ...sections.map((s) => s.node))));
  setTimeout(() => input.focus(), 0);
  return () => {};
}
