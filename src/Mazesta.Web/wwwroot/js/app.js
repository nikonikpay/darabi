// The shell: boot from the host, the numbered index (Ctrl+1 … Ctrl+0 open the first ten, so the numbers carry meaning), the stage where one
// page lives at a time, and the status band. A page is a module exporting mount(el) that returns an unmount function.
import { call, on, live } from "./bridge.js";
import { setStrings, t, fa } from "./i18n.js";
import { setUnits } from "./format.js";
import { loadHardware } from "./store.js";
import { h, clear, icon, toast } from "./ui.js";
import { LOGO_VIEWBOX, LOGO_PATHS } from "./logo.js";

export const PAGES = [
  { id: "dashboard", key: "Nav_Dashboard", load: () => import("./pages/dashboard.js") },
  { id: "monitoring", key: "Nav_Monitoring", load: () => import("./pages/monitoring.js") },
  { id: "tests", key: "Nav_Tests", load: () => import("./pages/tests.js") },
  { id: "system", key: "Nav_SystemInfo", load: () => import("./pages/system.js") },
  { id: "benchmarks", key: "Nav_Benchmarks", load: () => import("./pages/benchmarks.js") },
  { id: "gpu", key: "Nav_Gpu", load: () => import("./pages/component.js"), arg: "Gpu" },
  { id: "cpu", key: "Nav_Cpu", load: () => import("./pages/component.js"), arg: "Cpu" },
  { id: "network", key: "Nav_Network", load: () => import("./pages/component.js"), arg: "Network" },
  { id: "storage", key: "Nav_Storage", load: () => import("./pages/component.js"), arg: "Storage" },
  { id: "gaming", key: "Nav_Gaming", load: () => import("./pages/gaming.js") },
  { id: "overlay", key: "Nav_Overlay", load: () => import("./pages/overlay.js") },
  { id: "tuning", key: "Nav_Tuning", load: () => import("./pages/tuning.js") },
  { id: "tools", key: "Nav_WindowsTools", load: () => import("./pages/tools.js") },
  { id: "reports", key: "Nav_Reports", load: () => import("./pages/reports.js") },
  { id: "settings", key: "Nav_Settings", load: () => import("./pages/settings.js") },
];

export const boot = {};
// ?still turns motion off, for screenshots taken while the page is not on screen (a hidden page runs its animations slowly).
const still = new URLSearchParams(location.search).has("still");
if (still) document.documentElement.dataset.still = "";
const app = document.getElementById("app"), stage = document.getElementById("stage"), index = document.getElementById("index"), band = document.getElementById("band");
let unmount = null, current = null, ready = false;

export function go(id) { if (location.hash !== `#/${id}`) location.hash = `#/${id}`; else show(id); }

async function show(id) {
  const page = PAGES.find((p) => p.id === id) || PAGES[0];
  if (!ready || page === current) return;
  current = page;
  for (const a of index.querySelectorAll("a[data-id]")) a.removeAttribute("aria-current");
  index.querySelector(`a[data-id="${page.id}"]`)?.setAttribute("aria-current", "page");
  const mod = await page.load();
  const swap = () => {
    unmount?.(); unmount = null;
    clear(stage); stage.scrollTop = 0;
    const el = h("div", { class: "page" }); stage.append(el);
    unmount = mod.mount(el, page.arg) || null;
    document.title = `${t(page.key)} — Mazesta`;
  };
  if (document.startViewTransition && !still && !document.hidden && !matchMedia("(prefers-reduced-motion: reduce)").matches) {
    const vt = document.startViewTransition(swap);
    vt.ready.catch(() => {}); vt.finished.catch(() => {});   // a transition the browser skips still runs the swap; its promises just reject
  } else swap();
}

function logo() {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", LOGO_VIEWBOX); svg.setAttribute("role", "img"); svg.setAttribute("aria-label", "Mazesta");
  for (const d of LOGO_PATHS) { const p = document.createElementNS("http://www.w3.org/2000/svg", "path"); p.setAttribute("d", d); svg.append(p); }
  return svg;
}

function renderIndex(info) {
  clear(index);
  index.append(
    h("div", { class: "brand" }, h("img", { src: "img/logo.png", alt: "" }), h("div", {}, h("div", { class: "brand-word" }, logo()), h("div", { class: "brand-sub" }, "TEST SUITE"))),
    h("ul", { class: "index-list" }, PAGES.map((p, i) => [
      i === 5 || i === 9 ? h("li", { class: "sep", role: "presentation" }) : null,
      h("li", {}, h("a", { href: `#/${p.id}`, "data-id": p.id, title: i < 10 ? `Ctrl+${(i + 1) % 10}` : null },
        h("span", { class: "no" }, fa(String(i + 1).padStart(2, "0"))), h("span", { class: "nm" }, t(p.key)))),
    ])),
    h("div", { class: "index-foot" },
      h("label", { for: "svc" }, t("Service_Number")),
      h("input", { id: "svc", class: "field lat", style: { width: "100%", textAlign: "left" }, maxlength: "40", value: info.serviceNumber || "",
        onchange: async (e) => { e.target.value = await call("app.setServiceNumber", { value: e.target.value }); } })));
}

function renderBand(info) {
  clear(band);
  const status = h("span", { id: "prov" }, info.provider.text);
  const interval = h("span", { id: "intv" }, t("Status_Interval", info.interval));
  const overlay = h("button", { class: "btn quiet", id: "ovl", title: t("Overlay_ToggleHint"), onclick: () => call("app.toggleOverlay") }, t("Overlay_Toggle"));
  const pause = h("button", { class: "btn quiet", id: "pause", onclick: async () => setPause(await call("app.togglePause")) }, t(info.paused ? "Status_Resume" : "Status_Pause"));
  band.append(...[status, interval, h("span", { class: "grow" }), live ? null : h("span", { class: "pill warn" }, t("Web_DemoData")), overlay, pause].filter(Boolean));
}
function setPause(p) { const b = document.getElementById("pause"); if (b) b.textContent = t(p ? "Status_Resume" : "Status_Pause"); }

function banner(text, link) {
  if (!text) return;
  const el = h("div", { class: "banner", role: "status" }, h("span", { class: "grow" }, text),
    link ? h("button", { class: "btn", onclick: () => call("app.openLink", { key: link }) }, t("Banner_InstallPawnIo")) : null,
    h("button", { class: "btn quiet", onclick: () => el.remove(), "aria-label": "close" }, icon("x")));
  stage.before(el); el.style.gridColumn = "2";
  app.style.gridTemplateRows = "auto 1fr auto"; index.style.gridRow = "1 / 4"; stage.style.gridRow = "2"; band.style.gridRow = "3";
}

async function start() {
  const info = await call("app.boot");
  Object.assign(boot, info);
  setStrings(info.strings, info.rtl, info.language);
  setUnits(info.units);
  renderIndex(info); renderBand(info);
  if (info.banner) banner(info.banner);
  on("provider", (p) => {
    const el = document.getElementById("prov"); if (el) el.textContent = p.text;
    if (p.state === "Degraded" || p.state === "Failed") banner(t(p.state === "Degraded" ? "Banner_ProviderDegraded" : "Banner_ProviderFailed", p.reason || ""), p.pawnIo ? "pawnio" : null);
    if (!ready && ["Ready", "Degraded", "Failed"].includes(p.state)) whenReady();
  });
  on("engine", (e) => setPause(e.paused));
  on("interval", (s) => { const el = document.getElementById("intv"); if (el) el.textContent = t("Status_Interval", s); });
  on("overlay", (v) => document.getElementById("ovl")?.classList.toggle("primary", v));
  on("visibility", (v) => { app.dataset.visible = String(v); });
  on("toast", (m) => toast(m.text, m.kind));
  // The pages read the hardware list once; they wait until the sensor scan has settled, as the WPF edition does.
  if (await call("app.navReady")) whenReady();
  else { stage.append(h("div", { class: "page" }, h("p", { class: "page-lede" }, t("Nav_Starting")))); }
}

async function whenReady() {
  if (ready) return;
  await loadHardware();
  ready = true; app.dataset.state = "ready";
  show((location.hash.match(/^#\/(\w+)/) || [])[1] || "dashboard");
}

window.addEventListener("hashchange", () => show((location.hash.match(/^#\/(\w+)/) || [])[1]));
window.addEventListener("keydown", (e) => {
  if (!e.ctrlKey || e.altKey || e.shiftKey) return;
  const n = "1234567890".indexOf(e.key);
  if (n >= 0 && PAGES[n]) { e.preventDefault(); go(PAGES[n].id); }
});
document.addEventListener("visibilitychange", () => { app.dataset.visible = String(!document.hidden); });

// A page error is logged by the host (once per message), next to the host's own, so a log brought back from a machine shows it too.
const reported = new Set();
function report(message) { if (!live || reported.has(message) || reported.size > 50) return; reported.add(message); call("app.logError", { message }).catch(() => {}); }
window.addEventListener("error", (e) => report(`${e.message} at ${e.filename}:${e.lineno}:${e.colno}`));
window.addEventListener("unhandledrejection", (e) => report(String(e.reason && e.reason.stack || e.reason)));

start().catch((err) => { stage.append(h("pre", { class: "console" }, String(err && err.stack || err))); });
