// The overlay's own page: a ready-made set to start from (game, render, troubleshooting), every item it can show grouped by part in the part's
// hue (on or off, chart or not), its look, and a live preview drawn from the same snapshots the overlay reads. Items this machine has no sensor
// for say so and cannot be turned on; the frame rate is measured only while the overlay is on screen.
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

export function mount(el) {
  let state = null, frames = null;
  const history = new Map();   // item id -> recent values, for the preview's charts

  const show = h("input", { type: "checkbox", class: "switch", "aria-label": t("Web_Overlay_Show"), onchange: (e) => call("overlay.set", { field: "visible", value: e.target.checked }) });
  const corner = h("select", { class: "field", "aria-label": t("Overlay_Corner"), onchange: (e) => call("overlay.set", { field: "corner", value: e.target.value }) });
  const opacity = h("input", { type: "range", class: "range", min: "50", max: "100", step: "5", "aria-label": t("Web_Overlay_Opacity"),
    oninput: (e) => { fill(e.target); preview.style.setProperty("--ov-alpha", e.target.value / 100); }, onchange: (e) => call("overlay.set", { field: "opacity", value: e.target.value / 100 }) });
  const sizes = h("div", { class: "seg", role: "group", "aria-label": t("Web_Overlay_Size") }, [["0.85", "Web_Overlay_Small"], ["1", "Web_Overlay_Normal"], ["1.2", "Web_Overlay_Large"]].map(([v, k]) =>
    h("button", { type: "button", "data-v": v, onclick: () => call("overlay.set", { field: "scale", value: +v }) }, t(k))));
  const hotkey = h("span", { class: "kbd lat" });
  const problem = h("p", { class: "banner", hidden: true });

  const presets = h("div", { class: "presets" });
  const groups = h("div", { class: "ov-groups" });
  const preview = h("div", { class: "ov-preview", "aria-label": t("Web_Overlay_Preview") });

  el.append(
    h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Overlay")), h("p", { class: "page-lede" }, t("Web_Overlay_Lede")))),
    h("section", { class: "ov-bar" },
      h("label", { class: "ov-show" }, show, h("span", {}, h("b", {}, t("Web_Overlay_Show")), h("small", {}, t("Web_Overlay_Hotkey"), " ", hotkey))),
      h("label", { class: "ov-ctl" }, h("span", {}, t("Overlay_Corner")), corner),
      h("div", { class: "ov-ctl" }, h("span", {}, t("Web_Overlay_Size")), sizes),
      h("label", { class: "ov-ctl grow" }, h("span", {}, t("Web_Overlay_Opacity")), opacity)),
    problem,
    h("h2", { class: "section-title" }, t("Web_Overlay_Presets")), presets,
    h("div", { class: "ov-split" },
      h("div", {}, h("h2", { class: "section-title" }, t("Web_Overlay_Items")), groups),
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

  // ——— Items: a panel per part; each row is on/off, its live value, and its chart toggle ———
  const rowEls = new Map();
  function renderItems(s) {
    groups.replaceChildren(); rowEls.clear();
    PARTS.forEach((kind, i) => {
      const items = s.items.filter((x) => x.part === kind);
      if (!items.length) return;
      const p = part(kind);
      const list = h("div", { class: "ov-list" }, items.map((it) => {
        const val = h("span", { class: "ov-val num" });
        const onBox = h("input", { type: "checkbox", class: "switch", checked: it.on, disabled: !it.available, "aria-label": it.label,
          onchange: (e) => call("overlay.item", { id: it.id, on: e.target.checked, chart: it.chart }) });
        const chartBtn = h("button", { class: "icon-btn", type: "button", disabled: !it.available || !it.on, "aria-pressed": String(it.chart), title: t("Web_Overlay_ChartToggle"), "aria-label": `${t("Web_Overlay_ChartToggle")}: ${it.label}`,
          onclick: () => call("overlay.item", { id: it.id, on: true, chart: !it.chart }) }, icon("chart"));
        const row = h("div", { class: `ov-row ${it.available ? "" : "na"} ${it.on ? "on" : ""}` },
          h("label", { class: "ov-name" }, onBox, h("span", {}, it.label, it.aggregate !== "First" && !it.frame ? h("small", {}, t(`Web_Overlay_Agg_${it.aggregate}`)) : null)),
          it.available ? val : h("span", { class: "ov-na" }, t("Web_Overlay_NotHere")), chartBtn);
        rowEls.set(it.id, { it, val });
        return row;
      }));
      groups.append(h("section", { class: `panel group ${p.cls}`, style: { "--i": i } },
        h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon(p.icon)), h("div", { class: "ttl" }, h("h3", { class: "panel-title" }, t(p.key))),
          h("span", { class: "group-count" }, t("Web_Group_Count", items.length, items.filter((x) => x.on).length))),
        list));
    });
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
      if (it.on && it.chart) { const q = history.get(it.id) || []; q.push(c ? c.v : null); if (q.length > TREND) q.shift(); history.set(it.id, q); }
    }
    renderPreview();
  }

  // ——— The preview: the overlay as it will look, from the same items, in the same order and colours ———
  function renderPreview() {
    const chosen = state.order.map((id) => state.items.find((x) => x.id === id)).filter((x) => x && x.available);
    const blocks = PARTS.map((kind) => [kind, chosen.filter((x) => x.part === kind)]).filter(([, xs]) => xs.length);
    preview.replaceChildren(h("div", { class: "ov-brand" }, h("i"), "MAZESTA"), ...blocks.map(([kind, xs]) =>
      h("div", { class: `ov-block ${part(kind).cls}` },
        h("div", { class: "ov-title" }, h("i"), SHORT[kind], kind === "Gaming" && frames?.app ? h("small", {}, frames.app) : null),
        xs.map((it) => {
          const c = current(it);
          return h("div", { class: "ov-line" }, h("div", { class: "ov-lv" }, h("span", {}, it.label), h("b", { class: "num" }, c ? c.text : "—")),
            it.chart ? spark(history.get(it.id) || [], hueOf(kind)) : null);
        }))));
    if (!blocks.length) preview.append(h("p", { class: "caption" }, t("Web_Overlay_Empty")));
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
    const rebuild = !state || JSON.stringify(s.items.map((x) => [x.id, x.on, x.chart])) !== JSON.stringify(state.items.map((x) => [x.id, x.on, x.chart]));
    state = s;
    show.checked = s.visible; hotkey.textContent = s.hotkey;
    if (!corner.options.length) corner.replaceChildren(...s.corners.map((c) => h("option", { value: c.value }, c.label)));
    corner.value = s.corner;
    opacity.value = Math.round(s.opacity * 100); fill(opacity); preview.style.setProperty("--ov-alpha", s.opacity);
    for (const b of sizes.children) b.setAttribute("aria-pressed", String(Math.abs(+b.dataset.v - s.scale) < 0.01));
    preview.style.setProperty("--ov-scale", s.scale);
    problem.hidden = !s.frameProblem; problem.textContent = s.frameProblem ? t("Web_Overlay_FrameProblem", s.frameProblem) : "";
    renderPresets(s);
    if (rebuild) renderItems(s);
    tick();
  }

  call("overlay.state").then(update);
  const offs = [on("overlayState", update), on("overlayFrames", (f) => { frames = f; }), subscribe(tick)];
  return () => offs.forEach((off) => off());
}
