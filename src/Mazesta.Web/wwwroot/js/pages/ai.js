// AI: which language models this machine can run, and how fast they really run. Before anything is downloaded, each model's fit is estimated
// from its own figures and this machine's memory (as llmfit does); the user downloads the ones they want, and llama.cpp's own benchmark
// measures them on the GPU or the CPU. Estimates and measurements are labelled apart and never mixed.
import { call, on } from "../bridge.js";
import { t } from "../i18n.js";
import { h, icon, toast } from "../ui.js";

const FIT = { Gpu: ["pass", "Ai_Fit_Gpu"], Split: ["warn", "Ai_Fit_Split"], Cpu: ["warn", "Ai_Fit_Cpu"], TooBig: ["fail", "Ai_Fit_TooBig"] };
// A rough reading of generation speed: people read a chat answer at about 5-10 tokens a second.
const speedWord = (tps) => tps < 5 ? "Ai_Speed_Slow" : tps < 15 ? "Ai_Speed_Usable" : tps < 40 ? "Ai_Speed_Smooth" : "Ai_Speed_Fast";
const lat = (text) => h("span", { class: "lat" }, text);

export function mount(el) {
  const machine = h("dl", { class: "kv" }), runtime = h("div", { class: "ai-runtime" }), models = h("div", { class: "ai-models" }), error = h("p", { class: "banner", hidden: true });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Ai")), h("p", { class: "page-lede" }, t("Ai_Lede")))),
    h("div", { class: "ai-top" },
      h("section", { class: "panel p-ai" }, h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("gpu")), h("h2", { class: "panel-title" }, t("Ai_Machine"))), machine, runtime),
      h("section", { class: "panel ai-help" }, h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("chat")), h("h2", { class: "panel-title" }, t("Ai_HowTo_Title"))),
        h("ul", { class: "upd-points" }, t("Ai_HowTo").split("\n").map((line) => h("li", {}, line))))),
    error, models);

  const exec = (cmd, extra = {}) => call("ai.exec", { cmd, ...extra }).catch((e) => toast(String(e.message || e), "fail"));
  let last = "";

  function renderMachine(s) {
    const m = s.machine, row = (k, v) => h("div", {}, h("dt", {}, t(k)), h("dd", { class: "num" }, v ?? "-"));
    machine.replaceChildren(row("Ai_Gpu", m.gpu ? lat(m.gpu) : t("Ai_NoGpu")), row("Ai_Vram", m.vram), row("Ai_Bandwidth", m.bandwidth), row("Ai_Ram", m.ram), row("Ai_RamFree", m.ramFree));
    const r = s.runtime, x = r.transfer;
    runtime.replaceChildren(...[
      h("div", { class: "ai-rt-line" }, h("span", { class: `pill ${r.ready ? "pass" : "none"}` }, r.ready ? t("Ai_Runtime_Ready", r.build) : t("Ai_Runtime_Missing")),
        h("span", { class: "grow" }),
        !r.ready && !x?.active ? h("button", { class: "btn primary", disabled: !!s.downloading, onclick: () => exec("download", { id: "runtime" }) }, t("Ai_Download", r.size)) : null,
        x?.active ? h("button", { class: "btn stop", onclick: () => exec("cancelDownload", { id: "runtime" }) }, t("Ai_CancelDownload")) : null,
        h("button", { class: "btn quiet", onclick: () => exec("openFolder") }, icon("folder"), t("Ai_OpenFolder"))),
      x?.active ? progress(x) : null, x?.error ? h("p", { class: "ai-err" }, x.error) : null,
      h("p", { class: "caption" }, t("Ai_Runtime_Note"))].filter(Boolean));
  }

  function progress(x) {
    const bar = h("div", { class: "progress" }, h("i")); bar.firstChild.style.setProperty("--p", x.percent / 100);
    return h("div", { class: "ai-prog" }, bar, h("span", { class: "caption lat" }, `${Math.floor(x.percent)}% · ${x.done}${x.speed ? " · " + x.speed : ""}`));
  }

  function measured(r, deviceKey) {
    if (!r) return null;
    return h("div", { class: "ai-result", title: r.detail || "" },
      h("div", { class: "ai-res-head" }, t(deviceKey), h("span", { class: "caption lat" }, r.at)),
      h("div", { class: "ai-res-nums" },
        h("div", { class: "metric" }, h("div", { class: "v" }, r.gen), h("div", { class: "n" }, t("Ai_Gen"))),
        r.prompt ? h("div", { class: "metric" }, h("div", { class: "v" }, r.prompt), h("div", { class: "n" }, t("Ai_Prompt"))) : null,
        h("span", { class: "pill run" }, t(speedWord(r.genRaw)))));
  }

  function card(m, s, i) {
    const [cls, key] = FIT[m.fit.mode], busy = s.busy || !!s.running, run = s.running?.model === m.id ? s.running : null, x = m.transfer;
    const where = m.fit.mode === "Split" ? t("Ai_Fit_SplitShare", m.fit.share) : t(key);
    const actions = h("div", { class: "btn-row" },
      !m.downloaded && !x?.active ? h("button", { class: `btn ${s.recommended === m.id ? "primary" : ""}`, disabled: !!s.downloading, onclick: () => exec("download", { id: m.id }) },
        m.partial ? t("Ai_Resume", m.partial, m.size) : t("Ai_Download", m.size)) : null,
      x?.active ? h("button", { class: "btn stop", onclick: () => exec("cancelDownload", { id: m.id }) }, t("Ai_CancelDownload")) : null,
      m.downloaded && !run ? h("button", { class: "btn go", disabled: busy || !s.runtime.ready || m.fit.mode === "TooBig" || !s.machine.gpu, onclick: () => exec("run", { id: m.id, device: "gpu" }) }, icon("play"), t("Ai_RunGpu")) : null,
      m.downloaded && !run ? h("button", { class: "btn", disabled: busy || !s.runtime.ready || !m.cpuFits, onclick: () => exec("run", { id: m.id, device: "cpu" }) }, icon("cpu"), t("Ai_RunCpu")) : null,
      run ? h("button", { class: "btn stop", onclick: () => exec("cancel") }, icon("stop"), t("Ai_Cancel")) : null,
      h("span", { class: "grow" }),
      m.downloaded || m.partial ? h("button", { class: "btn quiet", disabled: !!run || s.downloading === m.id, onclick: () => { if (confirm(t("Ai_ConfirmDelete", m.name))) exec("delete", { id: m.id }); } }, icon("x"), t("Ai_Delete")) : null);
    const facts = h("dl", { class: "kv" },
      h("div", {}, h("dt", {}, t("Ai_File")), h("dd", { class: "num" }, m.size)),
      h("div", {}, h("dt", {}, t("Ai_Need")), h("dd", { class: "num" }, m.fit.need)),
      m.fit.ceiling ? h("div", {}, h("dt", { title: t("Ai_Ceiling_Hint") }, t("Ai_Ceiling")), h("dd", { class: "num" }, m.fit.ceiling)) : null,
      m.fit.maxContext ? h("div", {}, h("dt", { title: t("Ai_MaxContext_Hint") }, t("Ai_MaxContext")), h("dd", { class: "num" }, lat(m.fit.maxContext.toLocaleString("en-US")))) : null);
    return h("section", { class: `panel ai-model ${s.recommended === m.id ? "suggested" : ""}`, style: { "--i": i } },
      h("header", { class: "panel-head" },
        h("div", { class: "ttl" }, h("h2", { class: "panel-title lat" }, m.name), h("div", { class: "panel-sub fa" }, `${m.tier} · `, lat(`${m.params} · ${m.quant}`), m.moe ? ` · ${t("Ai_Moe")}` : "")),
        s.recommended === m.id ? h("span", { class: "pill run" }, icon("star"), t("Ai_Recommended")) : null),
      h("p", { class: "ai-purpose" }, m.purpose),
      h("div", { class: "ai-fit" }, h("span", { class: `pill ${m.fit.tight && cls === "pass" ? "warn" : cls}` }, where), m.fit.tight ? h("span", { class: "caption" }, t("Ai_Fit_Tight")) : null,
        h("span", { class: "caption" }, t("Ai_Estimate"))),
      facts,
      x?.active ? progress(x) : null, x?.error ? h("p", { class: "ai-err" }, x.error) : null,
      run ? h("div", { class: "ai-prog" }, (() => { const b = h("div", { class: "progress" }, h("i")); b.firstChild.style.setProperty("--p", run.percent / 100); return b; })(),
        h("span", { class: "caption" }, t(run.device === "gpu" ? "Ai_Running_Gpu" : "Ai_Running_Cpu"))) : null,
      measured(m.gpu, "Ai_Measured_Gpu"), measured(m.cpu, "Ai_Measured_Cpu"),
      m.downloaded && !s.runtime.ready ? h("p", { class: "caption" }, t("Ai_NeedRuntime")) : null,
      actions);
  }

  function render(s) {
    const key = JSON.stringify(s);
    if (key === last) return; last = key;
    renderMachine(s);
    error.hidden = !s.error; error.textContent = s.error || "";
    models.replaceChildren(...s.models.map((m, i) => card(m, s, i)));
  }
  const off = on("ai", render);
  call("ai.state").then(render);
  // Free RAM changes while the page is open; the fit follows it every few seconds (nothing runs while the window is hidden).
  const timer = setInterval(() => { if (!document.hidden) call("ai.state").then(render).catch(() => {}); }, 10000);
  return () => { off(); clearInterval(timer); };
}
