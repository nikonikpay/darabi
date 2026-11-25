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
/** A whole toman amount in Persian words ("یک میلیون و دویست و پنجاه هزار"); '' for nothing. Up to the trillions, which no invoice here reaches. */
function toWords(n) {
  n = Math.floor(+n); if (!(n >= 0)) return ''; if (n === 0) return 'صفر';
  const U = ['', 'یک', 'دو', 'سه', 'چهار', 'پنج', 'شش', 'هفت', 'هشت', 'نه', 'ده', 'یازده', 'دوازده', 'سیزده', 'چهارده', 'پانزده', 'شانزده', 'هفده', 'هجده', 'نوزده'], T = ['', '', 'بیست', 'سی', 'چهل', 'پنجاه', 'شصت', 'هفتاد', 'هشتاد', 'نود'],
    H = ['', 'صد', 'دویست', 'سیصد', 'چهارصد', 'پانصد', 'ششصد', 'هفتصد', 'هشتصد', 'نهصد'], B = ['', 'هزار', 'میلیون', 'میلیارد', 'تریلیون'];
  const three = x => { const p = []; if (x >= 100) p.push(H[Math.floor(x / 100)]); x %= 100; if (x >= 20) { p.push(T[Math.floor(x / 10)]); x %= 10; } if (x) p.push(U[x]); return p.join(' و '); };
  const parts = []; for (let i = 0; n > 0; i++, n = Math.floor(n / 1000)) { const g = n % 1000; if (g) parts.unshift((i === 1 && g === 1 ? '' : three(g) + (B[i] ? ' ' : '')) + (B[i] || '')); }
  return parts.join(' و ');
}
const grouped = s => digits(s).replace(/\D/g, '').replace(/\B(?=(\d{3})+(?!\d))/g, ',');
/** Groups a typed number by threes as it is typed (1,250,000), keeping the caret on the same digit. The site strips the commas again. */
function groupDigits(el) {
  const before = digits(el.value.slice(0, el.selectionStart)).replace(/\D/g, '').length;
  el.value = grouped(el.value);
  if (document.activeElement === el) { let n = 0, p = 0; while (p < el.value.length && n < before) { if (/\d/.test(el.value[p])) n++; p++; } el.setSelectionRange(p, p); }
}
/** Makes an amount box group its digits and returns the line under it that writes the amount in words ("… تومان"). */
function moneyInput(el) {
  const words = h('small', { class: 'words' }), upd = () => { const d = digits(el.value).replace(/\D/g, ''); words.textContent = d ? toWords(+d) + ' تومان' : ''; };
  el.addEventListener('input', () => { groupDigits(el); upd(); }); el.value = grouped(el.value); upd();
  return words;
}
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
