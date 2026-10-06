// The page's only line to the machine: call(method, params) returns a promise, on(event, fn) listens to host pushes.
// Outside the app (a plain browser, for design work) there is no host; a clearly labelled demo stands in so the page can be seen.
const host = window.chrome && window.chrome.webview;
export const live = !!host;

const pending = new Map();
const listeners = new Map();
let seq = 0;
let demo = null;

if (host) {
  host.addEventListener("message", (e) => {
    const msg = typeof e.data === "string" ? JSON.parse(e.data) : e.data;
    if (msg.ev) { emit(msg.ev, msg.d); return; }
    const p = pending.get(msg.id);
    if (!p) return;
    pending.delete(msg.id);
    msg.ok ? p.resolve(msg.r) : p.reject(new Error(msg.e || "failed"));
  });
}

function emit(ev, data) {
  for (const fn of listeners.get(ev) || []) {
    try { fn(data); } catch (err) { console.error(ev, err); }
  }
}

export async function call(m, p = {}) {
  if (!host) {
    demo ??= await import("./demo.js");
    return demo.call(m, p, emit);
  }
  const id = ++seq;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    host.postMessage({ id, m, p });
  });
}

export function on(ev, fn) {
  if (!listeners.has(ev)) listeners.set(ev, new Set());
  listeners.get(ev).add(fn);
  return () => listeners.get(ev).delete(fn);
}
