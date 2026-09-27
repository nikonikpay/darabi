// Live sensor state: the hardware list once, then each poll's readings and running min/avg/max. Pages subscribe; nothing polls on its own.
import { call, on } from "./bridge.js";

export const hw = { nodes: [], sensors: new Map() };
export const readings = new Map();   // id -> { v, q }
export const stats = new Map();      // id -> [min, avg, max]
const subs = new Set();

export async function loadHardware() {
  hw.nodes = await call("app.hardware");
  hw.sensors.clear();
  for (const n of hw.nodes) for (const s of n.sensors) hw.sensors.set(s.id, { ...s, node: n });
}

on("snapshot", (d) => {
  for (const [id, v, q] of d.r) readings.set(id, { v, q });
  for (const [id, mn, av, mx] of d.s) stats.set(id, [mn, av, mx]);
  for (const fn of subs) fn(d.t);
});

export function subscribe(fn) { subs.add(fn); return () => subs.delete(fn); }

export function value(id) { const r = id && readings.get(id); return r && r.q === "Ok" ? r.v : null; }
export function quality(id) { return readings.get(id)?.q ?? "Missing"; }

export function topNodes(kind) { return hw.nodes.filter((n) => !n.parent && (!kind || n.kind === kind)); }

// The first sensor of the node with the first role that exists, as the WPF dashboard picks it.
export function pick(node, ...roles) {
  if (!node) return null;
  for (const r of roles) { const s = node.sensors.find((x) => x.role === r); if (s) return s; }
  return null;
}
