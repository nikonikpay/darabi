// Sensors of one kind in families, as HWiNFO shows them: "Core 0 T0 Effective Clock" … "Core 15 T1 Effective Clock" are one family under one
// head, apart from "Core 0 Clock" … (a different family, though both are clocks). A family is the sensors whose names are the same once their
// numbers are taken out; three or more make a group. Its head is the device's own summary of them when it has one ("Average Effective Clock",
// "Core Max", "GPU Memory Total") - a real reading, never one worked out here - or just the family's name.
const NUM = /#?\d+/g;
const numbered = (name) => /\d/.test(name);
// Words that say "of all of them" or "which one", left out when a summary is matched to its family.
const GENERIC = new Set(["average", "avg", "max", "maximum", "min", "minimum", "total", "all", "core", "thread", "of"]);
const SUMMARY = /\b(average|avg|max|maximum|total)\b/i;
const keyOf = (name) => name.replace(NUM, "#").replace(/\s+/g, " ").trim();
// A Ryzen's cores sharing a level-3 cache are one CCD: each CCD's cores are a family of their own ("CCD 1 · Core #"), without the whole-processor summary.
const famKey = (s) => (s.ccd ? `CCD ${s.ccd} · ` : "") + keyOf(s.name);
const gist = (name) => name.toLowerCase().replace(NUM, " ").replace(/[()#:,_/-]/g, " ").split(/\s+/)
  .map((w) => w.replace(/s$/, "")).filter((w) => w.length > 1 && !GENERIC.has(w)).join(" ");

// sensors: one kind's sensors in their order. Returns items in that order: { sensor } for a single row, or { family, head, members } for a group
// (head: the summary sensor or null). A group takes the place of its head or its first member, whichever comes first.
export function families(sensors) {
  const byKey = new Map();
  for (const s of sensors) if (numbered(s.name)) { const k = famKey(s); if (!byKey.has(k)) byKey.set(k, []); byKey.get(k).push(s); }
  const keyOf2 = new Map(), headOf = new Map();
  for (const [k, members] of byKey) {
    if (members.length < 3) continue;
    for (const s of members) keyOf2.set(s, k);
    const want = members[0].ccd ? null : gist(k.replace(/^CCD \d+ · /, ""));
    // A family named only by generic words ("Core #1") takes a summary that says it is one ("Core Max").
    const head = want === null ? undefined : sensors.find((s) => !numbered(s.name) && !keyOf2.has(s) && gist(s.name) === want && (want || SUMMARY.test(s.name)));
    if (head) { keyOf2.set(head, k); headOf.set(k, head); }
  }
  const out = [], placed = new Set();
  for (const s of sensors) {
    const k = keyOf2.get(s);
    if (!k) { out.push({ sensor: s }); continue; }
    if (placed.has(k)) continue;
    placed.add(k);
    out.push({ family: k, head: headOf.get(k) ?? null, members: byKey.get(k) });
  }
  return out;
}
