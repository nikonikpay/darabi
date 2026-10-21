// The side bar's search: a word finds a page, or a box on a page (the same [data-a] marks the assistant points at), and a click or Enter goes there
// and marks it. It searches the names the page itself shows (the string table), so it follows the language; nothing is typed in twice.
import { t } from "./i18n.js";
import { h, icon } from "./ui.js";

// [page, box mark, name key] - a box on a page; the pages themselves are added from the page list.
const BOXES = [
  ["tools", "dns", "Dns_Title"], ["tools", "netfix", "NetFix_Title"], ["play", "gameboost", "GameBoost_Title"], ["play", "power", "Gaming_PowerPlan"], ["play", "gamemode", "Gaming_GameMode"],
  ["tools", "crashes", "Bsod_Title"], ["tools", "repair", "Tools_Repair"], ["tools", "cleanup", "Tools_Windows"], ["tools", "hibernate", "Tools_Hib_Title"], ["tools", "vm", "Tools_Vm_Title"], ["tools", "hosts", "Tools_Hosts_Title"],
  ["tuning", "curve", "Tuning_Curve_Title"], ["tuning", "profiles", "Tuning_Profiles"], ["tuning", "rules", "Tuning_Rules_Title"], ["tuning", "scene", "Tuning_SceneTest_Title"], ["tuning", "memory", "Tuning_Memory"],
  ["fans", "fans", "Fans_Profiles_Title"], ["lights", "rgb", "Rgb_Title"], ["settings", null, "Settings_DataFolder"], ["settings", null, "Settings_Tray_Section"], ["appupdate", null, "AppUpd_Version"],
  ["drivers", null, "Drivers_Board_Title"], ["drivers", null, "Drivers_Gpu_Title"], ["drivers", null, "Drivers_Wu_Title"], ["tweaks", null, "Tweaks_Essential"], ["tweaks", null, "Tweaks_Advanced"],
];
const DIGITS = { "۰": 0, "۱": 1, "۲": 2, "۳": 3, "۴": 4, "۵": 5, "۶": 6, "۷": 7, "۸": 8, "۹": 9 };
export const norm = (s) => String(s).toLowerCase().replace(/[۰-۹]/g, (c) => DIGITS[c]).replace(/ي/g, "ی").replace(/ك/g, "ک").replace(/[‌\s]+/g, " ").trim();

export function mark(target, tries = 0) {
  const el = document.querySelector(`#stage [data-a="${CSS.escape(target)}"]`);
  if (!el) { if (tries < 30) setTimeout(() => mark(target, tries + 1), 100); return; }
  el.closest("details")?.setAttribute("open", "");
  el.scrollIntoView({ block: "center", behavior: "smooth" });
  el.classList.remove("flash"); void el.offsetWidth; el.classList.add("flash"); setTimeout(() => el.classList.remove("flash"), 3200);
}

export function searchBox(pages, go) {
  const input = h("input", { class: "field", type: "search", placeholder: t("Nav_Search"), "aria-label": t("Nav_Search"), autocomplete: "off", spellcheck: false });
  const list = h("ul", { class: "search-list", role: "listbox", hidden: true });
  let found = [], at = 0;
  const entries = () => [...pages.map((p) => ({ page: p.id, target: null, name: t(p.key) })),
    ...BOXES.filter(([page]) => pages.some((p) => p.id === page)).map(([page, target, key]) => ({ page, target, name: t(key), where: t(pages.find((p) => p.id === page).key) }))];
  function render() {
    const q = norm(input.value); list.replaceChildren();
    found = q ? entries().filter((e) => norm(e.name).includes(q)).slice(0, 9) : [];
    at = 0; list.hidden = !found.length && !q;
    if (q && !found.length) list.append(h("li", { class: "none" }, t("Nav_Search_None")));
    found.forEach((e, i) => list.append(h("li", { role: "option", "aria-selected": i === 0 ? "true" : "false", onmousedown: (ev) => { ev.preventDefault(); pick(e); } }, h("span", {}, e.name), e.where ? h("small", {}, e.where) : null)));
  }
  function pick(e) { input.value = ""; list.hidden = true; found = []; go(e.page); if (e.target) mark(e.target); input.blur(); }
  function move(d) { if (!found.length) return; at = (at + d + found.length) % found.length; [...list.children].forEach((li, i) => li.setAttribute("aria-selected", i === at ? "true" : "false")); }
  input.addEventListener("input", render);
  input.addEventListener("blur", () => { list.hidden = true; });
  input.addEventListener("focus", render);
  input.addEventListener("keydown", (e) => {
    if (e.key === "ArrowDown") { e.preventDefault(); move(1); } else if (e.key === "ArrowUp") { e.preventDefault(); move(-1); }
    else if (e.key === "Enter" && found[at]) pick(found[at]); else if (e.key === "Escape") { input.value = ""; input.blur(); }
  });
  return h("div", { class: "index-search" }, icon("search"), input, list);
}
