// The dashboard: the machine's standing and a live tile per part (processor, graphics, memory, network; each opens its page), then the board
// and the drives, which the tiles do not hold, the drives side by side. Then the shop's own product, one of its ready systems and its people.
// The panels sit on a grid read from the page's own width (not the window's, the assistant's column takes part of it). Every number is a live
// reading or says it is not available.
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { fmt } from "../format.js";
import { topNodes, pick, value, stats, subscribe } from "../store.js";
import { h, val, icon } from "../ui.js";
import { liveTile, percentOf, ratioOf } from "../tiles.js";
import { go, boot } from "../app.js";
import { part, sensorsUnder } from "../parts.js";

export function mount(el) {
  const cpu = topNodes("Cpu")[0], gpus = topNodes("Gpu"), gpu = gpus.find((g) => pick(g, "GpuCoreTemp")) || gpus[0];
  const ram = topNodes("Memory").find((n) => pick(n, "RamUsed"));
  const drives = topNodes("Storage"), nets = topNodes("Network").filter((n) => !/^vEthernet/i.test(n.name)), board = topNodes("Motherboard")[0];
  const cpuTemp = pick(cpu, "CpuPackageTemp", "CpuTctlTdie"), gpuTemp = pick(gpu, "GpuCoreTemp");
  const updates = [];   // closures run on every poll

  // ——— The first row: the machine's standing and what to do next, beside a live tile per part ———
  const verdict = h("h2", { class: "verdict" }, t("Web_Dash_Verdict_None"));
  const machine = h("div", { class: "machine lat" }, [cpu?.name, gpu?.name].filter(Boolean).join("  ·  "));
  const date = new Intl.DateTimeFormat(boot.rtl ? "fa-IR-u-ca-persian" : "en-GB", { weekday: "long", day: "numeric", month: "long", year: "numeric" }).format(new Date());
  const action = (cls, ico, key, onclick) => h("button", { class: cls, type: "button", onclick }, h("span", { class: "ico" }, icon(ico)), t(key));
  const status = h("section", { class: "plane status-card enter" },
    h("span", { class: "status-kicker" }, t("Web_Dash_Live")), verdict, machine, h("div", { class: "grow" }),
    h("div", { class: "quick" },
      action("", "flask", "Web_Dash_RunTests", () => go("tests")), action("p-gpu", "trophy", "Nav_Benchmarks", () => go("benchmarks")),
      action("p-game", "overlay", "Overlay_Toggle", () => call("app.toggleOverlay")), action("p-board", "doc", "Nav_Reports", () => go("reports"))),
    h("div", { class: "colophon" }, h("span", {}, boot.shopName), h("span", {}, date)));
  // Each temperature with its part's load beside it and as the bar, like the overlay; memory used, its load, and the total under it.
  const net = nets.find((n) => pick(n, "NetDownload") && value(pick(n, "NetDownload").id)) || nets[0];
  const ramUsed = ram && pick(ram, "RamUsed"), ramTotal = ram && pick(ram, "RamTotal"), ramLoad = ram && pick(ram, "RamLoad");
  const cpuLoad = cpu && pick(cpu, "CpuTotalLoad"), gpuLoad = gpu && pick(gpu, "GpuLoad3D", "GpuLoadD3D3D");
  const tiles = [
    cpu && liveTile({ kind: "Cpu", label: t("Web_Dash_CpuTemp"), main: cpuTemp, side: cpuLoad, share: percentOf(cpuLoad), foot: cpu.name, page: "cpu", i: 0 }),
    gpu && liveTile({ kind: "Gpu", label: t("Web_Dash_GpuTemp"), main: gpuTemp, side: gpuLoad, share: percentOf(gpuLoad), foot: gpu.name, page: "gpu", i: 1 }),
    ram && liveTile({ kind: "Memory", label: t("Dashboard_Ram"), main: ramUsed, side: ramLoad, share: ramLoad ? percentOf(ramLoad) : ratioOf(ramUsed, ramTotal),
      foot: ramTotal ? h("span", {}, t("Dashboard_Ram_Total"), " ", h("span", { class: "lat" }, fmt(value(ramTotal.id), ramTotal.unit) ?? "—")) : null, i: 2 }),
    net && liveTile({ kind: "Network", label: t("Nav_Network"), main: pick(net, "NetDownload"), side: () => { const s = pick(net, "NetUpload"), v = s && fmt(value(s.id), s.unit); return v && `↑ ${v}`; },
      share: percentOf(pick(net, "NetUtilization")), foot: net.name, page: "network", i: 3 }),
  ].filter(Boolean);
  for (const x of tiles) updates.push(x.update);
  const plane = h("div", { class: "hero-row" }, status, h("div", { class: "tiles" }, tiles.map((x) => x.el)));

  // ——— Pieces a panel is made of; each registers its own update ———
  // A big reading: the number and its unit apart. A sensor that is missing, or has no reading now, is the hatch.
  const stat = (label, sensor) => {
    const v = h("span", { class: "v" });
    updates.push(() => {
      const text = sensor ? fmt(value(sensor.id), sensor.unit) : null;
      if (text === null) { v.replaceChildren(val(null)); return; }
      const cut = text.lastIndexOf(" ");
      v.replaceChildren(cut > 0 ? text.slice(0, cut) : text, cut > 0 ? h("small", {}, text.slice(cut + 1)) : "");
    });
    return h("div", { class: "stat" }, h("span", { class: "k" }, label), v);
  };
  // A share of something: a percent sensor, or a used/total pair. Without both numbers the bar stays empty and the text says so.
  const meter = (label, share) => {
    const num = h("span", {}), bar = h("i", {});
    updates.push(() => {
      const p = share();
      bar.style.setProperty("--p", p === null ? 0 : Math.min(1, Math.max(0, p)));
      num.replaceChildren(p === null ? val(null) : h("span", { class: "num" }, `${Math.round(p * 100)}%`));
    });
    return h("div", { class: "meter" }, h("div", { class: "row" }, h("span", {}, label), num), h("div", { class: "bar", role: "presentation" }, bar));
  };
  // Details: the sensors of the listed roles that the summary does not show, by their own names, grouped under a heading when asked.
  const details = (sensors, roles, shown = []) => {
    const list = sensors.filter((s) => roles.includes(s.role) && !shown.includes(s));
    return list.map((s) => {
      const dd = h("dd", { class: "num" });
      updates.push(() => dd.replaceChildren(val(fmt(value(s.id), s.unit))));
      return [h("dt", {}, h("span", { class: "lat" }, s.name), h("small", { class: "kind" }, t(`SensorKind_${s.kind}`))), dd];
    });
  };

  let order = 0;
  const panel = ({ kind, title, sub, body, more, extraClass = "" }) => {
    const p = part(kind), el = h("section", { class: `panel ${p.cls} ${extraClass}`, style: { "--i": order++ } });
    const moreKids = [more].flat(Infinity).filter(Boolean);
    const btn = moreKids.length ? h("button", { class: "more", type: "button", "aria-expanded": "false", title: t("Web_Details"), "aria-label": t("Web_Details") }, icon("chevron")) : null;
    const toggle = () => { const open = el.classList.toggle("open"); btn.setAttribute("aria-expanded", String(open)); };
    btn?.addEventListener("click", toggle);
    el.append(...[
      h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon(p.icon)),
        h("div", { class: "ttl" }, h("h3", { class: "panel-title" }, p.page ? h("a", { href: `#/${p.page}` }, title) : title), sub ? h("div", { class: "panel-sub", title: sub }, sub) : null), btn),
      body,
      moreKids.length ? h("div", { class: "panel-more" }, h("div", {}, h("dl", { class: "kv" }, moreKids))) : null].flat(Infinity).filter(Boolean));
    return el;
  };

  // The parts the tiles above already show (processor, graphics, memory, network) have their own pages; this row is what the tiles do not hold.
  const panels = h("div", { class: "panels dash pair" });
  // Board and system: what the machine is (from the inventory, filled in when it arrives) and the board's own sensors.
  const boardSensors = board ? sensorsUnder(board) : [];
  const inv = { board: h("span", {}), bios: h("dd", { class: "lat" }), os: h("dd", { class: "lat" }) };
  panels.append(panel({ kind: "System", title: t("Web_Dash_System"), sub: null,
    body: [h("div", { class: "panel-sub", style: { marginTop: "6px" } }, inv.board),
      h("div", { class: "stats" }, stat(t("Web_Dash_BoardTemp"), boardSensors.find((s) => s.role === "BoardTemp")), stat(t("Web_Dash_ChipsetTemp"), boardSensors.find((s) => s.role === "ChipsetTemp")))],
    more: [h("dt", {}, t("Dashboard_Inv_Bios")), inv.bios, h("dt", {}, t("Dashboard_Inv_Os")), inv.os,
      details(boardSensors, ["BoardFan", "CpuFan", "BoardVoltage", "BoardTemp", "ChipsetTemp"], [boardSensors.find((s) => s.role === "BoardTemp"), boardSensors.find((s) => s.role === "ChipsetTemp")])] }));

  const driveHealth = new Map();   // drive name → its health line, filled in when the inventory arrives
  if (drives.length) {
    const rows = drives.map((d) => {
      const temp = pick(d, "StorageTemp"), tv = h("span", {}), hv = h("span", { class: "health" });
      driveHealth.set(d.name.trim().toLowerCase(), hv);
      updates.push(() => tv.replaceChildren(val(temp ? fmt(value(temp.id), temp.unit) : null)));
      return h("div", { class: "unit-row" }, h("span", { class: "nm", title: d.name }, d.name), h("span", { class: "vals" }, hv, tv),
        meter(t("Web_Dash_UsedSpace"), percentOf(pick(d, "StorageUsedSpace"))));
    });
    panels.append(panel({ kind: "Storage", title: t("Nav_Storage"), sub: t("Web_Dash_Drives", fa(drives.length)), extraClass: "span3",
      body: h("div", { class: "units cols-auto" }, rows),
      more: drives.map((d) => { const kv = details(d.sensors, ["StorageReadRate", "StorageWriteRate", "StorageTotalActivity", "StorageRemainingLife", "StorageWear", "StorageSpare", "StorageDataWritten", "StoragePowerOnHours", "StoragePowerCycles", "StorageFreeSpace"]); return kv.length ? [h("dt", { class: "sub lat" }, d.name), kv] : null; }) }));
  }
  // ——— The shop and its people ———
  const company = h("div", { class: "panels company" }, shopPanel("product"), shopPanel("system"), contactPanel());
  el.append(plane, panels, h("h2", { class: "section-title" }, t("Web_Company_Title")), company);

  const tick = () => { for (const fn of updates) fn(); };
  tick();
  const off = subscribe(tick);

  call("reports.state").then((r) => {
    const last = r.items.find((x) => x.kind === "TestSession");
    verdict.textContent = t(`Web_Dash_Verdict_${last ? last.badge : "None"}`);
  });
  // The last start's inventory of the same parts draws at once; this start's own read replaces it (a drive's health may have changed).
  let cached = false;
  const load = () => call("inventory.get").then((i) => {
    cached = i.cached;
    inv.board.replaceChildren(i.board || "");
    inv.bios.replaceChildren(val(i.bios, "lat")); inv.os.replaceChildren(val(i.os, "lat"));
    for (const d of i.drives || []) {
      const el = d.name && driveHealth.get(d.name.trim().toLowerCase());
      if (el) { el.textContent = d.health; el.dataset.status = d.status || ""; }
    }
  });
  load();
  const offFresh = on("hardwareFresh", () => { if (cached) load(); });
  el.classList.add("dash-page");
  return () => { off(); offFresh(); };
}

// A random product from the shop's site ("product": any, "system": one of its ready-built computers). The host turns its HTML into plain text and
// its picture into a data URL; offline, the last one is kept.
function shopPanel(kind) {
  const system = kind === "system";
  const body = h("div", { class: "shop", "aria-busy": "true" }, h("div", { class: "img skeleton" }), h("div", {}, h("div", { class: "skeleton", style: { height: "18px", width: "70%" } }),
    h("div", { class: "skeleton", style: { height: "12px", marginTop: "12px" } }), h("div", { class: "skeleton", style: { height: "12px", marginTop: "8px", width: "85%" } })));
  const another = h("button", { class: "btn quiet", type: "button", onclick: () => load(true) }, icon("refresh"), t(system ? "Web_Shop_AnotherSystem" : "Web_Shop_Another"));
  const el = h("section", { class: "panel p-shop" },
    h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon(system ? "cpu" : "shop")),
      h("div", { class: "ttl" }, h("h3", { class: "panel-title" }, t(system ? "Web_Shop_Systems_Title" : "Web_Shop_Title")), h("div", { class: "panel-sub fa" }, t(system ? "Web_Shop_Systems_Sub" : "Dashboard_Mazesta_L2")))),
    body,
    h("div", { class: "panel-foot" }, h("a", { href: "#", onclick: (e) => { e.preventDefault(); call("app.openLink", { key: system ? "systems" : "shop" }); } }, t(system ? "Web_Shop_AllSystems" : "Web_Shop_All"), icon("popout")), another));
  async function load(fresh) {
    another.disabled = true; body.setAttribute("aria-busy", "true");
    try {
      const p = await call("shop.product", { another: fresh, kind });
      if (!p) { body.replaceChildren(h("p", { class: "muted", style: { gridColumn: "1 / -1", margin: 0 } }, t("Web_Shop_Offline"))); return; }
      body.replaceChildren(
        p.image ? h("img", { class: "img", src: p.image, alt: p.title, loading: "lazy" }) : h("div", { class: "img noimg" }, icon("shop")),
        h("div", {}, h("h4", {}, p.title), p.summary ? h("p", {}, p.summary) : null,
          h("div", { class: "acts" }, h("button", { class: "slab small", type: "button", onclick: () => call("shop.open", { url: p.link }) }, t("Web_Shop_View"), icon("arrow")))));
    } catch { body.replaceChildren(h("p", { class: "muted", style: { margin: 0 } }, t("Web_Shop_Offline"))); }
    finally { another.disabled = false; body.removeAttribute("aria-busy"); }
  }
  load(false);
  return el;
}

// The company's sales and support lines as its own site publishes them; the chips open only the links the host knows by name.
function contactPanel() {
  const c = boot.contact || {};
  const chip = (key, ico, label) => h("button", { class: "chip", type: "button", onclick: () => call("app.openLink", { key }) }, icon(ico), label);
  const line = (who, number, ...chips) => h("div", { class: "line" }, h("span", { class: "who" }, who), h("span", { class: "no" }, number), chips.length ? h("div", { class: "links" }, chips) : null);
  const messengers = (desk) => [chip(`${desk}-telegram`, "send", t("Web_Contact_Telegram")), chip(`${desk}-whatsapp`, "chat", t("Web_Contact_WhatsApp")), chip("bale", "chat", t("Web_Contact_Bale"))];
  return h("section", { class: "panel p-contact" },
    h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("phone")),
      h("div", { class: "ttl" }, h("h3", { class: "panel-title" }, t("Web_Contact_Title")), h("div", { class: "panel-sub fa" }, t("Dashboard_Mazesta_L1")))),
    h("div", { class: "contact" },
      c.office && line(t("Web_Contact_Office"), c.office),
      c.sales && line(t("Web_Contact_Sales"), c.sales, ...messengers("sales")),
      c.support && line(t("Web_Contact_Support"), c.support, ...messengers("support")),
      c.hours && h("p", { class: "note" }, icon("clock"), h("span", {}, t(c.hours))),
      c.address && h("p", { class: "note" }, icon("pin"), h("span", {}, t(c.address), c.postcode ? [` — ${t("Web_Contact_Postcode")} `, h("span", { class: "lat" }, c.postcode)] : null)),
      h("div", { class: "links" }, chip("site", "net", t("Dashboard_Mazesta_Site")), chip("channel-telegram", "send", t("Web_Contact_Channel")), chip("instagram", "camera", t("Web_Contact_Instagram")))));
}
