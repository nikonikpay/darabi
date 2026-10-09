// Benchmarks: numbers only, no score and no verdict, in a folding panel per part (CPU apart from GPU apart from storage …). Ticked rows run one
// after another; a single row can run on its own. Each row shows the best result kept on this system, and after a run how the new one
// compares with it: only a better run replaces the record (the host keeps it, per system, in Data/benchmarks).
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { setField, applies } from "./tests.js";
import { groupPanel, byPart } from "../groups.js";
import { findingCard, bySeverity } from "./checkup.js";
import { boot } from "../app.js";
import { durationField } from "../duration.js";

// The list, optionally only one part's benchmarks (the component pages reuse it).
export function benchList(component = null) {
  const wrap = h("div", {});
  const runSel = h("button", { class: "btn go", onclick: () => call("bench.exec", { cmd: "runSelected" }) }, icon("play"), t("Bench_RunSelected"));
  const cancel = h("button", { class: "btn stop", onclick: () => call("bench.exec", { cmd: "cancel" }) }, icon("stop"), t("Bench_Cancel"));
  const queue = h("span", { class: "pill run", hidden: true });
  const oc = h("input", { type: "checkbox", class: "check", onchange: (e) => call("bench.oc", { value: e.target.checked }) });
  const list = h("div", { class: "groups" });
  // This copy's logged runs go to Mazesta's site, which builds the comparison lists every copy reads (Mazesta's edition only).
  const upload = !boot.staff ? null : h("button", { class: "btn quiet", title: t("Site_Runs_Hint"), "data-a": "send-site", onclick: async () => {
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
  // Sending every logged run to the lists needs the site's key: without one the button says so and opens where to connect.
  let hasKey = false;
  const keyed = (s) => { if (s) hasKey = !!s.hasKey; };
  if (boot.staff) call("site.state").then(keyed).catch(() => {});
  const offSite = on("site", keyed);
  wrap.append(list, h("div", { class: "dock" }, runSel, cancel, queue, h("span", { class: "grow" }), share, upload,
    h("label", { class: "oc-toggle", title: t("Bench_OverclockedHint") }, oc, t("Bench_Overclocked")),
    h("button", { class: "btn quiet", onclick: () => call("bench.exec", { cmd: "selectAll" }) }, t("Test_SelectAll")),
    h("button", { class: "btn quiet", onclick: () => call("bench.exec", { cmd: "clear" }) }, t("Test_ClearSelection"))));

  const rows = new Map(), groups = [];
  function build(s) {
    // The parts in the order a machine is judged by: processor, graphics, memory, storage, and the network (which measures the line) last.
    const ORDER = ["Cpu", "Gpu", "Memory", "Storage", "Network"], place = (k) => (ORDER.indexOf(k) + 1) || ORDER.length + 1;
    const parts = [...byPart(s.rows.filter((r) => !component || r.component === component), (r) => r.component)].sort((a, b) => place(a[0]) - place(b[0]));
    const index = new Map(parts.flatMap(([, members]) => members).map((r, i) => [r.id, i]));
    let gi = 0;
    for (const [kind, members] of parts) {
      const g = groupPanel("bench", kind, gi++, (on) => { for (const r of members) call("bench.set", { id: r.id, field: "selected", value: on }); });
      groups.push({ g, ids: members.map((r) => r.id) }); list.append(g.el);
      for (const r of members) addRow(r, index.get(r.id), g.body);
    }
  }
  function addRow(r, i, into) {
    const set = (field, value, extra = {}) => call("bench.set", { id: r.id, field, value, ...extra });
    const check = h("input", { type: "checkbox", class: "check", "aria-label": r.name, onchange: (e) => set("selected", e.target.checked) });
    const dur = durationField((v) => set("duration", v));
    const run = h("button", { class: "btn", onclick: () => call("bench.exec", { cmd: "run", id: r.id }) }, t("Bench_Run"));
    const opts = r.options.map((o) => {
      // a choice between off and on is a switch
      const input = o.choices && o.choices.map((c) => c.value).join() === "off,on"
        ? h("input", { type: "checkbox", class: "switch", onchange: (e) => set("option", e.target.checked ? "on" : "off", { key: o.key }) })
        : o.choices
        ? h("select", { class: "field", onchange: (e) => set("option", e.target.value, { key: o.key }) }, o.choices.map((c) => h("option", { value: c.value }, c.label)))
        : h("input", { class: "field lat", style: { width: "110px" }, oninput: (e) => set("option", e.target.value, { key: o.key }) });
      return { o, input, el: h("label", {}, o.label, input) };
    });
    const bar = h("div", { class: "progress" }, h("i")), status = h("span", { class: "caption" }), metrics = h("div", { class: "metrics" }), detail = h("details", { class: "rec-more run-more", hidden: true });
    const tags = h("span", { class: "row-tags" });
    const rec = h("div", { class: "rec" }), unavailable = h("div", { class: "unavailable", hidden: true }), peers = h("div", { class: "peers", hidden: true }), finds = h("details", { class: "row-checkup rec-more", hidden: true });
    const row = h("div", { class: "q-row", style: { "--i": i } },
      h("span", { class: "step" }, fa(String(i + 1).padStart(2, "0"))), check, h("span", { class: "name" }, r.name, tags),
      // Not a <label>: a label hands every click and hover inside it to its first control, which here would be the hours' up arrow.
      h("div", { class: "ctrls" }, h("div", { class: "lbl" }, t("Bench_Duration"), dur.el), run),
      opts.length ? h("div", { class: "extra" }, opts.map((x) => x.el)) : null,
      h("div", { class: "state" }, bar, status), unavailable, metrics, detail, rec, finds, peers);
    into.append(row);
    rows.set(r.id, { row, check, dur, run, opts, bar, status, metrics, rec, peers, detail, unavailable, finds, tags, lastTags: "", last: "", lastRec: "", lastPeers: "", lastCheck: "" });
  }
  function update(s) {
    if (!rows.size) build(s);
    runSel.disabled = !s.canRunSelected; cancel.disabled = !s.running;
    queue.hidden = !s.queue; queue.textContent = s.queue || "";
    oc.checked = !!s.overclocked; oc.disabled = s.running;
    for (const r of s.rows) {
      const x = rows.get(r.id); if (!x) continue;
      x.check.checked = r.selected; x.check.disabled = !!r.unavailable; x.dur.set(r.duration); x.run.disabled = s.running || !!r.unavailable;
      x.row.classList.toggle("off", !!r.unavailable); x.unavailable.hidden = !r.unavailable; x.unavailable.textContent = r.unavailable || "";
      for (const o of x.opts) { const cur = r.options.find((y) => y.key === o.o.key); if (cur) setField(o.input, cur.value); }
      for (const o of x.opts) o.el.hidden = !applies(o.o.when, r.options);
      x.bar.firstChild.style.setProperty("--p", r.percent / 100);
      x.status.textContent = r.status || "";
      x.row.classList.toggle("active", r.active);
      // The switches that are on (ray tracing) mark the row: its numbers, record and standing are that mode's.
      const tagKey = (r.tags || []).join("|");
      if (tagKey !== x.lastTags) { x.lastTags = tagKey; x.tags.replaceChildren(...(r.tags || []).map((g) => h("span", { class: "tag mode" }, g))); }
      // The run's results stand in the row; the conditions it ran in, how it was set up and the benchmark's own account fold away under them.
      const n = r.numbers, key = JSON.stringify([n, r.tags]);
      if (key !== x.last) {
        x.last = key;
        x.metrics.replaceChildren(...n.metrics.map((m) => h("div", { class: "metric" }, h("div", { class: "v" }, m.value), h("div", { class: "n" }, m.name))));
        x.detail.hidden = !n.more.length && !n.detail;
        x.detail.replaceChildren(h("summary", {}, t("Web_Bench_RunDetails")),
          n.more.length ? itemGrid(n.more) : null,
          n.detail ? h("p", { class: "detail" }, n.detail) : null);
      }
      const recKey = JSON.stringify([r.best, r.compared]);
      if (recKey !== x.lastRec) { x.lastRec = recKey; x.rec.replaceChildren(...record(r).filter(Boolean), r.best || r.compared ? h("button", { class: "btn quiet", type: "button", onclick: () => lastRun(r) }, icon("chart"), t("Bench_Chart")) : null); }
      // A run in progress keeps the last standing on screen (the host sends none while the row runs).
      const peerKey = JSON.stringify(r.peers);
      // What this run showed about the machine (the checkup), the ones that need action first; kept on screen while a new run is under way.
      const checkKey = JSON.stringify(r.checkup);
      if (r.checkup && checkKey !== x.lastCheck) { x.lastCheck = checkKey; x.finds.hidden = !r.checkup.length; x.finds.replaceChildren(h("summary", {}, t("Web_Bench_Checkup"), " ", h("span", { class: "lat" }, `(${r.checkup.length})`)), ...bySeverity(r.checkup).map(findingCard));
        x.finds.open = r.checkup.some((f) => f.level === "Problem" || f.level === "Attention"); }   // folded while every finding says all is well
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

// One part's figures (graphics card, processor, RAM) as a folding group with a heading, closed until opened.
function sectionBox(sec) {
  return h("details", { class: "cmp-group" }, h("summary", {}, t(sec.key)), itemGrid(sec.items));
}

// Figures in columns, each value right beside its name (a long list stays short, and a value is never a screen away from what it is).
function itemGrid(items) {
  return h("div", { class: "sec-grid" }, items.map((x) => h("div", { class: "sec-item" }, h("span", { class: "k" }, x.name), h("b", { class: "v num" }, x.value))));
}

// Which of two runs is the faster at a figure and by how much: the larger of the two over the smaller, less one, as a percentage - on the
// quicker side (a lower time is the quicker). Only for a result that has a direction (a condition of the run has none) and two real values.
export function gain(a, b) {
  if (!a || !b || !(a.raw > 0) || !(b.raw > 0) || a.hb === null || a.hb === undefined || a.raw === b.raw) return null;
  const aWins = a.hb ? a.raw > b.raw : a.raw < b.raw;
  return { win: aWins ? 0 : 1, pct: (Math.max(a.raw, b.raw) / Math.min(a.raw, b.raw) - 1) * 100 };
}
const gainTag = (g) => h("span", { class: "gain", title: t("Web_Detail_GainHint") }, `+${g.pct >= 100 ? g.pct.toFixed(0) : g.pct.toFixed(1)}%`);

// The record line: the best result kept on this system; after a run, the run against it. A better run is saved, a lower one is shown and
// dropped. The change is written as a signed percentage in the part's hue for a record, plain for a lower run (never pass/fail colours).
// Under it, folded, every number the kept result measured and the conditions it ran in.
function record(r) {
  const c = r.compared, b = r.best;
  const cell = (label, m, extra = null) => h("div", { class: "rec-cell" }, h("span", { class: "k" }, label), h("span", { class: "v num" }, m.value), h("span", { class: "d" }, m.name, extra ? " · " : "", extra ? h("span", { class: "lat" }, extra) : null));
  const numbers = (m) => {
    const groups = [["Web_Detail_Conditions", m?.metrics?.conditions], ["Web_Detail_Results", m?.metrics?.results]].filter(([, xs]) => xs && xs.length);
    const sections = m?.metrics?.sections || [];
    if (!groups.length && !sections.length) return null;
    return h("details", { class: "rec-more" }, h("summary", {}, t("Web_Bench_AllNumbers")),
      h("div", { class: "spec-cols" }, groups.map(([k, xs]) => h("div", { class: "sec-block" }, h("div", { class: "spec-h" }, t(k)), itemGrid(xs)))),
      ...sections.map(sectionBox));
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

// Where this system stands among other systems: the comparison list is one entry per part model, from Mazesta's published lists and this
// copy's own runs. An entry's number is the median of the reference runs Mazesta verified for that model where there are any (the row says
// so), the median of its systems' best runs otherwise. The row shows the few entries around this result; the whole list, searchable and
// filtered, opens apart, since it can hold thousands of models. Any entry opens its details beside this system's.
function standing(r) {
  const p = r.peers;
  const head = h("div", { class: "peers-head" }, icon("chart"), h("b", {}, t("Web_Peers_Title")),
    p.total ? h("span", { class: "caption" }, t("Web_Peers_Count", fa(p.total))) : null, h("span", { class: "grow" }),
    p.total ? h("button", { class: "btn quiet", onclick: () => allPeers(r) }, t("Web_Peers_All")) : null,
    h("button", { class: "btn quiet", onclick: () => history(r) }, t("Web_Peers_History")));
  if (!p.total) return [head, h("p", { class: "rec-none" }, t("Web_Peers_None"))];
  const out = [head];
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
  return h("details", { class: "cmp-group members", open: d.members.length > 1 || null },
    h("summary", {}, d.references ? t("Web_Peers_Members_Ref", fa(d.members.length), d.median || "", fa(d.references)) : t("Web_Peers_Members", fa(d.members.length), d.median || "")),
    h("ol", { class: "peer-list" }, d.members.map((m, i) => h("li", { class: "peer" }, h("span", { class: "rk num" }, i + 1), h("span", { class: "pn caption lat" }, m.at),
      h("span", { class: "pv num" }, m.value), h("span", { class: "ps" }), gapCell(m.gap)))));
}

function addPeer(list, r, e, rank) {
  const li = h("li", { class: `peer ${e.same ? "same" : ""}` }, h("span", { class: "rk num" }, rank),
    h("span", { class: "pn" }, h("span", { class: "lat" }, e.part), e.oc ? ocTag() : null, e.references ? h("span", { class: "tag ref", title: t("Web_Peers_ReferenceMedian", fa(e.references)) }, icon("star"), t("Web_Peers_Reference")) : null,
      e.same ? h("span", { class: "tag" }, t("Web_Peers_Same")) : null, e.local ? h("span", { class: "tag" }, t("Web_Peers_Local")) : null),
    h("span", { class: "pv num", title: e.references ? t("Web_Peers_ReferenceMedian", fa(e.references)) : t("Web_Peers_Median") }, e.value), h("span", { class: "ps caption" }, t("Web_Peers_Systems", fa(e.systems), fa(e.runs))), gapCell(e.gap));
  openable(li, list, () => call("bench.detail", { id: r.id, part: e.part, oc: !!e.oc }));
}

function youRow(p, rank) {
  return h("li", { class: "peer you" }, h("span", { class: "rk num" }, rank),
    h("span", { class: "pn" }, h("b", {}, t("Web_Peers_You")), p.part ? h("span", { class: "sep" }, "·") : null, p.part ? h("span", { class: "lat" }, p.part) : null, p.oc ? ocTag() : null),
    h("span", { class: "pv num" }, p.mine || ""), h("span", { class: "ps" }), h("span", { class: "pd" }));
}

// One run in full, or two side by side (this system and another): the result, then the conditions measured during the run, the other numbers
// it measured, the part's specifications and the system's, each group folding so the list stays readable. A value one side lacks is a dash.
function compare(mine, theirs, two, more = []) {
  const sides = two ? [mine, theirs, ...more] : [mine], pair = sides.length === 2;
  if (!sides.some(Boolean)) return h("p", { class: "rec-none" }, t("Web_Detail_None"));
  // Each row is a figure's name and, per side, its value (with the number behind it where the page can compare it); two sides get the quicker one marked.
  const rows = (pick) => {
    const names = [];
    for (const s of sides) for (const x of pick(s) || []) if (!names.includes(x.name)) names.push(x.name);
    return names.map((n) => [n, ...sides.map((s) => (pick(s) || []).find((x) => x.name === n))]);
  };
  const cell = (x, win = false, g = null) => h("td", { class: `num ${win ? "win" : ""}` }, x?.value ?? "—", win && g ? gainTag(g) : null);
  const group = (key, list, open = false) => {
    if (!list.length) return null;
    return h("details", { class: `cmp-group ${key.startsWith("Web_Detail_") ? "p-" + key.slice(11).toLowerCase() : ""}`, open: open || null }, h("summary", {}, t(key)),
      h("table", { class: "cmp" }, h("tbody", {}, list.map(([n, ...xs]) => {
        const g = pair ? gain(xs[0], xs[1]) : null;
        return h("tr", {}, h("th", {}, n), xs.map((x, i) => cell(x, g?.win === i, g)));
      }))));
  };
  const head = h("table", { class: "cmp cmp-head" }, h("thead", {}, h("tr", {}, h("th", {}), sides.map((s, i) => h("th", {}, s?.label ?? (two ? t(i ? "Web_Detail_That" : "Web_Detail_This") : t("Web_Detail_Result")))))),
    h("tbody", {}, (() => {
      const g = pair ? gain(mine && { raw: mine.raw, hb: mine.hb }, theirs && { raw: theirs.raw, hb: theirs.hb }) : null;
      return h("tr", { class: "big" }, h("th", {}, t("Web_Detail_Result")), sides.map((s, i) => h("td", { class: `num big ${g?.win === i ? "win" : ""}` }, s?.value ?? "—", g?.win === i ? gainTag(g) : null)));
    })(),
      h("tr", {}, h("th", {}, t("Web_Detail_Date")), sides.map((s) => h("td", { class: "lat" }, s?.at ?? "—"))),
      sides.some((s) => s?.oc) ? h("tr", {}, h("th", {}, t("Web_Peers_Oc")), sides.map((s) => h("td", {}, s ? (s.oc ? "✓" : "—") : "—"))) : null));
  // The graphics card's, the processor's and the RAM's own figures, each under a heading of its own, closed until opened.
  const sectionKeys = [];
  for (const s of sides) for (const sec of s?.metrics?.sections || []) if (!sectionKeys.includes(sec.key)) sectionKeys.push(sec.key);
  const noDetail = sides.every((s) => !s || (!s.part?.length && !s.system?.length && !s.run?.length && !s.metrics?.conditions?.length && !s.metrics?.sections?.length));
  return h("div", { class: "cmp-wrap" }, head,
    group("Web_Detail_Results", rows((s) => s?.metrics?.results), true),
    group("Web_Detail_Run", rows((s) => s?.run), true),
    ...sectionKeys.map((k) => group(k, rows((s) => s?.metrics?.sections?.find((x) => x.key === k)?.items))),
    group("Web_Detail_Conditions", rows((s) => s?.metrics?.conditions)),
    group("Web_Detail_Part", rows((s) => s?.part)),
    group("Web_Detail_System", rows((s) => s?.system)),
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

// The whole list: searchable, filtered (every model, the models with a reference result only, or no overclocked entries), a hundred at a time.
async function allPeers(r) {
  const d = await call("bench.peers", { id: r.id });
  if (!d) return;
  const items = [];
  d.rows.forEach((e, idx) => { if (d.mineIndex === idx) items.push({ you: true }); items.push({ e }); });
  if (d.mineIndex !== null && d.mineIndex >= d.rows.length) items.push({ you: true });
  items.forEach((it, k) => { it.rank = k + 1; });
  const PAGE = 100;
  let shown = PAGE, filter = "all";
  const refs = d.rows.filter((e) => e.references).length;
  const search = h("input", { class: "field", type: "search", placeholder: t("Web_Peers_Search"), "aria-label": t("Web_Peers_Search") });
  const chips = h("div", { class: "chips", role: "group" }, [["all", "Web_Peers_FilterAll"], ["featured", "Web_Peers_FilterFeatured"], ["stock", "Web_Peers_FilterStock"]]
    .map(([k, key]) => h("button", { class: "chip", type: "button", "data-k": k, onclick: () => { filter = k; shown = PAGE; paint(); } }, t(key), k === "featured" ? h("span", { class: "lat" }, ` ${refs}`) : null)));
  const found = h("span", { class: "caption" });
  const list = h("ol", { class: "peer-list long" }), more = h("button", { class: "btn quiet", type: "button" });
  function paint() {
    for (const c of chips.children) c.classList.toggle("on", c.dataset.k === filter);
    const q = search.value.trim().toLowerCase(), match = (s) => !q || s.toLowerCase().includes(q);
    list.replaceChildren();
    const hits = items.filter((it) => it.you || (match(it.e.part) && (filter !== "stock" || !it.e.oc) && (filter !== "featured" || it.e.references)));
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

// This copy's own recorded runs of a benchmark, on every machine it has been used on. Each opens to its full record. In Mazesta's edition
// the star makes a run a reference result of its model (published with the next lists) and the box marks it overclocked; the users' edition
// only shows the runs (a reference is chosen by Mazesta, in its edition or on the site).
async function history(r) {
  const runs = await call("bench.history", { id: r.id });
  const staff = !!boot.staff;
  const cols = ["Web_Runs_Pick", "Web_Runs_Date", "Web_Runs_Machine", "Web_Runs_Part", "Web_Runs_Value", "Web_Runs_Oc", ...(staff ? ["Web_Runs_Featured"] : [])];
  const mark = (x, patch) => call("bench.mark", { id: r.id, run: x.id, ...patch });
  const body = [], picked = new Set();
  const cmpBtn = h("button", { class: "btn", type: "button", disabled: true, onclick: () => compareRuns(r, runs.filter((x) => picked.has(x.id))) }, icon("chart"), t("Web_Runs_Compare"));
  if (runs && runs.length) {
    const tbody = h("tbody", {});
    for (const x of runs) {
      const star = h("button", { class: `icon-btn star-btn ${x.featured ? "on" : ""}`, type: "button", "aria-pressed": String(!!x.featured), title: t("Web_Runs_MarkHint"), "aria-label": t("Web_Runs_Featured"),
        onclick: async (e) => { e.stopPropagation(); x.featured = !x.featured; star.classList.toggle("on", x.featured); star.setAttribute("aria-pressed", String(x.featured)); await mark(x, { featured: x.featured }); } }, icon("star"));
      const oc = h("input", { type: "checkbox", class: "check", "aria-label": t("Web_Runs_Oc"), onclick: (e) => e.stopPropagation(), onchange: async (e) => { x.detail.oc = e.target.checked; await mark(x, { oc: e.target.checked }); } });
      oc.checked = !!x.detail?.oc; oc.disabled = !staff;
      const pick = h("input", { type: "checkbox", class: "check", "aria-label": t("Web_Runs_Pick"), onclick: (e) => e.stopPropagation(), onchange: (e) => {
        if (e.target.checked) picked.add(x.id); else picked.delete(x.id);
        if (picked.size > 4) { picked.delete(x.id); e.target.checked = false; toast(t("Web_Runs_CompareMax"), "fail"); }
        cmpBtn.disabled = picked.size < 2; } });
      const tr = h("tr", { class: "openable", tabIndex: 0, title: t("Web_Detail_Open") },
        h("td", {}, pick), h("td", { class: "lat" }, x.at), h("td", { class: "lat" }, x.machine), h("td", { class: "lat" }, x.part), h("td", { class: "num" }, x.value), h("td", {}, oc), staff ? h("td", {}, star) : null);
      let open = null;
      const toggle = () => {
        if (open) { open.remove(); open = null; return; }
        const note = h("input", { class: "field", value: x.note || "", maxlength: "120", placeholder: t("Web_Runs_NoteLabel"), "aria-label": t("Web_Runs_NoteLabel"),
          onchange: (e) => { x.note = e.target.value; mark(x, { note: e.target.value }); } });
        open = h("tr", { class: "run-detail" }, h("td", { colspan: String(cols.length) }, staff ? h("label", { class: "note-field" }, t("Web_Runs_NoteLabel"), note) : null, compare(x.detail, null, false)));
        tr.after(open);
      };
      tr.addEventListener("click", toggle);
      tr.addEventListener("keydown", (e) => { if (e.target === tr && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); toggle(); } });
      tbody.append(tr);
    }
    body.push(staff ? h("p", { class: "note" }, t("Web_Runs_MarkHint")) : null, h("p", { class: "note" }, t("Web_Runs_CompareHint")), h("div", { class: "runs-wrap" }, h("table", { class: "runs" }, h("thead", {}, h("tr", {}, cols.map((c) => h("th", {}, t(c))))), tbody)));
  } else body.push(h("p", { class: "rec-none" }, t("Web_Runs_Empty")));
  sheet(r.name, t(staff ? "Web_Runs_Note" : "Web_Runs_Note_Client"), [...body, h("div", { class: "btn-row" }, cmpBtn, staff ? h("button", { class: "btn", type: "button", onclick: () => call("bench.exec", { cmd: "openRuns" }) }, icon("folder"), t("Web_Runs_Folder")) : null)]);
}

// The lines of a chart, one per measured quantity: a line wears its part's hue (the other lines of the part a step lighter), a chip in the
// legend shows or hides it, and the pointer reads every shown line's value at that second. Each line has its own scale (0 to its largest
// value, a percentage to 100) so a clock and a load can share the picture; a comparison of runs shares one scale instead.
const PART_HUE = { Gpu: "--c-gpu", Cpu: "--c-cpu", Ram: "--c-ram" };
const SVGNS = "http://www.w3.org/2000/svg";
function chart(lines, shared = false) {
  const W = 760, H = 240, L = 8, R = 8, T = 10, B = 22, n = Math.max(...lines.map((l) => l.values.length)), on = new Set(lines.map((l) => l.id));
  const ns = (tag, a) => { const e = document.createElementNS(SVGNS, tag); for (const k in a) e.setAttribute(k, a[k]); return e; };
  const svg = ns("svg", { viewBox: `0 0 ${W} ${H}`, class: "trace", role: "img" });
  const wrap = h("div", { class: "trace-wrap" }), tip = h("div", { class: "trace-tip", hidden: true }), legend = h("div", { class: "chips trace-legend", role: "group" });
  const x = (i) => L + (n < 2 ? 0 : i / (n - 1)) * (W - L - R);
  const maxOf = (l) => Math.max(...l.values.filter((v) => v !== null && v !== undefined), 0);
  const sharedTop = shared ? Math.max(...lines.map(maxOf), 1) : 1;
  const top = (l) => shared ? sharedTop : l.unit === "%" ? 100 : maxOf(l) || 1;
  const y = (l, v) => T + (1 - v / top(l)) * (H - T - B);
  const cursor = ns("line", { class: "cur", y1: T, y2: H - B, visibility: "hidden" });
  function paint() {
    svg.replaceChildren();
    for (let g = 0; g <= 4; g++) svg.append(ns("line", { class: "grid", x1: L, x2: W - R, y1: T + g * (H - T - B) / 4, y2: T + g * (H - T - B) / 4 }));
    const step = Math.max(1, Math.ceil(n / 8));
    for (let i = 0; i < n; i += step) { const tx = ns("text", { class: "ax", x: x(i), y: H - 6, "text-anchor": i === 0 ? "start" : "middle" }); tx.textContent = `${i}s`; svg.append(tx); }
    for (const l of lines) {
      if (!on.has(l.id)) continue;
      let d = "", pen = false;
      l.values.forEach((v, i) => { if (v === null || v === undefined) { pen = false; return; } d += `${pen ? "L" : "M"}${x(i).toFixed(1)} ${y(l, v).toFixed(1)}`; pen = true; });
      svg.append(ns("path", { d, class: "ln", stroke: l.color }));
    }
    svg.append(cursor);
  }
  for (const l of lines) {
    const vals = l.values.filter((v) => v !== null && v !== undefined), avg = vals.length ? vals.reduce((a, c) => a + c, 0) / vals.length : 0;
    const chip = h("button", { class: "chip on", type: "button", onclick: () => { if (on.has(l.id)) on.delete(l.id); else on.add(l.id); chip.classList.toggle("on", on.has(l.id)); paint(); } },
      h("i", { class: "sw", style: { background: l.color } }), l.label, h("span", { class: "lat" }, ` ${avg.toFixed(0)} ${l.unit}`));
    legend.append(chip);
  }
  svg.addEventListener("pointermove", (e) => {
    const r = svg.getBoundingClientRect(), i = Math.max(0, Math.min(n - 1, Math.round(((e.clientX - r.left) / r.width * W - L) / (W - L - R) * (n - 1))));
    cursor.setAttribute("x1", x(i)); cursor.setAttribute("x2", x(i)); cursor.setAttribute("visibility", "visible");
    tip.hidden = false; tip.style.left = `${Math.min(70, (x(i) / W) * 100)}%`;
    tip.replaceChildren(h("b", { class: "lat" }, `${i}s`), ...lines.filter((l) => on.has(l.id) && l.values[i] !== null && l.values[i] !== undefined)
      .map((l) => h("div", {}, h("i", { class: "sw", style: { background: l.color } }), l.label, " ", h("span", { class: "lat" }, `${l.values[i]} ${l.unit}`))));
  });
  svg.addEventListener("pointerleave", () => { tip.hidden = true; cursor.setAttribute("visibility", "hidden"); });
  paint(); wrap.append(svg, tip);
  return h("div", { class: "trace-box" }, wrap, legend);
}

// A part's lines in the part's hue, each next one of the same part a step lighter.
function traceLines(trace) {
  const seen = {};
  return (trace || []).map((s) => {
    const k = seen[s.part] = (seen[s.part] ?? -1) + 1;
    return { id: s.key, label: s.name, unit: s.unit, values: s.values, color: `color-mix(in oklab, var(${PART_HUE[s.part] || "--c-gpu"}) ${100 - k * 22}%, white)` };
  });
}

// The latest run of a row in full: the chart of what the monitor read during it, then every number (the row keeps only a summary).
async function lastRun(r) {
  const d = await call("bench.last", { id: r.id });
  if (!d) { toast(t("Web_Detail_None"), "fail"); return; }
  const lines = traceLines(d.trace);
  sheet(r.name, `${d.machine} · ${d.at}`, [h("div", { class: "trace-head" }, h("b", { class: "num big" }, d.value), h("span", { class: "caption lat" }, d.part)),
    lines.length ? chart(lines) : h("p", { class: "rec-none" }, t("Web_Chart_NoTrace")), compare(d.detail, null, false)]);
}

// Two to four logged runs (from any machine this copy has logged) side by side: their figures in columns and, for the quantity picked, their lines on one scale.
function compareRuns(r, picked) {
  const hues = ["var(--c-gpu)", "var(--c-cpu)", "var(--c-ram)", "#e0b040"];
  const runs = picked.map((x, i) => ({ ...x, label: `${x.machine} · ${x.at.slice(0, 10)}`, color: hues[i] }));
  const keys = [];
  for (const x of runs) for (const s of x.trace || []) if (!keys.includes(s.key)) keys.push(s.key);
  const holder = h("div", {});
  const chips = h("div", { class: "chips", role: "group" }, keys.map((k) => h("button", { class: "chip", type: "button", "data-k": k, onclick: () => show(k) }, runs.flatMap((x) => x.trace || []).find((s) => s.key === k).name)));
  function show(k) {
    for (const c of chips.children) c.classList.toggle("on", c.dataset.k === k);
    const lines = runs.map((x) => { const s = (x.trace || []).find((q) => q.key === k); return s && { id: x.id, label: x.label, unit: s.unit, values: s.values, color: x.color }; }).filter(Boolean);
    holder.replaceChildren(lines.length ? chart(lines, true) : h("p", { class: "rec-none" }, t("Web_Chart_NoTrace")));
  }
  const side = (x) => ({ ...x.detail, label: x.label });
  sheet(r.name, t("Web_Runs_Compare"), [keys.length ? chips : h("p", { class: "rec-none" }, t("Web_Chart_NoTrace")), holder, compare(side(runs[0]), side(runs[1]), true, runs.slice(2).map(side))]);
  if (keys.length) show(keys[0]);
}

export function mount(el) {
  const list = benchList();
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Benchmarks")), h("p", { class: "page-lede" }, t("Bench_Note")))), list.el);
  return list.off;
}
