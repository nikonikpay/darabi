// Small DOM helpers: no framework, no build step.
const h = (tag, attrs, ...kids) => {
  const e = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v == null || v === false) continue;
    if (k === 'class') e.className = v; else if (k === 'html') e.innerHTML = v; else if (k.startsWith('on')) e.addEventListener(k.slice(2), v);
    else if (k === 'value') e.value = v; else if (k === 'checked' || k === 'disabled' || k === 'selected' || k === 'readOnly' || k === 'required') e[k] = !!v; else e.setAttribute(k, v === true ? '' : v);
  }
  for (const c of kids.flat(9)) if (c != null && c !== false) e.append(c.nodeType ? c : document.createTextNode(String(c)));
  return e;
};
const $ = (s, r) => (r || document).querySelector(s);
const clear = e => { while (e.firstChild) e.removeChild(e.firstChild); return e; };
const debounce = (f, ms) => { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => f(...a), ms); }; };
const money = n => n == null ? '—' : Number(n).toLocaleString('en-US') + ' تومان';
const digits = s => String(s == null ? '' : s).replace(/[۰-۹]/g, d => '۰۱۲۳۴۵۶۷۸۹'.indexOf(d)).replace(/[٠-٩]/g, d => '٠١٢٣٤٥٦٧٨٩'.indexOf(d));
const today = () => { const d = new Date(); return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0'); };

function toast(msg, kind) {
  const t = h('div', { class: 'toast ' + (kind || 'ok'), role: 'status' }, msg);
  $('#toasts').append(t); setTimeout(() => t.remove(), kind === 'err' ? 7000 : 3200);
}

/** A dialog; returns {close, body}. Escape and a click outside close it. */
function modal(title, build, wide) {
  const body = h('div', { class: 'mbody' });
  const close = () => { back.remove(); document.removeEventListener('keydown', esc); };
  const esc = e => { if (e.key === 'Escape') close(); };
  const back = h('div', { class: 'backdrop', onmousedown: e => { if (e.target === back) close(); } },
    h('div', { class: 'modal' + (wide ? ' wide' : ''), role: 'dialog', 'aria-label': title }, h('header', {}, h('h3', {}, title), h('button', { class: 'x', 'aria-label': 'بستن', onclick: close }, '×')), body));
  document.addEventListener('keydown', esc); document.body.append(back);
  build(body, close);
  return { close, body };
}

const ask = (text, yes) => new Promise(res => modal('تأیید', (b, close) => {
  b.append(h('p', {}, text), h('div', { class: 'row end' }, h('button', { class: 'btn', onclick: () => { close(); res(false); } }, 'نه'), h('button', { class: 'btn primary', onclick: () => { close(); res(true); } }, yes || 'بله')));
}));

/** A labelled field. `input` is the control. */
const field = (label, input, hint, cls) => h('label', { class: 'field ' + (cls || '') }, h('span', {}, label), input, hint ? h('small', {}, hint) : null);
const text = (name, v, o) => h('input', Object.assign({ type: 'text', name, value: v == null ? '' : v, autocomplete: 'off' }, o));
const area = (name, v, o) => h('textarea', Object.assign({ name, rows: 3 }, o), v == null ? '' : v);
const select = (name, opts, v, o) => h('select', Object.assign({ name }, o), Object.entries(opts).map(([k, t]) => h('option', { value: k, selected: String(k) === String(v) }, t)));
const check = (name, v, label) => h('label', { class: 'check' }, h('input', { type: 'checkbox', name, checked: !!v }), label);

function qrSvg(textValue, cell) {
  const q = qrcode(0, 'M'); q.addData(String(textValue)); q.make();
  return q.createSvgTag({ cellSize: cell || 4, margin: 0, scalable: true });
}
