// Drivers, in three sections: the graphics card's driver against its maker's newest (NVIDIA asked directly, Game Ready and Studio side by side,
// the line the installed programs suit marked, the user choosing), the drivers Windows Update offers this computer (ticked, then installed one by
// one), and the devices Windows has no working driver for. Nothing is downloaded or installed but by a button and a confirmation.
import { call, on } from "../bridge.js";
import { t, fa, has } from "../i18n.js";
import { h, icon, toast } from "../ui.js";
import { box } from "../groups.js";

export function mount(el) {
  const gpuBody = h("div", { class: "drv-gpu" }), wuBody = h("div", { class: "drv-wu" }), probBody = h("div", {});
  const checkBtn = h("button", { class: "btn", onclick: () => run("drivers.check") }, icon("refresh"), t("Drivers_Check"));
  const checked = h("span", { class: "caption" });
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Drivers")), h("p", { class: "page-lede" }, t("Drivers_Lede")))),
    h("div", { class: "panels flow", style: { marginTop: 0 } },
      box({ kind: "Gpu", title: t("Drivers_Gpu_Title"), sub: t("Drivers_Gpu_Sub"), actions: [checked, checkBtn], body: gpuBody, wide: true, i: 0 }),
      box({ kind: "Motherboard", ico: "update", title: t("Drivers_Wu_Title"), sub: t("Drivers_Wu_Sub"), body: wuBody, wide: true, i: 1 }),
      box({ kind: "Storage", ico: "alert", title: t("Drivers_Problems_Title"), sub: t("Drivers_Problems_Sub"), body: probBody, i: 2 })));

  let s = null, line = null, clean = false;
  const picked = new Set();

  async function run(method, p) {
    try { render(await call(method, p)); } catch (e) { toast(String(e.message || e), "fail"); }
  }

  function release(r, kind, newer, suggested, installedLine) {
    if (!r) return null;
    const id = kind === "studio" ? "Studio" : "GameReady", label = kind === "pro" ? "Pro" : id;
    const input = h("input", { type: "radio", name: "nvline", value: id, checked: line === id, onchange: () => { line = id; render(s); } });
    return h("label", { class: `drv-line ${line === id ? "on" : ""}` }, input,
      h("b", {}, t(`Drivers_Nv_${label}`), suggested === id ? h("span", { class: "pill pass" }, t("Drivers_Suggested")) : null, installedLine === id ? h("span", { class: "pill none" }, t("Drivers_InstalledLine")) : null),
      h("span", { class: "lat" }, `${r.version} · ${r.date ?? ""} · ${r.size ?? ""}`),
      h("small", {}, t(`Drivers_Nv_${label}_What`)),
      newer === false ? h("small", { class: "drv-ok" }, t("Drivers_UpToDate")) : null);
  }

  function renderGpu() {
    const kids = [];
    if (!s.gpus.length) kids.push(h("p", { class: "note" }, s.checking ? t("Drivers_Checking") : t("Drivers_NotChecked")));
    for (const g of s.gpus) {
      kids.push(h("dl", { class: "kv" },
        h("dt", {}, t("Drivers_Card")), h("dd", { class: "lat" }, g.name),
        h("dt", {}, t("Drivers_Installed")), h("dd", {}, h("span", { class: "lat" }, g.version ?? "—"), g.date ? h("span", { class: "caption" }, " · ", t("Drivers_Dated", g.date, fa(g.ageDays))) : null)));
      if (g.vendor === "amd" || g.vendor === "intel")
        kids.push(h("p", { class: "note" }, t(`Drivers_${g.vendor}_Note`)), h("div", { class: "btn-row" }, h("button", { class: "btn", onclick: () => call("drivers.open", { what: g.vendor }) }, icon("popout"), t(`Drivers_${g.vendor}_Open`))));
    }
    const nv = s.nvidia;
    if (nv?.error) kids.push(h("p", { class: "msg fail" }, nv.error), h("div", { class: "btn-row" }, h("button", { class: "btn", onclick: () => call("drivers.open", { what: "nvidia" }) }, icon("popout"), t("Drivers_nvidia_Open"))));
    if (nv && !nv.error) {
      const sug = s.advice?.suggested ?? null;
      if (line == null) line = nv.geforce ? (sug ?? null) : "GameReady";
      if (nv.geforce) {
        const why = s.advice ? (s.advice.creative.length && s.advice.games.length ? t("Drivers_Advice_Both", s.advice.creative.slice(0, 4).join("، "), s.advice.games.slice(0, 4).join("، "))
          : s.advice.creative.length ? t("Drivers_Advice_Studio", s.advice.creative.slice(0, 5).join("، "))
          : s.advice.games.length ? t("Drivers_Advice_Games", s.advice.games.slice(0, 5).join("، ")) : t("Drivers_Advice_None")) : "";
        kids.push(h("p", { class: "drv-advice" }, icon("star"), why));
        kids.push(h("div", { class: "drv-lines", role: "radiogroup", "aria-label": t("Drivers_Choose") },
          release(nv.gameReady, "gameReady", nv.newerGameReady, sug, nv.installedLine), release(nv.studio, "studio", nv.newerStudio, sug, nv.installedLine)));
      } else kids.push(h("div", { class: "drv-lines" }, release(nv.gameReady, "pro", nv.newerGameReady, null, null)));
      const job = s.job, busy = ["downloading", "verifying", "installing"].includes(job.state);
      const chosen = line === "Studio" ? nv.studio : nv.gameReady;
      const cleanBox = h("input", { type: "checkbox", class: "check", checked: clean, onchange: (e) => { clean = e.target.checked; } });
      const install = h("button", { class: "btn primary", disabled: busy || !line || !chosen || !!s.busy, onclick: () => {
        if (confirm(t("Drivers_Nv_Confirm", t(`Drivers_Nv_${line}`), chosen.version, chosen.size ?? ""))) run("drivers.nvInstall", { line, clean });
      } }, icon("update"), line ? t("Drivers_Nv_Install", t(`Drivers_Nv_${line}`)) : t("Drivers_Choose"));
      kids.push(h("div", { class: "btn-row" }, install, h("label", { class: "drv-clean" }, cleanBox, t("Drivers_Nv_Clean")),
        job.state === "downloading" ? h("button", { class: "btn stop", onclick: () => run("drivers.cancel") }, icon("stop"), t("Test_Cancel")) : null));
      if (job.state !== "idle") {
        const text = { downloading: t("Drivers_Job_Downloading", job.version, fa(Math.round(job.progress * 100))), verifying: t("Drivers_Job_Verifying"),
          installing: t("Drivers_Job_Installing", job.version), done: t("Drivers_Job_Done", job.version), failed: job.error || "" }[job.state];
        kids.push(h("p", { class: `msg ${job.state === "failed" ? "fail" : job.state === "done" ? "ok" : ""}` }, text));
        if (job.state === "downloading") { const bar = h("div", { class: "progress" }, h("i")); bar.firstChild.style.setProperty("--p", job.progress); kids.push(bar); }
      }
      if (s.busy && !busy) kids.push(h("p", { class: "note" }, t(`Workload_Busy_${s.busy}`)));
      kids.push(h("p", { class: "note" }, t("Drivers_Nv_Note")));
    }
    gpuBody.replaceChildren(...kids);
  }

  function renderWu() {
    const w = s.wu, busy = w.state === "searching" || w.state === "installing";
    const kids = [];
    const search = h("button", { class: "btn", disabled: busy, onclick: () => run("drivers.wuSearch") }, icon("refresh"), t(w.state === "idle" ? "Drivers_Wu_Search" : "Drivers_Wu_SearchAgain"));
    if (w.state === "searching") kids.push(h("p", { class: "msg" }, t("Drivers_Wu_Searching")));
    if (w.state === "failed") kids.push(h("p", { class: "msg fail" }, w.error));
    if (w.state === "installing" && w.step) kids.push(h("p", { class: "msg" }, t("Drivers_Wu_Installing", fa(w.step.index + 1), fa(w.step.count), w.step.title)));
    if (w.searchedAt && !busy) kids.push(h("p", { class: "caption" }, w.updates.length ? t("Drivers_Wu_Found", fa(w.updates.length), w.searchedAt) : t("Drivers_Wu_None", w.searchedAt)));
    for (const id of [...picked]) if (!w.updates.some((u) => u.id === id)) picked.delete(id);
    if (w.updates.length) kids.push(h("ul", { class: "drv-list" }, w.updates.map((u) => h("li", {},
      h("label", {}, h("input", { type: "checkbox", class: "check", disabled: busy || u.licence, checked: picked.has(u.id), onchange: (e) => { e.target.checked ? picked.add(u.id) : picked.delete(u.id); render(s); } }),
        h("span", { class: "lat" }, u.title)),
      h("span", { class: "caption" }, [u.cls, u.date, u.mb != null ? `${fa(u.mb)} MB` : null].filter(Boolean).join(" · ")),
      u.licence ? h("span", { class: "caption" }, t("Drivers_Wu_Licence")) : null))));
    const done = w.results || [];
    if (done.length) kids.push(h("ul", { class: "drv-list" }, done.map((r) => h("li", {}, h("span", { class: `pill ${r.ok ? "pass" : "fail"}` }, t(r.ok ? "Drivers_Wu_Ok" : "Drivers_Wu_Bad")),
      h("span", { class: "lat" }, r.title), r.error ? h("span", { class: "caption lat" }, r.error === "licence" ? t("Drivers_Wu_Licence") : r.error) : null))));
    if (done.some((r) => r.reboot)) kids.push(h("p", { class: "banner" }, t("Drivers_Reboot")));
    const install = h("button", { class: "btn primary", disabled: busy || picked.size === 0 || !!s.busy, onclick: () => {
      if (confirm(t("Drivers_Wu_Confirm", fa(picked.size)))) run("drivers.wuInstall", { ids: [...picked] });
    } }, icon("update"), t("Drivers_Wu_Install", fa(picked.size)));
    kids.push(h("div", { class: "btn-row" }, search, w.updates.length ? install : null, h("button", { class: "btn quiet", onclick: () => call("drivers.open", { what: "wu" }) }, icon("popout"), t("Drivers_Wu_Open"))));
    kids.push(h("p", { class: "note" }, t("Drivers_Wu_Note")));
    wuBody.replaceChildren(...kids);
  }

  function renderProblems() {
    const p = s.problems || [];
    probBody.replaceChildren(
      !s.checkedAt ? h("p", { class: "note" }, t("Drivers_NotChecked")) : p.length === 0 ? h("p", { class: "msg ok" }, t("Drivers_Problems_None"))
        : h("ul", { class: "drv-list" }, p.map((d) => h("li", {}, h("span", { class: "lat" }, d.name), h("span", { class: "caption" },
          has(`Drivers_Code_${d.code}`) ? t(`Drivers_Code_${d.code}`) : t("Drivers_Code_Other", d.code), d.cls ? ` · ${d.cls}` : "")))),
      h("div", { class: "btn-row" }, h("button", { class: "btn quiet", onclick: () => call("drivers.open", { what: "devmgr" }) }, icon("popout"), t("Win_devmgr"))));
  }

  function render(state) {
    if (!state) return;
    s = state;
    checkBtn.disabled = s.checking; checked.textContent = s.checkedAt ? t("Drivers_CheckedAt", s.checkedAt) : "";
    renderGpu(); renderWu(); renderProblems();
  }
  call("drivers.state").then((x) => { render(x); if (!x.checkedAt && !x.checking) run("drivers.check"); });
  return on("drivers", render);
}
