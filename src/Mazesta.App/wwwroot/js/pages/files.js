// The file tools of the Windows tools page: what holds a file open, where the disk space went, pictures made smaller. Files come only from the host's own dialogs (files.pick); the host
// acts only on what was picked there. Every answer is what Windows reported; "could not check" is never shown as "nothing found".
import { call } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { box } from "../groups.js";

const lat = (s) => h("span", { class: "lat" }, s);
const pickBtn = (kind, key, then) => h("button", { class: "btn", onclick: async () => { const p = await call("files.pick", { kind }); if (p?.length) then(p); } }, icon(kind === "folder" ? "folder" : "doc"), t(key));
const shown = (paths) => paths.length === 1 ? lat(paths[0]) : t("Files_Picked_Many", paths.length);

// ——— What holds a file open (Restart Manager) ———
export function lockBox(i) {
  let paths = [];
  const picked = h("p", { class: "fl-picked" }), msg = h("p", { class: "msg", "aria-live": "polite" }), list = h("ul", { class: "fl-list" });
  const again = h("button", { class: "btn quiet", hidden: true, onclick: () => find() }, icon("refresh"), t("Files_Recheck"));
  const use = (p) => { paths = p; picked.replaceChildren(shown(p)); find(); };
  async function find() {
    list.replaceChildren(); again.hidden = false; msg.className = "msg"; msg.textContent = t("Tools_Working");
    try {
      const r = await call("locks.find", { paths });
      if (r.error) { msg.className = "msg fail"; msg.textContent = t("Files_Lock_Failed", r.error); return; }
      msg.className = `msg ${r.holders.length ? "" : "ok"}`;
      msg.textContent = (r.holders.length ? t("Files_Lock_Found", r.holders.length) : t("Files_Lock_None")) + (r.truncated ? " " + t("Files_Lock_Truncated", r.cap) : "");
      list.replaceChildren(...r.holders.map(row));
    } catch (e) { msg.className = "msg fail"; msg.textContent = String(e.message || e); }
  }
  const row = (x) => h("li", {},
    h("div", {}, h("b", {}, x.name), " ", h("span", { class: "caption" }, t(`Files_Lock_Kind_${x.kind}`), " · ", lat(`PID ${x.pid}`)), x.path ? h("small", {}, lat(x.path)) : null),
    x.canClose ? h("button", { class: "btn stop", onclick: () => close(x) }, t("Files_Lock_Close")) : h("span", { class: "caption" }, t("Files_Lock_Protected")));
  async function close(x) {
    if (!confirm(t("Files_Lock_CloseAsk", x.name))) return;
    const r = await call("locks.close", { paths, pid: x.pid, start: x.start });
    if (r.error) toast(t("Files_Lock_CloseFailed", x.name, r.error), "fail"); else toast(t("Files_Lock_Closed", x.name));
    find();
  }
  return box({ ico: "lock", cls: "p-tool", title: t("Files_Lock_Title"), sub: t("Files_Lock_Sub"), i, a: "locks",
    body: [h("div", { class: "btn-row", style: { marginTop: 0 } }, pickBtn("files", "Files_PickFile", use), pickBtn("folder", "Files_PickFolder", use), h("span", { class: "grow" }), again), picked, msg, list] });
}

// ——— Pictures made smaller: the longest side down to a size, a new file beside each original ———
export function imageBox(i, onEvent) {
  let paths = [];
  const picked = h("p", { class: "fl-picked" }), msg = h("p", { class: "msg", "aria-live": "polite" }), list = h("ul", { class: "fl-list" });
  const size = h("select", { class: "field lat", "aria-label": t("Files_Img_Size") },
    [800, 1280, 1920, 2560].map((n) => h("option", { value: n, selected: n === 1280 }, `${n} px`)), h("option", { value: "custom" }, t("Files_Img_Custom")));
  const custom = h("input", { class: "field lat", inputmode: "numeric", hidden: true, "aria-label": t("Files_Img_Custom"), placeholder: "1600" });
  const quality = h("select", { class: "field", "aria-label": t("Files_Img_Quality") },
    [[95, "Files_Img_Q_High"], [85, "Files_Img_Q_Normal"], [70, "Files_Img_Q_Small"]].map(([v, k]) => h("option", { value: v, selected: v === 85 }, t(k))));
  size.onchange = () => { custom.hidden = size.value !== "custom"; };
  const run = h("button", { class: "btn primary", disabled: true, onclick: go }, icon("play"), t("Files_Img_Run"));
  const use = (p) => { paths = p; picked.replaceChildren(shown(p)); run.disabled = false; list.replaceChildren(); msg.textContent = ""; };
  async function go() {
    run.disabled = true; list.replaceChildren(); msg.className = "msg"; msg.textContent = t("Tools_Working");
    try {
      const r = await call("img.resize", { paths, size: size.value === "custom" ? custom.value.trim() : size.value, quality: quality.value });
      msg.className = `msg ${r.ok === r.total ? "ok" : "fail"}`; msg.textContent = t("Files_Img_Done", r.ok, r.total);
    } catch (e) { msg.className = "msg fail"; msg.textContent = String(e.message || e); }
    finally { run.disabled = false; }
  }
  onEvent("imgdone", (x) => {
    msg.textContent = t("Files_Img_Progress", x.index + 1, x.total);
    list.append(h("li", {}, h("div", {}, h("b", {}, lat(x.name)), x.error
      ? h("small", { class: "fail" }, x.error === "format" ? t("Files_Img_Format") : x.error)
      : h("small", {}, lat(`${x.width}×${x.height} → ${x.newWidth}×${x.newHeight} · ${bytes(x.oldBytes)} → ${bytes(x.newBytes)}`), " ", lat(x.output))), h("span")));
  });
  return box({ ico: "camera", cls: "p-tool", title: t("Files_Img_Title"), sub: t("Files_Img_Sub"), i, a: "images",
    body: [h("div", { class: "btn-row", style: { marginTop: 0 } }, pickBtn("images", "Files_PickImages", use)), picked,
      h("div", { class: "fl-opts" }, h("label", {}, t("Files_Img_Size"), size, custom), h("label", {}, t("Files_Img_Quality"), quality)),
      h("div", { class: "btn-row" }, run), msg, list],
    more: h("p", { class: "note" }, t("Files_Img_Note")) });
}

// Bytes as people read them (1 KB = 1024 bytes, as Explorer shows), Latin digits in a .lat run.
export function bytes(n) {
  const u = ["B", "KB", "MB", "GB", "TB"]; let i = 0, v = n;
  while (v >= 1024 && i < u.length - 1) { v /= 1024; i++; }
  return `${v >= 100 || i === 0 ? Math.round(v) : v.toFixed(1)} ${u[i]}`;
}

// ——— Where the disk space went: scan a drive or a folder, then walk into the biggest folders ———
export function spaceBox(i, onEvent) {
  let at = [];
  const drives = h("div", { class: "btn-row", style: { marginTop: 0 } }), msg = h("p", { class: "msg", "aria-live": "polite" });
  const crumb = h("div", { class: "fl-crumb" }), list = h("ul", { class: "fl-list fl-space" }), top = h("ul", { class: "fl-list" });
  const cancel = h("button", { class: "btn stop", hidden: true, onclick: () => call("space.cancel") }, icon("stop"), t("Test_Cancel"));
  const start = async (r) => { at = []; list.replaceChildren(); top.replaceChildren(); crumb.replaceChildren(); try { await call("space.scan", { root: r }); cancel.hidden = false; msg.className = "msg"; msg.textContent = t("Tools_Working"); } catch (e) { msg.className = "msg fail"; msg.textContent = String(e.message || e); } };
  call("space.drives").then((ds) => drives.replaceChildren(...ds.map((d) => h("button", { class: "btn", onclick: () => start(d.root) }, icon("drive"), lat(d.root), " ",
    h("span", { class: "caption" }, t("Files_Space_Free", bytes(d.free), bytes(d.total))))), pickBtn("folder", "Files_PickFolder", (p) => start(p[0]))));
  const sizeText = (x) => (x.unread ? t("Files_Space_Unread") : (x.partUnread ? t("Files_Space_AtLeast") + " " : "") + bytes(x.size));
  async function show() {
    const r = await call("space.list", { at });
    if (!r) return;
    crumb.replaceChildren(...(at.length ? [h("button", { class: "btn quiet", onclick: () => { at = at.slice(0, -1); show(); } }, icon("arrow"), t("Files_Space_Up"))] : []), lat(r.path), " ",
      h("span", { class: "caption" }, sizeText(r), " · ", t("Files_Space_Files", r.files)), h("button", { class: "btn quiet", onclick: () => call("space.open", { at }) }, icon("popout"), t("Files_Space_Open")));
    const max = Math.max(1, ...r.children.map((c) => c.size), r.here);
    const bar = (v) => h("i", { class: "fl-bar", style: { "--w": v / max } });
    list.replaceChildren(...r.children.map((c) => h("li", { class: c.files ? "go" : "", onclick: c.files ? () => { at = [...at, c.name]; show(); } : null },
      h("div", {}, h("b", {}, lat(c.name)), " ", h("span", { class: "caption" }, sizeText(c)), bar(c.size)), c.files ? icon("chevronend") : h("span"))),
      ...(r.others ? [h("li", {}, h("div", {}, h("span", { class: "caption" }, t("Files_Space_Others"), " ", lat(bytes(r.others))), bar(r.others)), h("span"))] : []),
      ...(r.here ? [h("li", {}, h("div", {}, h("span", { class: "caption" }, t("Files_Space_Here"), " ", lat(bytes(r.here))), bar(r.here)), h("span"))] : []));
    if (r.top) top.replaceChildren(...r.top.map((f) => h("li", {}, h("div", {}, lat(bytes(f.size)), h("small", {}, lat(f.path))),
      h("button", { class: "btn quiet", onclick: () => call("space.open", { file: f.path }) }, icon("popout"), t("Files_Space_Show")))));
  }
  onEvent("space", (s) => {
    cancel.hidden = !s.busy;
    if (s.busy) { msg.className = "msg"; msg.textContent = t("Files_Space_Scanning", s.files, bytes(s.bytes)); }
    else if (s.done) { msg.className = "msg ok"; msg.textContent = t("Files_Space_Done"); show(); }
    else if (s.cancelled) { msg.className = "msg"; msg.textContent = t("Files_Space_Cancelled"); }
    else if (s.error) { msg.className = "msg fail"; msg.textContent = t("Files_Space_Failed", s.error); }
  });
  show();   // a scan finished earlier in this session is shown again
  return box({ ico: "drive", cls: "p-tool", title: t("Files_Space_Title"), sub: t("Files_Space_Sub"), wide: true, i, a: "space",
    body: [drives, h("div", { class: "btn-row" }, msg, h("span", { class: "grow" }), cancel), crumb, list],
    more: [h("h3", { class: "fl-h" }, t("Files_Space_Biggest")), top, h("p", { class: "note" }, t("Files_Space_Note"))] });
}
