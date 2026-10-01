// Hands-on checks: what only a person can judge - a dead pixel, a key that does not register, a silent speaker, a dead microphone, a mouse
// button that double-clicks. The page gives each part a way to be exercised and shows exactly what the computer received; the technician
// decides. Nothing here is a measurement the app could make on its own, so the page draws no verdict itself.
import { t } from "../i18n.js";
import { h, icon } from "../ui.js";

// A compact full-size layout, by KeyboardEvent.code (the physical key, whatever the keyboard language).
const KEYS = [
  ["Escape", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12", "PrintScreen", "ScrollLock", "Pause"],
  ["Backquote", "Digit1", "Digit2", "Digit3", "Digit4", "Digit5", "Digit6", "Digit7", "Digit8", "Digit9", "Digit0", "Minus", "Equal", "Backspace", "Insert", "Home", "PageUp", "NumLock", "NumpadDivide", "NumpadMultiply", "NumpadSubtract"],
  ["Tab", "KeyQ", "KeyW", "KeyE", "KeyR", "KeyT", "KeyY", "KeyU", "KeyI", "KeyO", "KeyP", "BracketLeft", "BracketRight", "Backslash", "Delete", "End", "PageDown", "Numpad7", "Numpad8", "Numpad9", "NumpadAdd"],
  ["CapsLock", "KeyA", "KeyS", "KeyD", "KeyF", "KeyG", "KeyH", "KeyJ", "KeyK", "KeyL", "Semicolon", "Quote", "Enter", "Numpad4", "Numpad5", "Numpad6"],
  ["ShiftLeft", "KeyZ", "KeyX", "KeyC", "KeyV", "KeyB", "KeyN", "KeyM", "Comma", "Period", "Slash", "ShiftRight", "ArrowUp", "Numpad1", "Numpad2", "Numpad3", "NumpadEnter"],
  ["ControlLeft", "MetaLeft", "AltLeft", "Space", "AltRight", "ContextMenu", "ControlRight", "ArrowLeft", "ArrowDown", "ArrowRight", "Numpad0", "NumpadDecimal"],
];
const LABEL = { Escape: "Esc", Backquote: "`", Minus: "-", Equal: "=", Backspace: "⌫", BracketLeft: "[", BracketRight: "]", Backslash: "\\", Semicolon: ";", Quote: "'",
  Comma: ",", Period: ".", Slash: "/", CapsLock: "Caps", ShiftLeft: "Shift", ShiftRight: "Shift", ControlLeft: "Ctrl", ControlRight: "Ctrl", MetaLeft: "Win", AltLeft: "Alt",
  AltRight: "Alt", ContextMenu: "Menu", Space: "Space", Enter: "Enter", Tab: "Tab", ArrowUp: "↑", ArrowDown: "↓", ArrowLeft: "←", ArrowRight: "→", PrintScreen: "PrtSc",
  ScrollLock: "ScrLk", Pause: "Pause", Insert: "Ins", Delete: "Del", Home: "Home", End: "End", PageUp: "PgUp", PageDown: "PgDn", NumLock: "Num", NumpadDivide: "/",
  NumpadMultiply: "*", NumpadSubtract: "-", NumpadAdd: "+", NumpadEnter: "Ent", NumpadDecimal: "." };
const label = (code) => LABEL[code] || code.replace(/^(Key|Digit|Numpad)/, (m) => (m === "Numpad" ? "N" : ""));
const WIDE = { Backspace: 2, Tab: 1.5, Backslash: 1.5, CapsLock: 1.8, Enter: 2.2, ShiftLeft: 2.3, ShiftRight: 2.7, Space: 6, ControlLeft: 1.3, ControlRight: 1.3 };
// Full-screen colours for dead or stuck pixels, then a gradient for banding and a fine checkerboard for pixel response.
const SCREENS = ["#000000", "#ffffff", "#ff0000", "#00ff00", "#0000ff", "#808080", "linear-gradient(90deg, #000, #fff)", "repeating-conic-gradient(#000 0 25%, #fff 0 50%) 0 0 / 2px 2px"];

const CHECK_TARGET = { Display: "display", Keys: "keys", Mouse: "mouse", Speakers: "speakers", Mic: "mic" };
function card(key, ico, body) {
  const verdict = h("div", { class: "chk-verdict" });
  let state = "";
  const pick = (v) => { state = state === v ? "" : v; ok.classList.toggle("on", state === "ok"); bad.classList.toggle("on", state === "bad"); };
  const ok = h("button", { class: "btn quiet chk-ok", onclick: () => pick("ok") }, icon("check"), t("Checks_Ok"));
  const bad = h("button", { class: "btn quiet chk-bad", onclick: () => pick("bad") }, icon("alert"), t("Checks_Problem"));
  verdict.append(h("span", { class: "caption" }, t("Checks_YourCall")), ok, bad);
  return h("section", { class: "panel chk", "data-a": CHECK_TARGET[key] }, h("header", { class: "panel-head" }, h("span", { class: "ico" }, icon(ico)),
    h("div", { class: "ttl" }, h("h2", { class: "panel-title" }, t(`Checks_${key}`)), h("div", { class: "panel-sub fa" }, t(`Checks_${key}_Sub`)))), body, verdict);
}

function display() {
  function start() {
    let i = 0;
    const hint = h("div", { class: "chk-hint" }, t("Checks_Display_Hint"));
    const layer = h("div", { class: "chk-screen", tabindex: "0" }, hint);
    const paint = () => { layer.style.background = SCREENS[i]; };
    const close = () => { if (document.fullscreenElement) document.exitFullscreen().catch(() => {}); layer.remove(); };
    const step = (d) => { i += d; if (i < 0) i = 0; if (i >= SCREENS.length) return close(); paint(); };
    layer.addEventListener("click", () => step(1));
    layer.addEventListener("contextmenu", (e) => { e.preventDefault(); step(-1); });
    layer.addEventListener("keydown", (e) => { e.preventDefault(); if (e.key === "Escape") close(); else if (e.key === "ArrowLeft" || e.key === "Backspace") step(-1); else step(1); });
    document.addEventListener("fullscreenchange", function gone() { if (!document.fullscreenElement) { document.removeEventListener("fullscreenchange", gone); layer.remove(); } });
    document.body.append(layer); paint(); layer.focus();
    layer.requestFullscreen().catch(() => {});
    setTimeout(() => hint.classList.add("fade"), 2500);
  }
  return card("Display", "eye", h("div", {}, h("p", { class: "chk-text" }, t("Checks_Display_Text")),
    h("div", { class: "btn-row" }, h("button", { class: "btn go", onclick: start }, icon("play"), t("Checks_Display_Start")))));
}

function keyboard(cleanup) {
  const keys = new Map(), last = h("span", { class: "lat" }, "-"), count = h("span", { class: "lat" }, "0");
  const board = h("div", { class: "kb" }, KEYS.map((row) => h("div", { class: "kb-row" }, row.map((code) => {
    const k = h("span", { class: "kb-key lat", style: { "--w": WIDE[code] || 1 }, title: code }, label(code)); keys.set(code, k); return k;
  }))));
  let armed = false;
  const press = (e) => {
    e.preventDefault();
    keys.get(e.code)?.classList.add("hit", "down");
    last.textContent = `${e.code}${e.key && e.key.length === 1 ? ` (${e.key})` : ""}`;
    count.textContent = String([...keys.values()].filter((k) => k.classList.contains("hit")).length) + " / " + keys.size;
  };
  const onDown = (e) => { if (armed) press(e); };
  // Windows gives Print Screen to the system on the way down: the page sees only the release, so that is where the key counts.
  const onUp = (e) => { if (!armed) return; if (e.code === "PrintScreen") press(e); e.preventDefault(); keys.get(e.code)?.classList.remove("down"); };
  window.addEventListener("keydown", onDown, true); window.addEventListener("keyup", onUp, true);
  cleanup.push(() => { window.removeEventListener("keydown", onDown, true); window.removeEventListener("keyup", onUp, true); });
  const arm = h("button", { class: "btn go", onclick: () => { armed = !armed; arm.classList.toggle("on", armed); arm.lastChild.textContent = t(armed ? "Checks_Keys_Stop" : "Checks_Keys_Start"); } },
    icon("play"), h("span", {}, t("Checks_Keys_Start")));
  const reset = h("button", { class: "btn quiet", onclick: () => { for (const k of keys.values()) k.classList.remove("hit", "down"); count.textContent = "0"; last.textContent = "-"; } }, icon("refresh"), t("Checks_Reset"));
  return card("Keys", "menu", h("div", {}, h("p", { class: "chk-text" }, t("Checks_Keys_Text")),
    h("div", { class: "btn-row" }, arm, reset, h("span", { class: "grow" }), h("span", { class: "caption" }, t("Checks_Keys_Last")), last, h("span", { class: "caption" }, t("Checks_Keys_Count")), count),
    h("div", { class: "kb-wrap" }, board)));
}

function speakers(cleanup) {
  let ctx = null, playing = null;
  const audio = () => (ctx ||= new AudioContext());
  const stop = () => { try { playing?.stop(); } catch { /* already stopped */ } playing = null; };
  cleanup.push(() => { stop(); ctx?.close(); });
  // A tone panned hard left, right or centre; the sweep runs 20 Hz to 20 kHz to find a rattle or a dead range.
  function tone(pan, sweep = false) {
    stop();
    const a = audio(), osc = a.createOscillator(), gain = a.createGain(), panner = a.createStereoPanner(), now = a.currentTime, len = sweep ? 10 : 1.5;
    osc.type = "sine"; panner.pan.value = pan;
    if (sweep) { osc.frequency.setValueAtTime(20, now); osc.frequency.exponentialRampToValueAtTime(20000, now + len); } else osc.frequency.value = 440;
    gain.gain.setValueAtTime(0, now); gain.gain.linearRampToValueAtTime(0.25, now + 0.05); gain.gain.setValueAtTime(0.25, now + len - 0.1); gain.gain.linearRampToValueAtTime(0, now + len);
    osc.connect(gain).connect(panner).connect(a.destination); osc.start(now); osc.stop(now + len); playing = osc;
  }
  return card("Speakers", "bolt", h("div", {}, h("p", { class: "chk-text" }, t("Checks_Speakers_Text")),
    h("div", { class: "btn-row" },
      h("button", { class: "btn", onclick: () => tone(-1) }, t("Checks_Left")), h("button", { class: "btn", onclick: () => tone(0) }, t("Checks_Both")),
      h("button", { class: "btn", onclick: () => tone(1) }, t("Checks_Right")), h("button", { class: "btn", onclick: () => tone(0, true) }, t("Checks_Sweep")),
      h("button", { class: "btn quiet", onclick: stop }, icon("stop"), t("Checks_Stop")))));
}

function microphone(cleanup) {
  const bar = h("div", { class: "progress chk-level" }, h("i")), peak = h("span", { class: "lat" }, "-"), status = h("p", { class: "caption" });
  let stream = null, ctx = null, raf = 0, top = 0;
  const stop = () => { cancelAnimationFrame(raf); stream?.getTracks().forEach((x) => x.stop()); ctx?.close(); stream = ctx = null; bar.firstChild.style.setProperty("--p", 0); };
  cleanup.push(stop);
  async function start() {
    stop(); top = 0;
    try { stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: false, noiseSuppression: false, autoGainControl: false } }); }
    catch (e) { status.textContent = t("Checks_Mic_Denied", e.name || String(e)); return; }
    const track = stream.getAudioTracks()[0]; status.textContent = t("Checks_Mic_Device", track?.label || "-");
    ctx = new AudioContext(); const an = ctx.createAnalyser(); an.fftSize = 2048; ctx.createMediaStreamSource(stream).connect(an);
    const buf = new Float32Array(an.fftSize);
    const tick = () => {
      an.getFloatTimeDomainData(buf); let sum = 0; for (const v of buf) sum += v * v;
      const db = 20 * Math.log10(Math.sqrt(sum / buf.length) || 1e-9);   // dBFS: 0 is the loudest the input can take
      top = Math.max(top, db); bar.firstChild.style.setProperty("--p", Math.max(0, Math.min(1, (db + 60) / 60)));
      peak.textContent = `${db.toFixed(0)} dBFS (max ${top.toFixed(0)})`; raf = requestAnimationFrame(tick);
    };
    tick();
  }
  return card("Mic", "chat", h("div", {}, h("p", { class: "chk-text" }, t("Checks_Mic_Text")),
    h("div", { class: "btn-row" }, h("button", { class: "btn go", onclick: start }, icon("play"), t("Checks_Mic_Start")), h("button", { class: "btn quiet", onclick: stop }, icon("stop"), t("Checks_Stop")),
      h("span", { class: "grow" }), peak), bar, status));
}

function mouse(cleanup) {
  const names = ["Checks_Mouse_Left", "Checks_Mouse_Middle", "Checks_Mouse_Right", "Checks_Mouse_Back", "Checks_Mouse_Forward"];
  const btns = names.map((k) => h("span", { class: "kb-key wide" }, t(k), h("b", { class: "lat" }, "0")));
  const wheel = h("span", { class: "lat" }, "↑ 0 · ↓ 0"), dbl = h("span", { class: "lat" }, "0");
  let up = 0, down = 0, doubles = 0;
  const pad = h("div", { class: "chk-pad", tabindex: "0" }, t("Checks_Mouse_Pad"));
  const press = (i) => { const b = btns[i]; if (!b) return; b.classList.add("hit", "down"); const n = b.lastChild; n.textContent = String(+n.textContent + 1); };
  pad.addEventListener("mousedown", (e) => { e.preventDefault(); if (e.button <= 2) press(e.button); });
  pad.addEventListener("mouseup", (e) => { e.preventDefault(); if (e.button <= 2) btns[e.button]?.classList.remove("down"); });
  // Back and Forward (buttons 3 and 4) would otherwise walk the page's history, leave this page and lose the test, so their pointer events are
  // cancelled - and a cancelled pointerdown gets no mousedown after it, so these two are counted here, from the pointer events. A button pressed
  // while another is held arrives as a pointermove whose button names it; the held-buttons mask tells a press from a release.
  const XBIT = { 3: 8, 4: 16 };
  for (const type of ["pointerdown", "pointermove", "pointerup"]) pad.addEventListener(type, (e) => {
    if (!XBIT[e.button]) return;
    e.preventDefault();
    if (e.buttons & XBIT[e.button]) press(e.button); else btns[e.button].classList.remove("down");
  });
  const keepHere = (e) => { if (e.button > 2 && pad.matches(":hover")) { e.preventDefault(); e.stopPropagation(); } };
  for (const type of ["mouseup", "pointerup", "auxclick"]) window.addEventListener(type, keepHere, true);
  cleanup.push(() => { for (const type of ["mouseup", "pointerup", "auxclick"]) window.removeEventListener(type, keepHere, true); });
  pad.addEventListener("contextmenu", (e) => e.preventDefault());
  pad.addEventListener("auxclick", (e) => e.preventDefault());
  pad.addEventListener("dblclick", () => { dbl.textContent = String(++doubles); });
  pad.addEventListener("wheel", (e) => { e.preventDefault(); if (e.deltaY < 0) up++; else if (e.deltaY > 0) down++; wheel.textContent = `↑ ${up} · ↓ ${down}`; }, { passive: false });
  return card("Mouse", "arrow", h("div", {}, h("p", { class: "chk-text" }, t("Checks_Mouse_Text")), pad,
    h("div", { class: "chk-mouse" }, btns, h("span", { class: "caption" }, t("Checks_Mouse_Wheel")), wheel, h("span", { class: "caption" }, t("Checks_Mouse_Double")), dbl)));
}

export function mount(el) {
  const cleanup = [];
  el.append(h("header", { class: "page-head" }, h("div", {}, h("h1", { class: "page-title" }, t("Nav_Checks")), h("p", { class: "page-lede" }, t("Checks_Lede")))),
    h("div", { class: "chk-grid" }, display(), speakers(cleanup), microphone(cleanup), mouse(cleanup)), keyboard(cleanup));
  return () => cleanup.forEach((f) => f());
}
