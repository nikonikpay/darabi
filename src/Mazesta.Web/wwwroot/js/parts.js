// The parts of a machine and the hue each one wears everywhere (panels, page planes, chart lines, test groups): one table, so a part never
// changes colour between pages. Hues are CSS tokens (--c-cpu …); the class sets --hue for everything inside it.
import { hw } from "./store.js";

export const PART = {
  Cpu: { cls: "p-cpu", hue: "--c-cpu", icon: "cpu", key: "Nav_Cpu", page: "cpu" },
  Gpu: { cls: "p-gpu", hue: "--c-gpu", icon: "gpu", key: "Nav_Gpu", page: "gpu" },
  Memory: { cls: "p-ram", hue: "--c-ram", icon: "ram", key: "Dashboard_Ram", page: null },
  Storage: { cls: "p-storage", hue: "--c-storage", icon: "drive", key: "Nav_Storage", page: "storage" },
  Network: { cls: "p-net", hue: "--c-net", icon: "net", key: "Nav_Network", page: "network" },
  Motherboard: { cls: "p-board", hue: "--c-board", icon: "board", key: "Web_Kind_Motherboard", page: "system" },
  System: { cls: "p-board", hue: "--c-board", icon: "board", key: "Web_Group_System", page: "system" },
  Windows: { cls: "p-win", hue: "--paper-2", icon: "win", key: "Web_Group_Windows", page: "tools" },
  Power: { cls: "p-power", hue: "--c-power", icon: "bolt", key: "Web_Group_Power", page: null },
};
export const part = (kind) => PART[kind] || PART.System;

// A part's resolved colour, for the canvas (which cannot read var()).
export function hueOf(kind) { return getComputedStyle(document.documentElement).getPropertyValue(part(kind).hue).trim() || "#fdd400"; }

// Tests and benchmarks belong to a part by their id; an id the table does not know goes to System, never nowhere.
const PREFIX = [[/cpu/i, "Cpu"], [/gpu/i, "Gpu"], [/mem|ram/i, "Memory"], [/stor|disk|drive/i, "Storage"], [/net|internet|latency/i, "Network"],
  [/power|psu/i, "Power"], [/win|sfc|dism/i, "Windows"]];
export function partOfId(id) { for (const [re, kind] of PREFIX) if (re.test(id)) return kind; return "System"; }

// A node and everything under it (a board's Super I/O chips hang below it).
export function sensorsUnder(node) {
  const out = [...node.sensors];
  for (const child of hw.nodes.filter((n) => n.parent === node.id)) out.push(...sensorsUnder(child));
  return out;
}
