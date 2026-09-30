// Assistant: a chat with a language model that runs on this computer's graphics card (llama.cpp's llama-server on the loopback). Opt-in: the
// download, the start and every question are the user's. The chat is kept in memory only. In this version the model can only talk.
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

  let a = null, ai = null, stick = true;
  const exec = (m, cmd, extra = {}) => call(m, { cmd, ...extra }).catch((e) => toast(String(e.message || e), "fail"));
  const ready = () => a?.server === "ready";
  const submit = () => { const text = input.value.trim(); if (!text || !ready() || a.busy) return; input.value = ""; stick = true; exec("assistant.exec", "send", { text }); };
  send.onclick = submit; stop.onclick = () => exec("assistant.exec", "cancel"); clear.onclick = () => exec("assistant.exec", "clear");
  input.addEventListener("keydown", (e) => { if (e.key === "Enter" && !e.shiftKey && !e.isComposing) { e.preventDefault(); submit(); } });
  log.addEventListener("scroll", () => { stick = log.scrollHeight - log.scrollTop - log.clientHeight < 40; });

  function progress(x) {
    const b = h("div", { class: "progress" }, h("i")); b.firstChild.style.setProperty("--p", x.percent / 100);
    return h("div", { class: "ai-prog" }, b, h("span", { class: "caption lat" }, `${Math.floor(x.percent)}% · ${x.done}${x.speed ? " · " + x.speed : ""}`));
  }

  function renderGate() {
    const m = a.model, rt = ai?.runtime, tr = rt?.transfer?.active ? rt.transfer : ai?.models.find((x) => x.id === m?.id)?.transfer;
    const downloading = !!tr?.active || (ai?.downloading != null), failed = rt?.transfer?.error || ai?.models.find((x) => x.id === m?.id)?.transfer?.error;
    const installed = a.runtimeReady && m?.downloaded, on_ = a.server !== "off";
    gate.hidden = installed && on_; chat.hidden = !(installed && on_);
    if (gate.hidden) return;
    const kids = [h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("chat")), h("h2", { class: "panel-title" }, installed ? t("Nav_Assistant") : t("Assist_Enable_Title")))];
    if (NO[a.status]) kids.push(h("p", { class: "banner" }, t(NO[a.status])));
    else if (!installed) {
      kids.push(h("p", { class: "ai-purpose" }, t("Assist_Enable_Text", m.name, m.size)));
      kids.push(h("div", { class: "btn-row" }, h("button", { class: "btn primary", disabled: downloading, onclick: () => exec("ai.exec", "assistantEnable") }, downloading ? t("Assist_Downloading") : t("Assist_Enable"))));
      if (tr?.active) kids.push(progress(tr));
      if (failed) kids.push(h("p", { class: "ai-err" }, failed));
    } else {
      kids.push(h("p", { class: "ai-purpose" }, a.server === "starting" ? t("Assist_Starting") : t("Assist_Off")));
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
    bar.replaceChildren(h("span", { class: "pill pass" }, t("Assist_Ready")), h("span", { class: "caption lat" }, a.model?.name ?? ""), h("span", { class: "grow" }),
      h("button", { class: "btn quiet", onclick: () => exec("assistant.exec", "stop") }, icon("stop"), t("Assist_Stop")));
    const msgs = a.messages.map((m) => h("div", { class: `as-msg ${m.role}` }, h("div", { class: "as-who" }, t(m.role === "user" ? "Assist_You" : "Assist_Name")),
      // dir="auto": a Persian question and an English answer each take their own direction.
      h("div", { class: "as-text", dir: "auto" }, m.text || (a.busy ? "…" : ""))));
    log.replaceChildren(...(msgs.length ? msgs : [h("p", { class: "caption as-empty" }, t("Assist_Empty"))]));
    if (stick) log.scrollTop = log.scrollHeight;
    send.disabled = a.busy; stop.hidden = !a.busy; clear.disabled = a.busy || !a.messages.length; input.disabled = false;
    if (a.error) log.append(h("p", { class: "ai-err" }, a.error));
  }

  const render = (s) => { a = s; renderGate(); renderChat(); };
  const offA = on("assistant", render), offAi = on("ai", (s) => { ai = s; if (a) { renderGate(); } });
  Promise.all([call("ai.state").then((s) => { ai = s; }), call("assistant.state")]).then(([, s]) => render(s));
  // Free memory decides which model is offered; it follows it every few seconds while the page is open and the window is shown.
  const timer = setInterval(() => { if (!document.hidden && !a?.busy && a?.server === "off") call("assistant.state").then(render).catch(() => {}); }, 10000);
  return () => { offA(); offAi(); clearInterval(timer); };
}
