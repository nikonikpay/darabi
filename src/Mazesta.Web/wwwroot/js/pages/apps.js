// Programs: which professional programs this computer runs, and at which of their publishers' tiers. Each program is judged by the host
// (SoftwareCatalog) on what can be read here (RAM, graphics memory, cores, what the card must support); the publisher's example cards and
// processor figures are shown beside each tier for the user to compare, never compared by name. The assistant answers from the same verdicts.
import { call } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon } from "../ui.js";
import { go } from "../app.js";

const LEVEL = { HighEnd: "run", Recommended: "pass", Minimum: "warn" };
const lat = (text) => h("span", { class: "lat" }, text);
const gb = (v) => (v == null ? "—" : `${fa(v)} GB`);

// The program's own site icon where it has one of its own, else a lettered tile in its colour.
export function appIcon(a, size = 40) {
  return a.icon ? h("img", { class: "app-ico", src: `img/apps/${a.icon}`, alt: "", width: size, height: size, loading: "lazy" })
    : h("span", { class: "app-ico mono lat", style: { "--c": a.color }, "aria-hidden": "true" }, a.mono);
}

export function levelPill(level) {
  return h("span", { class: `pill ${LEVEL[level] || "fail"}` }, t(`Soft_Level_${level || "Below"}`));
}

export function mount(el) {
  const machine = h("dl", { class: "kv apps-pc" }), chips = h("div", { class: "apps-chips", role: "tablist" }), list = h("div", { class: "apps-list" });
  const count = h("span", { class: "group-count" });
  const search = h("input", { class: "field apps-search", type: "search", placeholder: t("Apps_Search"), "aria-label": t("Apps_Search"), oninput: () => draw() });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Apps_Title")), h("p", { class: "page-lede" }, t("Apps_Lede")))),
    h("div", { class: "apps-top" },
      h("section", { class: "panel p-ai" }, h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("board")), h("h2", { class: "panel-title" }, t("Apps_This")), count), machine),
      h("section", { class: "panel apps-ai" }, h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("chat")), h("h2", { class: "panel-title" }, t("Apps_Ai"))),
        h("p", { class: "caption" }, t("Apps_Ai_Text")), h("div", { class: "btn-row" }, h("button", { class: "btn", onclick: () => go("ai") }, icon("chat"), t("Nav_Ai"))))),
    h("div", { class: "apps-bar" }, chips, h("span", { class: "grow" }), search), list);

  let s = null, cat = "";
  function chip(id, name) {
    return h("button", { class: "apps-chip", type: "button", role: "tab", "aria-selected": String(cat === id), onclick: () => { cat = id; drawChips(); draw(); } }, name);
  }
  function drawChips() { chips.replaceChildren(chip("", t("Apps_All")), ...s.categories.map((c) => chip(c.id, c.name))); }

  function card(a) {
    const tiers = h("details", { class: "apps-tiers" }, h("summary", {}, t("Apps_Tiers")),
      h("table", { class: "apps-table" },
        h("thead", {}, h("tr", {}, h("th", {}), h("th", {}, t("Apps_Ram")), h("th", {}, t("Apps_Vram")), h("th", {}, t("Apps_Gpu")), h("th", {}, t("Apps_Cpu")))),
        h("tbody", {}, a.tiers.map((x) => h("tr", { class: x.met ? "met" : "" },
          h("th", {}, h("span", { class: `pill ${x.met ? LEVEL[x.kind] : "none"}` }, t(`Soft_Level_${x.kind}`))),
          h("td", { class: "num" }, gb(x.ram)), h("td", { class: "num" }, gb(x.vram)),
          h("td", {}, x.gpu ? lat(x.gpu) : "—", x.rt ? h("div", { class: "caption" }, t("Apps_RayTracing")) : null),
          h("td", {}, x.cpu ? lat(x.cpu) : x.cores ? `${fa(x.cores)} ${t("Apps_Cores")}` : "—"))))),
      h("p", { class: "caption" }, t("Apps_Source"), " ", lat(a.source)));
    return h("article", { class: `apps-card lv-${a.level || "Below"}`, "data-app": a.id },
      h("header", { class: "apps-head" }, appIcon(a),
        h("div", { class: "apps-name" }, h("h3", {}, lat(a.name)), h("span", { class: "caption" }, a.vendor)), levelPill(a.level)),
      h("p", { class: "apps-purpose" }, a.purpose),
      a.suits ? h("p", { class: "apps-suits" }, h("b", {}, t("Apps_Suits"), ": "), a.suits) : null,
      a.next ? h("div", { class: "apps-next" }, h("span", { class: "caption" }, t("Apps_Next", t(`Soft_Level_${a.next}`))),
        h("ul", {}, a.missing.map((m) => h("li", {}, m)))) : h("p", { class: "caption" }, icon("check"), " ", t("Apps_Top")),
      a.note ? h("p", { class: "caption apps-note" }, a.note) : null, tiers);
  }

  function draw() {
    if (!s) return;
    const q = search.value.trim().toLowerCase();
    const shown = s.apps.filter((a) => (!cat || a.category === cat) && (!q || a.name.toLowerCase().includes(q) || a.vendor.toLowerCase().includes(q)));
    const groups = s.categories.filter((c) => shown.some((a) => a.category === c.id));
    list.replaceChildren(...groups.map((c) => h("section", { class: "apps-group" }, h("h2", { class: "apps-group-title" }, c.name),
      h("div", { class: "apps-grid" }, shown.filter((a) => a.category === c.id).map(card)))));
  }

  function show(state) {
    s = state; const m = s.machine;
    const row = (k, v) => h("div", {}, h("dt", {}, t(k)), h("dd", {}, v ?? "—"));
    machine.replaceChildren(row("Apps_Cpu", m.cpu ? lat(m.cpu) : null), row("Apps_Cores", m.cores != null ? `${fa(m.cores)}${m.threads ? " / " + fa(m.threads) : ""}` : null),
      row("Apps_Ram", m.ram != null ? gb(m.ram) : null), row("Apps_Gpu", m.gpu ? lat(m.gpu) : null), row("Apps_Vram", m.vram != null ? gb(m.vram) : null),
      row("Apps_RayTracing", m.rt == null ? null : t(m.rt ? "Value_Yes" : "Value_No")));
    count.textContent = t("Apps_Count", fa(s.apps.filter((a) => a.level).length), fa(s.apps.length));
    drawChips(); draw();
  }
  call("apps.state").then(show);
  return null;
}
