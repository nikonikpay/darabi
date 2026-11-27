// Printing: the invoice's templates (the customer's A4 page, an A5 one, the assembly sheet), a service's intake receipt and the label. Each is shown as a page of its real
// size with its look chosen beside it, then printed. The look, the shop's header and the ready texts are this PC's own (prefs.print); the lines and the amounts always
// come from the saved record - the preview never changes them.

/* ---------- what is kept ---------- */
const PAPER = { A4: [210, 297], A5: [148, 210] };
const BASE = { paper: 'A4', land: false, margin: 10, font: '', size: 10, color: '#14533c', fa: true };
const SHEETS = {
  full: { name: 'فاکتور مشتری (A4)', d: Object.assign({}, BASE, { logo: true, seller: true, buyer: true, no: true, cat: true, serials: true, warranty: true, words: true, pays: true, pay: true, notes: true, sign: true }) },
  a5: { name: 'فاکتور A5 (کالای کم)', d: Object.assign({}, BASE, { paper: 'A5', margin: 7, size: 8.5, logo: true, seller: true, buyer: true, no: true, cat: false, serials: true, warranty: true, words: true, pays: true, pay: true, notes: true, sign: true }) },
  assembly: { name: 'برگهٔ اسمبل (بدون قیمت)', d: Object.assign({}, BASE, { size: 11, logo: true, no: false, cat: true, serials: true, address: false, tick: true, notes: true, sign: true }) },
  receipt: { name: 'رسید پذیرش', d: Object.assign({}, BASE, { paper: 'A5', size: 10.5, logo: true, qr: true, notes: true, sign: true }) },
  label: { name: 'برچسب', d: { w: 70, h: 50, font: 'Segoe UI', size: 9, fa: false } },
};
const FLAGS = { logo: 'لوگو', seller: 'مشخصات فروشنده', buyer: 'مشخصات خریدار', no: 'شمارهٔ فاکتور', cat: 'ستون نوع کالا', serials: 'سریال‌ها', warranty: 'گارانتی', words: 'مبلغ به حروف', pays: 'دریافتی‌ها و مانده', pay: 'اطلاعات واریز', address: 'نشانی مشتری', tick: 'ستون تیک مونتاژ', qr: 'کد QR', notes: 'توضیحات', sign: 'جای امضا' };
const NOTE_KINDS = { sale: 'فاکتور فروش', proforma: 'پیش‌فاکتور', return: 'برگشت از فروش', assembly: 'برگهٔ اسمبل', receipt: 'رسید پذیرش' };

const printCfg = () => Object.assign({ shop: {}, notes: null, tpl: {}, last: {} }, prefs.get().print || {});
const setPrint = o => prefs.set({ print: Object.assign(printCfg(), o) });
/** A template's look: its own start, then what the older settings held (the receipt's paper, the label's size), then what was chosen in the preview. */
function sheetOpts(key) {
  const p = prefs.get(), old = key === 'receipt' ? { paper: p.paper } : key === 'label' ? { w: p.labelW, h: p.labelH } : {};
  return Object.assign({}, SHEETS[key].d, old, printCfg().tpl[key]);
}
/** The seller as it is printed: what was written on this PC, else what the site's settings page says. */
function shopInfo() {
  const s = printCfg().shop, a = (S.cfg && S.cfg.acc) || {}, p = prefs.get();
  return Object.assign({}, s, { name: s.name || a.shop || p.shop, phone: s.phone || a.phone || p.phone, address: s.address || a.address || '', foot: s.foot || a.foot || '' });
}
const defaultNotes = () => [
  { id: 'warranty', title: 'گارانتی', text: 'برای استفاده از خدمات گارانتی، ارائهٔ این فاکتور لازم است.', on: ['sale'] },
  { id: 'valid', title: 'اعتبار پیش‌فاکتور', text: 'قیمت‌ها تا تاریخ اعتبار درج‌شده معتبر است.', on: ['proforma'] },
  { id: 'receipt', title: 'همراه داشتن رسید', text: prefs.get().foot, on: ['receipt'] },
  { id: 'build', title: 'کنترل اسمبل', text: 'سریال قطعات و نتیجهٔ کنترل نهایی روی همین برگه ثبت شود.', on: ['assembly'] },
];
const printNotes = () => printCfg().notes || defaultNotes();

/* ---------- pieces of a page ---------- */
/** Numbers of one page: Persian digits when chosen, always read left to right. Names, models and serials are never rewritten. */
function numbers(o) {
  const fa = x => o.fa ? String(x).replace(/\d/g, d => '۰۱۲۳۴۵۶۷۸۹'[d]) : String(x);
  const n = x => x == null || x === '' ? null : h('span', { class: 'n' }, fa(x));
  return { fa, n, m: v => n(v == null ? '—' : Number(v).toLocaleString('en-US')) };
}
const kv = rows => { const r = rows.filter(x => x && x[1] != null && x[1] !== '' && x[1] !== false); return r.length ? h('dl', {}, r.map(x => h('div', {}, h('dt', {}, x[0]), h('dd', {}, x[1])))) : null; };
const box = (head, rows) => h('section', { class: 'box' }, h('h3', {}, head), kv(rows));
function sheetHead(o, title, meta, extra) {
  const s = shopInfo(), N = numbers(o);
  return h('header', {}, h('div', { class: 'shop' }, o.logo && s.logo ? h('img', { class: 'logo', src: s.logo, alt: '' }) : null,
    h('div', {}, h('h1', {}, s.name), s.slogan ? h('p', {}, s.slogan) : null, s.web ? h('p', {}, h('span', { class: 'n' }, s.web)) : null, !o.seller && s.phone ? h('p', {}, N.n(s.phone)) : null, !o.seller && s.address ? h('p', {}, N.fa(s.address)) : null)),
    h('h2', {}, title), h('div', { class: 'meta' }, kv(meta), extra || null));
}
const sellerBox = o => { const s = shopInfo(), N = numbers(o); return box('فروشنده', [['نام', s.name], ['شناسه / کد ملی', N.n(s.nid)], ['کد اقتصادی', N.n(s.eco)], ['شمارهٔ ثبت', N.n(s.reg)], ['تلفن', N.n(s.phone)], ['کد پستی', N.n(s.postal)], ['نشانی', s.address ? N.fa(s.address) : '']]); };
const noteBlock = (o, texts) => o.notes && texts.length ? h('section', { class: 'notes' }, h('h3', {}, 'توضیحات'), texts.map(t => h('p', {}, t))) : null;
const signs = (o, names) => o.sign ? h('div', { class: 'sign' }, names.map(t => h('span', {}, t))) : null;
// the end of a page stays together: the signatures are not parted from the text under them
const tail = (...kids) => h('div', { class: 'tail' }, kids);
const catCell = (o, x) => o.cat ? h('td', { class: 'cat' }, (S.cfg.categories || {})[x.category] || '') : undefined;
const serialList = list => list.map((s, i) => [i ? '، ' : null, h('span', { class: 'n serialno' }, s)]);
// a cell left out by its template is `undefined`; an empty one is still a cell
const itemsTable = (heads, rows) => h('table', { class: 'lines' }, h('thead', {}, h('tr', {}, heads.filter(Boolean).map(x => h('th', {}, x)))), h('tbody', {}, rows.map(r => h('tr', {}, r.filter(c => c !== undefined)))));

/* ---------- the invoice ---------- */
// a payment by a bounced or cancelled cheque never arrived: the site leaves it out of what was paid, and so does the page
const deadPay = p => !!p.cheque && (p.cheque.status === 'bounced' || p.cheque.status === 'cancelled');
function invoiceSheet(v, o, more) {
  const N = numbers(o), s = shopInfo(), settled = (v.kind === 'sale' || v.kind === 'return') && v.status === 'final';
  const pays = settled && o.pays ? (v.payments || []).filter(p => !deadPay(p)).concat(more.extra) : [];
  const sign = p => (p.direction === 'in') === (v.kind === 'sale') ? 1 : -1;   // on a return, money paid back is what settles it
  const paid = pays.reduce((a, p) => a + sign(p) * p.amount, 0), left = v.total - paid;
  const how = p => [p.methodText, p.cheque ? [' ', N.n(p.cheque.no), p.cheque.due_at_j ? [' — سررسید ', N.n(p.cheque.due_at_j)] : null, p.cheque.status === 'pending' ? ' (در جریان)' : null] : null, !p.invoice_id ? ' — علی‌الحساب' : null];
  return h('div', { class: 'sheet invoice' },
    sheetHead(o, v.kindText, [o.no ? ['شماره', N.n(v.no || '—')] : null, ['تاریخ', N.n(v.at_j)], v.kind === 'proforma' ? ['اعتبار تا', N.n(v.valid_until_j)] : null],
      v.status === 'void' ? h('div', { class: 'stamp' }, 'باطل شده') : v.status === 'draft' && v.kind !== 'proforma' ? h('div', { class: 'stamp' }, 'پیش‌نویس') : null),
    o.seller || o.buyer ? h('div', { class: 'parties' }, o.seller ? sellerBox(o) : null, o.buyer ? box('خریدار', [['نام', v.customer_name], ['موبایل', N.n(v.customer_mobile)], ['کد ملی', N.n(v.customer_national_id)], ['نشانی', v.customer_address ? N.fa(v.customer_address) : '']]) : null) : null,
    itemsTable(['ردیف', o.cat ? 'نوع' : null, 'شرح کالا یا خدمت', 'تعداد', 'قیمت واحد (تومان)', 'مبلغ (تومان)'], v.items.map((x, i) => { const sn = o.serials && x.serials.length, w = o.warranty && x.warranty_months;
      return [h('td', { class: 'c' }, N.n(i + 1)), catCell(o, x),
        h('td', { class: 'desc' }, h('bdi', {}, x.name), sn || w ? h('div', { class: 'sub' }, w ? h('span', {}, N.n(x.warranty_months), ' ماه گارانتی') : null, sn ? h('span', {}, 'سریال: ', serialList(x.serials)) : null) : null),
        h('td', { class: 'c' }, N.n(x.qty)), h('td', { class: 'amt' }, N.m(x.unit_price)), h('td', { class: 'amt' }, N.m(x.line_total))]; })),
    h('div', { class: 'bottom' },
      h('div', { class: 'side' }, o.words && v.total > 0 ? h('p', { class: 'words' }, 'مبلغ به حروف: ', h('b', {}, toWords(v.total) + ' تومان')) : null,
        pays.length ? h('section', { class: 'pays' }, h('h3', {}, v.kind === 'return' ? 'مبالغ بازپرداخت‌شده' : 'دریافتی‌ها'), itemsTable(['تاریخ', 'روش', 'پیگیری', 'مبلغ (تومان)'], pays.map(p => [h('td', {}, N.n(p.at_j)), h('td', {}, how(p)), h('td', {}, N.n(p.tx)), h('td', { class: 'amt' }, sign(p) < 0 ? 'برگشتی ' : null, N.m(p.amount))]))) : null,
        o.pay && s.pay && v.kind !== 'return' ? h('p', { class: 'payinfo' }, 'اطلاعات واریز: ', N.fa(s.pay)) : null, noteBlock(o, (v.notes ? [v.notes] : []).concat(more.notes))),
      h('dl', { class: 'totals' }, [['جمع کل', v.subtotal], v.discount ? ['تخفیف', v.discount] : null, v.discount && v.tax ? ['پس از تخفیف', v.subtotal - v.discount] : null, v.tax ? ['مالیات بر ارزش افزوده (' + N.fa(v.tax_pct) + '٪)', v.tax] : null, ['مبلغ قابل پرداخت', v.total, 'big'],
        settled && o.pays ? [v.kind === 'return' ? 'بازپرداخت‌شده' : 'دریافت‌شده', paid, 'paid'] : null, settled && o.pays ? left > 0 ? ['مانده', left, 'left'] : left < 0 ? ['اضافه‌دریافت (بستانکار)', -left, 'left'] : ['مانده (تسویه)', 0, 'left'] : null]
        .filter(Boolean).map(x => h('div', { class: x[2] || '' }, h('dt', {}, x[0]), h('dd', {}, N.m(x[1])))))),
    tail(signs(o, ['مهر و امضای فروشنده', 'امضای خریدار']), s.foot ? h('p', { class: 'foot' }, s.foot) : null));
}

/* ---------- the assembly sheet: who it is for and which parts - never a price ---------- */
function assemblySheet(d, o, more) {
  const N = numbers(o), lines = d.lines.filter((x, i) => !more.skip.has(i));
  return h('div', { class: 'sheet assembly' }, sheetHead(Object.assign({}, o, { seller: true }), 'برگهٔ اسمبل', [o.no ? ['شمارهٔ فاکتور', N.n(d.no || '—')] : null, ['تاریخ', N.n(d.date)]]),
    h('div', { class: 'parties one' }, box('مشتری', [['نام', d.customer.name], ['موبایل', N.n(d.customer.mobile)], o.address ? ['نشانی', d.customer.address ? N.fa(d.customer.address) : ''] : null])),
    itemsTable(['ردیف', o.cat ? 'نوع' : null, 'قطعه', 'تعداد', o.serials ? 'سریال' : null, o.tick ? 'نصب شد' : null], lines.map((x, i) => [h('td', { class: 'c' }, N.n(i + 1)), catCell(o, x), h('td', { class: 'desc' }, h('bdi', {}, x.name)), h('td', { class: 'c' }, N.n(x.qty)),
      o.serials ? h('td', { class: 'serial' }, serialList(x.serials)) : undefined, o.tick ? h('td', { class: 'c' }, h('span', { class: 'tickbox' })) : undefined])),
    noteBlock(o, (d.notes ? [d.notes] : []).concat(more.notes)), tail(signs(o, ['اسمبل‌کننده', 'کنترل نهایی'])));
}

/* ---------- the intake receipt of a service ---------- */
function receiptSheet(j, o, more) {
  const N = numbers(o), recv = ((j.checks || {}).recv || []).map(k => (((S.cfg.checklists || { recv: {} }).recv[j.device_type]) || {})[k]).filter(Boolean).concat((j.recv || []).map(x => x.name + (x.serial ? ' (' + x.serial + ')' : ''))).join('، ');
  const s = shopInfo(), contact = [s.phone ? N.n(s.phone) : null, s.address ? N.fa(s.address) : null].filter(Boolean);
  return h('div', { class: 'sheet receipt' }, sheetHead(Object.assign({}, o, { seller: true }), 'رسید پذیرش',[['شمارهٔ پذیرش', h('b', { class: 'n big' }, j.service_no)], ['تاریخ پذیرش', N.n(j.received_at_j)], ['قول تحویل', N.n(j.due_at_j)]], o.qr ? h('div', { class: 'qr', html: qrSvg(j.service_no, 5) }) : null),
    h('div', { class: 'parties one' }, box('مشتری', [['نام', j.customer_name], ['موبایل', N.n(j.customer_mobile)]])),
    box('دستگاه', [['دستگاه', j.device], ['سریال', j.device_serial ? h('span', { class: 'n' }, j.device_serial) : null], ['مشکل اعلام‌شده', j.complaint], ['لوازم تحویل‌گرفته‌شده', recv], ['توضیحات لوازم', (j.checks || {}).note]]),
    noteBlock(o, more.notes), tail(signs(o, ['امضای تحویل‌دهنده (مشتری)', 'امضای تحویل‌گیرنده']), contact.length ? h('p', { class: 'foot' }, contact.map((c, i) => [i ? ' — ' : null, c])) : null));
}

/* ---------- the preview ---------- */
let winFonts = null;
/** The Windows fonts as a select (the program asks Windows once); `own` names the choice kept as ''. */
function fontSelect(name, value, own, onchange) {
  const el = select(name, Object.assign({ '': own }, value ? { [value]: value } : {}), value, onchange ? { onchange } : {});
  (winFonts || (winFonts = bridge.fonts())).then(r => { const now = el.value; clear(el).append(h('option', { value: '' }, own), ...((r.data && r.data.fonts) || []).filter(f => f !== 'Vazirmatn').map(f => h('option', { value: f }, f))); el.value = now; });
  return el;
}

/**
 * The preview: the page beside the choices that shape it; every change draws the page, the copy that is printed and the page size again.
 * spec: keys (the templates offered), kind (whose ready texts are ticked and whose last template is remembered), build(key, opts, more) → the page,
 * extra (customer deposits that may be added), lines (the assembly sheet's rows, to leave some out).
 */
function printWindow(titleText, spec) {
  const last = printCfg().last[spec.kind]; let key = spec.keys.includes(last) ? last : spec.keys[0];
  const kindOf = () => key === 'assembly' ? 'assembly' : spec.kind, ticked = () => new Set(printNotes().filter(n => (n.on || []).includes(kindOf())).map(n => n.id));
  const picks = { extra: new Set(), skip: new Set(), notes: ticked() };
  modal(titleText, body => {
    const side = h('div', { class: 'pside' }), stage = h('div', { class: 'pstage' });
    const draw = () => {
      const o = sheetOpts(key), label = key === 'label';
      const node = spec.build(key, o, { extra: (spec.extra || []).filter(p => picks.extra.has(p.id)), skip: picks.skip, notes: printNotes().filter(n => picks.notes.has(n.id)).map(n => n.text) });
      const [w, hh] = label ? [o.w, o.h] : o.land ? PAPER[o.paper].slice().reverse() : PAPER[o.paper], margin = label ? 0 : o.margin;
      node.style.setProperty('--pfont', cssFont(o.font) + ', "Segoe UI", Tahoma, sans-serif'); node.style.setProperty('--psize', o.size + 'pt'); if (o.color) node.style.setProperty('--pc', o.color);
      node.style.width = w + 'mm'; node.style[label ? 'height' : 'minHeight'] = hh + 'mm'; if (!label) node.style.padding = margin + 'mm';
      const count = o.fa ? ',persian' : '', pages = margin >= 5 ? `@bottom-center{content:counter(page${count}) " از " counter(pages${count});font:7pt Tahoma;color:#666}` : '';
      document.getElementById('printcss')?.remove(); document.head.append(h('style', { id: 'printcss' }, `@page{size:${w}mm ${hh}mm;margin:${margin}mm;${pages}}`));
      document.getElementById('printArea')?.remove(); document.body.append(h('div', { id: 'printArea' }, node.cloneNode(true)));
      clear(stage).append(node);
      const room = stage.clientWidth - 32; if (room > 0 && node.offsetWidth) node.style.zoom = Math.min(label ? 2.5 : 1, room / node.offsetWidth);
    };
    const keep = (k, v) => { const t = printCfg().tpl; setPrint({ tpl: Object.assign(t, { [key]: Object.assign({}, t[key], { [k]: v }) }) }); draw(); };
    const tick = (label, on, onchange) => h('label', { class: 'check' }, h('input', { type: 'checkbox', checked: on, onchange: e => onchange(e.target.checked) }), label);
    const flip = (set, id) => on => { if (on) set.add(id); else set.delete(id); draw(); };
    const print = async dialog => { draw(); await document.fonts.ready; bridge.print(dialog); };
    const controls = () => {
      const o = sheetOpts(key), label = key === 'label';
      const num = (k, min, max, step) => h('input', { type: 'number', name: k, value: o[k], min, max, step: step || 1, dir: 'ltr', onchange: e => { const x = parseFloat(e.target.value), v = Math.min(max, Math.max(min, Number.isFinite(x) ? x : SHEETS[key].d[k])); e.target.value = v; keep(k, v); } });
      const tpl = spec.keys.length > 1 ? field('قالب', select('tpl', Object.fromEntries(spec.keys.map(k => [k, SHEETS[k].name])), key, { onchange: e => { key = e.target.value; setPrint({ last: Object.assign(printCfg().last, { [spec.kind]: key }) }); picks.notes = ticked(); controls(); draw(); } })) : null;
      const notes = o.notes ? printNotes() : [];
      clear(side).append(...[
        h('div', { class: 'row' }, h('button', { type: 'button', class: 'btn primary', onclick: () => print('') }, 'چاپ…'), h('button', { type: 'button', class: 'btn', title: 'برای چاپگرهایی که کاغذشان فقط از پنجرهٔ خود ویندوز تنظیم می‌شود', onclick: () => print('system') }, 'پنجرهٔ چاپ ویندوز')),
        tpl,
        label ? h('div', { class: 'two' }, field('عرض (mm)', num('w', 20, 200)), field('ارتفاع (mm)', num('h', 20, 200)))
          : h('div', { class: 'two' }, field('کاغذ', select('paper', { A4: 'A4', A5: 'A5' }, o.paper, { onchange: e => keep('paper', e.target.value) })), field('جهت', select('land', { '': 'عمودی', 1: 'افقی' }, o.land ? 1 : '', { onchange: e => keep('land', !!e.target.value) }))),
        field('فونت چاپ', fontSelect('pfont', o.font, 'وزیرمتن', e => keep('font', e.target.value))),
        h('div', { class: 'two' }, field('اندازهٔ حروف', num('size', 6, 18, 0.5)), label ? null : field('حاشیه (mm)', num('margin', 0, 30))),
        label ? null : h('div', { class: 'two' }, field('رنگ', h('input', { type: 'color', name: 'color', value: o.color, onchange: e => keep('color', e.target.value) })), tick('ارقام فارسی', o.fa, v => keep('fa', v))),
        label ? null : h('fieldset', {}, h('legend', {}, 'در برگه بیاید'), Object.keys(FLAGS).filter(f => f in SHEETS[key].d).map(f => tick(FLAGS[f], o[f], v => { keep(f, v); if (f === 'notes') controls(); }))),
        notes.length ? h('fieldset', { class: 'pnotes' }, h('legend', {}, 'توضیحات آماده'), notes.map(n => tick(n.title || n.text.slice(0, 30), picks.notes.has(n.id), flip(picks.notes, n.id)))) : null,
        (key === 'full' || key === 'a5') && o.pays && (spec.extra || []).length ? h('fieldset', { class: 'pextra' }, h('legend', {}, 'دریافتی‌های دیگر مشتری (به این فاکتور وصل نیست)'), spec.extra.map(p => tick(p.at_j + ' — ' + p.methodText + ' — ' + money(p.amount), picks.extra.has(p.id), flip(picks.extra, p.id)))) : null,
        key === 'assembly' && spec.lines ? h('fieldset', { class: 'plines' }, h('legend', {}, 'ردیف‌های برگه'), spec.lines.map((x, i) => tick(x.name, !picks.skip.has(i), on => { if (on) picks.skip.delete(i); else picks.skip.add(i); draw(); }))) : null,
        typeof printSetup === 'function' && !label ? h('button', { type: 'button', class: 'btn', onclick: () => printSetup(() => { picks.notes = ticked(); controls(); draw(); }) }, 'سرفاکتور و توضیحات آماده…') : null,
        h('button', { type: 'button', class: 'link', onclick: () => { const t = printCfg().tpl; delete t[key]; setPrint({ tpl: t }); controls(); draw(); } }, 'برگشت به ظاهر اولیهٔ این قالب'),
      ].filter(Boolean));
    };
    body.append(side, stage); controls(); draw();
  }, 'full');
}

async function printInvoice(v) {
  if (!v.id) return;
  let extra = [];
  if (v.kind === 'sale' && v.status === 'final' && v.customer_id) {
    // money the customer paid that was entered without an invoice (a deposit before it was made): offered in the preview, added only if ticked
    try { extra = (await GET('staff/payments' + qs({ customer: v.customer_id, direction: 'in', per: 50 }))).items.filter(p => !p.invoice_id && !deadPay(p)); } catch (e) { toast('دریافتی‌های دیگر مشتری خوانده نشد: ' + e.message, 'err'); }
  }
  const lines = v.items.map(x => ({ category: x.category, name: x.name, qty: x.qty, serials: x.serials }));
  printWindow(v.kindText + ' ' + (v.no || ''), { kind: v.kind, keys: ['full', 'a5', 'assembly'], extra, lines,
    build: (key, o, more) => key === 'assembly' ? assemblySheet({ no: v.no, date: v.at_j, customer: { name: v.customer_name, mobile: v.customer_mobile, address: v.customer_address }, lines, notes: v.notes }, o, more) : invoiceSheet(v, o, more) });
}

/** The assembly sheet of a new computer, from its own parts: the people who build it need no accounting access. */
function printBuildSheet(b) {
  const lines = b.parts.map(x => ({ category: x.category, name: x.model, qty: 1, serials: x.serial ? [x.serial] : [] }));
  printWindow('برگهٔ اسمبل ' + b.number, { kind: 'assembly', keys: ['assembly'], lines, build: (key, o, more) => assemblySheet({ no: b.invoice_no || b.number, date: b.sold_at_j, customer: { name: b.customer_name, mobile: b.customer_mobile }, lines, notes: b.notes }, o, more) });
}

function printReceipt(j) {
  printWindow('رسید ' + j.service_no, { kind: 'receipt', keys: ['receipt'], build: (key, o, more) => receiptSheet(j, o, more) });
}

async function printLabel(kind, id) {
  try {
    const d = await GET(`staff/label/${kind}/${id}`);
    printWindow('برچسب ' + d.number, { kind: 'label', keys: ['label'], build: () => h('div', { class: 'label' }, h('div', { class: 'lhead' }, h('div', { class: 'lnum lat' }, d.number), h('div', { class: 'lqr', html: qrSvg(d.qr) })),
      h('dl', {}, d.lines.filter(l => l.key !== 'service_no' && l.value).map(l => [h('dt', {}, l.label), h('dd', {}, l.value)]))) });
  } catch (e) { fail(e); }
}
