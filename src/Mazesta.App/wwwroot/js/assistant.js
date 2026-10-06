// The assistant's column, at the reading-end edge of every page: a chat with a language model that runs on this computer's graphics card
// (llama.cpp's llama-server on the loopback). Opt-in: the download, the start and every question are the user's. It stays while the pages
// change under it, and it can change them: it opens the page it talks about, and when it starts a test the page follows the test to the part's
// own page, as a test started by hand does. A test or benchmark starts only after the user agreed on the confirm card, for the items left
// ticked; what a run gave is drawn here from the run's own result, beside the model's words about it. The past chats are kept on this computer,
// listed beside the chat, and deleted one by one or all at once. The column is always there (folded or open, and the side bar opens it): on a
// computer that cannot run the assistant it says why. The graphics card is read a moment after the app opens, so that answer is asked again
// until the card is known.
// Downloads are the AI page's (same bridge, "ai" event); the chat is this column's ("assistant" event).
import { call, on } from "./bridge.js";
import { t, fa } from "./i18n.js";
import { h, icon, toast } from "./ui.js";
import { go, boot } from "./app.js";
import { OUTCOME } from "./pages/tests.js";
import { pageOfRun } from "./testrun.js";
import { lengthText } from "./duration.js";

const store = (key, v) => { try { if (v === undefined) return localStorage.getItem(key); localStorage.setItem(key, v); } catch { /* not kept */ } return null; };

export function mountAssistant(app, root) {
  const exec = (m, cmd, extra = {}) => call(m, { cmd, ...extra }).catch((e) => toast(String(e.message || e), "fail"));
  // open / closed is a per-viewer convenience kept in the browser profile; "none" only while the bridge has not answered.
  const setOpen = (open) => { app.dataset.asst = open ? "open" : "closed"; store("mazesta.asst", open ? "open" : "closed"); };
  const setHist = (open) => { app.dataset.hist = open ? "open" : ""; store("mazesta.asst.hist", open ? "open" : ""); histBtn.setAttribute("aria-pressed", String(open)); };

  const dot = h("span", { class: "asst-dot", "aria-hidden": "true" });
  const strip = h("button", { class: "asst-strip", type: "button", title: t("Assist_Open"), "aria-label": t("Assist_Open"), onclick: () => setOpen(true) }, icon("chat"), dot, h("span", { class: "asst-strip-name" }, t("Nav_Assistant")));
  const histBtn = h("button", { class: "icon-btn", type: "button", title: t("Assist_History"), "aria-label": t("Assist_History"), onclick: () => setHist(app.dataset.hist !== "open") }, icon("clock"));
  const newBtn = h("button", { class: "icon-btn", type: "button", title: t("Assist_Clear"), "aria-label": t("Assist_Clear"), onclick: () => { exec("assistant.exec", "new"); input.focus(); } }, icon("refresh"));
  const shut = h("button", { class: "icon-btn", type: "button", title: t("Assist_Collapse"), "aria-label": t("Assist_Collapse"), onclick: () => setOpen(false) }, icon("x"));
  const state = h("span", { class: "pill none" });
  const head = h("header", { class: "asst-head" }, h("span", { class: "ico" }, icon("chat")), h("h2", { class: "asst-title" }, t("Nav_Assistant")), state, h("span", { class: "grow" }), histBtn, newBtn, shut);

  const histList = h("div", { class: "asst-hist-list" });
  const clearAll = h("button", { class: "btn quiet", type: "button", onclick: () => { if (confirm(t("Assist_DeleteAll_Confirm"))) exec("assistant.exec", "deleteAll"); } }, icon("x"), t("Assist_DeleteAll"));
  const hist = h("section", { class: "asst-hist", "aria-label": t("Assist_History") }, h("h3", { class: "asst-hist-title" }, t("Assist_History")), histList, h("div", { class: "asst-hist-foot" }, clearAll));

  const gate = h("div", { class: "asst-gate" });
  const log = h("div", { class: "as-log", role: "log", "aria-live": "polite" });
  const confirmBox = h("div", { class: "as-confirm", role: "alertdialog", hidden: true }), activityBox = h("div", { class: "as-activity", hidden: true });
  const input = h("textarea", { class: "as-input", rows: 2, dir: "auto", placeholder: t("Assist_Placeholder") });
  const send = h("button", { class: "btn primary", type: "button" }, icon("send"), t("Assist_Send")), stop = h("button", { class: "btn stop", type: "button", hidden: true }, icon("stop"), t("Assist_StopReply"));
  const foot = h("div", { class: "asst-foot" });
  const compose = h("div", { class: "as-compose" }, input, h("div", { class: "btn-row" }, send, stop, h("span", { class: "grow" }), foot));
  const chatBox = h("div", { class: "asst-chat" }, log, activityBox, confirmBox, compose, h("p", { class: "caption asst-note" }, t("Assist_Disclaimer")));
  const main = h("div", { class: "asst-main" }, gate, chatBox);
  const panel = h("div", { class: "asst-panel" }, head, h("div", { class: "asst-body" }, main, hist));
  root.replaceChildren(strip, panel);

  let a = null, ai = null, stick = true, msgShape = "", msgEls = [];
  const ready = () => a?.server === "ready";
  const submit = () => { const text = input.value.trim(); if (!text || !ready() || a.busy) return; input.value = ""; stick = true; exec("assistant.exec", "send", { text }); };
  send.onclick = submit; stop.onclick = () => exec("assistant.exec", "cancel");
  input.addEventListener("keydown", (e) => { if (e.key === "Enter" && !e.shiftKey && !e.isComposing) { e.preventDefault(); submit(); } });
  log.addEventListener("scroll", () => { stick = log.scrollHeight - log.scrollTop - log.clientHeight < 40; });

  // The downloaded models this computer can run; changing it stops the assistant, the next start uses the new one.
  function picker() {
    if (!a.choices?.length) return null;
    const sel = h("select", { class: "as-model lat", "aria-label": t("Assist_Model"), disabled: a.busy || a.server === "starting" },
      a.choices.map((c) => h("option", { value: c.id, selected: c.id === a.model?.id }, `${c.name} · ${c.size}`)));
    sel.onchange = () => exec("assistant.exec", "select", { id: sel.value });
    return h("label", { class: "as-pick" }, h("span", { class: "caption" }, t("Assist_Model")), sel);
  }
  function progress(x) {
    const fill = h("i"), text = h("span", { class: "caption lat" }), el = h("div", { class: "ai-prog" }, h("div", { class: "progress" }, fill), text);
    el.upd = (x) => { fill.style.setProperty("--p", x.percent / 100); text.textContent = `${Math.floor(x.percent)}% · ${x.done}${x.speed ? " · " + x.speed : ""}`; };
    el.upd(x); return el;
  }

  // Before the chat: download, start, or why not. Rebuilt only when what it says changes; a download's ticks move its bar in place.
  let gateKey = "", gateProg = null;
  function renderGate() {
    if (a.status !== "Available") {   // why this computer cannot run it (no card with its own memory, too little of it, no free memory now)
      chatBox.hidden = true; gate.hidden = false;
      const key = "why|" + a.status; if (key === gateKey) return; gateKey = key;
      return gate.replaceChildren(...[a.status === "Reading" ? null : h("h3", { class: "asst-gate-title" }, t("Assist_Unavailable")), h("p", { class: "ai-purpose" }, t(`Assist_${a.status}`))].filter(Boolean));
    }
    const m = a.model, rt = ai?.runtime, tr = rt?.transfer?.active ? rt.transfer : ai?.models?.find((x) => x.id === m?.id)?.transfer;
    const downloading = !!tr?.active || (ai?.downloading != null), failed = rt?.transfer?.error || ai?.models?.find((x) => x.id === m?.id)?.transfer?.error;
    const installed = a.runtimeReady && m?.downloaded, on_ = a.server !== "off";
    chatBox.hidden = !(installed && on_); gate.hidden = !chatBox.hidden;
    const key = JSON.stringify([a.status, m && { id: m.id, d: m.downloaded }, a.choices, a.runtimeReady, a.server, a.error, a.blocked, downloading, !!tr?.active, failed]);
    if (key === gateKey) { if (tr?.active) gateProg?.upd(tr); return; }
    gateKey = key;
    if (gate.hidden) return;
    const kids = [];
    if (!installed) {
      kids.push(h("h3", { class: "asst-gate-title" }, t("Assist_Enable_Title")), h("p", { class: "ai-purpose" }, t("Assist_Enable_Text", m.name, m.size)));
      kids.push(h("div", { class: "btn-row" }, h("button", { class: "btn primary", disabled: downloading, onclick: () => exec("ai.exec", "assistantEnable") }, downloading ? t("Assist_Downloading") : t("Assist_Enable"))));
      gateProg = tr?.active ? progress(tr) : null; if (gateProg) kids.push(gateProg);
      if (failed) kids.push(h("p", { class: "ai-err" }, failed));
    } else {
      kids.push(h("p", { class: "ai-purpose" }, a.server === "starting" ? t("Assist_Starting") : t("Assist_Off")), picker());
      kids.push(h("div", { class: "btn-row" }, a.server === "starting"
        ? h("button", { class: "btn stop", onclick: () => exec("assistant.exec", "stop") }, t("Assist_Stop"))
        : h("button", { class: "btn primary", disabled: a.blocked, onclick: () => exec("assistant.exec", "start") }, icon("play"), t("Assist_Start"))));
      if (a.blocked) kids.push(h("p", { class: "caption" }, t("Assist_Blocked")));
    }
    if (a.error) kids.push(h("p", { class: "ai-err" }, a.error));
    gate.replaceChildren(...kids.filter(Boolean));
  }

  // What a run gave, drawn from its own result: each test with its outcome (only Passed is green), or the benchmark's number and its change.
  function evidence(x) {
    let r; try { r = JSON.parse(x.result); } catch { return null; }
    if (!r || r.error) return r?.error ? h("div", { class: "as-card" }, h("span", { class: "caption" }, t("Assist_Run_NotStarted"))) : null;
    if (r.started === false) return h("div", { class: "as-card" }, h("span", { class: "caption" }, t("Assist_Run_NotStarted")));
    if (x.name === "export_report") return r.made ? h("div", { class: "as-card as-file" }, icon("doc"), h("span", { class: "lat" }, r.file), h("span", { class: "grow" }),
      h("button", { class: "btn primary", type: "button", onclick: () => exec("assistant.exec", "openFile", { path: r.path }) }, icon("popout"), t("Assist_OpenFile"))) : null;
    // A Windows command the app ran: its own output, as Windows printed it; the battery report is a file to open.
    if (x.name === "run_windows_command") return r.declined ? null : h("div", { class: "as-card" }, h("div", { class: "as-card-row" }, h("span", { class: "lat" }, r.command), h("span", { class: `pill ${r.exitCode === 0 ? "none" : "fail"}` }, h("span", { class: "lat" }, `exit ${r.exitCode}`))),
      r.output ? h("pre", { class: "lat as-out", dir: "ltr" }, r.output) : null,
      r.path ? h("button", { class: "btn primary", type: "button", onclick: () => exec("assistant.exec", "openFile", { path: r.path }) }, icon("popout"), t("Assist_OpenFile")) : null);
    if (x.name === "check_software") return h("div", { class: "as-card" }, (r.programs || []).slice(0, 8).map((p) => h("div", { class: "as-card-row" }, h("span", { class: "lat" }, p.name),
      h("span", { class: `pill ${{ HighEnd: "run", Recommended: "pass", Meets: "pass", Minimum: "warn" }[p.level] || "fail"}` }, p.levelName))));
    if (x.name === "run_tests") return h("div", { class: "as-card" }, (r.results || []).map((y) => h("div", { class: "as-card-row" }, h("span", {}, y.name),
      h("span", { class: `pill ${OUTCOME[y.outcome] || "none"}` }, t(`Test_Outcome_${y.outcome}`)))));
    const bench = (b) => b.completed ? h("div", { class: "as-card-row" }, h("span", {}, b.benchmark),
      h("span", { class: "num" }, `${b.value} ${b.unit ?? ""}`), b.changePercent != null ? h("span", { class: `pill ${b.changePercent >= 0 ? "none" : "warn"}` }, h("span", { class: "lat" }, `${b.changePercent > 0 ? "+" : ""}${b.changePercent}%`)) : null)
      : h("div", { class: "as-card-row" }, h("span", {}, b.benchmark ?? ""), h("span", { class: "caption" }, t("Assist_Run_NoResult")));
    if (x.name === "run_benchmark" && Array.isArray(r.results)) return h("div", { class: "as-card" }, r.results.map(bench));
    if (r.completed) return h("div", { class: "as-card" }, bench(r));   // a chat kept from when a benchmark ran alone
    return h("div", { class: "as-card" }, h("span", { class: "caption" }, t("Assist_Run_NoResult")));
  }

  function renderChat() {
    if (chatBox.hidden) return;
    foot.key !== JSON.stringify([a.model?.id, a.choices, a.busy]) && (foot.key = JSON.stringify([a.model?.id, a.choices, a.busy]),
      foot.replaceChildren(picker() ?? h("span", { class: "caption lat" }, a.model?.name ?? ""), h("button", { class: "btn quiet", type: "button", title: t("Assist_Stop"), onclick: () => exec("assistant.exec", "stop") }, icon("stop"))));
    renderConfirm(); renderActivity();
    const shape = a.chat + "|" + a.messages.map((m) => m.role + (m.tools?.length ?? 0) + (m.tools || []).map((x) => x.result ? 1 : 0).join("")).join();
    if (shape === msgShape && msgEls.length === a.messages.length) a.messages.forEach((m, i) => { const txt = m.text || (a.busy ? "…" : ""); if (msgEls[i].textContent !== txt) msgEls[i].textContent = txt; });
    else {
      msgShape = shape;
      msgEls = a.messages.map((m) => h("div", { class: "as-text", dir: "auto" }, m.text || (a.busy ? "…" : "")));   // dir="auto": each message takes its own direction
      log.replaceChildren(...(a.messages.length ? a.messages.map((m, i) => h("div", { class: `as-msg ${m.role}` }, h("div", { class: "as-who" }, t(m.role === "user" ? "Assist_You" : "Assist_Name")), msgEls[i],
        m.tools?.length ? h("div", { class: "as-tools" }, m.tools.map((x) => h("span", { class: `pill ${x.ok ? "none" : "fail"}` }, t(`Assist_Tool_${x.name}`) + (x.ok ? "" : " · " + t("Assist_Tool_failed"))))) : null,
        ...(m.tools || []).filter((x) => x.result).map(evidence)))
        : [h("p", { class: "caption as-empty" }, t("Assist_Empty"))]));
    }
    if (stick) log.scrollTop = log.scrollHeight;
    send.disabled = a.busy || a.server !== "ready"; stop.hidden = !a.busy; newBtn.disabled = a.busy;
    log.querySelector(".ai-err")?.remove(); if (a.error) log.append(h("p", { class: "ai-err" }, a.error));
    log.querySelector(".as-paused")?.remove();   // said once, not added again on every refresh while the run lasts
    if (a.server === "paused") log.append(h("p", { class: "caption as-paused" }, t("Assist_Paused")));
  }

  // The tool asked to start tests or a benchmark: nothing starts until "Start", and only what is left ticked. The column opens to ask.
  let confirmKey = "";
  function renderConfirm() {
    const c = a.confirm, key = JSON.stringify(c);
    confirmBox.hidden = !c; if (key === confirmKey) return; confirmKey = key; if (!c) return confirmBox.replaceChildren();
    setOpen(true);
    const boxes = c.items.map(() => h("input", { type: "checkbox", class: "check", checked: true }));
    const yes = h("button", { class: "btn primary", onclick: () => exec("assistant.exec", "confirm", { value: true, keep: boxes.map((b, i) => (b.checked ? i : -1)).filter((i) => i >= 0) }) }, icon("play"), t("Assist_Confirm_Yes"));
    for (const b of boxes) b.onchange = () => { yes.disabled = !boxes.some((x) => x.checked); };
    confirmBox.replaceChildren(h("h3", { class: "as-confirm-title" }, t(c.kind === "tests" ? "Assist_Confirm_Tests" : c.kind === "command" ? "Assist_Confirm_Command" : c.items.length > 1 ? "Assist_Confirm_Benchmarks" : "Assist_Confirm_Benchmark")),
      h("ul", { class: "as-confirm-list" }, c.items.map((i, n) => h("li", {}, h("label", {}, c.items.length > 1 ? boxes[n] : null, h("span", {}, i.name)), i.duration ? h("span", { class: "caption" }, t("Assist_About", lengthText(i.duration))) : null))),
      h("p", { class: "caption" }, t("Assist_Confirm_Text")),
      h("div", { class: "btn-row" }, yes, h("button", { class: "btn quiet", onclick: () => exec("assistant.exec", "confirm", { value: false }) }, t("Assist_Confirm_No"))));
    confirmBox.scrollIntoView({ block: "nearest" });
  }

  // What the assistant started, with its progress. The page goes where a run started by hand goes: the tested part's page once the first test is
  // under way (its run panel follows the queue from part to part from there), the benchmarks page for a benchmark.
  let actFill = null, actText = null, actKind = "", followTests = false;
  function renderActivity() {
    const x = a.activity; activityBox.hidden = !x;
    if (!x) { actKind = ""; followTests = false; return; }
    if (actKind !== x.kind) {
      actKind = x.kind; actFill = h("i"); actText = h("span", { class: "caption" });
      activityBox.replaceChildren(h("span", { class: "pill run" }, t(x.kind === "tests" ? "Assist_Running_Tests" : "Assist_Running_Benchmark")), actText, h("div", { class: "progress" }, actFill));
      if (x.kind === "tests") followTests = true; else go("benchmarks");
    }
    actText.textContent = x.name ? `${x.name}${x.percent != null ? " · " + fa(Math.floor(x.percent)) + "%" : ""}` : "";
    actFill.style.setProperty("--p", (x.percent ?? 0) / 100);
  }
  const offTests = on("tests", (s) => { if (followTests && s.running && s.current && s.current.outcome === "Running") { followTests = false; go(pageOfRun(s.current)); } });

  // The past chats beside this one, newest first; the open one is marked. Rebuilt only when the list changes.
  let histKey = "";
  const when = new Intl.DateTimeFormat(boot.rtl ? "fa-IR-u-ca-persian" : "en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });
  function renderHistory() {
    const key = JSON.stringify([a.history, a.chat, a.busy]);
    if (key === histKey) return; histKey = key;
    clearAll.disabled = a.busy || !a.history.length;
    histList.replaceChildren(...(a.history.length ? a.history.map((c) => h("div", { class: "asst-hist-item", "aria-current": c.id === a.chat ? "true" : null },
      h("button", { class: "asst-hist-open", type: "button", disabled: a.busy, onclick: () => { stick = true; exec("assistant.exec", "open", { id: c.id }); } },
        h("span", { class: "asst-hist-name", dir: "auto" }, c.title || "…"), h("span", { class: "caption" }, when.format(new Date(c.updated)))),
      h("button", { class: "icon-btn", type: "button", title: t("Assist_DeleteChat"), "aria-label": t("Assist_DeleteChat"), disabled: a.busy && c.id === a.chat,
        onclick: () => exec("assistant.exec", "delete", { id: c.id }) }, icon("x"))))
      : [h("p", { class: "caption" }, t("Assist_History_Empty"))]));
  }

  function render(s) {
    a = s;
    if (app.dataset.asst === "none" || !app.dataset.asst) app.dataset.asst = store("mazesta.asst") === "open" ? "open" : "closed";
    if (a.status !== "Available") { state.className = "pill none"; state.textContent = t("Assist_OffShort"); dot.dataset.state = ""; return renderGate(); }
    state.className = `pill ${a.server === "ready" ? "pass" : a.server === "off" ? "none" : "run"}`;
    state.textContent = t(a.server === "ready" ? "Assist_Ready" : a.server === "starting" ? "Assist_Loading" : a.server === "paused" ? "Assist_PausedShort" : "Assist_OffShort");
    dot.dataset.state = a.confirm ? "ask" : a.busy || a.activity ? "busy" : a.server === "ready" ? "ready" : "";
    renderGate(); renderChat(); renderHistory();
  }

  setHist(store("mazesta.asst.hist") === "open");
  const offA = on("assistant", render), offAi = on("ai", (s) => { ai = s; if (a) renderGate(); });
  // The assistant opens a page, and may mark one control on it ([data-a] on the page, or a program's card): the page draws after it mounts,
  // so the mark waits for the control to appear.
  const offNav = on("assistantNav", (x) => { go(x.page); if (x.target) mark(x.target); });
  // The Tests page's Advanced view, switched from the chat: kept where the page keeps it, and told to the page if it is open.
  on("testsAdvanced", (x) => { try { localStorage.setItem("mazesta.tests.advanced", x.on ? "1" : "0"); } catch { /* not kept */ } window.dispatchEvent(new CustomEvent("tests:advanced", { detail: !!x.on })); });
  function mark(target, tries = 0) {
    const el = document.querySelector(`#stage [data-a="${CSS.escape(target)}"], #stage [data-app="${CSS.escape(target)}"]`);
    if (!el) { if (tries < 30) setTimeout(() => mark(target, tries + 1), 100); return; }
    el.closest("details")?.setAttribute("open", "");
    el.scrollIntoView({ block: "center", behavior: "smooth" });
    el.classList.remove("flash"); void el.offsetWidth; el.classList.add("flash");
    setTimeout(() => el.classList.remove("flash"), 3200);
  }
  const onShow = () => setOpen(app.dataset.asst !== "open"); window.addEventListener("assistant:toggle", onShow);
  // A page asks on the user's behalf (a blue screen's "ask the assistant"): the column opens and the question is sent as if typed; while the
  // assistant is off or busy it waits in the box, to be sent by hand.
  const onAsk = (e) => {
    const text = String(e.detail || "").trim(); if (!text) return;
    setOpen(true);
    if (ready() && !a.busy) { stick = true; exec("assistant.exec", "send", { text }); }
    else { input.value = text; input.focus(); if (!ready()) toast(t("Bsod_Ask_Off"), "fail"); }
  };
  window.addEventListener("assistant:ask", onAsk);
  Promise.all([call("ai.state").then((s) => { ai = s; }).catch(() => {}), call("assistant.state")]).then(([, s]) => render(s)).catch(() => { app.dataset.asst = "none"; });
  // Free memory decides which model is offered, and the card is read a moment after start: both are followed every few seconds while the
  // assistant is off or not yet possible and the window is shown.
  const timer = setInterval(() => { if (!document.hidden && !a?.busy && (a?.status !== "Available" || a?.server === "off" && app.dataset.asst === "open")) call("assistant.state").then(render).catch(() => {}); }, 5000);
  return () => { offA(); offAi(); offNav(); offTests(); clearInterval(timer); window.removeEventListener("assistant:toggle", onShow); window.removeEventListener("assistant:ask", onAsk); };
}
