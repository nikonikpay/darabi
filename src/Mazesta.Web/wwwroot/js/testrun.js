// The live test panel, on top of the tested part's own page (or of Monitoring for a test no part page shows): which test of the queue is running, how far it is and for how long, and a log of what it is
// doing right now - each step in words, with the formula it checks or the command it runs beside it. It only shows what the host reports; the
// verdict is the test's own, never the log's. It tells the page which part is under test, so the sensors of that part come forward.
import { call, on } from "./bridge.js";
import { t, fa } from "./i18n.js";
import { h, icon } from "./ui.js";
import { part, partOfId } from "./parts.js";
import { OUTCOME } from "./pages/tests.js";

const MAX_LINES = 500;
// The monitor's hardware kinds a test's part is measured by: the power test loads the processor and the graphics card together.
export const KINDS = { Cpu: ["Cpu"], Gpu: ["Gpu"], Memory: ["Memory"], Storage: ["Storage"], Network: ["Network"], Power: ["Cpu", "Gpu", "Psu"] };
// Where a running test is watched: its part's page, or Monitoring for the ones no part page covers (the combined power test, Windows' checks).
const PAGE_OF = { Cpu: "cpu", Gpu: "gpu", Memory: "ram", Storage: "storage", Network: "network" };
export const pageOfTest = (id) => PAGE_OF[partOfId(id)] || "monitoring";
const remember = (key, fallback) => { try { const v = JSON.parse(localStorage.getItem(key)); return v ?? fallback; } catch { return fallback; } };
const keep = (key, v) => { try { localStorage.setItem(key, JSON.stringify(v)); } catch { /* not kept */ } };

// page: the page the panel sits on. onFocus(kinds | null, partKind): the test under way moved to another part, or following was switched.
// On a part's page the panel shows only while that part is under test; on Monitoring it shows the last session's log too. When the queue
// moves to a part that is watched on another page and following is on, the page follows it there.
export function runPanel(onFocus, page = "monitoring") {
  let follow = remember("mazesta.run.follow", true), current = null, running = false, lastPart = undefined, started = null, timer = 0;
  const title = h("h2", { class: "run-name" }), step = h("span", { class: "run-step" }), pill = h("span", { class: "pill none" });
  const elapsed = h("span", { class: "run-time lat" }), status = h("span", { class: "caption" });
  const bar = h("div", { class: "progress" }, h("i"));
  const ico = h("span", { class: "ico" });
  const followBox = h("input", { type: "checkbox", class: "switch", checked: follow, "aria-label": t("Web_Run_Follow"),
    onchange: (e) => { follow = e.target.checked; keep("mazesta.run.follow", follow); lastPart = undefined; focus(); } });
  const cancel = h("button", { class: "btn stop", type: "button", onclick: () => call("tests.exec", { cmd: "cancel" }) }, icon("stop"), t("Test_Cancel"));
  const back = h("a", { class: "btn quiet", href: "#/tests" }, icon("flask"), t("Web_Run_Back"));
  // A finished session's panel stays until it is closed or the next session starts, so its last log lines can still be read.
  let closed = false;
  const close = h("button", { class: "icon-btn", type: "button", title: t("Web_Run_Close"), "aria-label": t("Web_Run_Close"), onclick: () => { closed = true; el.hidden = true; } }, icon("x"));
  const lines = h("div", { class: "run-log", role: "log", "aria-live": "polite", "aria-label": t("Web_Run_Log") });
  const el = h("section", { class: "plane run-panel", hidden: true },
    h("div", { class: "run-head" }, ico,
      h("div", { class: "run-ttl" }, step, title),
      pill, h("span", { class: "grow" }),
      h("label", { class: "run-follow" }, followBox, t("Web_Run_Follow")), back, cancel, close),
    h("div", { class: "run-bar" }, bar, h("div", { class: "run-sub" }, status, h("span", { class: "grow" }), elapsed)),
    h("h3", { class: "run-log-head" }, t("Web_Run_Log")), lines);

  // The host sends a key and its arguments: a number reads in the page's digits, an '@' argument is a key (a test's name, an outcome), and
  // anything else (a device name, a pattern) stays as it came.
  const arg = (a) => a.startsWith("@") ? t(a.slice(1)) : /^-?\d+(\.\d+)?$/.test(a) ? Number(a) : a;
  const words = (line) => t(line.key, ...(line.args || []).map(arg));

  // Keeps the newest line in view unless the technician has scrolled up to read an older one.
  function append(line) {
    const stick = lines.scrollHeight - lines.scrollTop - lines.clientHeight < 24;
    lines.append(h("div", { class: `run-line ${line.level.toLowerCase()}` },
      h("span", { class: "run-at lat" }, line.at), h("span", { class: "run-text" }, words(line)),
      line.formula ? h("code", { class: "run-formula" }, line.formula) : null));
    while (lines.childElementCount > MAX_LINES) lines.firstElementChild.remove();
    if (stick) lines.scrollTop = lines.scrollHeight;
  }

  function tick() {
    if (!started || !running) return;
    const s = Math.max(0, Math.round((Date.now() - started) / 1000));
    elapsed.textContent = t("Web_Run_Elapsed", `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}`);
  }

  function focus() {
    const p = running && current ? partOfId(current.id) : null;
    if (p === lastPart) return;
    const moved = lastPart !== undefined && p !== null;   // not on arrival: a page the technician opened is not taken from them
    lastPart = p;
    if (moved && follow && pageOfTest(current.id) !== page) { location.hash = `#/${pageOfTest(current.id)}`; return; }
    onFocus?.(follow && p ? KINDS[p] || null : null, p);
  }

  function update(s) {
    running = !!s.running; current = s.current || null;
    const wasHidden = el.hidden;
    if (running) closed = false;
    close.hidden = running;
    el.hidden = closed || (page === "monitoring" ? !running && !lines.childElementCount : !(current && pageOfTest(current.id) === page));
    if (wasHidden && !el.hidden) requestAnimationFrame(() => { lines.scrollTop = lines.scrollHeight; });   // first shown: start at the newest line
    cancel.hidden = !running;
    if (current) {
      const p = partOfId(current.id);
      el.className = `plane run-panel ${part(p).cls}`;
      ico.replaceChildren(icon(part(p).icon));
      step.textContent = t("Web_Run_Step", fa(current.index), fa(current.total));
      title.textContent = current.name;
      pill.className = `pill ${OUTCOME[current.outcome] || "none"}`; pill.textContent = current.outcomeText;
      bar.firstChild.style.setProperty("--p", current.percent || 0);
      status.textContent = running ? current.status || "" : t("Web_Run_Done");
      started = current.startedAt || null;
    } else {
      step.textContent = ""; title.textContent = t(running ? "Web_Run_Title" : "Web_Run_Done"); pill.className = "pill none"; pill.textContent = "";
      bar.firstChild.style.setProperty("--p", 0); status.textContent = "";
    }
    if (!running) elapsed.textContent = "";
    // The clock ticks only while a test runs: nothing wakes the page for an idle panel.
    if (running && !timer) timer = setInterval(tick, 1000);
    if (!running && timer) { clearInterval(timer); timer = 0; }
    tick(); focus();
  }

  call("tests.log").then((all) => { for (const l of all || []) append(l); return call("tests.state"); }).then(update).catch(() => {});
  const offs = [on("tests", update), on("testlog", (l) => { append(l); if (el.hidden && !closed && page === "monitoring") { el.hidden = false; lines.scrollTop = lines.scrollHeight; } })];
  return { el, off: () => { for (const o of offs) o(); clearInterval(timer); } };
}
