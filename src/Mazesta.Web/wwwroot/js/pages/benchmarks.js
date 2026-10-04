// Benchmarks: numbers only, no score and no verdict, in a folding panel per part (CPU apart from GPU apart from storage …). Ticked rows run one
// after another; a single row can run on its own. Each row shows the best result kept on this system, and after a run how the new one
// compares with it: only a better run replaces the record (the host keeps it, per system, in Data/benchmarks).
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { setField } from "./tests.js";
import { groupPanel, byPart } from "../groups.js";
import { findingCard, bySeverity } from "./checkup.js";

// The list, optionally only one part's benchmarks (the component pages reuse it).
export function benchList(component = null) {
  const wrap = h("div", {});
  const runSel = h("button", { class: "btn go", onclick: () => call("bench.exec", { cmd: "runSelected" }) }, icon("play"), t("Bench_RunSelected"));
  const cancel = h("button", { class: "btn stop", onclick: () => call("bench.exec", { cmd: "cancel" }) }, icon("stop"), t("Bench_Cancel"));
  const queue = h("span", { class: "pill run", hidden: true });
  const oc = h("input", { type: "checkbox", class: "check", onchange: (e) => call("bench.oc", { value: e.target.checked }) });
  const list = h("div", { class: "groups" });
  // This copy's logged runs go to the shop's site, which builds the comparison lists every copy reads.
  const upload = h("button", { class: "btn quiet", title: t("Site_Runs_Hint"), "data-a": "send-site", onclick: async () => {
    if (!hasKey) { toast(t("Site_Runs_NeedKey"), "fail"); location.hash = "#/appupdate"; return; }
    upload.disabled = true;
    try {
      const r = await call("site.runs");
      if (r.error) toast(r.error, "fail");
      else toast(r.nothing || (!r.added && !r.known && !r.rejected) ? t("Site_Runs_Nothing") : r.pending ? t("Site_Runs_Pending", fa(r.added)) : t("Site_Runs_Done", fa(r.added), fa(r.known), fa(r.rejected)), "ok");
    } catch (e) { toast(String(e.message || e), "fail"); }
    upload.disabled = false;
  } }, icon("update"), t("Site_Runs_Send"));
  // Anyone's own latest results, as a page on the site with a link to pass on (no key needed; the computer's name is not sent).
  const share = h("button", { class: "btn quiet", title: t("Site_Share_Hint"), "data-a": "share-site", onclick: async () => {
    if (!confirm(t("Site_Share_Confirm"))) return;
    share.disabled = true;
    try {
      const r = await call("site.share");
      if (r.error) toast(r.error, "fail");
      else {
        let copied = false;
        try { await navigator.clipboard.writeText(r.link); copied = true; } catch { /* the link is still shown and opened */ }
        toast(t(copied ? "Site_Share_Done_Copied" : "Site_Share_Done", fa(r.rows)), "ok");
        call("site.open", { url: r.link });
      }
    } catch (e) { toast(String(e.message || e), "fail"); }
    share.disabled = false;
  } }, icon("popout"), t("Site_Share_Send"));
  // Sending every logged run to the lists is the shop's (it needs the site's key): without one the button says so and opens where to connect.
  let hasKey = false;
  const keyed = (s) => { if (s) hasKey = !!s.hasKey; };
  call("site.state").then(keyed).catch(() => {});
  const offSite = on("site", keyed);
  wrap.append(list, h("div", { class: "dock" }, runSel, cancel, queue, h("span", { class: "grow" }), share, upload,
    h("label", { class: "oc-toggle", title: t("Bench_OverclockedHint") }, oc, t("Bench_Overclocked")),
    h("button", { class: "btn quiet", onclick: () => call("bench.exec", { cmd: "selectAll" }) }, t("Test_SelectAll")),
    h("button", { class: "btn quiet", onclick: () => call("bench.exec", { cmd: "clear" }) }, t("Test_ClearSelection"))));

  const rows = new Map(), groups = [];
  function build(s) {
    const mine = s.rows.filter((r) => !component || r.component === component), index = new Map(mine.map((r, i) => [r.id, i]));
    let gi = 0;
    for (const [kind, members] of byPart(mine, (r) => r.component)) {
      const g = groupPanel("bench", kind, gi++, (on) => { for (const r of members) call("bench.set", { id: r.id, field: "selected", value: on }); });
      groups.push({ g, ids: members.map((r) => r.id) }); list.append(g.el);
      for (const r of members) addRow(r, index.get(r.id), g.body);
    }
  }
  function addRow(r, i, into) {
    const set = (field, value, extra = {}) => call("bench.set", { id: r.id, field, value, ...extra });
    const check = h("input", { type: "checkbox", class: "check", "aria-label": r.name, onchange: (e) => set("selected", e.target.checked) });
    const dur = h("input", { class: "field lat short", inputmode: "numeric", oninput: (e) => set("duration", e.target.value) });
    const run = h("button", { class: "btn", onclick: () => call("bench.exec", { cmd: "run", id: r.id }) }, t("Bench_Run"));
    const opts = r.options.map((o) => {
      const input = o.choices
        ? h("select", { class: "field", onchange: (e) => set("option", e.target.value, { key: o.key }) }, o.choices.map((c) => h("option", { value: c.value }, c.label)))
        : h("input", { class: "field lat", style: { width: "110px" }, oninput: (e) => set("option", e.target.value, { key: o.key }) });
      return { o, input, el: h("label", {}, o.label, input) };
    });
    const bar = h("div", { class: "progress" }, h("i")), status = h("span", { class: "caption" }), metrics = h("div", { class: "metrics" }), detail = h("div", { class: "detail", hidden: true });
    const rec = h("div", { class: "rec" }), unavailable = h("div", { class: "unavailable", hidden: true }), peers = h("div", { class: "peers", hidden: true }), finds = h("div", { class: "row-checkup", hidden: true });
    const row = h("div", { class: "q-row", style: { "--i": i } },
      h("span", { class: "step" }, fa(String(i + 1).padStart(2, "0"))), check, h("span", { class: "name" }, r.name),
      h("div", { class: "ctrls" }, h("label", {}, t("Bench_Duration"), dur, t("Test_Seconds")), run),
      opts.length ? h("div", { class: "extra" }, opts.map((x) => x.el)) : null,
      h("div", { class: "state" }, bar, status), unavailable, metrics, finds, rec, peers, detail);
    into.append(row);
    rows.set(r.id, { row, check, dur, run, opts, bar, status, metrics, rec, peers, detail, unavailable, finds, last: "", lastRec: "", lastPeers: "", lastCheck: "" });
  }
  function update(s) {
    if (!rows.size) build(s);
    runSel.disabled = !s.canRunSelected; cancel.disabled = !s.running;
    queue.hidden = !s.queue; queue.textContent = s.queue || "";
    oc.checked = !!s.overclocked; oc.disabled = s.running;
    for (const r of s.rows) {
      const x = rows.get(r.id); if (!x) continue;
      x.check.checked = r.selected; x.check.disabled = !!r.unavailable; setField(x.dur, r.duration); x.run.disabled = s.running || !!r.unavailable;
      x.row.classList.toggle("off", !!r.unavailable); x.unavailable.hidden = !r.unavailable; x.unavailable.textContent = r.unavailable || "";
      for (const o of x.opts) { const cur = r.options.find((y) => y.key === o.o.key); if (cur) setField(o.input, cur.value); }
      x.bar.firstChild.style.setProperty("--p", r.percent / 100);
      x.status.textContent = r.status || "";
      x.row.classList.toggle("active", r.active);
      const key = JSON.stringify(r.metrics);
      if (key !== x.last) { x.last = key; x.metrics.replaceChildren(...r.metrics.map((m) => h("div", { class: "metric" }, h("div", { class: "v" }, m.value), h("div", { class: "n" }, m.name)))); }
      x.detail.hidden = !r.detail; x.detail.textContent = r.detail || "";
      const recKey = JSON.stringify([r.best, r.compared]);
      if (recKey !== x.lastRec) { x.lastRec = recKey; x.rec.replaceChildren(...record(r).filter(Boolean)); }
      // A run in progress keeps the last standing on screen (the host sends none while the row runs).
      const peerKey = JSON.stringify(r.peers);
      // What this run showed about the machine (the checkup), the ones that need action first; kept on screen while a new run is under way.
      const checkKey = JSON.stringify(r.checkup);
      if (r.checkup && checkKey !== x.lastCheck) { x.lastCheck = checkKey; x.finds.hidden = !r.checkup.length; x.finds.replaceChildren(h("div", { class: "peers-head" }, icon("check"), h("b", {}, t("Web_Bench_Checkup"))), ...bySeverity(r.checkup).map(findingCard)); }
      if (r.peers !== null && peerKey !== x.lastPeers) { x.lastPeers = peerKey; x.peers.hidden = false; x.peers.replaceChildren(...standing(r).filter(Boolean)); }
      else if (r.peers === null && !r.active) x.peers.hidden = true;
    }
    const byId = new Map(s.rows.map((r) => [r.id, r]));
    for (const { g, ids } of groups) {
      const rs = ids.map((id) => byId.get(id)).filter(Boolean);
      g.sync(rs.length, rs.filter((r) => r.selected).length, rs.some((r) => r.active));
    }
  }
  call("bench.state").then(update);
  const offBench = on("bench", update);
  return { el: wrap, off: () => { offBench(); offSite(); } };
}

// The record line: the best result kept on this system; after a run, the run against it. A better run is saved, a lower one is shown and
// dropped. The change is written as a signed percentage in the part's hue for a record, plain for a lower run (never pass/fail colours).
// Under it, folded, every number the kept result measured and the conditions it ran in.
function record(r) {
  const c = r.compared, b = r.best;
  const cell = (label, m, extra = null) => h("div", { class: "rec-cell" }, h("span", { class: "k" }, label), h("span", { class: "v num" }, m.value), h("span", { class: "d" }, m.name, extra ? " · " : "", extra ? h("span", { class: "lat" }, extra) : null));
  const numbers = (m) => {
    const groups = [["Web_Detail_Conditions", m?.metrics?.conditions], ["Web_Detail_Results", m?.metrics?.results]].filter(([, xs]) => xs && xs.length);
    if (!groups.length) return null;
    return h("details", { class: "rec-more" }, h("summary", {}, t("Web_Bench_AllNumbers")),
      h("div", { class: "spec-cols" }, groups.map(([k, xs]) => h("dl", { class: "spec" }, h("dt", { class: "spec-h" }, t(k)), xs.flatMap((x) => [h("dt", {}, x.name), h("dd", { class: "num" }, x.value)])))));
  };
  if (c) {
    const pct = c.change === null || c.change === undefined ? null : `${c.change > 0 ? "+" : c.change < 0 ? "−" : ""}${Math.abs(c.change).toFixed(1)}%`;
    const verdict = !c.previous ? "Web_Bench_FirstRecord" : c.saved ? "Web_Bench_NewRecord" : c.change === 0 ? "Web_Bench_Equal" : "Web_Bench_Lower";
    return [h("div", { class: `rec-grid ${c.saved ? "up" : "down"}` },
      cell(t("Web_Bench_ThisRun"), c.now), c.previous ? cell(t("Web_Bench_Best"), c.previous, c.previous.at) : null,
      h("div", { class: "rec-verdict" }, c.saved ? icon("trophy") : null, pct ? h("span", { class: "num pct" }, pct) : null, h("span", {}, t(verdict)))), numbers(c.saved ? c.now : c.previous || c.now)];
  }
  if (b) return [h("div", { class: "rec-grid" }, cell(t("Web_Bench_Best"), b, b.at)), numbers(b)];
  return [h("p", { class: "rec-none" }, t("Web_Bench_NoRecord"))];
}

// A phrase with a Latin number in it ("۴.۳× سریع‌تر" reads as "4.3× faster"): the number keeps its own left-to-right run.
function phrase(key, num) {
  const [a, b] = t(key, "\u0001").split("\u0001");
  return [a, h("span", { class: "lat" }, num), b];
}

// How far this system is from another, said from the faster side: "4.3× faster" on a faster row, "this system 25% faster" on a slower one
// (a percentage up to double, a multiple from there). This system's lead takes the part's hue; another's stays plain (never pass/fail colours).
function gapCell(g) {
  if (!g) return h("span", { class: "pd" });
  if (g.equal) return h("span", { class: "pd even", title: t("Web_Peers_GapHint") }, t("Web_Peers_Even"));
  return h("span", { class: `pd ${g.lead ? "behind" : "up"}`, title: t("Web_Peers_GapHint") }, phrase(g.lead ? "Web_Peers_TheyFaster" : "Web_Peers_YouFaster", g.text));
}
const ocTag = () => h("span", { class: "tag oc" }, t("Web_Peers_Oc"));

// Where this system stands among other systems: the comparison list is one entry per part model (the median of its systems' best runs), from the
// shop's published lists and this copy's own runs, and the shop's featured runs above it. The row shows the few entries around this result; the
// whole list, searchable and filtered, opens apart, since it can hold thousands of models. Any entry opens its details beside this system's.
function standing(r) {
  const p = r.peers;
  const head = h("div", { class: "peers-head" }, icon("chart"), h("b", {}, t("Web_Peers_Title")),
    p.total ? h("span", { class: "caption" }, t("Web_Peers_Count", fa(p.total))) : null, h("span", { class: "grow" }),
    p.total || p.featuredTotal ? h("button", { class: "btn quiet", onclick: () => allPeers(r) }, t("Web_Peers_All")) : null,
    h("button", { class: "btn quiet", onclick: () => history(r) }, t("Web_Peers_History")));
  if (!p.total && !p.featuredTotal) return [head, h("p", { class: "rec-none" }, t("Web_Peers_None"))];
  const out = [head];
  if (p.featuredTotal) {
    const fl = h("ol", { class: "peer-list featured" });
    for (const f of p.featured) addFeatured(fl, r, f);
    out.push(h("div", { class: "peers-sub" }, icon("star"), h("b", {}, t("Web_Peers_Featured")), h("span", { class: "caption" }, t("Web_Peers_FeaturedHint"))), fl);
    if (p.featuredTotal > p.featured.length) out.push(h("p", { class: "rec-none" }, t("Web_Peers_FeaturedMore", fa(p.featuredTotal - p.featured.length))));
  }
  if (!p.total) return out;
  if (p.mineIndex !== null) out.push(standingLine(p.beaten, p.total, p.around[p.mineIndex - 1 - p.from]));
  else out.push(h("p", { class: "rec-none" }, t("Web_Peers_RunFirst")));
  const list = h("ol", { class: "peer-list" }), rank = (idx) => idx + 1 + (p.mineIndex !== null && idx >= p.mineIndex ? 1 : 0);
  p.around.forEach((e, k) => { const idx = p.from + k; if (p.mineIndex === idx) list.append(youRow(p, idx + 1)); addPeer(list, r, e, rank(idx)); });
  if (p.mineIndex !== null && p.mineIndex >= p.from + p.around.length) list.append(youRow(p, p.mineIndex + 1));
  out.push(list);
  return out;
}

// Where this result stands, said plainly: ahead of some of the models (and what share of them), slower than all of them (with how far behind
// the nearest one it is, never a bare "0%"), or faster than all of them. `above` is the entry just ahead of this result, if any.
function standingLine(beaten, total, above) {
  if (beaten === 0) {
    const [a, b, c] = t("Web_Peers_Nearest", "\u0001", "\u0002").split(/[\u0001\u0002]/);
    return h("div", { class: "peers-sum behind" }, h("span", {}, t("Web_Peers_Last", fa(total))),
      above?.gap && !above.gap.equal ? h("span", { class: "caption" }, a, h("span", { class: "lat" }, above.part), b, h("span", { class: "lat" }, above.gap.text), c) : null);
  }
  if (beaten >= total) return h("div", { class: "peers-sum" }, h("span", { class: "num pct" }, "100%"), h("span", {}, t("Web_Peers_First", fa(total))));
  return h("div", { class: "peers-sum" }, h("span", { class: "num pct" }, `${Math.round(beaten / total * 100)}%`), h("span", {}, t("Web_Peers_Ahead", fa(beaten), fa(total))));
}

// A row that opens, under itself, its details beside this system's (fetched when opened: the lists carry none, to stay small).
function openable(li, list, ask) {
  let panel = null;
  li.classList.add("openable"); li.tabIndex = 0; li.setAttribute("role", "button"); li.setAttribute("aria-expanded", "false"); li.title = t("Web_Detail_Open");
  const toggle = async () => {
    if (panel) { panel.remove(); panel = null; li.setAttribute("aria-expanded", "false"); return; }
    panel = h("li", { class: "peer-detail" }, h("p", { class: "rec-none" }, t("Web_Detail_Loading")));
    li.after(panel); li.setAttribute("aria-expanded", "true");
    const d = await ask();
    if (panel) panel.replaceChildren(...[membersBox(d), compare(d?.mine, d?.theirs, true)].filter(Boolean));
  };
  li.addEventListener("click", toggle);
  li.addEventListener("keydown", (e) => { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); toggle(); } });
  list.append(li);
}

// The systems behind a model's row, one by one (each system's best run, best first): the row's number is their median.
function membersBox(d) {
  if (!d?.members?.length) return null;
  return h("details", { class: "cmp-group members", open: d.members.length > 1 || null }, h("summary", {}, t("Web_Peers_Members", fa(d.members.length), d.median || "")),
    h("ol", { class: "peer-list" }, d.members.map((m, i) => h("li", { class: "peer" }, h("span", { class: "rk num" }, i + 1), h("span", { class: "pn caption lat" }, m.at),
      h("span", { class: "pv num" }, m.value), h("span", { class: "ps" }), gapCell(m.gap)))));
}

function addPeer(list, r, e, rank) {
  const li = h("li", { class: `peer ${e.same ? "same" : ""}` }, h("span", { class: "rk num" }, rank),
    h("span", { class: "pn" }, h("span", { class: "lat" }, e.part), e.oc ? ocTag() : null, e.same ? h("span", { class: "tag" }, t("Web_Peers_Same")) : null, e.local ? h("span", { class: "tag" }, t("Web_Peers_Local")) : null),
    h("span", { class: "pv num", title: t("Web_Peers_Median") }, e.value), h("span", { class: "ps caption" }, t("Web_Peers_Systems", fa(e.systems), fa(e.runs))), gapCell(e.gap));
  openable(li, list, () => call("bench.detail", { id: r.id, part: e.part, oc: !!e.oc }));
}

function addFeatured(list, r, f) {
  const li = h("li", { class: "peer star" }, h("span", { class: "rk" }, icon("star")),
    h("span", { class: "pn" }, h("span", { class: "lat" }, f.part), f.oc ? ocTag() : null, f.local ? h("span", { class: "tag" }, t("Web_Peers_Local")) : null, f.note ? h("span", { class: "caption" }, f.note) : null),
    h("span", { class: "pv num" }, f.value), h("span", { class: "ps caption lat" }, f.at), gapCell(f.gap));
  openable(li, list, () => call("bench.detail", { id: r.id, run: f.id }));
}

function youRow(p, rank) {
  return h("li", { class: "peer you" }, h("span", { class: "rk num" }, rank),
    h("span", { class: "pn" }, h("b", {}, t("Web_Peers_You")), p.part ? h("span", { class: "sep" }, "·") : null, p.part ? h("span", { class: "lat" }, p.part) : null, p.oc ? ocTag() : null),
    h("span", { class: "pv num" }, p.mine || ""), h("span", { class: "ps" }), h("span", { class: "pd" }));
}

// One run in full, or two side by side (this system and another): the result, then the conditions measured during the run, the other numbers
// it measured, the part's specifications and the system's, each group folding so the list stays readable. A value one side lacks is a dash.
function compare(mine, theirs, two) {
  const sides = two ? [mine, theirs] : [mine];
  if (!sides.some(Boolean)) return h("p", { class: "rec-none" }, t("Web_Detail_None"));
  const rows = (pick) => {
    const names = [];
    for (const s of sides) for (const x of pick(s) || []) if (!names.includes(x.name)) names.push(x.name);
    return names.map((n) => [n, ...sides.map((s) => (pick(s) || []).find((x) => x.name === n)?.value)]);
  };
  const cell = (v, cls = "") => h("td", { class: `num ${cls}` }, v ?? "—");
  const group = (key, list, open = true) => {
    if (!list.length) return null;
    return h("details", { class: "cmp-group", open: open || null }, h("summary", {}, t(key)),
      h("table", { class: "cmp" }, h("tbody", {}, list.map(([n, ...vs]) => h("tr", {}, h("th", {}, n), vs.map((v) => cell(v)))))));
  };
  const head = h("table", { class: "cmp cmp-head" }, h("thead", {}, h("tr", {}, h("th", {}), sides.map((s, i) => h("th", {}, two ? t(i ? "Web_Detail_That" : "Web_Detail_This") : t("Web_Detail_Result"))))),
    h("tbody", {}, h("tr", { class: "big" }, h("th", {}, t("Web_Detail_Result")), sides.map((s) => cell(s?.value, "big"))),
      h("tr", {}, h("th", {}, t("Web_Detail_Date")), sides.map((s) => h("td", { class: "lat" }, s?.at ?? "—"))),
      sides.some((s) => s?.oc) ? h("tr", {}, h("th", {}, t("Web_Peers_Oc")), sides.map((s) => h("td", {}, s ? (s.oc ? "✓" : "—") : "—"))) : null));
  const noDetail = sides.every((s) => !s || (!s.part?.length && !s.system?.length && !s.run?.length && !s.metrics?.conditions?.length));
  return h("div", { class: "cmp-wrap" }, head,
    group("Web_Detail_Run", rows((s) => s?.run)),
    group("Web_Detail_Conditions", rows((s) => s?.metrics?.conditions)),
    group("Web_Detail_Part", rows((s) => s?.part)),
    group("Web_Detail_Results", rows((s) => s?.metrics?.results), false),
    group("Web_Detail_System", rows((s) => s?.system), false),
    noDetail ? h("p", { class: "rec-none" }, t("Web_Detail_None")) : null);
}

// A native modal dialog: Esc and the close button end it, and it leaves nothing behind.
function sheet(title, sub, body) {
  const d = h("dialog", { class: "sheet" },
    h("header", { class: "sheet-head" }, h("div", { class: "ttl" }, h("h2", { class: "panel-title" }, title), sub ? h("div", { class: "caption" }, sub) : null),
      h("button", { class: "icon-btn", type: "button", "aria-label": t("Web_Peers_Close"), title: t("Web_Peers_Close"), onclick: () => d.close() }, icon("x"))),
    h("div", { class: "sheet-body" }, body));
  d.addEventListener("close", () => d.remove());
  document.body.append(d); d.showModal();
  return d;
}

// The whole list: searchable, filtered (every model, the featured runs only, or no overclocked entries), a hundred at a time.
async function allPeers(r) {
  const d = await call("bench.peers", { id: r.id });
  if (!d) return;
  const items = [];
  d.rows.forEach((e, idx) => { if (d.mineIndex === idx) items.push({ you: true }); items.push({ e }); });
  if (d.mineIndex !== null && d.mineIndex >= d.rows.length) items.push({ you: true });
  items.forEach((it, k) => { it.rank = k + 1; });
  const PAGE = 100;
  let shown = PAGE, filter = d.featured.length && !d.rows.length ? "featured" : "all";
  const search = h("input", { class: "field", type: "search", placeholder: t("Web_Peers_Search"), "aria-label": t("Web_Peers_Search") });
  const chips = h("div", { class: "chips", role: "group" }, [["all", "Web_Peers_FilterAll"], ["featured", "Web_Peers_FilterFeatured"], ["stock", "Web_Peers_FilterStock"]]
    .map(([k, key]) => h("button", { class: "chip", type: "button", "data-k": k, onclick: () => { filter = k; shown = PAGE; paint(); } }, t(key), k === "featured" ? h("span", { class: "lat" }, ` ${d.featured.length}`) : null)));
  const found = h("span", { class: "caption" });
  const list = h("ol", { class: "peer-list long" }), more = h("button", { class: "btn quiet", type: "button" });
  function paint() {
    for (const c of chips.children) c.classList.toggle("on", c.dataset.k === filter);
    const q = search.value.trim().toLowerCase(), match = (s) => !q || s.toLowerCase().includes(q);
    list.replaceChildren();
    if (filter === "featured") {
      const hits = d.featured.filter((f) => match(f.part) || (q && (f.note || "").toLowerCase().includes(q)));
      for (const f of hits.slice(0, shown)) addFeatured(list, r, f);
      found.textContent = t("Web_Peers_Found", fa(hits.length));
      more.hidden = hits.length <= shown; more.textContent = t("Web_Peers_More", fa(Math.min(PAGE, hits.length - shown)));
      return;
    }
    const hits = items.filter((it) => it.you || (match(it.e.part) && (filter !== "stock" || !it.e.oc)));
    for (const it of hits.slice(0, shown)) { if (it.you) list.append(youRow(d, it.rank)); else addPeer(list, r, it.e, it.rank); }
    found.textContent = t("Web_Peers_Found", fa(hits.filter((it) => !it.you).length));
    more.hidden = hits.length <= shown; more.textContent = t("Web_Peers_More", fa(Math.min(PAGE, hits.length - shown)));
  }
  more.onclick = () => { shown += PAGE; paint(); };
  search.oninput = () => { shown = PAGE; paint(); };
  const sub = [d.metric, d.built ? t("Web_Peers_Built", d.built) : ""].filter(Boolean).join(" · ");
  sheet(d.name, sub, [h("p", { class: "note" }, t("Web_Peers_Note")),
    d.mineIndex !== null ? standingLine(d.beaten, d.rows.length, d.rows[d.mineIndex - 1]) : null,
    h("div", { class: "sheet-tools" }, search, chips, found), list, more]);
  paint();
  list.querySelector(".you")?.scrollIntoView({ block: "center" });
}

// This copy's own recorded runs of a benchmark, on every machine it has been used on: the "results it has recorded", and what the shop gathers.
// Each opens to its full record; the star makes it a featured result (published with the next lists), the box marks it overclocked.
async function history(r) {
  const runs = await call("bench.history", { id: r.id });
  const cols = ["Web_Runs_Date", "Web_Runs_Machine", "Web_Runs_Part", "Web_Runs_Value", "Web_Runs_Oc", "Web_Runs_Featured"];
  const mark = (x, patch) => call("bench.mark", { id: r.id, run: x.id, ...patch });
  const body = [];
  if (runs && runs.length) {
    const tbody = h("tbody", {});
    for (const x of runs) {
      const star = h("button", { class: `icon-btn star-btn ${x.featured ? "on" : ""}`, type: "button", "aria-pressed": String(!!x.featured), title: t("Web_Runs_MarkHint"), "aria-label": t("Web_Runs_Featured"),
        onclick: async (e) => { e.stopPropagation(); x.featured = !x.featured; star.classList.toggle("on", x.featured); star.setAttribute("aria-pressed", String(x.featured)); await mark(x, { featured: x.featured }); } }, icon("star"));
      const oc = h("input", { type: "checkbox", class: "check", "aria-label": t("Web_Runs_Oc"), onclick: (e) => e.stopPropagation(), onchange: async (e) => { x.detail.oc = e.target.checked; await mark(x, { oc: e.target.checked }); } });
      oc.checked = !!x.detail?.oc;
      const tr = h("tr", { class: "openable", tabIndex: 0, title: t("Web_Detail_Open") },
        h("td", { class: "lat" }, x.at), h("td", { class: "lat" }, x.machine), h("td", { class: "lat" }, x.part), h("td", { class: "num" }, x.value), h("td", {}, oc), h("td", {}, star));
      let open = null;
      const toggle = () => {
        if (open) { open.remove(); open = null; return; }
        const note = h("input", { class: "field", value: x.note || "", maxlength: "120", placeholder: t("Web_Runs_NoteLabel"), "aria-label": t("Web_Runs_NoteLabel"),
          onchange: (e) => { x.note = e.target.value; mark(x, { note: e.target.value }); } });
        open = h("tr", { class: "run-detail" }, h("td", { colspan: String(cols.length) }, h("label", { class: "note-field" }, t("Web_Runs_NoteLabel"), note), compare(x.detail, null, false)));
        tr.after(open);
      };
      tr.addEventListener("click", toggle);
      tr.addEventListener("keydown", (e) => { if (e.target === tr && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); toggle(); } });
      tbody.append(tr);
    }
    body.push(h("p", { class: "note" }, t("Web_Runs_MarkHint")), h("div", { class: "runs-wrap" }, h("table", { class: "runs" }, h("thead", {}, h("tr", {}, cols.map((c) => h("th", {}, t(c))))), tbody)));
  } else body.push(h("p", { class: "rec-none" }, t("Web_Runs_Empty")));
  sheet(r.name, t("Web_Runs_Note"), [...body, h("div", { class: "btn-row" }, h("button", { class: "btn", type: "button", onclick: () => call("bench.exec", { cmd: "openRuns" }) }, icon("folder"), t("Web_Runs_Folder")))]);
}

export function mount(el) {
  const list = benchList();
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Benchmarks")), h("p", { class: "page-lede" }, t("Bench_Note")))), list.el);
  return list.off;
}
