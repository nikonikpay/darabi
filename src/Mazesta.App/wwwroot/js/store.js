// Live sensor state: the hardware list once, then each poll's readings and running min/avg/max. Pages subscribe; nothing polls on its own.
import { call, on } from "./bridge.js";

export const hw = { nodes: [], sensors: new Map() };
export const readings = new Map();   // id -> { v, q }
export const stats = new Map();      // id -> [min, avg, max]
const subs = new Set();

// The network adapters connected now ({ internet, up: [names] }), the internet's first; asked again by the network page when it opens.
export const net = { internet: null, up: null };
export async function loadNetwork() {
  try { const r = await call("app.network"); net.internet = r?.internet ?? null; net.up = r?.up ?? null; } catch { /* the order of adapters only */ }
  return net;
}
// Network adapters, the internet's first, then the other connected ones, then the rest.
export function netRank(n) { return n.name === net.internet ? 0 : net.up?.includes(n.name) ? 1 : 2; }

export async function loadHardware() {
  hw.nodes = await call("app.hardware");
  await loadNetwork();
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
