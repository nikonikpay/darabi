// The dashboard: the machine's standing and a live tile per part (processor, graphics, memory, network; each opens its page), then the board
// and the drives, which the tiles do not hold, the drives side by side. Then the shop's own product, one of its ready systems and its people.
// The panels sit on a grid read from the page's own width (not the window's, the assistant's column takes part of it). Every number is a live
// reading or says it is not available.
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { fmt } from "../format.js";
import { hw, topNodes, pick, value, stats, subscribe, netRank } from "../store.js";
import { h, val, icon, toast } from "../ui.js";
import { liveTile, percentOf, ratioOf } from "../tiles.js";
import { go, boot } from "../app.js";
import { contactLines } from "../contact.js";
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
  const action = (cls, ico, key, onclick, extra) => h("button", { class: cls, type: "button", onclick }, h("span", { class: "ico" }, icon(ico)), h("span", { class: "nm" }, t(key)), extra || null);
  // Windows Update, on or off, from here: the same profiles as its own page (Windows' own behaviour, or everything off), read back from Windows.
  const wu = h("span", { class: "pill none" }, "—"); let wuState = null;
  const showWu = (state) => { wuState = state; const off = state === "Disabled"; wu.className = `pill ${off ? "warn" : "pass"}`; wu.textContent = t(off ? "Web_Dash_Updates_Off" : "Web_Dash_Updates_On"); };
  const toggleWu = async () => {
    if (!wuState) return;
    const to = wuState === "Disabled" ? "Default" : "Disabled";
    if (to === "Disabled" && !confirm(t("Updates_ConfirmDisable"))) return;
    try { const r = await call("tweaks.update", { profile: to }); showWu(r.update); toast(r.error ? t("Tweaks_Failed", t("Nav_Updates"), r.error) : t("Updates_Done", t(`Updates_${to}`)), r.error ? "fail" : "ok"); }
    catch (e) { toast(String(e.message || e), "fail"); }
  };
  call("tweaks.state").then((x) => showWu(x.update)).catch(() => {});
  const status = h("section", { class: "plane status-card enter" },
    h("div", { class: "status-text" }, h("span", { class: "status-kicker" }, t("Web_Dash_Live"), h("span", { class: "status-date" }, date)), verdict, machine),
    h("div", { class: "quick" },
      action("", "check", "Web_Dash_Checkup", () => go("checkup")), action("p-gpu", "trophy", "Web_Dash_Bench", () => go("benchmarks")),
      action("p-game", "update", "Web_Dash_Updates", toggleWu, wu), action("p-board", "board", "Web_Dash_Info", () => go("system"))));
  // Each temperature with its part's load beside it and as the bar, like the overlay; memory used, its load, and the total under it.
  // The adapter the internet goes through, as the network page shows it; the first connected one without it.
  const net = [...nets].sort((a, b) => netRank(a) - netRank(b))[0];
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
  const plane = h("div", { class: "hero-row" }, status, h("div", { class: "tiles compact" }, tiles.map((x) => x.el)));

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
  // Board, system and power in one panel: what the machine is (from the inventory, filled in when it arrives), the board's own sensors, and
  // the parts' power added up without counting anything twice (the same rule as PowerTotals on the host): a power supply's output reading is
  // the whole of it; else the CPU by its package (its cores are inside it), each graphics card by its one card reading, RAM and drives where
  // they report power. The board, the fans and what has no power sensor are named as not measured, never guessed.
  const boardSensors = board ? sensorsUnder(board) : [];
  const inv = { board: h("span", {}), bios: h("dd", { class: "lat" }), os: h("dd", { class: "lat" }) };
  {
    const all = hw.nodes, under = (n) => sensorsUnder(n).filter((x) => x.kind === "Power");
    const psu = all.filter((n) => n.kind === "Psu").flatMap(under).sort((x, y) => /total/i.test(y.name) - /total/i.test(x.name));
    const parts = [];
    for (const n of topNodes()) {
      const p = under(n);
      if (n.kind === "Cpu") parts.push(...p.filter((x) => x.role === "CpuPackagePower").slice(0, 1).map((x) => [n, x]));
      else if (n.kind === "Gpu") parts.push(...p.filter((x) => x.role === "GpuPower").slice(0, 1).map((x) => [n, x]));
      else if (n.kind === "Memory" || n.kind === "Storage") parts.push(...p.map((x) => [n, x]));
    }
    const has = (k) => parts.some(([n]) => n.kind === k);
    const unmeasured = ["Motherboard", "Cooler", ...["Memory", "Storage"].filter((k) => topNodes(k).length && !has(k))];
    const total = h("span", { class: "v" }), note = h("p", { class: "power-note" });
    updates.push(() => {
      const ps = psu.find((x) => value(x.id) > 0);
      const vals = parts.map(([, x]) => value(x.id)).filter((v) => v !== null && v >= 0);
      const w = ps ? value(ps.id) : vals.length ? vals.reduce((x, y) => x + y, 0) : null;
      total.replaceChildren(w === null ? val(null) : h("span", { class: "num" }, Math.round(w)), w === null ? "" : h("small", {}, "W"));
      note.textContent = ps ? t("Web_Dash_PowerPsu") : t("Web_Dash_PowerUnmeasured", unmeasured.map((k) => t(`Web_Kind_${k}`)).join("، "));
    });
    const rows = parts.map(([n, x]) => {
      const v = h("span", { class: "num" });
      updates.push(() => v.replaceChildren(val(fmt(value(x.id), x.unit))));
      return h("div", { class: "power-row" }, h("span", { class: "nm", title: `${n.name} · ${x.name}` }, t(`Web_Kind_${n.kind}`), " ", h("small", { class: "lat" }, n.name)), v);
    });
    const boardTemp = boardSensors.find((x) => x.role === "BoardTemp"), chipTemp = boardSensors.find((x) => x.role === "ChipsetTemp");
    panels.append(panel({ kind: "System", title: t("Web_Dash_SystemPower"), sub: inv.board,
      body: [h("div", { class: "stats three" }, h("div", { class: "stat" }, h("span", { class: "k" }, t("Web_Dash_PowerTotal")), total), stat(t("Web_Dash_BoardTemp"), boardTemp), chipTemp ? stat(t("Web_Dash_ChipsetTemp"), chipTemp) : null),
        h("div", { class: "power-rows" }, rows), note],
      more: [h("dt", {}, t("Dashboard_Inv_Bios")), inv.bios, h("dt", {}, t("Dashboard_Inv_Os")), inv.os,
        details(boardSensors, ["BoardFan", "CpuFan", "BoardVoltage", "BoardTemp", "ChipsetTemp"], [boardTemp, chipTemp])] }));
  }

  const driveHealth = new Map();   // drive name → its health line, filled in when the inventory arrives
  if (drives.length) {
    // A drive more than nine tenths full is marked: its row and bar turn red and it says so (a full system drive slows Windows and stops updates).
    const FULL = 0.9, alert = h("span", { class: "pill fail", hidden: true });
    const shares = drives.map((d) => percentOf(pick(d, "StorageUsedSpace")));
    const rows = drives.map((d, k) => {
      const temp = pick(d, "StorageTemp"), tv = h("span", {}), hv = h("span", { class: "health" }), num = h("span", {}), bar = h("i", {}), full = h("span", { class: "full-mark", hidden: true }, icon("alert"), t("Web_Dash_DriveFull"));
      driveHealth.set(d.name.trim().toLowerCase(), hv);
      const row = h("div", { class: "unit-row" }, h("span", { class: "nm", title: d.name }, d.name), h("span", { class: "vals" }, hv, tv),
        h("div", { class: "meter" }, h("div", { class: "row" }, h("span", {}, t("Web_Dash_UsedSpace"), full), num), h("div", { class: "bar", role: "presentation" }, bar)));
      updates.push(() => {
        tv.replaceChildren(val(temp ? fmt(value(temp.id), temp.unit) : null));
        const p = shares[k]();
        bar.style.setProperty("--p", p === null ? 0 : Math.min(1, Math.max(0, p)));
        num.replaceChildren(p === null ? val(null) : h("span", { class: "num" }, `${Math.round(p * 100)}%`));
        row.classList.toggle("full", p !== null && p > FULL); full.hidden = !(p !== null && p > FULL);
      });
      return row;
    });
    updates.push(() => { const n = shares.filter((f) => { const p = f(); return p !== null && p > FULL; }).length; alert.hidden = !n; alert.textContent = t("Web_Dash_DrivesFull", fa(n)); });
    const p = panel({ kind: "Storage", title: t("Nav_Storage"), sub: t("Web_Dash_Drives", fa(drives.length)),
      body: h("div", { class: `units drives ${drives.length > 3 ? "scroll" : ""}`, tabindex: drives.length > 3 ? "0" : null }, rows),
      more: drives.map((d) => { const kv = details(d.sensors, ["StorageReadRate", "StorageWriteRate", "StorageTotalActivity", "StorageRemainingLife", "StorageWear", "StorageSpare", "StorageDataWritten", "StoragePowerOnHours", "StoragePowerCycles", "StorageFreeSpace"]); return kv.length ? [h("dt", { class: "sub lat" }, d.name), kv] : null; }) });
    p.querySelector(".panel-head .ttl").after(alert);
    panels.append(p);
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

// A product from the shop's site: one on special sale when there is one, else a random one ("product": any, "system": one of its ready-built computers). The host turns its HTML into plain text and
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
        h("div", {}, p.onSale ? h("span", { class: "sale" }, t("Web_Shop_OnSale")) : null, h("h4", {}, p.title), p.summary ? h("p", {}, p.summary) : null,
          h("div", { class: "acts" }, h("button", { class: "slab small", type: "button", onclick: () => call("shop.open", { url: p.link }) }, t("Web_Shop_View"), icon("arrow")))));
    } catch { body.replaceChildren(h("p", { class: "muted", style: { margin: 0 } }, t("Web_Shop_Offline"))); }
    finally { another.disabled = false; body.removeAttribute("aria-busy"); }
  }
  load(false);
  return el;
}

// The company's sales and support lines as its own site publishes them; the chips open only the links the host knows by name.
function contactPanel() {
  return h("section", { class: "panel p-contact" },
    h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("phone")),
      h("div", { class: "ttl" }, h("h3", { class: "panel-title" }, t("Web_Contact_Title")), h("div", { class: "panel-sub fa" }, t("Dashboard_Mazesta_L1")))),
    contactLines(boot.contact || {}));
}
