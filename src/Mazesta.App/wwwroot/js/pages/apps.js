// Programs (and, on their own page, games): which professional programs this computer runs, and at which of their publishers' tiers. Each program is judged by the host
// (SoftwareCatalog) on what can be read here (RAM, graphics memory, cores, what the card must support); the publisher's example cards and
// processor figures are shown beside each tier for the user to compare, never compared by name. The assistant answers from the same verdicts.
// A game's tier carries its publisher's own target (resolution, preset, frame rate) where the publisher gives one: shown as the publisher's aim,
// not as what this computer will reach.
import { call } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon } from "../ui.js";
import { go } from "../app.js";

const LEVEL = { HighEnd: "run", Recommended: "pass", Meets: "pass", Minimum: "warn", BelowRec: "warn" };
const lat = (text) => h("span", { class: "lat" }, text);
const gb = (v) => (v == null ? "—" : h("span", { class: "num" }, `${v} GB`));

// The program's own site icon where it has one of its own, else a lettered tile in its colour.
export function appIcon(a, size = 40) {
  return a.icon ? h("img", { class: "app-ico", src: `img/apps/${a.icon}`, alt: "", width: size, height: size, loading: "lazy" })
    : h("span", { class: "app-ico mono lat", style: { "--c": a.color }, "aria-hidden": "true" }, a.mono);
}

export function levelPill(level) {
  return h("span", { class: `pill ${LEVEL[level] || "fail"}` }, t(`Soft_Level_${level}`));
}

export function mount(el) { return mountList(el, false); }

// The one page for both lists: the programs (by what they do) or the games (one group, no category chips).
export function mountList(el, games) {
  const machine = h("dl", { class: "kv apps-pc" }), chips = h("div", { class: "apps-chips", role: "tablist" }), list = h("div", { class: "apps-list" });
  const count = h("span", { class: "group-count" });
  const search = h("input", { class: "field apps-search", type: "search", placeholder: t(games ? "Games_Search" : "Apps_Search"), "aria-label": t(games ? "Games_Search" : "Apps_Search"), oninput: () => draw() });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t(games ? "Games_Title" : "Apps_Title")), h("p", { class: "page-lede" }, t(games ? "Games_Lede" : "Apps_Lede")))),
    h("div", { class: "apps-top" },
      h("section", { class: "panel p-ai" }, h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("board")), h("h2", { class: "panel-title" }, t("Apps_This")), count), machine),
      h("section", { class: "panel apps-ai" }, h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("chat")), h("h2", { class: "panel-title" }, t("Apps_Ai"))),
        h("p", { class: "caption" }, t(games ? "Games_Ai_Text" : "Apps_Ai_Text")), h("div", { class: "btn-row" }, h("button", { class: "btn", onclick: () => go("ai") }, icon("chat"), t("Nav_Ai"))))),
    h("div", { class: "apps-bar" }, games ? h("p", { class: "caption games-note" }, t("Games_Note")) : chips, h("span", { class: "grow" }), search), list);

  let s = null, cat = "";
  function chip(id, name) {
    return h("button", { class: "apps-chip", type: "button", role: "tab", "aria-selected": String(cat === id), onclick: () => { cat = id; drawChips(); draw(); } }, name);
  }
  function drawChips() { chips.replaceChildren(chip("", t("Apps_All")), ...s.categories.filter((c) => c.id !== "Game").map((c) => chip(c.id, c.name))); }

  function card(a) {
    const tiers = h("details", { class: "apps-tiers" }, h("summary", {}, t(games ? "Games_Tiers" : "Apps_Tiers")),
      h("table", { class: "apps-table" },
        h("thead", {}, h("tr", {}, h("th", {}), games ? h("th", {}, t("Games_Target")) : null, h("th", {}, t("Apps_Ram")), h("th", {}, t("Apps_Vram")), h("th", {}, t("Apps_Gpu")), h("th", {}, t("Apps_Cpu")))),
        h("tbody", {}, a.tiers.map((x) => h("tr", { class: x.met ? "met" : "" },
          h("th", {}, h("span", { class: `pill ${x.met ? LEVEL[x.kind] : "none"}` }, x.name)),
          games ? h("td", {}, x.target ? lat(x.target) : h("span", { class: "muted" }, t("Games_NoTargetShort"))) : null,
          h("td", { class: "num" }, gb(x.ram)), h("td", { class: "num" }, gb(x.vram)),
          h("td", {}, x.gpu ? lat(x.gpu) : "—", x.rt ? h("div", { class: "caption" }, t("Apps_RayTracing")) : null),
          h("td", {}, x.cpu ? lat(x.cpu) : x.cores ? [h("span", { class: "num" }, x.cores), " ", t("Apps_Cores")] : "—"))))),
      h("p", { class: "caption" }, t("Apps_Source"), " ", lat(a.source)));
    return h("article", { class: `apps-card lv-${a.label === "Meets" ? "Recommended" : a.level || (a.label === "BelowRec" ? "Minimum" : "Below")}`, "data-app": a.id },
      h("header", { class: "apps-head" }, appIcon(a),
        h("div", { class: "apps-name" }, h("h3", {}, lat(a.name)), h("span", { class: "caption" }, a.vendor)), levelPill(a.label)),
      h("p", { class: "apps-purpose" }, a.purpose),
      a.suits ? h("p", { class: "apps-suits" }, h("b", {}, t("Apps_Suits"), ": "), a.suits) : null,
      a.next ? h("div", { class: "apps-next" }, h("span", { class: "caption" }, t("Apps_Next", a.nextName)),
        h("ul", {}, a.missing.map((m) => h("li", {}, m)))) : h("p", { class: "caption" }, icon("check"), " ", t(games ? "Games_Top" : "Apps_Top")),
      a.unchecked && a.unchecked.length ? h("p", { class: "caption apps-unchecked" }, t("Apps_Unchecked"), " ", a.unchecked.join("، ")) : null,
      a.note ? h("p", { class: "caption apps-note" }, a.note) : null, tiers);
  }

  function draw() {
    if (!s) return;
    const q = search.value.trim().toLowerCase();
    const shown = s.apps.filter((a) => (a.category === "Game") === games && (!cat || a.category === cat) && (!q || a.name.toLowerCase().includes(q) || a.vendor.toLowerCase().includes(q)));
    const groups = s.categories.filter((c) => shown.some((a) => a.category === c.id));
    list.replaceChildren(...groups.map((c) => h("section", { class: "apps-group" }, h("h2", { class: "apps-group-title" }, c.name),
      h("div", { class: "apps-grid" }, shown.filter((a) => a.category === c.id).map(card)))));
  }

  function show(state) {
    s = state; const m = s.machine;
    const row = (k, v) => h("div", {}, h("dt", {}, t(k)), h("dd", {}, v ?? "—"));
    machine.replaceChildren(row("Apps_Cpu", m.cpu ? lat(m.cpu) : null), row("Apps_Cores", m.cores != null ? h("span", { class: "num" }, `${m.cores}${m.threads ? " / " + m.threads : ""}`) : null),
      row("Apps_Ram", m.ram != null ? gb(m.ram) : null), row("Apps_Gpu", m.gpu ? lat(m.gpu) : null), row("Apps_Vram", m.vram != null ? gb(m.vram) : null),
      row("Apps_RayTracing", m.rt == null ? null : t(m.rt ? "Value_Yes" : "Value_No")));
    const mine = s.apps.filter((a) => (a.category === "Game") === games);
    count.textContent = t(games ? "Games_Count" : "Apps_Count", fa(mine.filter((a) => a.level).length), fa(mine.length));
    drawChips(); draw();
  }
  call("apps.state").then(show);
  return null;
}
