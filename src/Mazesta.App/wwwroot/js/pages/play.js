// Gaming, on a page of its own: Windows' power plan and game switches, and the game mode that stops background services while a game runs.
import { t } from "../i18n.js";
import { h } from "../ui.js";
import { gamingBoxes, gameBoostBox } from "./gaming.js";

export function mount(el) {
  const gaming = gamingBoxes(0);
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Gaming")), h("p", { class: "page-lede" }, t("Play_Lede")))),
    h("div", { class: "panels flow" }, ...gaming.boxes, gameBoostBox(2)));
  return gaming.off;
}
