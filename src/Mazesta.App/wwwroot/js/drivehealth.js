// The drive health box on the storage page, as Hard Disk Sentinel and CrystalDiskInfo show a drive: its health on a bar, its temperature in the colour
// of its level, what is wrong in plain words (with the backup warning when data is at risk), and every SMART / NVMe parameter. A faulty parameter's
// row takes the state colour and opens to what it means. Everything is the drive's own report, read and judged by the app (DriveReport); nothing guessed.
import { call } from "./bridge.js";
import { t, fa } from "./i18n.js";
import { h, icon, val } from "./ui.js";

const STATE = { Good: "pass", Watch: "warn", Fail: "fail" };

export function driveHealthPanel() {
  const body = h("div", { class: "dh-list" }, h("p", { class: "caption" }, t("Web_Drive_Loading")));
  const el = h("section", { class: "panel dh-panel", "data-a": "drivehealth" },
    h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon("drive")), h("div", { class: "ttl" }, h("h2", { class: "panel-title" }, t("Web_Drive_Title")))), body);
  call("drives.health").then((r) => {
    body.replaceChildren(...(r.drives.length ? r.drives.map(drive) : [h("p", { class: "chk-text" }, t("Web_Drive_None"))]));
  }).catch((e) => body.replaceChildren(h("p", { class: "ai-err" }, t("Web_Drive_Error", String(e.message || e)))));
  return el;
}

function drive(d) {
  const health = d.health === null || d.health === undefined ? null : d.health;
  const hl = health === null ? null : health < 60 ? "Fail" : health < 80 ? "Watch" : "Good";
  const flagged = d.params.filter((p) => p.level !== "Good");
  return h("article", { class: `dh-drive lv-${d.level}` },
    h("div", { class: "dh-head" }, h("span", { class: "dh-name lat" }, d.name), d.letters ? h("span", { class: "caption lat" }, d.letters) : null,
      h("span", { class: `pill ${STATE[d.level]}` }, d.verdict)),
    h("div", { class: "dh-read" },
      h("div", { class: `dh-cell lv-${hl || "none"}` }, h("span", { class: "k" }, t("Web_Drive_Health")),
        health === null ? h("span", { class: "hatch" }, t("Web_Drive_NoHealth"))
          : [h("span", { class: "dh-big num" }, `${health}%`), h("div", { class: "bar", role: "meter", "aria-valuenow": health, "aria-valuemin": 0, "aria-valuemax": 100, "aria-label": t("Web_Drive_Health") }, h("i", { style: { "--p": health / 100 } }))]),
      h("div", { class: `dh-cell lv-${d.tempLevel || "none"}` }, h("span", { class: "k" }, t("Web_Drive_Temp")),
        d.temp === null ? val(null) : h("span", { class: "dh-big num" }, `${d.temp} °C`),
        h("span", { class: "caption" }, t("Web_Drive_TempLimits", fa(d.watchC), fa(d.failC))))),
    d.issues.length
      ? h("div", { class: `dh-alert ${STATE[d.level]}`, role: d.level === "Fail" ? "alert" : null },
        h("ul", {}, d.issues.map((i) => h("li", { class: STATE[i.level] }, i.text))),
        d.backup ? h("p", { class: "dh-backup" }, icon("alert"), t("Web_Drive_Backup")) : null)
      : h("p", { class: "chk-text dh-fine" }, t("Web_Drive_Fine")),
    d.params.length ? params(d, flagged) : null);
}

// The table: ATA attributes show the drive's normalized current / worst / threshold and the raw value; NVMe log fields show one value.
// A flagged row is a button that opens its explanation underneath (keyboard and screen readers get aria-expanded).
function params(d, flagged) {
  const head = d.ata ? ["ID", t("Web_Drive_Col_Name"), t("Web_Drive_Col_Cur"), t("Web_Drive_Col_Worst"), t("Web_Drive_Col_Thr"), t("Web_Drive_Col_Raw")]
    : [t("Web_Drive_Col_Name"), t("Web_Drive_Col_Value")];
  const rows = d.params.flatMap((p, k) => {
    const cells = d.ata ? [h("td", { class: "lat" }, p.id), name(p, k), num(p.cur), num(p.worst), num(p.thr), h("td", { class: "lat" }, p.value)]
      : [name(p, k), h("td", { class: "lat" }, p.value)];
    const tr = h("tr", { class: p.level !== "Good" ? `flag ${STATE[p.level]}` : "" }, cells);
    return p.why ? [tr, h("tr", { class: `why ${STATE[p.level]}`, id: `dhw-${d.name}-${k}`, hidden: true }, h("td", { colspan: head.length }, p.why))] : [tr];
  });
  return h("details", { class: "dh-params", open: flagged.length ? "" : null },
    h("summary", {}, t("Web_Drive_Params"), h("span", { class: "lat caption" }, `(${d.params.length})`),
      flagged.length ? h("span", { class: `pill ${flagged.some((p) => p.level === "Fail") ? "fail" : "warn"}` }, t("Web_Drive_Flagged", fa(flagged.length))) : null),
    h("div", { class: "spec-table-wrap" }, h("table", { class: "table dh-table" }, h("thead", {}, h("tr", {}, head.map((x) => h("th", {}, x)))), h("tbody", {}, rows))));

  function name(p, k) {
    if (!p.why) return h("td", {}, p.name);
    const id = `dhw-${d.name}-${k}`;
    const b = h("button", { type: "button", class: "dh-why", "aria-expanded": "false", "aria-controls": id }, icon("chevron"), p.name);
    b.onclick = () => { const row = b.closest("tbody").querySelector(`[id="${CSS.escape(id)}"]`), open = row.hidden; row.hidden = !open; b.setAttribute("aria-expanded", String(open)); };
    return h("td", {}, b);
  }
}

const num = (v) => h("td", { class: "lat" }, v === null || v === undefined ? "—" : String(v));
