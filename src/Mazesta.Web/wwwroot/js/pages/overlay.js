// The overlay's own page: a ready-made set to start from (game, render, troubleshooting), every item it can show grouped by part in the part's
// hue (on or off, chart or not; each drive's own read, write, temperature and activity too), the order it is drawn in (dragged, or moved with
// the arrows), its look (one column or two, corner, size, opacity), and a live preview drawn from the same snapshots the overlay reads. Items
// this machine has no sensor for say so and cannot be turned on; the frame rate is measured only while the overlay is on screen.
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { fmt } from "../format.js";
import { hw, value, subscribe } from "../store.js";
import { h, icon, toast } from "../ui.js";
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
  const layouts = seg(t("Web_Overlay_Layout"), "layout", [["list", "Web_Overlay_Layout_List"], ["columns", "Web_Overlay_Layout_Columns"], ["line", "Web_Overlay_Layout_Line"]]);
  const hotkey = h("span", { class: "kbd lat" });
  // Where the ping, loss and jitter are measured to: an address or a name (a game server's, for the figure that matters in that game).
  const pingTarget = h("input", { class: "field lat", style: { width: "150px" }, "aria-label": t("Web_Overlay_PingTarget"), title: t("Web_Overlay_PingTarget_Hint"),
    onchange: (e) => call("overlay.set", { field: "pingTarget", value: e.target.value }).catch((x) => { toast(String(x.message || x)); call("overlay.state").then(update); }) });
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
      h("label", { class: "ov-ctl" }, h("span", {}, t("Web_Overlay_PingTarget")), pingTarget),
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
      const v = frames && { fps: frames.fps, low1: frames.low1, frametime: frames.frametime, "fps.avg": frames.avg, "fps.min": frames.min, "fps.max": frames.max,
        "net.ping": frames.ping, "net.loss": frames.loss, "net.jitter": frames.jitter }[it.id];
      if (v === null || v === undefined) return null;
      // The link's figures come from the echoes the overlay sends while it is on screen: ping and jitter in ms, loss in percent.
      if (it.id.startsWith("net.")) return { v, text: it.id === "net.loss" ? `${Math.round(v)} %` : `${it.id === "net.jitter" ? v.toFixed(1) : Math.round(v)} ms` };
      return { v, text: it.id === "frametime" ? `${v.toFixed(1)} ms` : `${Math.round(v)} FPS` };
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

  // ——— The preview: the overlay as it will look, from the same blocks, in the same order, colours and layout. Stacked: the frame-rate box on
  // top (the rate big, 1 % low and frame time beside it, the session's average, lowest and highest under it, its trace), then a box per part
  // in the part's colour under its bold title, each reading's number with its unit small, a scale for a share of a fixed top or its trace.
  // As a line: the same boxes in one strip, each reading a label over its number ———
  function split(c) { if (!c) return ["—", ""]; const i = c.text.lastIndexOf(" "); return i > 0 ? [c.text.slice(0, i), c.text.slice(i + 1)] : [c.text, ""]; }
  function renderPreview() {
    const blocks = shownBlocks(), game = blocks.find((b) => b.part === "Gaming"), cards = blocks.filter((b) => b.part !== "Gaming");
    const line = state.layout === "line";
    preview.classList.toggle("cols", state.layout === "columns"); preview.classList.toggle("line", line);
    const get = (id) => game?.items.find((x) => x.id === id);
    const stat = (id, tag) => { const it = get(id); return it ? h("div", { class: "ov-stat" }, h("small", {}, tag), h("b", { class: "num" }, split(current(it))[0])) : null; };
    const title = (b) => h("div", { class: "ov-title" }, h("span", { class: "ov-key" }, SHORT[b.part]), b.device && !line ? h("small", {}, b.deviceName || "") : null);
    if (line) {
      const fps = get("fps");
      preview.replaceChildren(h("div", { class: "ov-strip" },
        game ? h("div", { class: "ov-card p-game" },
          fps ? h("div", { class: "ov-fps" }, h("b", { class: "num" }, split(current(fps))[0]), h("small", {}, "FPS")) : null,
          stat("low1", "1% LOW"), stat("fps.avg", "AVG"), stat("fps.min", "MIN"), stat("fps.max", "MAX"), stat("frametime", "MS")) : null,
        cards.map((b) => h("div", { class: `ov-card ${part(b.part).cls}` }, title(b), b.items.map((it) => {
          const [num, unit] = split(current(it));
          return h("div", { class: "ov-stat" }, h("small", {}, it.label), h("b", { class: "num" }, num, unit ? h("i", {}, unit) : null));
        })))));
      if (!blocks.length) preview.append(h("p", { class: "caption" }, t("Web_Overlay_Empty")));
      return;
    }
    const hero = game && (() => {
      const fps = get("fps"), low = get("low1"), ft = get("frametime");
      const side = (it, tag) => it ? h("div", { class: "ov-side-v" }, h("b", { class: "num" }, split(current(it))[0]), h("small", {}, tag)) : null;
      const stats = [stat("fps.avg", "AVG"), stat("fps.min", "MIN"), stat("fps.max", "MAX")].filter(Boolean);
      return h("div", { class: "ov-card ov-hero p-game" },
        h("div", { class: "ov-title" }, h("span", { class: "ov-key" }, "GAME")),
        h("div", { class: "ov-hero-top" },
          fps ? h("div", { class: "ov-fps" }, h("b", { class: "num" }, split(current(fps))[0]), h("small", {}, "FPS")) : h("span"),
          h("div", { class: "ov-hero-side" }, side(low, "1% LOW"), side(ft, "MS"))),
        stats.length ? h("div", { class: "ov-stats" }, stats) : null,
        fps ? frameChart(history.get("fps") || [], low ? current(low)?.v ?? null : null) : null);
    })();
    preview.replaceChildren(
      h("div", { class: "ov-brand" }, "MAZESTA", frames?.app ? h("small", {}, frames.app) : null),
      hero || "",
      h("div", { class: "ov-cards" }, cards.map((b) =>
        h("div", { class: `ov-card ${part(b.part).cls}` }, title(b),
          b.items.map((it) => {
            const c = current(it), [num, unit] = split(c);
            return h("div", { class: "ov-line" }, h("div", { class: "ov-lv" }, h("span", {}, it.label), h("b", { class: "num" }, num, unit ? h("small", {}, unit) : null)),
              it.chart ? spark(history.get(it.id) || [], hueOf(b.part))
                : it.max && c ? h("div", { class: "ov-meter" }, h("i", { style: { "--p": Math.min(1, Math.max(0, c.v / it.max)) } })) : null);
          })))));
    if (!blocks.length) preview.append(h("p", { class: "caption" }, t("Web_Overlay_Empty")));
  }
  // The frame rate's last minute as a trace on a scope screen: dotted divisions, the area under the line faintly filled, the 1 % low as a
  // dashed level, the newest point marked. Scaled from zero to a little above the highest shown, so a dip reads as a dip and not as a cliff.
  // Drawn the same way as the overlay's FrameChart.
  function frameChart(values, low) {
    const W = 520, H = 144, c = h("canvas", { class: "ov-frames", width: W, height: H, "aria-hidden": "true" }), g = c.getContext("2d");
    const yellow = getComputedStyle(document.documentElement).getPropertyValue("--yellow").trim() || "#fdd400";
    g.strokeStyle = "rgba(255,255,255,0.13)"; g.lineWidth = 2; g.setLineDash([2, 6]);
    for (let i = 1; i < 6; i++) { const x = Math.round((W * i) / 6); g.beginPath(); g.moveTo(x, 0); g.lineTo(x, H); g.stroke(); }
    for (let i = 1; i < 3; i++) { const y = Math.round((H * i) / 3); g.beginPath(); g.moveTo(0, y); g.lineTo(W, y); g.stroke(); }
    g.setLineDash([]);
    const fin = values.filter((v) => v !== null && v !== undefined);
    if (fin.length < 2) return c;
    const top = Math.max(...fin, low ?? 0) * 1.15 || 1, step = W / (TREND - 1), x0 = W - (values.length - 1) * step, Y = (v) => H - 4 - (v / top) * (H - 8);
    const runs = []; let run = [];
    values.forEach((v, i) => { if (v === null || v === undefined) { if (run.length) runs.push(run); run = []; } else run.push([x0 + i * step, Y(v)]); });
    if (run.length) runs.push(run);
    const grad = g.createLinearGradient(0, 0, 0, H); grad.addColorStop(0, "rgba(253,212,0,0.28)"); grad.addColorStop(1, "rgba(253,212,0,0)");
    for (const r of runs.filter((x) => x.length > 1)) {
      g.beginPath(); r.forEach(([x, y], i) => (i ? g.lineTo(x, y) : g.moveTo(x, y))); g.lineTo(r[r.length - 1][0], H); g.lineTo(r[0][0], H); g.closePath(); g.fillStyle = grad; g.fill();
      g.beginPath(); r.forEach(([x, y], i) => (i ? g.lineTo(x, y) : g.moveTo(x, y))); g.strokeStyle = yellow; g.lineWidth = 3.5; g.lineJoin = "round"; g.stroke();
    }
    if (low !== null && low !== undefined) { const y = Math.round(Y(low)); g.strokeStyle = "rgba(255,255,255,0.55)"; g.lineWidth = 2; g.setLineDash([8, 6]); g.beginPath(); g.moveTo(0, y); g.lineTo(W, y); g.stroke(); g.setLineDash([]); }
    const last = values[values.length - 1];
    if (last !== null && last !== undefined) { g.fillStyle = "#fff"; g.beginPath(); g.arc(W - 5, Y(last), 5, 0, Math.PI * 2); g.fill(); }
    return c;
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
    if (document.activeElement !== pingTarget) pingTarget.value = s.pingTarget || "";
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
