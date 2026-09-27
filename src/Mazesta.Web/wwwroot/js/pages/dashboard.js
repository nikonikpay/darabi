// The dashboard: the yellow plane with the machine's standing and its two temperatures at poster scale, then a boxed panel per part, each in its
// own hue: the few readings that matter as a summary, the rest folded under "details". Then the shop's own product and people. Every number is
// a live reading or says it is not available.
import { call } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { fmt, whole } from "../format.js";
import { topNodes, pick, value, stats, subscribe } from "../store.js";
import { h, val, roll, icon, regMarks, toast } from "../ui.js";
import { go, boot } from "../app.js";
import { part, sensorsUnder } from "../parts.js";

export function mount(el) {
  const cpu = topNodes("Cpu")[0], gpus = topNodes("Gpu"), gpu = gpus.find((g) => pick(g, "GpuCoreTemp")) || gpus[0];
  const ram = topNodes("Memory").find((n) => pick(n, "RamUsed"));
  const drives = topNodes("Storage"), nets = topNodes("Network").filter((n) => !/^vEthernet/i.test(n.name)), board = topNodes("Motherboard")[0];
  const cpuTemp = pick(cpu, "CpuPackageTemp", "CpuTctlTdie"), gpuTemp = pick(gpu, "GpuCoreTemp");
  const updates = [];   // closures run on every poll

  // ——— The plane ———
  const verdict = h("h2", { class: "verdict" }, t("Web_Dash_Verdict_None"));
  const machine = h("div", { class: "machine lat" }, [cpu?.name, gpu?.name].filter(Boolean).join("  ·  "));
  const giant = (labelKey, sensor) => {
    const v = h("div", { class: "val" }), r = h("div", { class: "rng" }), span = h("i", { class: "span" }), now = h("i", { class: "now" });
    return { sensor, v, r, span, now, el: h("div", { class: "giant" }, h("div", { class: "lbl" }, h("span", {}, t(labelKey)), h("span", { class: "muted lat" }, sensor ? sensor.node.name ?? "" : "")), v,
      h("div", { class: "range-rule", "aria-hidden": "true" }, span, now), r) };
  };
  const g1 = giant("Web_Dash_CpuTemp", cpuTemp && { ...cpuTemp, node: cpu }), g2 = giant("Web_Dash_GpuTemp", gpuTemp && { ...gpuTemp, node: gpu });
  const date = new Intl.DateTimeFormat(boot.rtl ? "fa-IR-u-ca-persian" : "en-GB", { weekday: "long", day: "numeric", month: "long", year: "numeric" }).format(new Date());
  const summaryBtn = h("button", { class: "btn", style: { borderColor: "var(--on-yellow)", color: "var(--on-yellow)" }, onclick: async () => {
    summaryBtn.disabled = true; toast(t("Reports_SummaryBusy"));
    try { await call("reports.exec", { cmd: "summary" }); } finally { summaryBtn.disabled = false; }
  } }, icon("doc"), t("Reports_Summary"));
  const plane = h("section", { class: "plane enter" }, regMarks(),
    verdict, machine,
    h("div", { class: "giants" }, g1.el, g2.el),
    h("div", { class: "plane-actions" }, h("button", { class: "slab", onclick: () => go("tests") }, t("Web_Dash_RunTests"), icon("arrow")), summaryBtn),
    h("div", { class: "colophon" }, h("span", {}, boot.shopName), h("span", {}, date)));
  updates.push(() => {
    for (const g of [g1, g2]) {
      const s = g.sensor, v = s ? value(s.id) : null;
      roll(g.v, whole(v));
      if (v !== null && !g.v.querySelector(".unit")) g.v.append(h("span", { class: "unit" }, "°C"));
      // The session range, drawn on a 0-100 °C scale and written out; figures stay Latin (passed as text, not numbers).
      const st = s && stats.get(s.id), pct = (x) => `${Math.min(100, Math.max(0, x))}%`;
      g.r.textContent = st ? t("Web_Dash_Range", String(Math.round(st[0])), String(Math.round(st[1])), `‎${Math.round(st[2])} °C‎`) : "";
      g.span.style.left = st ? pct(st[0]) : "0"; g.span.style.width = st ? pct(st[2] - st[0]) : "0";
      g.now.hidden = v === null; if (v !== null) g.now.style.left = pct(v);
    }
  });

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
  const percentOf = (s) => () => { const v = s ? value(s.id) : null; return v === null ? null : v / 100; };
  const ratioOf = (a, b) => () => { const x = a ? value(a.id) : null, y = b ? value(b.id) : null; return x === null || !y ? null : x / y; };
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

  const panels = h("div", { class: "panels" });
  if (cpu) {
    const all = sensorsUnder(cpu), shown = [cpuTemp, pick(cpu, "CpuEffectiveClockAverage", "CpuCoreClockAverage", "CpuCoreClock"), pick(cpu, "CpuTotalLoad"), pick(cpu, "CpuPackagePower")];
    panels.append(panel({ kind: "Cpu", title: t("Nav_Cpu"), sub: cpu.name,
      body: [h("div", { class: "stats" }, stat(t("Web_Dash_Temp"), shown[0]), stat(t("Dashboard_Line_Clock"), shown[1]), stat(t("Dashboard_Line_Load"), shown[2]), stat(t("Dashboard_Line_Power"), shown[3])),
        meter(t("Dashboard_Line_Load"), percentOf(shown[2]))],
      more: details(all, ["CpuVcore", "CpuCcdTemp", "CpuTctlTdie", "CpuPackageTemp", "CpuEffectiveClockAverage", "CpuCoreClockAverage", "CpuBusClock", "CpuFan"], shown) }));
  }
  gpus.forEach((g, i) => {
    const all = sensorsUnder(g), shown = [pick(g, "GpuCoreTemp"), pick(g, "GpuLoad3D", "GpuLoadD3D3D"), pick(g, "GpuCoreClock"), pick(g, "GpuPower")];
    const used = pick(g, "GpuVramUsed"), total = pick(g, "GpuVramTotal");
    panels.append(panel({ kind: "Gpu", title: gpus.length > 1 ? `${t("Nav_Gpu")} ${fa(i + 1)}` : t("Nav_Gpu"), sub: g.name,
      body: [h("div", { class: "stats" }, stat(t("Web_Dash_Temp"), shown[0]), stat(t("Dashboard_Line_Load"), shown[1]), stat(t("Dashboard_Line_Clock"), shown[2]), stat(t("Dashboard_Line_Power"), shown[3])),
        meter(t("Dashboard_Line_VramUsed"), ratioOf(used, total))],
      more: details(all, ["GpuHotSpotTemp", "GpuVramTemp", "GpuMemoryClock", "GpuVoltage", "GpuFanRpm", "GpuFanPercent", "GpuLoadVideo", "GpuLoadCompute", "GpuVramUsed", "GpuVramTotal"], shown) }));
  });
  if (ram) {
    const used = pick(ram, "RamUsed"), free = pick(ram, "RamFree"), load = pick(ram, "RamLoad");
    panels.append(panel({ kind: "Memory", title: t("Dashboard_Ram"), sub: null,
      body: [h("div", { class: "stats" }, stat(t("Dashboard_Line_Used"), used), stat(t("Dashboard_Line_Free"), free)), meter(t("Dashboard_Line_Load"), percentOf(load))],
      more: details(topNodes("Memory").flatMap(sensorsUnder), ["RamTotal", "DimmTemp"]) }));
  }
  if (drives.length) {
    const rows = drives.map((d) => {
      const temp = pick(d, "StorageTemp"), tv = h("span", {});
      updates.push(() => tv.replaceChildren(val(temp ? fmt(value(temp.id), temp.unit) : null)));
      return h("div", { class: "unit-row" }, h("span", { class: "nm", title: d.name }, d.name), h("span", { class: "vals" }, tv),
        meter(t("Web_Dash_UsedSpace"), percentOf(pick(d, "StorageUsedSpace"))));
    });
    panels.append(panel({ kind: "Storage", title: t("Nav_Storage"), sub: t("Web_Dash_Drives", fa(drives.length)),
      body: h("div", { class: "units" }, rows),
      more: drives.map((d) => { const kv = details(d.sensors, ["StorageReadRate", "StorageWriteRate", "StorageRemainingLife", "StoragePowerOnHours"]); return kv.length ? [h("dt", { class: "sub lat" }, d.name), kv] : null; }) }));
  }
  if (nets.length) {
    const rows = nets.slice(0, 4).map((n) => {
      const down = pick(n, "NetDownload"), up = pick(n, "NetUpload"), dv = h("span", {}), uv = h("span", {});
      updates.push(() => { dv.replaceChildren("↓ ", val(down ? fmt(value(down.id), down.unit) : null)); uv.replaceChildren("↑ ", val(up ? fmt(value(up.id), up.unit) : null)); });
      return h("div", { class: "unit-row" }, h("span", { class: "nm", title: n.name }, n.name), h("span", { class: "vals" }, dv, uv));
    });
    panels.append(panel({ kind: "Network", title: t("Nav_Network"), sub: null, body: h("div", { class: "units" }, rows),
      more: nets.slice(0, 4).map((n) => { const kv = details(n.sensors, ["NetUtilization"]); return kv.length ? [h("dt", { class: "sub lat" }, n.name), kv] : null; }) }));
  }
  // Board and system: what the machine is (from the inventory, filled in when it arrives) and the board's own sensors.
  const boardSensors = board ? sensorsUnder(board) : [];
  const inv = { board: h("span", {}), bios: h("dd", { class: "lat" }), os: h("dd", { class: "lat" }) };
  panels.append(panel({ kind: "System", title: t("Web_Dash_System"), sub: null,
    body: [h("div", { class: "panel-sub", style: { marginTop: "6px" } }, inv.board),
      h("div", { class: "stats" }, stat(t("Web_Dash_BoardTemp"), boardSensors.find((s) => s.role === "BoardTemp")), stat(t("Web_Dash_ChipsetTemp"), boardSensors.find((s) => s.role === "ChipsetTemp")))],
    more: [h("dt", {}, t("Dashboard_Inv_Bios")), inv.bios, h("dt", {}, t("Dashboard_Inv_Os")), inv.os,
      details(boardSensors, ["BoardFan", "CpuFan", "BoardVoltage", "BoardTemp", "ChipsetTemp"], [boardSensors.find((s) => s.role === "BoardTemp"), boardSensors.find((s) => s.role === "ChipsetTemp")])] }));

  // ——— The shop and its people ———
  const company = h("div", { class: "panels company" }, shopPanel(), contactPanel());
  el.append(plane, panels, h("h2", { class: "section-title" }, t("Web_Company_Title")), company);

  const tick = () => { for (const fn of updates) fn(); };
  tick();
  const off = subscribe(tick);

  call("reports.state").then((r) => {
    const last = r.items.find((x) => x.kind === "TestSession");
    verdict.textContent = t(`Web_Dash_Verdict_${last ? last.badge : "None"}`);
  });
  call("inventory.get").then((i) => {
    inv.board.replaceChildren(i.board || "");
    inv.bios.replaceChildren(val(i.bios, "lat")); inv.os.replaceChildren(val(i.os, "lat"));
  });
  return off;
}

// A random product from the shop's site. The host turns its HTML into plain text and its picture into a data URL; offline, the last one is kept.
function shopPanel() {
  const body = h("div", { class: "shop", "aria-busy": "true" }, h("div", { class: "img skeleton" }), h("div", {}, h("div", { class: "skeleton", style: { height: "18px", width: "70%" } }),
    h("div", { class: "skeleton", style: { height: "12px", marginTop: "12px" } }), h("div", { class: "skeleton", style: { height: "12px", marginTop: "8px", width: "85%" } })));
  const another = h("button", { class: "btn quiet", type: "button", onclick: () => load(true) }, icon("refresh"), t("Web_Shop_Another"));
  const el = h("section", { class: "panel p-shop" },
    h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("shop")),
      h("div", { class: "ttl" }, h("h3", { class: "panel-title" }, t("Web_Shop_Title")), h("div", { class: "panel-sub fa" }, t("Dashboard_Mazesta_L2")))),
    body,
    h("div", { class: "panel-foot" }, h("a", { href: "#", onclick: (e) => { e.preventDefault(); call("app.openLink", { key: "shop" }); } }, t("Web_Shop_All"), icon("popout")), another));
  async function load(fresh) {
    another.disabled = true; body.setAttribute("aria-busy", "true");
    try {
      const p = await call("shop.product", { another: fresh });
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
  return h("section", { class: "panel p-contact" },
    h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("phone")),
      h("div", { class: "ttl" }, h("h3", { class: "panel-title" }, t("Web_Contact_Title")), h("div", { class: "panel-sub fa" }, t("Dashboard_Mazesta_L1")))),
    h("div", { class: "contact" },
      c.sales && line(t("Web_Contact_Sales"), c.sales, chip("sales-whatsapp", "chat", t("Web_Contact_WhatsApp"))),
      c.support && line(t("Web_Contact_Support"), c.support, chip("support-whatsapp", "chat", t("Web_Contact_WhatsApp")), chip("support-telegram", "send", t("Web_Contact_Telegram"))),
      c.office && line(t("Web_Contact_Office"), c.office),
      c.fax && line(t("Web_Contact_Fax"), c.fax),
      c.email && line(t("Web_Contact_Email"), c.email, chip("email", "mail", t("Web_Contact_SendMail"))),
      c.hours && h("p", { class: "note" }, icon("clock"), h("span", {}, t(c.hours))),
      c.address && h("p", { class: "note" }, icon("pin"), h("span", {}, t(c.address), c.postcode ? [` — ${t("Web_Contact_Postcode")} `, h("span", { class: "lat" }, c.postcode)] : null)),
      h("div", { class: "links" }, chip("site", "net", t("Dashboard_Mazesta_Site")), chip("channel-telegram", "send", t("Web_Contact_Channel")), chip("instagram", "camera", t("Web_Contact_Instagram")))));
}
