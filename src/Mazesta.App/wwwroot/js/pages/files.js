// The file tools of the Windows tools page: what holds a file open, where the disk space went, pictures made smaller, many files renamed, a file's checksum, a colour from the screen. Files come only from the host's own dialogs (files.pick); the host
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

// ——— Many files renamed: a preview of every new name first; the batch runs only when nothing clashes, and the last one can be undone ———
export function renameBox(i) {
  let paths = [];
  const picked = h("p", { class: "fl-picked" }), msg = h("p", { class: "msg", "aria-live": "polite" }), list = h("ul", { class: "fl-list fl-ren" });
  const field = (key, ph) => h("input", { class: "field", "aria-label": t(key), placeholder: ph || "", oninput: preview });
  const find = field("Files_Ren_Find"), replace = field("Files_Ren_Replace"), start = h("input", { class: "field lat", inputmode: "numeric", value: "1", "aria-label": t("Files_Ren_Start"), oninput: preview });
  const check = (key) => h("input", { type: "checkbox", class: "switch", "aria-label": t(key), onchange: preview });
  const regex = check("Files_Ren_Regex"), matchCase = check("Files_Ren_Case");
  const mode = h("div", { class: "choice", role: "radiogroup", "aria-label": t("Files_Ren_Title") },
    ["replace", "number"].map((v) => h("label", {}, h("input", { type: "radio", name: "ren-mode", value: v, checked: v === "replace", onchange: () => { sync(); preview(); } }), h("b", {}, t(`Files_Ren_Mode_${v}`)))));
  const findRow = h("label", {}, t("Files_Ren_Find"), find), startRow = h("label", { hidden: true }, t("Files_Ren_Start"), start);
  const replaceLabel = h("span", {}, t("Files_Ren_Replace")), flags = h("div", { class: "fl-opts" }, h("label", { class: "fl-check" }, regex, t("Files_Ren_Regex")), h("label", { class: "fl-check" }, matchCase, t("Files_Ren_Case")));
  const run = h("button", { class: "btn primary", disabled: true, onclick: apply }, t("Files_Ren_Run"));
  const undo = h("button", { class: "btn quiet", hidden: true, onclick: undoLast }, icon("refresh"), t("Files_Ren_Undo"));
  const number = () => mode.querySelector("input:checked")?.value === "number";
  function sync() { const n = number(); findRow.hidden = n; flags.hidden = n; startRow.hidden = !n; replaceLabel.textContent = t(n ? "Files_Ren_Base" : "Files_Ren_Replace"); }
  const rule = () => ({ paths, find: find.value, replace: replace.value, regex: regex.checked, matchCase: matchCase.checked, number: number(), start: start.value.trim() });
  const use = (p) => { paths = p; picked.replaceChildren(shown(p)); preview(); };
  let seq = 0;
  async function preview() {
    if (!paths.length) return;
    const my = ++seq, r = await call("rename.preview", rule());
    if (my !== seq) return;   // a newer keystroke's preview is on its way
    if (r.error) { msg.className = "msg fail"; msg.textContent = t("Files_Ren_BadRegex", r.error); list.replaceChildren(); run.disabled = true; return; }
    const STATE = { Same: "", Ready: "ok", Clash: "fail", Bad: "fail" };
    list.replaceChildren(...r.items.slice(0, 300).map((x) => h("li", { class: STATE[x.state] }, h("div", {}, lat(x.name), x.state === "Same" ? null : h("small", {}, "→ ", lat(x.newName))),
      h("span", { class: "caption" }, x.state === "Same" ? t("Files_Ren_Same") : x.state === "Ready" ? "" : t(`Files_Ren_${x.state}`)))));
    run.disabled = !r.ready || r.blocked > 0;
    msg.className = `msg ${r.blocked ? "fail" : ""}`;
    msg.textContent = r.blocked ? t("Files_Ren_Blocked_N", r.blocked) : t("Files_Ren_Ready", r.ready, r.items.length);
  }
  async function apply() {
    run.disabled = true;
    try {
      const r = await call("rename.apply", rule());
      msg.className = `msg ${r.failed.length ? "fail" : "ok"}`;
      msg.textContent = t("Files_Ren_Done", r.renamed) + (r.failed.length ? " " + t("Files_Ren_Failed", r.failed.map((f) => f.name).join("، ")) : "");
      list.replaceChildren(); paths = []; picked.replaceChildren(); last();
    } catch (e) { msg.className = "msg fail"; msg.textContent = String(e.message || e); run.disabled = false; }
  }
  async function undoLast() {
    const r = await call("rename.undo");
    msg.className = `msg ${r.failed.length ? "fail" : "ok"}`;
    msg.textContent = t("Files_Ren_Undone", r.renamed) + (r.failed.length ? " " + t("Files_Ren_Failed", r.failed.map((f) => f.name).join("، ")) : "");
    last();
  }
  const last = () => call("rename.last").then((x) => { undo.hidden = !x.count; undo.textContent = ""; undo.append(icon("refresh"), t("Files_Ren_Undo_N", x.count)); });
  last();
  return box({ ico: "doc", cls: "p-tool", title: t("Files_Ren_Title"), sub: t("Files_Ren_Sub"), wide: true, i, a: "rename",
    body: [h("div", { class: "btn-row", style: { marginTop: 0 } }, pickBtn("files", "Files_PickFile", use), pickBtn("folder", "Files_PickFolder", use), h("span", { class: "grow" }), undo), picked, mode,
      h("div", { class: "fl-opts" }, findRow, h("label", {}, replaceLabel, replace), startRow), flags, h("div", { class: "btn-row" }, run), msg, list],
    more: h("p", { class: "note" }, t("Files_Ren_Note")) });
}

// ——— A file's checksum, to compare with the one on its download page ———
export function hashBox(i, onEvent) {
  let sums = null;
  const msg = h("p", { class: "msg", "aria-live": "polite" }), out = h("dl", { class: "kv fl-hash" });
  const paste = h("input", { class: "field lat", "aria-label": t("Files_Hash_Paste"), placeholder: t("Files_Hash_Paste"), oninput: compare });
  const verdict = h("p", { class: "msg" });
  const cancel = h("button", { class: "btn stop", hidden: true, onclick: () => call("hash.cancel") }, icon("stop"), t("Test_Cancel"));
  const copy = (v) => h("button", { class: "btn quiet", onclick: async () => { try { await navigator.clipboard.writeText(v); toast(t("Files_Copied")); } catch { /* the value is still shown */ } } }, t("Files_Copy"));
  async function go(p) {
    sums = null; out.replaceChildren(); verdict.textContent = ""; cancel.hidden = false; msg.className = "msg"; msg.textContent = t("Tools_Working");
    try {
      const r = await call("hash.file", { paths: p });
      if (r.cancelled) { msg.textContent = t("Files_Space_Cancelled"); return; }
      sums = r; msg.className = "msg"; msg.replaceChildren(lat(r.name), " · ", lat(bytes(r.size)));
      out.replaceChildren(...[["MD5", r.md5], ["SHA-1", r.sha1], ["SHA-256", r.sha256]].flatMap(([k, v]) => [h("dt", {}, lat(k)), h("dd", {}, lat(v), " ", copy(v))]));
      compare();
    } catch (e) { msg.className = "msg fail"; msg.textContent = String(e.message || e); }
    finally { cancel.hidden = true; }
  }
  async function compare() {
    const text = paste.value.trim();
    if (!text) { verdict.textContent = ""; return; }
    const r = await call("hash.compare", { text });
    if (!r.kind) { verdict.className = "msg fail"; verdict.textContent = t("Files_Hash_NotHash"); return; }
    if (!sums) { verdict.className = "msg"; verdict.textContent = t("Files_Hash_PickFirst", r.kind); return; }
    const same = [sums.md5, sums.sha1, sums.sha256].includes(r.clean);
    verdict.className = `msg ${same ? "ok" : "fail"}`;
    verdict.textContent = same ? t("Files_Hash_Match", r.kind) : t("Files_Hash_NoMatch", r.kind);
  }
  onEvent("hash", (x) => { msg.textContent = t("Files_Hash_Reading", Math.round(x.share * 100)); });
  return box({ ico: "check", cls: "p-tool", title: t("Files_Hash_Title"), sub: t("Files_Hash_Sub"), i, a: "checksum",
    body: [h("div", { class: "btn-row", style: { marginTop: 0 } }, pickBtn("one", "Files_PickFile", go), h("span", { class: "grow" }), cancel), msg, out, paste, verdict] });
}

// ——— A colour from anywhere on the screen; the last ones are kept in this browser profile (a convenience, nothing depends on it) ———
const RECENT = "mazesta.colors.recent";
const recentRead = () => { try { return JSON.parse(localStorage.getItem(RECENT)) || []; } catch { return []; } };
export function colorBox(i, onEvent) {
  let recent = recentRead();
  const now = h("div", { class: "fl-color" }), list = h("div", { class: "fl-swatches" }), msg = h("p", { class: "msg", "aria-live": "polite" }), keyNote = h("p", { class: "note" });
  const copy = async (v) => { try { await navigator.clipboard.writeText(v); toast(t("Files_Copied")); } catch { /* the value is still shown */ } };
  const code = (v) => h("button", { class: "btn quiet lat", title: t("Files_Copy"), onclick: () => copy(v) }, v);
  function show(c) {
    now.replaceChildren(h("span", { class: "fl-swatch big", style: { background: c.hex } }), h("div", { class: "fl-codes" }, code(c.hex), code(c.rgb), code(c.hsl)));
    list.replaceChildren(...recent.map((x) => h("button", { class: "fl-swatch", style: { background: x.hex }, title: x.hex, "aria-label": x.hex, onclick: () => show(x) })));
  }
  function add(c) {
    recent = [c, ...recent.filter((x) => x.hex !== c.hex)].slice(0, 16);
    try { localStorage.setItem(RECENT, JSON.stringify(recent)); } catch { /* kept for this visit only */ }
    show(c); msg.className = "msg ok"; msg.textContent = t("Files_Color_Copied", c.hex);
  }
  const pick = h("button", { class: "btn primary", onclick: async () => { pick.disabled = true; try { const c = await call("color.pick"); if (c) add(c); } finally { pick.disabled = false; } } }, icon("drop"), t("Files_Color_Pick"));
  onEvent("colorpicked", add);
  call("color.key").then((k) => { keyNote.textContent = t(k.held ? "Files_Color_Key" : "Files_Color_KeyTaken"); });
  if (recent.length) show(recent[0]);
  return box({ ico: "drop", cls: "p-tool", title: t("Files_Color_Title"), sub: t("Files_Color_Sub"), i, a: "color",
    body: [h("div", { class: "btn-row", style: { marginTop: 0 } }, pick), msg, now, list, keyNote] });
}

// ——— The quick file search: it lives in the tray (its index and window), so this only opens it there ———
export function searchBox(i) {
  const msg = h("p", { class: "msg", "aria-live": "polite" }), how = h("p", { class: "note" }, t("Files_Search_How"));
  const label = h("span", {}, t("Files_Search_Open"));
  const open = h("button", { class: "btn primary", onclick: async () => {
    const r = await call("search.open");
    msg.className = r.open ? "msg" : "msg fail";
    msg.textContent = r.open ? "" : t(r.on ? "Files_Search_NoTray" : "Files_Search_Off");
  } }, icon("search"), label);
  call("search.state").then((s) => {
    label.textContent = t("Files_Search_Open_Key", s.key);
    if (!s.on) { msg.className = "msg fail"; msg.textContent = t("Files_Search_Off"); } else if (!s.tray) { msg.className = "msg fail"; msg.textContent = t("Files_Search_NoTray"); }
  });
  return box({ ico: "search", cls: "p-tool", title: t("Files_Search_Title"), sub: t("Files_Search_Sub"), i, a: "search",
    body: [h("div", { class: "btn-row", style: { marginTop: 0 } }, open), msg, how] });
}

// ——— Each program's volume: a panel of the tray, opened from here or from the tray icon's menu ———
export function mixerBox(i) {
  const msg = h("p", { class: "msg", "aria-live": "polite" });
  const open = h("button", { class: "btn primary", onclick: async () => {
    const r = await call("mixer.open");
    msg.className = r.open ? "msg" : "msg fail"; msg.textContent = r.open ? "" : t("Files_Search_NoTray");
  } }, icon("volume"), t("Files_Mixer_Open"));
  return box({ ico: "volume", cls: "p-tool", title: t("Files_Mixer_Title"), sub: t("Files_Mixer_Sub"), i, a: "mixer",
    body: [h("div", { class: "btn-row", style: { marginTop: 0 } }, open), msg, h("p", { class: "note" }, t("Files_Mixer_How"))] });
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
