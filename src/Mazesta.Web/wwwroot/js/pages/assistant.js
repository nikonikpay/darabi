// Assistant: a chat with a language model that runs on this computer's graphics card (llama.cpp's llama-server on the loopback). Opt-in: the
// download, the start and every question are the user's. The chat is kept in memory only. The model reads the machine through tools and may start a test or a benchmark, but only after the user
// agreed on the confirm card below the chat.
// Downloads are the AI page's (same bridge, same "ai" event); the chat is this page's ("assistant" event).
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon, toast } from "../ui.js";

const NO = { NoGpu: "Assist_NoGpu", LittleVram: "Assist_LittleVram", NoRoom: "Assist_NoRoom" };

export function mount(el) {
  const gate = h("section", { class: "panel as-gate" }), log = h("div", { class: "as-log", role: "log", "aria-live": "polite" });
  const input = h("textarea", { class: "as-input", rows: 2, dir: "auto", placeholder: t("Assist_Placeholder") });
  const send = h("button", { class: "btn primary" }, icon("play"), t("Assist_Send")), stop = h("button", { class: "btn stop", hidden: true }, icon("stop"), t("Assist_StopReply"));
  const clear = h("button", { class: "btn quiet" }, t("Assist_Clear")), bar = h("div", { class: "as-bar" });
  const chat = h("section", { class: "panel as-chat" }, h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("chat")), h("h2", { class: "panel-title" }, t("Nav_Assistant")), bar),
    log, h("div", { class: "as-compose" }, input, h("div", { class: "btn-row" }, send, stop, h("span", { class: "grow" }), clear)), h("p", { class: "caption" }, t("Assist_Disclaimer")));
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Assistant")), h("p", { class: "page-lede" }, t("Assist_Lede")))), gate, chat);

  const confirmBox = h("div", { class: "as-confirm", role: "alertdialog", hidden: true }), activityBox = h("div", { class: "as-activity", hidden: true });
  chat.insertBefore(confirmBox, chat.querySelector(".as-compose")); chat.insertBefore(activityBox, confirmBox);

  let a = null, ai = null, stick = true, msgShape = "", msgEls = [];

  // The downloaded models this computer can run, to talk with; changing it stops the assistant, the next start uses the new one.
  function picker() {
    if (!a.choices?.length) return null;
    const sel = h("select", { class: "as-model lat", "aria-label": t("Assist_Model"), disabled: a.busy || a.server === "starting" },
      a.choices.map((c) => h("option", { value: c.id, selected: c.id === a.model?.id }, `${c.name} · ${c.size}`)));
    sel.onchange = () => exec("assistant.exec", "select", { id: sel.value });
    return h("label", { class: "as-pick" }, h("span", { class: "caption" }, t("Assist_Model")), sel);
  }
  const exec = (m, cmd, extra = {}) => call(m, { cmd, ...extra }).catch((e) => toast(String(e.message || e), "fail"));
  const ready = () => a?.server === "ready";
  const submit = () => { const text = input.value.trim(); if (!text || !ready() || a.busy) return; input.value = ""; stick = true; exec("assistant.exec", "send", { text }); };
  send.onclick = submit; stop.onclick = () => exec("assistant.exec", "cancel"); clear.onclick = () => exec("assistant.exec", "clear");
  input.addEventListener("keydown", (e) => { if (e.key === "Enter" && !e.shiftKey && !e.isComposing) { e.preventDefault(); submit(); } });
  log.addEventListener("scroll", () => { stick = log.scrollHeight - log.scrollTop - log.clientHeight < 40; });

  // Updated in place: a download's ticks change the bar and its text, not the card around it.
  function progress(x) {
    const fill = h("i"), text = h("span", { class: "caption lat" }), el = h("div", { class: "ai-prog" }, h("div", { class: "progress" }, fill), text);
    el.upd = (x) => { fill.style.setProperty("--p", x.percent / 100); text.textContent = `${Math.floor(x.percent)}% · ${x.done}${x.speed ? " · " + x.speed : ""}`; };
    el.upd(x); return el;
  }

  let gateKey = "", gateProg = null;
  function renderGate() {
    const m = a.model, rt = ai?.runtime, tr = rt?.transfer?.active ? rt.transfer : ai?.models.find((x) => x.id === m?.id)?.transfer;
    const downloading = !!tr?.active || (ai?.downloading != null), failed = rt?.transfer?.error || ai?.models.find((x) => x.id === m?.id)?.transfer?.error;
    const installed = a.runtimeReady && m?.downloaded, on_ = a.server !== "off";
    const key = JSON.stringify([a.status, m && { id: m.id, d: m.downloaded }, a.choices, a.runtimeReady, a.server, a.error, a.blocked, downloading, !!tr?.active, failed, a.model?.name]);
    if (key === gateKey) { if (tr?.active) gateProg?.upd(tr); gate.hidden = installed && on_; chat.hidden = !(installed && on_); return; }
    gateKey = key;
    gate.hidden = installed && on_; chat.hidden = !(installed && on_);
    if (gate.hidden) return;
    const kids = [h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("chat")), h("h2", { class: "panel-title" }, installed ? t("Nav_Assistant") : t("Assist_Enable_Title")))];
    if (NO[a.status]) kids.push(h("p", { class: "banner" }, t(NO[a.status])));
    else if (!installed) {
      kids.push(h("p", { class: "ai-purpose" }, t("Assist_Enable_Text", m.name, m.size)));
      kids.push(h("div", { class: "btn-row" }, h("button", { class: "btn primary", disabled: downloading, onclick: () => exec("ai.exec", "assistantEnable") }, downloading ? t("Assist_Downloading") : t("Assist_Enable"))));
      gateProg = tr?.active ? progress(tr) : null; if (gateProg) kids.push(gateProg);
      if (failed) kids.push(h("p", { class: "ai-err" }, failed));
    } else {
      kids.push(h("p", { class: "ai-purpose" }, a.server === "starting" ? t("Assist_Starting") : t("Assist_Off")));
      kids.push(picker());
      kids.push(h("div", { class: "btn-row" }, a.server === "starting"
        ? h("button", { class: "btn stop", onclick: () => exec("assistant.exec", "stop") }, t("Assist_Stop"))
        : h("button", { class: "btn primary", disabled: a.blocked, onclick: () => exec("assistant.exec", "start") }, icon("play"), t("Assist_Start"))));
      if (a.blocked) kids.push(h("p", { class: "caption" }, t("Assist_Blocked")));
    }
    if (a.error) kids.push(h("p", { class: "ai-err" }, a.error));
    gate.replaceChildren(...kids);
  }

  function renderChat() {
    if (chat.hidden) return;
    const barKey = JSON.stringify([a.model?.id, a.choices, a.busy]);
    if (barKey !== bar.key) bar.key = barKey, bar.replaceChildren(h("span", { class: "pill pass" }, t("Assist_Ready")), picker() ?? h("span", { class: "caption lat" }, a.model?.name ?? ""), h("span", { class: "grow" }),
      h("button", { class: "btn quiet", onclick: () => exec("assistant.exec", "stop") }, icon("stop"), t("Assist_Stop")));
    renderConfirm(); renderActivity();
    const shape = a.messages.map((m) => m.role + (m.tools?.length ?? 0)).join();
    if (shape === msgShape && msgEls.length) a.messages.forEach((m, i) => { const txt = m.text || (a.busy ? "…" : ""); if (msgEls[i].textContent !== txt) msgEls[i].textContent = txt; });
    else {
      msgShape = shape;
      msgEls = a.messages.map((m) => h("div", { class: "as-text", dir: "auto" }, m.text || (a.busy ? "…" : "")));   // dir="auto": each message takes its own direction
      log.replaceChildren(...(a.messages.length ? a.messages.map((m, i) => h("div", { class: `as-msg ${m.role}` }, h("div", { class: "as-who" }, t(m.role === "user" ? "Assist_You" : "Assist_Name")), msgEls[i],
        m.tools?.length ? h("div", { class: "as-tools" }, m.tools.map((x) => h("span", { class: `pill ${x.ok ? "none" : "fail"}` }, t(`Assist_Tool_${x.name}`) + (x.ok ? "" : " · " + t("Assist_Tool_failed"))))) : null))
        : [h("p", { class: "caption as-empty" }, t("Assist_Empty"))]));
    }
    if (stick) log.scrollTop = log.scrollHeight;
    send.disabled = a.busy; stop.hidden = !a.busy; clear.disabled = a.busy || !a.messages.length; input.disabled = false;
    log.querySelector(".ai-err")?.remove(); if (a.error) log.append(h("p", { class: "ai-err" }, a.error));
  }

  // The tool asked to start a test or a benchmark: nothing starts until "Start" here. Rebuilt only when the question changes.
  let confirmKey = "";
  function renderConfirm() {
    const c = a.confirm, key = JSON.stringify(c);
    confirmBox.hidden = !c; if (key === confirmKey) return; confirmKey = key; if (!c) return confirmBox.replaceChildren();
    confirmBox.replaceChildren(h("h3", { class: "as-confirm-title" }, t(c.kind === "tests" ? "Assist_Confirm_Tests" : "Assist_Confirm_Benchmark")),
      h("ul", { class: "as-confirm-list" }, c.items.map((i) => h("li", {}, h("span", {}, i.name), h("span", { class: "caption" }, t("Assist_Seconds", i.duration))))),
      h("p", { class: "caption" }, t("Assist_Confirm_Text")),
      h("div", { class: "btn-row" }, h("button", { class: "btn primary", onclick: () => exec("assistant.exec", "confirm", { value: true }) }, icon("play"), t("Assist_Confirm_Yes")),
        h("button", { class: "btn quiet", onclick: () => exec("assistant.exec", "confirm", { value: false }) }, t("Assist_Confirm_No"))));
  }
  // What the assistant started, with its progress; the stop button on the reply cancels it too.
  let actFill = null, actText = null, actKind = "";
  function renderActivity() {
    const x = a.activity; activityBox.hidden = !x; if (!x) { actKind = ""; return; }
    if (actKind !== x.kind) {
      actKind = x.kind; actFill = h("i"); actText = h("span", { class: "caption" });
      activityBox.replaceChildren(h("span", { class: "pill run" }, t(x.kind === "tests" ? "Assist_Running_Tests" : "Assist_Running_Benchmark")), actText, h("div", { class: "progress" }, actFill));
    }
    actText.textContent = x.name ? `${x.name}${x.percent != null ? " · " + Math.floor(x.percent) + "%" : ""}` : "";
    actFill.style.setProperty("--p", (x.percent ?? 0) / 100);
  }

  const render = (s) => { a = s; renderGate(); renderChat(); };
  const offA = on("assistant", render), offAi = on("ai", (s) => { ai = s; if (a) { renderGate(); } });
  Promise.all([call("ai.state").then((s) => { ai = s; }), call("assistant.state")]).then(([, s]) => render(s));
  // Free memory decides which model is offered; it follows it every few seconds while the page is open and the window is shown.
  const timer = setInterval(() => { if (!document.hidden && !a?.busy && a?.server === "off") call("assistant.state").then(render).catch(() => {}); }, 10000);
  return () => { offA(); offAi(); clearInterval(timer); };
}
