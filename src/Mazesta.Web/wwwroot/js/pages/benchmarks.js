// Benchmarks: numbers only, no score and no verdict, in a folding panel per part (CPU apart from GPU apart from storage …). Ticked rows run one
// after another; a single row can run on its own. Each row shows the best result kept on this system, and after a run how the new one
// compares with it: only a better run replaces the record (the host keeps it, per system, in Data/benchmarks).
import { call, on } from "../bridge.js";
import { t, fa } from "../i18n.js";
import { h, icon } from "../ui.js";
import { setField } from "./tests.js";
import { groupPanel, byPart } from "../groups.js";

// The list, optionally only one part's benchmarks (the component pages reuse it).
export function benchList(component = null) {
  const wrap = h("div", {});
  const runSel = h("button", { class: "btn go", onclick: () => call("bench.exec", { cmd: "runSelected" }) }, icon("play"), t("Bench_RunSelected"));
  const cancel = h("button", { class: "btn stop", onclick: () => call("bench.exec", { cmd: "cancel" }) }, icon("stop"), t("Bench_Cancel"));
  const queue = h("span", { class: "pill run", hidden: true });
  const list = h("div", { class: "groups" });
  wrap.append(list, h("div", { class: "dock" }, runSel, cancel, queue, h("span", { class: "grow" }),
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
    const rec = h("div", { class: "rec" }), unavailable = h("div", { class: "unavailable", hidden: true }), peers = h("div", { class: "peers", hidden: true });
    const row = h("div", { class: "q-row", style: { "--i": i } },
      h("span", { class: "step" }, fa(String(i + 1).padStart(2, "0"))), check, h("span", { class: "name" }, r.name),
      h("div", { class: "ctrls" }, h("label", {}, t("Bench_Duration"), dur, t("Test_Seconds")), run),
      opts.length ? h("div", { class: "extra" }, opts.map((x) => x.el)) : null,
      h("div", { class: "state" }, bar, status), unavailable, metrics, rec, peers, detail);
    into.append(row);
    rows.set(r.id, { row, check, dur, run, opts, bar, status, metrics, rec, peers, detail, unavailable, last: "", lastRec: "", lastPeers: "" });
  }
  function update(s) {
    if (!rows.size) build(s);
    runSel.disabled = !s.canRunSelected; cancel.disabled = !s.running;
    queue.hidden = !s.queue; queue.textContent = s.queue || "";
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
      if (recKey !== x.lastRec) { x.lastRec = recKey; x.rec.replaceChildren(...record(r)); }
      // A run in progress keeps the last standing on screen (the host sends none while the row runs).
      const peerKey = JSON.stringify(r.peers);
      if (r.peers !== null && peerKey !== x.lastPeers) { x.lastPeers = peerKey; x.peers.hidden = false; x.peers.replaceChildren(...standing(r)); }
      else if (r.peers === null && !r.active) x.peers.hidden = true;
    }
    const byId = new Map(s.rows.map((r) => [r.id, r]));
    for (const { g, ids } of groups) {
      const rs = ids.map((id) => byId.get(id)).filter(Boolean);
      g.sync(rs.length, rs.filter((r) => r.selected).length, rs.some((r) => r.active));
    }
  }
  call("bench.state").then(update);
  return { el: wrap, off: on("bench", update) };
}

// The record line: the best result kept on this system; after a run, the run against it. A better run is saved, a lower one is shown and
// dropped. The change is written as a signed percentage in the part's hue for a record, plain for a lower run (never pass/fail colours).
function record(r) {
  const c = r.compared, b = r.best;
  const cell = (label, m, extra = null) => h("div", { class: "rec-cell" }, h("span", { class: "k" }, label), h("span", { class: "v num" }, m.value), h("span", { class: "d" }, m.name, extra ? " · " : "", extra ? h("span", { class: "lat" }, extra) : null));
  if (c) {
    const pct = c.change === null || c.change === undefined ? null : `${c.change > 0 ? "+" : c.change < 0 ? "−" : ""}${Math.abs(c.change).toFixed(1)}%`;
    const verdict = !c.previous ? "Web_Bench_FirstRecord" : c.saved ? "Web_Bench_NewRecord" : c.change === 0 ? "Web_Bench_Equal" : "Web_Bench_Lower";
    return [h("div", { class: `rec-grid ${c.saved ? "up" : "down"}` },
      cell(t("Web_Bench_ThisRun"), c.now), c.previous ? cell(t("Web_Bench_Best"), c.previous, c.previous.at) : null,
      h("div", { class: "rec-verdict" }, c.saved ? icon("trophy") : null, pct ? h("span", { class: "num pct" }, pct) : null, h("span", {}, t(verdict))))];
  }
  if (b) return [h("div", { class: "rec-grid" }, cell(t("Web_Bench_Best"), b, b.at))];
  return [h("p", { class: "rec-none" }, t("Web_Bench_NoRecord"))];
}

// Where this system stands among other systems: the comparison list is one entry per part model (the median of its systems' best runs), from the
// shop's published lists and this copy's own runs. The row shows the few entries around this result; the whole list, searchable, opens apart,
// since it can hold thousands of models. The percentage is this result against each entry, positive when it is ahead; it takes the part's hue
// when ahead and stays plain when behind, never pass/fail colours.
function standing(r) {
  const p = r.peers;
  const head = h("div", { class: "peers-head" }, icon("chart"), h("b", {}, t("Web_Peers_Title")),
    p.total ? h("span", { class: "caption" }, t("Web_Peers_Count", fa(p.total))) : null, h("span", { class: "grow" }),
    p.total ? h("button", { class: "btn quiet", onclick: () => allPeers(r) }, t("Web_Peers_All")) : null,
    h("button", { class: "btn quiet", onclick: () => history(r) }, t("Web_Peers_History")));
  if (!p.total) return [head, h("p", { class: "rec-none" }, t("Web_Peers_None"))];
  const out = [head];
  if (p.mineIndex !== null) out.push(h("div", { class: "peers-sum" }, h("span", { class: "num pct" }, `${Math.round(p.beaten / p.total * 100)}%`), h("span", {}, t("Web_Peers_Ahead", fa(p.beaten), fa(p.total)))));
  else out.push(h("p", { class: "rec-none" }, t("Web_Peers_RunFirst")));
  const list = h("ol", { class: "peer-list" }), rank = (idx) => idx + 1 + (p.mineIndex !== null && idx >= p.mineIndex ? 1 : 0);
  p.around.forEach((e, k) => { const idx = p.from + k; if (p.mineIndex === idx) list.append(youRow(p, idx + 1)); list.append(peerRow(e, rank(idx))); });
  if (p.mineIndex !== null && p.mineIndex >= p.from + p.around.length) list.append(youRow(p, p.mineIndex + 1));
  out.push(list);
  return out;
}

function peerRow(e, rank) {
  const d = e.diff === null || e.diff === undefined ? null : `${e.diff > 0 ? "+" : e.diff < 0 ? "−" : ""}${Math.abs(e.diff).toFixed(1)}%`;
  return h("li", { class: `peer ${e.same ? "same" : ""}` }, h("span", { class: "rk num" }, rank),
    h("span", { class: "pn" }, h("span", { class: "lat" }, e.part), e.same ? h("span", { class: "tag" }, t("Web_Peers_Same")) : null, e.local ? h("span", { class: "tag" }, t("Web_Peers_Local")) : null),
    h("span", { class: "pv num", title: t("Web_Peers_Median") }, e.value), h("span", { class: "ps caption" }, t("Web_Peers_Systems", fa(e.systems), fa(e.runs))),
    h("span", { class: `pd num ${e.diff > 0 ? "up" : "down"}`, title: t("Web_Peers_DiffHint") }, d || ""));
}

function youRow(p, rank) {
  return h("li", { class: "peer you" }, h("span", { class: "rk num" }, rank),
    h("span", { class: "pn" }, h("b", {}, t("Web_Peers_You")), p.part ? "·" : null, p.part ? h("span", { class: "lat" }, p.part) : null),
    h("span", { class: "pv num" }, p.mine || ""), h("span", { class: "ps" }), h("span", { class: "pd" }));
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

async function allPeers(r) {
  const d = await call("bench.peers", { id: r.id });
  if (!d) return;
  const items = [];
  d.rows.forEach((e, idx) => { if (d.mineIndex === idx) items.push({ you: true }); items.push({ e }); });
  if (d.mineIndex !== null && d.mineIndex >= d.rows.length) items.push({ you: true });
  items.forEach((it, k) => { it.rank = k + 1; });
  const PAGE = 100;
  let shown = PAGE;
  const search = h("input", { class: "field", type: "search", placeholder: t("Web_Peers_Search"), "aria-label": t("Web_Peers_Search") });
  const list = h("ol", { class: "peer-list long" }), more = h("button", { class: "btn quiet", type: "button" });
  function paint() {
    const q = search.value.trim().toLowerCase();
    const hits = q ? items.filter((it) => it.you || it.e.part.toLowerCase().includes(q)) : items;
    list.replaceChildren(...hits.slice(0, shown).map((it) => (it.you ? youRow(d, it.rank) : peerRow(it.e, it.rank))));
    more.hidden = hits.length <= shown; more.textContent = t("Web_Peers_More", fa(Math.min(PAGE, hits.length - shown)));
  }
  more.onclick = () => { shown += PAGE; paint(); };
  search.oninput = () => { shown = PAGE; paint(); };
  const sub = [d.metric, d.built ? t("Web_Peers_Built", d.built) : ""].filter(Boolean).join(" · ");
  sheet(d.name, sub, [h("p", { class: "note" }, t("Web_Peers_Note")),
    d.mineIndex !== null ? h("div", { class: "peers-sum" }, h("span", { class: "num pct" }, `${Math.round(d.beaten / Math.max(1, d.rows.length) * 100)}%`), h("span", {}, t("Web_Peers_Ahead", fa(d.beaten), fa(d.rows.length)))) : null,
    search, list, more]);
  paint();
  list.querySelector(".you")?.scrollIntoView({ block: "center" });
}

// This copy's own recorded runs of a benchmark, on every machine it has been used on: the "results it has recorded", and what the shop gathers.
async function history(r) {
  const runs = await call("bench.history", { id: r.id });
  const cols = ["Web_Runs_Date", "Web_Runs_Machine", "Web_Runs_Part", "Web_Runs_Value"];
  const body = runs && runs.length
    ? h("div", { class: "runs-wrap" }, h("table", { class: "runs" }, h("thead", {}, h("tr", {}, cols.map((c) => h("th", {}, t(c))))),
      h("tbody", {}, runs.map((x) => h("tr", {}, h("td", { class: "lat" }, x.at), h("td", { class: "lat" }, x.machine), h("td", { class: "lat" }, x.part), h("td", { class: "num" }, x.value))))))
    : h("p", { class: "rec-none" }, t("Web_Runs_Empty"));
  sheet(r.name, t("Web_Runs_Note"), [body, h("div", { class: "btn-row" }, h("button", { class: "btn", type: "button", onclick: () => call("bench.exec", { cmd: "openRuns" }) }, icon("folder"), t("Web_Runs_Folder")))]);
}

export function mount(el) {
  const list = benchList();
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Benchmarks")), h("p", { class: "page-lede" }, t("Bench_Note")))), list.el);
  return list.off;
}
