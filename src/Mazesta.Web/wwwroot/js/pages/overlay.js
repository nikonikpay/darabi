// The overlay's own page: a ready-made set to start from (game, render, troubleshooting), every item it can show grouped by part in the part's
// hue (on or off, chart or not; each drive's own read, write, temperature and activity too), the order it is drawn in (dragged, or moved with
// the arrows), its look (one column or two, corner, size, opacity), and a live preview drawn from the same snapshots the overlay reads. Items
// this machine has no sensor for say so and cannot be turned on; the frame rate is measured only while the overlay is on screen.
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { fmt } from "../format.js";
import { hw, value, subscribe } from "../store.js";
import { h, icon } from "../ui.js";
import { part, hueOf } from "../parts.js";

const PARTS = ["Gaming", "Gpu", "Cpu", "Memory", "Storage", "Network"];
const SHORT = { Gaming: "GAME", Gpu: "GPU", Cpu: "CPU", Memory: "RAM", Storage: "DISK", Network: "NET" };
const PRESET_ICON = { game: "gamepad", render: "cpu", troubleshoot: "bug" };
const TREND = 60;

// The blocks the overlay draws, as the host builds them: a block per part (a drive's own items make that drive's block), in the order their
// first item was put, each with its items in order.
export function blocksOf(items) {
  const blocks = [];
  for (const it of items) {
    const key = `${it.part}|${it.device || ""}`;
    let b = blocks.find((x) => x.key === key);
    if (!b) blocks.push((b = { key, part: it.part, device: it.device, deviceName: it.deviceName, items: [] }));
    b.items.push(it);
  }
  return blocks;
}

export function mount(el) {
  let state = null, frames = null;
  const history = new Map();   // item id -> recent values, for the preview's charts

  const show = h("input", { type: "checkbox", class: "switch", "aria-label": t("Web_Overlay_Show"), onchange: (e) => call("overlay.set", { field: "visible", value: e.target.checked }) });
  const corner = h("select", { class: "field", "aria-label": t("Overlay_Corner"), onchange: (e) => call("overlay.set", { field: "corner", value: e.target.value }) });
  const opacity = h("input", { type: "range", class: "range", min: "50", max: "100", step: "5", "aria-label": t("Web_Overlay_Opacity"),
    oninput: (e) => { fill(e.target); preview.style.setProperty("--ov-alpha", e.target.value / 100); }, onchange: (e) => call("overlay.set", { field: "opacity", value: e.target.value / 100 }) });
  const seg = (label, field, options) => h("div", { class: "seg", role: "group", "aria-label": label }, options.map(([v, k]) =>
    h("button", { type: "button", "data-v": v, onclick: () => call("overlay.set", { field, value: field === "scale" ? +v : v }) }, t(k))));
  const sizes = seg(t("Web_Overlay_Size"), "scale", [["0.85", "Web_Overlay_Small"], ["1", "Web_Overlay_Normal"], ["1.2", "Web_Overlay_Large"]]);
  const layouts = seg(t("Web_Overlay_Layout"), "layout", [["list", "Web_Overlay_Layout_List"], ["columns", "Web_Overlay_Layout_Columns"]]);
  const hotkey = h("span", { class: "kbd lat" });
  const problem = h("p", { class: "banner", hidden: true });

  const presets = h("div", { class: "presets" });
  const groups = h("div", { class: "ov-groups" });
  const order = h("div", { class: "ov-order" });
  const preview = h("div", { class: "ov-preview", "aria-label": t("Web_Overlay_Preview") });

  el.append(
    h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Overlay")), h("p", { class: "page-lede" }, t("Web_Overlay_Lede")))),
    h("section", { class: "ov-bar" },
      h("label", { class: "ov-show" }, show, h("span", {}, h("b", {}, t("Web_Overlay_Show")), h("small", {}, t("Web_Overlay_Hotkey"), " ", hotkey))),
      h("div", { class: "ov-ctl" }, h("span", {}, t("Web_Overlay_Layout")), layouts),
      h("label", { class: "ov-ctl" }, h("span", {}, t("Overlay_Corner")), corner),
      h("div", { class: "ov-ctl" }, h("span", {}, t("Web_Overlay_Size")), sizes),
      h("label", { class: "ov-ctl grow" }, h("span", {}, t("Web_Overlay_Opacity")), opacity)),
    problem,
    h("h2", { class: "section-title" }, t("Web_Overlay_Presets")), presets,
    h("div", { class: "ov-split" },
      h("div", {},
        h("h2", { class: "section-title" }, t("Web_Overlay_Order")), h("p", { class: "caption" }, t("Web_Overlay_OrderNote")), order,
        h("h2", { class: "section-title" }, t("Web_Overlay_Items")), groups),
      h("aside", { class: "ov-side" }, h("h2", { class: "section-title" }, t("Web_Overlay_Preview")), preview, h("p", { class: "caption" }, t("Web_Overlay_PreviewNote")))));

  function fill(r) { r.style.setProperty("--fill", `${((r.value - r.min) / (r.max - r.min)) * 100}%`); }

  // ——— Presets: a card each; the one in use is marked, "custom" once changed by hand ———
  function renderPresets(s) {
    presets.replaceChildren(...s.presets.map((p) => {
      const active = s.preset === p.id;
      return h("button", { class: `preset ${active ? "on" : ""}`, type: "button", "aria-pressed": String(active), onclick: () => call("overlay.preset", { id: p.id }) },
        h("span", { class: "ico" }, icon(PRESET_ICON[p.id] || "overlay")),
        h("span", { class: "txt" }, h("b", {}, t(`Web_Overlay_Preset_${p.id}`)), h("small", {}, t(`Web_Overlay_Preset_${p.id}_Note`))),
        h("span", { class: "cnt" }, active ? [icon("check"), t("Web_Overlay_InUse")] : t("Web_Overlay_ItemsCount", fa(p.count))));
    }), s.preset === "custom" ? h("div", { class: "preset custom on", role: "status" }, h("span", { class: "ico" }, icon("overlay")),
      h("span", { class: "txt" }, h("b", {}, t("Web_Overlay_Preset_custom")), h("small", {}, t("Web_Overlay_Preset_custom_Note")))) : "");
  }

  // ——— Items: a panel per part; each row is on/off, its live value, and its chart toggle. Drives get a sub-list each. ———
  const rowEls = new Map();
  function itemRow(it) {
    const val = h("span", { class: "ov-val num" });
    const onBox = h("input", { type: "checkbox", class: "switch", checked: it.on, disabled: !it.available, "aria-label": it.deviceName ? `${it.label} · ${it.deviceName}` : it.label,
      onchange: (e) => call("overlay.item", { id: it.id, on: e.target.checked, chart: it.chart }) });
    const chartBtn = h("button", { class: "icon-btn", type: "button", disabled: !it.available || !it.on, "aria-pressed": String(it.chart), title: t("Web_Overlay_ChartToggle"), "aria-label": `${t("Web_Overlay_ChartToggle")}: ${it.label}`,
      onclick: () => call("overlay.item", { id: it.id, on: true, chart: !it.chart }) }, icon("chart"));
    rowEls.set(it.id, { it, val });
    return h("div", { class: `ov-row ${it.available ? "" : "na"} ${it.on ? "on" : ""}` },
      h("label", { class: "ov-name" }, onBox, h("span", {}, it.label, it.aggregate !== "First" && !it.frame ? h("small", {}, t(`Web_Overlay_Agg_${it.aggregate}`)) : null)),
      it.available ? val : h("span", { class: "ov-na" }, t("Web_Overlay_NotHere")), chartBtn);
  }
  function renderItems(s) {
    groups.replaceChildren(); rowEls.clear();
    PARTS.forEach((kind, i) => {
      const items = s.items.filter((x) => x.part === kind && !x.device);
      const drives = blocksOf(s.items.filter((x) => x.part === kind && x.device));
      if (!items.length && !drives.length) return;
      const p = part(kind), all = s.items.filter((x) => x.part === kind);
      groups.append(h("section", { class: `panel group ${p.cls}`, style: { "--i": i } },
        h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon(p.icon)), h("div", { class: "ttl" }, h("h3", { class: "panel-title" }, t(p.key))),
          h("span", { class: "group-count" }, t("Web_Group_Count", all.length, all.filter((x) => x.on).length))),
        h("div", { class: "ov-list" }, items.map(itemRow)),
        drives.map((d) => h("div", { class: "ov-drive" }, h("div", { class: "ov-drive-name lat" }, icon("drive"), d.deviceName || d.device), h("div", { class: "ov-list" }, d.items.map(itemRow))))));
    });
  }

  // ——— Order: the shown blocks and items, dragged into place (or moved with the arrow buttons); items move within their own block ———
  let drag = null;
  function sendOrder(blocks) { call("overlay.order", { ids: blocks.flatMap((b) => b.items.map((x) => x.id)) }); }
  function shownBlocks() { return blocksOf(state.order.map((id) => state.items.find((x) => x.id === id)).filter((x) => x && x.available)); }
  function move(list, from, to) { const [x] = list.splice(from, 1); list.splice(to, 0, x); }
  function dropAt(e, el) { const r = el.getBoundingClientRect(); return e.clientY > r.top + r.height / 2 ? 1 : 0; }
  function arrows(label, canUp, canDown, up, down) {
    return h("span", { class: "ov-arrows" },
      h("button", { class: "icon-btn up", type: "button", disabled: !canUp, "aria-label": `${t("Web_Overlay_MoveUp")}: ${label}`, title: t("Web_Overlay_MoveUp"), onclick: up }, icon("chevron")),
      h("button", { class: "icon-btn", type: "button", disabled: !canDown, "aria-label": `${t("Web_Overlay_MoveDown")}: ${label}`, title: t("Web_Overlay_MoveDown"), onclick: down }, icon("chevron")));
  }
  function renderOrder() {
    const blocks = shownBlocks();
    if (!blocks.length) { order.replaceChildren(h("p", { class: "chart-empty" }, icon("overlay"), t("Web_Overlay_Empty"))); return; }
    order.replaceChildren(...blocks.map((b, bi) => {
      const p = part(b.part), title = b.device ? `${SHORT[b.part]} · ${b.deviceName || b.device}` : t(p.key);
      const blockEl = h("div", { class: `ov-oblock ${p.cls}`, draggable: "true", "data-bi": bi },
        h("div", { class: "ov-ohead" }, h("span", { class: "grip", "aria-hidden": "true" }, "⋮⋮"), h("span", { class: "ico" }, icon(b.device ? "drive" : p.icon)),
          h("b", { class: b.device ? "lat" : "" }, title),
          arrows(title, bi > 0, bi < blocks.length - 1, () => { move(blocks, bi, bi - 1); sendOrder(blocks); }, () => { move(blocks, bi, bi + 1); sendOrder(blocks); })),
        h("div", { class: "ov-oitems" }, b.items.map((it, ii) => h("div", { class: "ov-oitem", draggable: "true", "data-bi": bi, "data-ii": ii },
          h("span", { class: "grip", "aria-hidden": "true" }, "⋮⋮"), h("span", { class: "nm" }, it.label), it.chart ? h("span", { class: "ov-tag" }, icon("chart")) : null,
          arrows(it.label, ii > 0, ii < b.items.length - 1, () => { move(b.items, ii, ii - 1); sendOrder(blocks); }, () => { move(b.items, ii, ii + 1); sendOrder(blocks); })))));
      return blockEl;
    }));
    // Drag: a block over another block, or an item over another item of the same block; the drop lands before or after by the pointer's half.
    order.ondragstart = (e) => {
      const itemEl = e.target.closest(".ov-oitem"), blockEl = e.target.closest(".ov-oblock");
      drag = itemEl ? { kind: "item", bi: +itemEl.dataset.bi, ii: +itemEl.dataset.ii, el: itemEl } : { kind: "block", bi: +blockEl.dataset.bi, el: blockEl };
      e.stopPropagation(); e.dataTransfer.effectAllowed = "move"; e.dataTransfer.setData("text/plain", "");
      requestAnimationFrame(() => drag?.el.classList.add("dragging"));
    };
    order.ondragover = (e) => {
      if (!drag) return;
      const target = drag.kind === "item" ? e.target.closest(".ov-oitem") : e.target.closest(".ov-oblock");
      order.querySelectorAll(".drop-before, .drop-after").forEach((x) => x.classList.remove("drop-before", "drop-after"));
      if (!target || target === drag.el || (drag.kind === "item" && +target.dataset.bi !== drag.bi)) return;
      e.preventDefault(); target.classList.add(dropAt(e, target) ? "drop-after" : "drop-before");
    };
    order.ondrop = (e) => {
      if (!drag) return;
      const target = drag.kind === "item" ? e.target.closest(".ov-oitem") : e.target.closest(".ov-oblock");
      if (!target || target === drag.el) return;
      e.preventDefault();
      const after = dropAt(e, target);
      if (drag.kind === "block") { const to = +target.dataset.bi + after; move(blocks, drag.bi, to > drag.bi ? to - 1 : to); }
      else { if (+target.dataset.bi !== drag.bi) return; const list = blocks[drag.bi].items, to = +target.dataset.ii + after; move(list, drag.ii, to > drag.ii ? to - 1 : to); }
      sendOrder(blocks);
    };
    order.ondragend = () => { drag?.el.classList.remove("dragging"); drag = null; order.querySelectorAll(".drop-before, .drop-after").forEach((x) => x.classList.remove("drop-before", "drop-after")); };
  }

  // ——— Values: sensor items from the snapshots, frame items from what the overlay measured ———
  function current(it) {
    if (it.frame) {
      const v = frames && (it.id === "fps" ? frames.fps : it.id === "low1" ? frames.low1 : frames.frametime);
      return v === null || v === undefined ? null : { v, text: it.id === "frametime" ? `${v.toFixed(1)} ms` : `${Math.round(v)} FPS` };
    }
    const vals = it.sensors.map((id) => value(id)).filter((v) => v !== null);
    if (!vals.length) return null;
    const v = it.aggregate === "Max" ? Math.max(...vals) : it.aggregate === "Sum" ? vals.reduce((a, b) => a + b, 0) : vals[0];
    return { v, text: fmt(v, hw.sensors.get(it.sensors[0])?.unit) };
  }
  function tick() {
    if (!state) return;
    for (const { it, val } of rowEls.values()) {
      if (!it.available) continue;
      const c = current(it);
      val.replaceChildren(c ? c.text : h("span", { class: "hatch" }, t("Value_NotAvailable")));
      // The frame rate always keeps its minute: the frame-rate card draws it as bars, charted or not.
      if (it.on && (it.chart || it.id === "fps")) { const q = history.get(it.id) || []; q.push(c ? c.v : null); if (q.length > TREND) q.shift(); history.set(it.id, q); }
    }
    renderPreview();
  }

  // ——— The preview: the overlay as it will look, from the same blocks, in the same order, colours and layout: the frame-rate card on top, then
  // a card per part, each reading's number big with its unit small, a bar for a share of a fixed top or its chart ———
  function split(c) { if (!c) return ["—", ""]; const i = c.text.lastIndexOf(" "); return i > 0 ? [c.text.slice(0, i), c.text.slice(i + 1)] : [c.text, ""]; }
  function renderPreview() {
    const blocks = shownBlocks(), game = blocks.find((b) => b.part === "Gaming"), cards = blocks.filter((b) => b.part !== "Gaming");
    preview.classList.toggle("cols", state.layout === "columns");
    const hero = game && (() => {
      const get = (id) => game.items.find((x) => x.id === id), fps = get("fps"), low = get("low1"), ft = get("frametime");
      const side = (it, tag) => it ? h("div", { class: "ov-side-v" }, h("b", { class: "num" }, split(current(it))[0]), h("small", {}, tag)) : null;
      return h("div", { class: "ov-hero" },
        h("div", { class: "ov-hero-top" },
          fps ? h("div", { class: "ov-fps" }, h("b", { class: "num" }, split(current(fps))[0]), h("small", {}, "FPS")) : h("span"),
          h("div", {}, side(low, "1% LOW"), side(ft, "MS"))),
        fps ? bars(history.get("fps") || []) : null);
    })();
    preview.replaceChildren(
      h("div", { class: "ov-brand" }, h("i"), "MAZESTA", frames?.app ? h("small", {}, frames.app) : null),
      hero || "",
      h("div", { class: "ov-cards" }, cards.map((b) =>
        h("div", { class: `ov-card ${part(b.part).cls}` },
          h("div", { class: "ov-title" }, SHORT[b.part], b.device ? h("small", {}, b.deviceName || "") : null),
          b.items.map((it) => {
            const c = current(it), [num, unit] = split(c);
            return h("div", { class: "ov-line" }, h("div", { class: "ov-lv" }, h("span", {}, it.label), h("b", { class: "num" }, num, unit ? h("small", {}, unit) : null)),
              it.chart ? spark(history.get(it.id) || [], hueOf(b.part))
                : it.max && c ? h("div", { class: "ov-meter" }, h("i", { style: { "--p": Math.min(1, Math.max(0, c.v / it.max)) } })) : null);
          })))));
    if (!blocks.length) preview.append(h("p", { class: "caption" }, t("Web_Overlay_Empty")));
  }
  // The frame rate's last minute as bars, the newest at full strength, scaled to the highest shown.
  function bars(values) {
    const n = 30, shown = values.slice(-n), max = Math.max(0, ...shown.filter((v) => v !== null));
    return h("div", { class: "ov-bars", "aria-hidden": "true" }, Array.from({ length: n }, (_, i) => {
      const v = shown[i - (n - shown.length)];
      return h("i", { class: i === n - 1 ? "now" : "", style: { "--p": v === null || v === undefined || !max ? 0 : Math.max(0.06, v / max) } });
    }));
  }
  function spark(values, color) {
    const c = h("canvas", { class: "ov-spark", width: 440, height: 48 });
    const g = c.getContext("2d"), fin = values.filter((v) => v !== null);
    if (fin.length > 1) {
      const max = Math.max(...fin) * 1.15 || 1, step = 440 / (TREND - 1), x0 = 440 - (values.length - 1) * step;
      g.strokeStyle = color; g.lineWidth = 3; g.lineJoin = "round"; g.beginPath();
      let open = false;
      values.forEach((v, i) => { if (v === null) { open = false; return; } const x = x0 + i * step, y = 46 - (v / max) * 44; open ? g.lineTo(x, y) : g.moveTo(x, y); open = true; });
      g.stroke();
    }
    return c;
  }

  function update(s) {
    const key = (x) => JSON.stringify(x.items.map((i) => [i.id, i.on, i.chart]));
    const rebuild = !state || key(s) !== key(state);
    const reorder = rebuild || JSON.stringify(s.order) !== JSON.stringify(state.order);
    state = s;
    show.checked = s.visible; hotkey.textContent = s.hotkey;
    if (!corner.options.length) corner.replaceChildren(...s.corners.map((c) => h("option", { value: c.value }, c.label)));
    corner.value = s.corner;
    opacity.value = Math.round(s.opacity * 100); fill(opacity); preview.style.setProperty("--ov-alpha", s.opacity);
    for (const b of sizes.children) b.setAttribute("aria-pressed", String(Math.abs(+b.dataset.v - s.scale) < 0.01));
    for (const b of layouts.children) b.setAttribute("aria-pressed", String(b.dataset.v === s.layout));
    preview.style.setProperty("--ov-scale", s.scale);
    problem.hidden = !s.frameProblem; problem.textContent = s.frameProblem ? t("Web_Overlay_FrameProblem", s.frameProblem) : "";
    renderPresets(s);
    if (rebuild) renderItems(s);
    if (reorder) renderOrder();
    tick();
  }

  call("overlay.state").then(update);
  const offs = [on("overlayState", update), on("overlayFrames", (f) => { frames = f; }), subscribe(tick)];
  return () => offs.forEach((off) => off());
}
