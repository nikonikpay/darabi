// The accounting screens: sales invoices, returns, proformas and ready-made invoices (templates), payments, cheques, a customer's account, the period's money.
// F1–F12 work on every screen once signed in (F1 lists them); on an invoice they act on that invoice. The site decides what each person may do (the "accounting" area).
NAV.push(['#/invoices', 'فاکتورها', () => can('accounting')], ['#/payments', 'دریافت و پرداخت', () => can('accounting')], ['#/cheques', 'چک‌ها', () => can('accounting')], ['#/acc', 'خلاصهٔ مالی', () => can('accounting')]);
routes.push([/^\/invoices$/, (m, q) => viewInvoices(q)], [/^\/invoice\/(new|\d+)$/, (m, q) => viewInvoice(m[1], q)], [/^\/payments$/, (m, q) => viewPayments(q)], [/^\/cheques$/, (m, q) => viewCheques(q)], [/^\/acc$/, (m, q) => viewAccSummary(q)]);

const A = () => S.cfg.acc;
const kindPill = v => pill(v.kindText + (v.no ? ' ' + v.no : ''), v.kind === 'proforma' ? '' : v.kind === 'return' ? 'bad' : 'ok');
const stPill = v => pill(v.statusText, v.status === 'final' ? 'ok' : v.status === 'void' ? 'bad' : '');
const num = v => +digits(v).replace(/[^\d]/g, '') || 0;

/* ---------- keys ---------- */
const KEYS = [['F1', 'راهنمای کلیدها'], ['F2', 'فاکتور فروش تازه'], ['F3', 'جستجوی کالا و افزودن به فاکتور'], ['F4', 'انتخاب از فاکتورهای آماده'], ['F5', 'انتخاب مشتری'], ['F6', 'ثبت دریافت / پرداخت'],
  ['F7', 'تخفیف'], ['F8', 'ذخیرهٔ پیش‌نویس'], ['F9', 'قطعی کردن فاکتور'], ['F10', 'چاپ'], ['F11', 'پیش‌فاکتور تازه'], ['F12', 'فهرست فاکتورها'], ['Esc', 'بستن پنجره']];
document.addEventListener('keydown', e => {
  if (!/^F([1-9]|1[0-2])$/.test(e.key) || !S.me) return;
  e.preventDefault();   // F5 must not reload the program, F7 must not turn on caret browsing
  const here = S.keys && S.keys.hash === location.hash ? S.keys : {};
  if (here[e.key]) return here[e.key]();
  if (e.key === 'F1') return keysHelp();
  if (!can('accounting')) return;
  ({ F2: () => { location.hash = '#/invoice/new'; }, F4: () => pickTemplate(), F6: () => paymentDialog({}), F11: () => { location.hash = '#/invoice/new?kind=proforma'; }, F12: () => { location.hash = '#/invoices'; }, F3: () => { location.hash = '#/stock'; }, F5: () => { location.hash = '#/customers'; } }[e.key] || (() => { }))();
});
function keysHelp() { modal('کلیدهای میانبر', b => b.append(table(['کلید', 'کار'], KEYS.map(([k, t]) => h('tr', {}, h('td', {}, h('b', { class: 'lat' }, k)), h('td', {}, t)))))); }
const keyBtn = (label, key, onclick, cls) => h('button', { type: 'button', class: 'btn ' + (cls || ''), onclick, title: key }, label, ' ', h('small', { class: 'lat muted' }, key));

/* ---------- lists ---------- */
async function viewInvoices(q) {
  const page = Math.max(1, +q.get('page') || 1), kind = q.get('kind') || '';
  const r = await GET('staff/invoices' + qs({ kind, status: q.get('status'), q: q.get('q'), page }));
  const hrefOf = p => { const x = new URLSearchParams(q); x.set('page', p); return '#/invoices?' + x; };
  const go = o => { const x = new URLSearchParams(q); for (const [k, v] of Object.entries(o)) { if (v) x.set(k, v); else x.delete(k); } x.delete('page'); location.hash = '#/invoices?' + x; };
  const kinds = h('div', { class: 'chips' }, h('button', { class: 'chip' + (kind ? '' : ' on'), onclick: () => go({ kind: '' }) }, 'همه'), Object.entries(A().kinds).map(([k, t]) => h('button', { class: 'chip' + (kind === k ? ' on' : ''), onclick: () => go({ kind: k }) }, t)));
  const add = can('accounting', 2) ? [keyBtn('+ فاکتور فروش', 'F2', () => { location.hash = '#/invoice/new'; }, 'primary'), keyBtn('+ پیش‌فاکتور', 'F11', () => { location.hash = '#/invoice/new?kind=proforma'; }), keyBtn('از فاکتور آماده', 'F4', () => pickTemplate()), h('a', { class: 'btn', href: '#/invoice/new?kind=template' }, '+ فاکتور آماده')] : [];
  return h('div', {}, title('فاکتورها', ...add), h('div', { class: 'row' }, filterBar(q, '#/invoices', A().statuses, 'شماره، مشتری، کالا یا سریال…'), kinds),
    table(['نوع', 'تاریخ', 'مشتری / نام', 'وضعیت', 'ردیف', 'مبلغ', 'مانده'], r.items.map(v => clickRow('#/invoice/' + v.id, [kindPill(v), h('span', { class: 'lat' }, v.at_j), v.customer_name || v.title, stPill(v), v.lines, money(v.total),
      v.kind === 'sale' && v.status === 'final' ? (v.remaining ? h('b', { class: 'err' }, money(v.remaining)) : pill('تسویه', 'ok')) : '']))), pager(r.total, r.per, page, hrefOf));
}

/** The ready-made invoices: pick one and it becomes a new draft sale (or proforma) at today's prices, to change and finish. */
async function pickTemplate(kind) {
  if (!can('accounting', 2)) return;
  let r; try { r = await GET('staff/invoices' + qs({ kind: 'template', per: 50 })); } catch (e) { return fail(e); }
  modal('انتخاب از فاکتورهای آماده', (b, close) => {
    const to = select('to', { sale: 'فاکتور فروش', proforma: 'پیش‌فاکتور' }, kind || 'sale');
    const list = h('div', { class: 'results tall' }, r.items.length ? r.items.map((t, i) => h('button', { class: 'opt', type: 'button', autofocus: i === 0, onclick: async () => {
      try { const v = await POST('staff/invoices/copy', { id: t.id, kind: to.value }); close(); location.hash = '#/invoice/' + v.id; toast('از «' + t.title + '» ساخته شد؛ مشتری و قطعات را تغییر دهید.'); } catch (e) { fail(e); }
    } }, h('b', {}, t.title), ' ', h('span', { class: 'muted' }, t.lines + ' ردیف — ' + money(t.total)))) : h('p', { class: 'muted' }, 'هنوز فاکتور آماده‌ای نیست؛ یک فاکتور را «ذخیره به‌عنوان فاکتور آماده» کنید.'));
    b.append(field('بساز به‌عنوان', to), list);
    setTimeout(() => { const f = $('.opt', list); if (f) f.focus(); }, 0);
  }, true);
}

/* ---------- one invoice ---------- */
async function viewInvoice(id, q) {
  const isNew = id === 'new'; const kind0 = isNew ? (q.get('kind') || 'sale') : null;
  let v = { kind: kind0, status: 'draft', no: null, at: today(), valid_until: '', discount: 0, tax_pct: A().tax, notes: '', title: '', items: [], payments: [], total: 0, paid: 0 }, cust = null;
  if (!isNew) { v = await GET('staff/invoices/' + id); if (v.customer_id) cust = { id: v.customer_id, name: v.customer_name, mobile: v.customer_mobile }; }
  else if (q.get('cid')) cust = (await GET('staff/customers/' + q.get('cid'))).customer;
  const draft = v.status === 'draft', ro = !draft || !can('accounting', 2), tpl = v.kind === 'template', sale = v.kind === 'sale' || v.kind === 'return';
  const picker = tpl ? null : customerPicker(cust, ro);
  const f = { at: h('input', { type: 'date', value: v.at || today(), readOnly: ro }), valid: h('input', { type: 'date', value: v.valid_until || '', readOnly: ro }), title: text('title', v.title, { readOnly: ro, placeholder: tpl ? 'مثلاً: سیستم گیمینگ اقتصادی' : 'اختیاری' }),
    discount: text('discount', v.discount || '', { dir: 'ltr', readOnly: ro, oninput: () => recalc() }), tax: text('tax_pct', v.tax_pct, { dir: 'ltr', readOnly: ro, oninput: () => recalc() }), notes: area('notes', v.notes, { readOnly: ro, rows: 2 }) };
  const cols = [{ k: 'name', label: 'کالا یا خدمت (از انبار یا نام آزاد)', type: 'product', fill: { unit_price: 'sell_price', buy_price: 'buy_price', warranty_months: 'warranty_months' } }, { k: 'qty', label: 'تعداد', type: 'num', def: 1 }, { k: 'unit_price', label: 'قیمت واحد', type: 'num' }]
    .concat(S.cfg.costSeen ? [{ k: 'buy_price', label: 'قیمت خرید', type: 'num', readOnly: !S.cfg.costEdit }] : []).concat([{ k: 'serials', label: 'سریال‌ها (با , جدا)', ltr: true }, { k: 'warranty_months', label: 'گارانتی (ماه)', type: 'num' }]);
  const rows = rowsEditor(cols, v.items.map(x => Object.assign({}, x, { serials: (x.serials || []).join(', ') })), { readOnly: ro, onchange: () => recalc() });
  const out = { sub: h('b', { class: 'lat' }), tax: h('b', { class: 'lat' }), total: h('b', { class: 'lat' }), profit: h('b', { class: 'lat' }) };
  const recalc = () => {
    let s = 0, c = 0; rows.get().forEach(x => { const n = num(x.qty) || 1; s += n * num(x.unit_price); c += n * num(x.buy_price); });
    const d = Math.min(s, num(f.discount.value)), pct = Math.max(0, Math.min(100, parseFloat(digits(f.tax.value)) || 0)), t = Math.round((s - d) * pct / 100);
    out.sub.textContent = money(s); out.tax.textContent = money(t); out.total.textContent = money(s - d + t); out.profit.textContent = money(s - d - c);
  };
  // F3: type part of a name, Enter adds the first match (or one more of a line already there)
  const finder = h('input', { type: 'search', placeholder: 'جستجوی کالا در انبار… (Enter: افزودن)', autocomplete: 'off' }), found = h('div', { class: 'results' }); let hits = [];
  const addProduct = p => {
    const tr = rows.el.querySelector(`tr[data-pid="${p.id}"]`);
    if (tr) { const qn = tr.querySelector('[name=qty]'); qn.value = (num(qn.value) || 1) + 1; } else rows.add({ product_id: p.id, name: p.name, qty: 1, unit_price: p.sell_price, buy_price: p.buy_price, warranty_months: p.warranty_months });
    recalc(); finder.value = ''; clear(found); finder.focus();
  };
  finder.addEventListener('input', debounce(async () => {
    const qv = finder.value.trim(); clear(found); hits = []; if (qv.length < 3) return;
    try { hits = (await GET('staff/products' + qs({ q: qv, per: 8 }))).items; } catch (e) { return fail(e); }
    hits.forEach(p => found.append(h('button', { type: 'button', class: 'opt', onclick: () => addProduct(p) }, h('b', {}, p.name), ' ', stockPill(p.stock), ' ', h('span', { class: 'muted' }, money(p.sell_price)))));
  }, 200));
  finder.addEventListener('keydown', e => { if (e.key === 'Enter') { e.preventDefault(); if (hits[0]) addProduct(hits[0]); } });

  const body = () => {
    const b = { id: isNew ? 0 : v.id, updated: v.updated, at: f.at.value, discount: digits(f.discount.value), tax_pct: digits(f.tax.value), notes: f.notes.value, title: f.title.value,
      items: rows.get().map(x => priceNum(Object.assign(x, { serials: String(x.serials || '').split(/[,،\s]+/).filter(Boolean) }))) };
    if (isNew) b.kind = kind0;
    if (v.kind === 'proforma') b.valid_until = f.valid.value;
    return picker ? Object.assign(b, picker.body()) : b;
  };
  const save = async (quiet) => {
    if (ro) return v;
    try {
      const r = await POST('staff/invoices', body());
      if (isNew) { location.hash = '#/invoice/' + r.id; return null; }
      if (!quiet) { toast('ذخیره شد.'); route(); } return r;
    } catch (err) { if (err.status === 409) { toast(err.message, 'err'); if (await ask('فاکتور را دوباره بارگذاری کنم؟ (تغییرات این صفحه از بین می‌رود)', 'بارگذاری')) route(); } else fail(err); return null; }
  };
  const finalize = async () => {
    if (!sale || !draft || !can('accounting', 2)) return;
    const saved = await save(true); if (!saved) return;
    let makeBuild = false;
    const okd = await new Promise(res => modal('قطعی کردن ' + v.kindText, (b, close) => {
      const mb = check('make_build', false, 'سیستم نو است: به صف اسمبل برود (قطعات از همین فاکتور)');
      b.append(h('p', {}, 'مبلغ: ', h('b', {}, money(saved.total)), '. فاکتور قطعی دیگر تغییر نمی‌کند (فقط ابطال یا برگشت).'), v.kind === 'sale' && can('builds', 2) && !saved.build_id ? mb : null,
        h('div', { class: 'row end' }, h('button', { class: 'btn', onclick: () => { close(); res(false); } }, 'نه'), h('button', { class: 'btn primary', autofocus: true, onclick: () => { makeBuild = $('input', mb).checked; close(); res(true); } }, 'قطعی شود')));
    }));
    if (!okd) return;
    try {
      const r = await POST('staff/invoices/finalize', { id: saved.id, updated: saved.updated, make_build: makeBuild });
      toast(r.kindText + ' ' + r.no + ' قطعی شد.'); (r.warnings || []).forEach(w => toast(w, 'err')); if (r.build_id && makeBuild) toast('سیستم نو در صف اسمبل ساخته شد.');
      route();
    } catch (e) { fail(e); }
  };
  const act = async (path, body2, msg) => { try { const r = await POST(path, body2); toast(msg); location.hash = '#/invoice/' + r.id; route(); } catch (e) { fail(e); } };
  const voidIt = async () => modal('ابطال ' + v.kindText + ' ' + (v.no || ''), (b, close) => {
    const reason = text('reason', '', { placeholder: 'دلیل ابطال', required: true, autofocus: true });
    b.append(h('p', {}, 'شمارهٔ فاکتور باطل‌شده دیگر به کار نمی‌رود؛ پرداخت‌هایش برای مشتری می‌ماند.'), reason, h('div', { class: 'row end' }, h('button', { class: 'btn primary', onclick: async () => { if (!reason.value.trim()) return; close(); await act('staff/invoices/void', { id: v.id, updated: v.updated, reason: reason.value }, 'باطل شد.'); } }, 'باطل شود')));
  });
  const del = async () => { if (await ask('این پیش‌نویس پاک شود؟', 'پاک کن')) { try { await POST('staff/invoices/delete', { id: v.id, updated: v.updated }); toast('پاک شد.'); location.hash = '#/invoices'; } catch (e) { fail(e); } } };
  const pay = () => paymentDialog({ invoice: v, customer: cust });

  S.keys = { hash: location.hash, F3: () => { if (!ro) finder.focus(); }, F5: () => { const i = picker && $('input', picker.el); if (i) i.focus(); }, F6: () => sale && v.status === 'final' ? pay() : paymentDialog({ customer: cust }),
    F7: () => { if (!ro) f.discount.focus(); }, F8: () => save(), F9: () => finalize(), F10: () => printInvoice(v), F4: () => pickTemplate(v.kind === 'proforma' ? 'proforma' : 'sale') };

  const can2 = can('accounting', 2);
  const actions = h('div', { class: 'row' },
    !ro ? keyBtn('ذخیره', 'F8', () => save(), sale ? '' : 'primary') : null, !ro && sale ? keyBtn('قطعی', 'F9', finalize, 'primary') : null, !isNew && !tpl ? keyBtn('چاپ', 'F10', () => printInvoice(v)) : null,
    can2 && sale && v.status === 'final' && v.kind === 'sale' ? keyBtn('دریافت', 'F6', pay) : null,
    can2 && v.kind === 'proforma' && v.status === 'draft' && !isNew ? h('button', { class: 'btn primary', onclick: () => act('staff/invoices/copy', { id: v.id, kind: 'sale' }, 'فاکتور فروش از پیش‌فاکتور ساخته شد.') }, 'تبدیل به فاکتور فروش') : null,
    can2 && v.kind === 'sale' && v.status === 'final' ? h('button', { class: 'btn', onclick: () => act('staff/invoices/copy', { id: v.id, kind: 'return' }, 'پیش‌نویس برگشت ساخته شد؛ ردیف‌های برنگشته را حذف کنید.') }, 'برگشت از فروش') : null,
    can2 && tpl && !isNew ? h('button', { class: 'btn primary', onclick: () => act('staff/invoices/copy', { id: v.id, kind: 'sale' }, 'فاکتور فروش ساخته شد.') }, 'فاکتور فروش از این') : null,
    can2 && !tpl && !isNew ? h('button', { class: 'btn', onclick: () => act('staff/invoices/copy', { id: v.id, kind: 'template' }, 'به فاکتورهای آماده اضافه شد.') }, 'ذخیره به‌عنوان فاکتور آماده') : null,
    can2 && !isNew && (v.status === 'final' || (v.kind === 'proforma' && v.status === 'draft')) ? h('button', { class: 'btn', onclick: voidIt }, 'ابطال') : null,
    can2 && !isNew && (tpl || (draft && !v.no)) ? h('button', { class: 'btn', onclick: del }, 'حذف') : null);
  const head = isNew ? A().kinds[kind0] + ' تازه' : v.kindText + ' ' + (v.no || (tpl ? v.title : '(پیش‌نویس)'));
  const el = h('div', {}, title(head, !isNew ? stPill(v) : null, actions),
    v.status === 'void' ? h('p', { class: 'panel err' }, 'باطل شد: ' + v.void_reason) : null,
    v.build_id ? h('p', { class: 'panel' }, 'این فاکتور سیستم نو است: ', h('a', { href: '#/build/' + v.build_id }, 'باز کردن سیستم'), ' (موجودی با قطعات همان سیستم حساب می‌شود)') : null,
    v.wc_order_id ? h('p', { class: 'panel' }, 'از سفارش شمارهٔ ' + v.wc_order_id + ' سایت') : null,
    h('div', { class: 'panel grid' }, tpl ? field('نام فاکتور آماده', f.title, '', 'wide') : h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'مشتری ', h('small', { class: 'lat muted' }, 'F5')), picker.el),
      tpl ? null : field('تاریخ', f.at), v.kind === 'proforma' ? field('اعتبار تا', f.valid) : null,
      ro ? null : h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'افزودن کالا ', h('small', { class: 'lat muted' }, 'F3')), finder, found),
      h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'ردیف‌ها'), rows.el),
      h('div', { class: 'wide money' }, field('تخفیف (تومان) — F7', f.discount), field('مالیات بر ارزش افزوده (٪)', f.tax), h('div', { class: 'sum' }, h('span', {}, 'جمع ردیف‌ها'), out.sub), h('div', { class: 'sum' }, h('span', {}, 'مالیات'), out.tax),
        h('div', { class: 'sum big' }, h('span', {}, 'مبلغ نهایی'), out.total), S.cfg.costSeen ? h('div', { class: 'sum' }, h('span', {}, 'سود (بدون مالیات)'), out.profit) : null,
        !isNew && sale && v.status === 'final' ? h('div', { class: 'sum' }, h('span', {}, 'پرداخت‌شده / مانده'), h('b', { class: 'lat' }, money(v.paid) + ' / ' + money(v.total - v.paid))) : null),
      tpl ? null : field('یادداشت', f.notes, '', 'wide')));
  if (v.payments && v.payments.length) el.append(h('section', { class: 'panel' }, h('h3', {}, 'پرداخت‌های این فاکتور'), paymentsTable(v.payments)));
  recalc(); return el;
}

/* ---------- printing ---------- */
function printInvoice(v) {
  if (!v.id) return;
  const a = A(), p = prefs.get(), proforma = v.kind === 'proforma';
  const node = h('div', { class: 'receipt invoice' }, h('header', {}, h('div', {}, h('h1', {}, a.shop || p.shop), a.phone || p.phone ? h('div', { class: 'lat' }, a.phone || p.phone) : null, a.address ? h('div', {}, a.address) : null),
    h('div', {}, h('h2', {}, v.kindText), h('div', {}, 'شماره: ', h('b', { class: 'lat' }, v.no || '—')), h('div', {}, 'تاریخ: ', h('span', { class: 'lat' }, v.at_j)), proforma && v.valid_until_j ? h('div', {}, 'اعتبار تا: ', h('span', { class: 'lat' }, v.valid_until_j)) : null)),
    h('dl', {}, [['خریدار', v.customer_name], ['موبایل', v.customer_mobile], ['کد ملی', v.customer_national_id], ['نشانی', v.customer_address]].filter(x => x[1]).map(x => [h('dt', {}, x[0]), h('dd', {}, x[1])])),
    h('table', { class: 'lines' }, h('thead', {}, h('tr', {}, ['#', 'شرح', 'تعداد', 'قیمت واحد', 'جمع'].map(x => h('th', {}, x)))),
      h('tbody', {}, v.items.map((x, i) => h('tr', {}, h('td', {}, i + 1), h('td', {}, x.name, x.serials.length ? h('div', { class: 'lat small' }, x.serials.join(', ')) : null, x.warranty_months ? h('div', { class: 'small' }, x.warranty_months + ' ماه گارانتی') : null), h('td', {}, x.qty), h('td', { class: 'lat' }, money(x.unit_price)), h('td', { class: 'lat' }, money(x.line_total)))))),
    h('dl', { class: 'totals' }, [['جمع', v.subtotal], v.discount ? ['تخفیف', v.discount] : null, v.tax ? ['مالیات بر ارزش افزوده (' + v.tax_pct + '٪)', v.tax] : null, ['مبلغ قابل پرداخت', v.total],
      v.kind === 'sale' && v.status === 'final' ? ['پرداخت‌شده', v.paid] : null, v.kind === 'sale' && v.status === 'final' && v.total - v.paid ? ['مانده', v.total - v.paid] : null].filter(Boolean).map(x => [h('dt', {}, x[0]), h('dd', { class: 'lat' }, money(x[1]))])),
    v.status === 'void' ? h('p', { class: 'foot' }, 'باطل شده') : null, v.notes ? h('p', {}, v.notes) : null, h('p', { class: 'foot' }, a.foot || p.foot), h('div', { class: 'sign' }, h('span', {}, 'مهر و امضای فروشنده'), h('span', {}, 'امضای خریدار')));
  printOverlay(v.kindText + ' ' + (v.no || ''), node, `@page{size:${p.paper};margin:10mm}`);
}

/* ---------- payments ---------- */
const paymentsTable = items => table(['تاریخ', 'نوع', 'طرف حساب / سرفصل', 'روش', 'حساب', 'مبلغ', 'فاکتور'], items.map(p => h('tr', { class: can('accounting', 2) ? 'click' : '', onclick: () => can('accounting', 2) && paymentDialog({ payment: p }) },
  h('td', { class: 'lat' }, p.at_j), h('td', {}, p.direction === 'in' ? pill('دریافت', 'ok') : pill('پرداخت', 'bad')), h('td', {}, p.customer_name || p.vendor_name || p.category), h('td', {}, p.methodText, p.cheque ? [' ', h('span', { class: 'lat' }, p.cheque.no), ' ', pill(p.cheque.statusText, p.cheque.status === 'bounced' ? 'bad' : '')] : null),
  h('td', {}, p.account), h('td', { class: 'lat' }, money(p.amount)), h('td', {}, p.invoice_id ? h('a', { href: '#/invoice/' + p.invoice_id, onclick: e => e.stopPropagation() }, p.invoice_no || 'باز کردن') : ''))), 'پرداختی نیست.');

async function viewPayments(q) {
  const page = Math.max(1, +q.get('page') || 1), r = await GET('staff/payments' + qs({ q: q.get('q'), direction: q.get('direction'), expense: q.get('expense'), page }));
  const hrefOf = p => { const x = new URLSearchParams(q); x.set('page', p); return '#/payments?' + x; };
  const chip = (label, o) => { const on = Object.entries(o).every(([k, v2]) => (q.get(k) || '') === v2); return h('button', { class: 'chip' + (on ? ' on' : ''), onclick: () => { location.hash = '#/payments' + qs(o); } }, label); };
  const add = can('accounting', 2) ? [keyBtn('+ دریافت', 'F6', () => paymentDialog({ direction: 'in' }), 'primary'), h('button', { class: 'btn', onclick: () => paymentDialog({ direction: 'out' }) }, '+ پرداخت'), h('button', { class: 'btn', onclick: () => paymentDialog({ direction: 'out', expense: true }) }, '+ هزینه')] : [];
  return h('div', {}, title('دریافت و پرداخت', ...add), h('div', { class: 'row' }, filterBar(q, '#/payments', null, 'مشتری، فروشنده، شمارهٔ تراکنش، شرح…'), h('div', { class: 'chips' }, chip('همه', { direction: '', expense: '' }), chip('دریافت‌ها', { direction: 'in', expense: '' }), chip('پرداخت‌ها', { direction: 'out', expense: '' }), chip('هزینه‌ها', { direction: '', expense: '1' }))),
    paymentsTable(r.items), pager(r.total, r.per, page, hrefOf));
}

/**
 * Receive or pay money. o.invoice (with o.customer): a payment of that invoice; o.customer: a deposit of that customer; o.vendor; o.expense; o.payment: change that one.
 * A cheque payment asks the cheque's number, bank and due date and enters the cheque too.
 */
async function paymentDialog(o) {
  const old = o.payment || null, inv = o.invoice || null;
  let vendors = []; try { vendors = (await GET('staff/suppliers')).items; } catch (e) { }
  modal(old ? 'ویرایش پرداخت' : inv ? 'دریافت برای ' + inv.kindText + ' ' + inv.no : 'دریافت / پرداخت', (b, close) => {
    const dir = select('direction', { in: 'دریافت (پول آمد)', out: 'پرداخت (پول رفت)' }, old ? old.direction : (o.direction || 'in'), { disabled: !!inv });
    const party = select('party', { customer: 'مشتری', vendor: 'فروشنده / تأمین‌کننده', expense: 'هزینه / سایر (بدون طرف حساب)' }, old ? (old.customer_id ? 'customer' : old.vendor_id ? 'vendor' : 'expense') : (o.expense ? 'expense' : o.vendor ? 'vendor' : 'customer'), { disabled: !!inv });
    const cust = customerPicker(old && old.customer_id ? { id: old.customer_id, name: old.customer_name } : (o.customer || (inv && inv.customer_id ? { id: inv.customer_id, name: inv.customer_name } : null)), !!inv);
    const vend = select('vendor_id', Object.assign({ 0: '— انتخاب کنید —' }, Object.fromEntries(vendors.map(x => [x.id, x.name]))), old ? old.vendor_id : (o.vendor || 0));
    const cat = text('category', old ? old.category : '', { list: 'expl', placeholder: 'اجاره، حقوق، …' }), expl = h('datalist', { id: 'expl' }, (A().expenses || []).map(x => h('option', { value: x })));
    const amount = text('amount', old ? old.amount : (inv ? Math.max(0, inv.total - inv.paid) : ''), { dir: 'ltr', required: true, autofocus: true });
    const method = select('method', A().methods, old ? old.method : 'cash'), account = text('account', old ? old.account : '', { list: 'accl' }), accl = h('datalist', { id: 'accl' }, (S.cfg.accounts || []).map(x => h('option', { value: x })));
    const at = h('input', { type: 'date', value: old ? old.at : today() }), tx = text('tx', old ? old.tx : '', { dir: 'ltr' }), notes = text('notes', old ? old.notes : '');
    const cq = { no: text('cno', old && old.cheque ? old.cheque.no : '', { dir: 'ltr', placeholder: 'شمارهٔ صیادی یا سری' }), bank: text('cbank', ''), due: h('input', { type: 'date', value: old && old.cheque ? old.cheque.due_at : '' }) };
    const chequeBox = h('div', { class: 'three' }, field('شمارهٔ چک', cq.no), field('بانک', cq.bank), field('سررسید', cq.due));
    const boxes = { customer: h('div', {}, cust.el), vendor: field('فروشنده', vend), expense: field('سرفصل', cat) };
    const show = () => { Object.entries(boxes).forEach(([k, el]) => { el.style.display = party.value === k ? '' : 'none'; }); chequeBox.style.display = method.value === 'cheque' ? '' : 'none'; };
    party.addEventListener('change', show); method.addEventListener('change', show);
    const save = async () => {
      const body = { id: old ? old.id : 0, updated: old ? old.updated : undefined, direction: dir.value, amount: digits(amount.value), method: method.value, account: account.value, at: at.value, tx: digits(tx.value), notes: notes.value, invoice_id: inv ? inv.id : (old ? old.invoice_id : 0) };
      if (party.value === 'customer') { const cb = cust.body(); if (!cb.customer_id) return toast('مشتری را از فهرست انتخاب کنید.', 'err'); body.customer_id = cb.customer_id; body.vendor_id = 0; }
      else if (party.value === 'vendor') { body.vendor_id = +vend.value; body.customer_id = 0; if (!body.vendor_id) return toast('فروشنده را انتخاب کنید.', 'err'); }
      else { body.customer_id = 0; body.vendor_id = 0; body.category = cat.value; }
      if (method.value === 'cheque') body.cheque = { no: digits(cq.no.value), bank: cq.bank.value, due_at: cq.due.value };
      try { await POST('staff/payments', body); close(); toast('ثبت شد.'); route(); } catch (e) { fail(e); }
    };
    b.append(h('div', { class: 'grid' }, field('نوع', dir), field('طرف حساب', party), h('div', { class: 'wide' }, boxes.customer, boxes.vendor, boxes.expense), field('مبلغ (تومان)', amount), field('روش', method),
      h('div', { class: 'wide' }, chequeBox), field('حساب / صندوق', account, 'کارتخوان، حساب بانکی یا صندوق'), field('تاریخ', at), field('شمارهٔ پیگیری', tx), field('شرح', notes), expl, accl),
      h('div', { class: 'row end' }, old && can('accounting', 2) ? h('button', { class: 'btn', onclick: async () => { if (await ask('این پرداخت پاک شود؟', 'پاک کن')) { try { await POST('staff/payments/delete', { id: old.id }); close(); toast('پاک شد.'); route(); } catch (e) { fail(e); } } } }, 'حذف') : null,
        h('button', { class: 'btn', onclick: close }, 'انصراف'), h('button', { class: 'btn primary', onclick: save }, 'ثبت')));
    b.addEventListener('keydown', e => { if (e.key === 'Enter' && e.target.tagName === 'INPUT') { e.preventDefault(); save(); } });
    show();
  }, true);
}

/* ---------- cheques ---------- */
const chequeRow = q => h('tr', {}, h('td', {}, h('span', { class: 'lat' }, q.due_at_j), ' ', q.status === 'pending' ? (q.days < 0 ? pill(-q.days + ' روز گذشته', 'bad') : q.days <= A().remind ? pill(q.days ? q.days + ' روز مانده' : 'امروز', 'bad') : h('small', { class: 'muted' }, q.days + ' روز')) : null),
  h('td', {}, q.direction === 'out' ? pill('پرداختنی', 'bad') : pill('دریافتنی', 'ok')), h('td', { class: 'lat' }, q.no), h('td', {}, q.bank), h('td', {}, q.customer_name || q.vendor_name || q.party), h('td', { class: 'lat' }, money(q.amount)), h('td', {}, pill(q.statusText, q.status === 'cleared' ? 'ok' : q.status === 'bounced' ? 'bad' : '')),
  h('td', {}, can('accounting', 2) && q.status === 'pending' ? [h('button', { class: 'btn sm', onclick: () => chequeStatus(q, 'cleared') }, 'پاس شد'), ' ', h('button', { class: 'btn sm', onclick: () => chequeStatus(q, 'bounced') }, 'برگشت خورد'), ' '] : null,
    can('accounting', 2) ? h('button', { class: 'btn sm', onclick: () => chequeDialog(q) }, 'ویرایش') : null));
const chequesTable = items => table(['سررسید', 'نوع', 'شماره', 'بانک', 'طرف حساب', 'مبلغ', 'وضعیت', ''], items.map(chequeRow), 'چکی نیست.');
async function chequeStatus(q, st) { try { await POST('staff/cheques', { id: q.id, updated: q.updated, status: st }); toast('چک ' + q.no + ': ' + A().chequeStatuses[st]); route(); } catch (e) { fail(e); } }

async function viewCheques(q) {
  const page = Math.max(1, +q.get('page') || 1), r = await GET('staff/cheques' + qs({ q: q.get('q'), due: q.get('due'), status: q.get('status'), direction: q.get('direction'), page }));
  const hrefOf = p => { const x = new URLSearchParams(q); x.set('page', p); return '#/cheques?' + x; };
  const chip = (label, o) => { const on = Object.entries(o).every(([k, v2]) => (q.get(k) || '') === v2) && [...q.keys()].filter(k => k !== 'q' && k !== 'page').every(k => k in o); return h('button', { class: 'chip' + (on ? ' on' : ''), onclick: () => { location.hash = '#/cheques' + qs(o); } }, label); };
  return h('div', {}, title('چک‌ها', can('accounting', 2) ? h('button', { class: 'btn primary', onclick: () => chequeDialog(null) }, '+ چک') : null),
    h('div', { class: 'row' }, filterBar(q, '#/cheques', null, 'شمارهٔ چک، بانک، طرف حساب…'), h('div', { class: 'chips' }, chip('همه', {}), chip('نزدیک سررسید', { due: 'soon' }), chip('سررسید گذشته', { due: 'late' }), chip('دریافتنی', { direction: 'in' }), chip('پرداختنی', { direction: 'out' }),
      Object.entries(A().chequeStatuses).map(([k, t]) => chip(t, { status: k })))), chequesTable(r.items), pager(r.total, r.per, page, hrefOf));
}

/** A cheque on its own (a guarantee cheque, or one entered before its payment): "also a payment" enters the payment of that customer or supplier with it. */
async function chequeDialog(q) {
  let vendors = []; try { vendors = (await GET('staff/suppliers')).items; } catch (e) { }
  modal(q ? 'چک ' + q.no : 'چک تازه', (b, close) => {
    const dir = select('direction', { in: 'دریافتنی (از دیگران گرفتیم)', out: 'پرداختنی (چک خودمان)' }, q ? q.direction : 'out');
    const f = { no: text('no', q ? q.no : '', { dir: 'ltr', required: true, autofocus: true }), bank: text('bank', q ? q.bank : ''), account: text('account', q ? q.account : '', { placeholder: 'حساب جاری ما (برای چک خودمان)' }), amount: text('amount', q ? q.amount : '', { dir: 'ltr' }),
      issued: h('input', { type: 'date', value: q ? q.issued_at : today() }), due: h('input', { type: 'date', value: q ? q.due_at : '' }), party: text('party', q ? q.party : '', { placeholder: 'اگر مشتری یا فروشندهٔ ثبت‌شده نیست' }), notes: text('notes', q ? q.notes : ''),
      status: select('status', A().chequeStatuses, q ? q.status : 'pending'), vendor: select('vendor_id', Object.assign({ 0: '—' }, Object.fromEntries(vendors.map(x => [x.id, x.name]))), q ? q.vendor_id : 0) };
    const cust = customerPicker(q && q.customer_id ? { id: q.customer_id, name: q.customer_name } : null, false), pay = check('pay', !q, 'به‌عنوان دریافت / پرداخت همین طرف حساب هم ثبت شود');
    const save = async () => {
      const cb = cust.body(); const body = { id: q ? q.id : 0, updated: q ? q.updated : undefined, direction: dir.value, no: digits(f.no.value), bank: f.bank.value, account: f.account.value, amount: digits(f.amount.value), issued_at: f.issued.value, due_at: f.due.value,
        party: f.party.value, notes: f.notes.value, status: f.status.value, vendor_id: +f.vendor.value, customer_id: cb.customer_id || 0, pay: !q && $('input', pay).checked };
      try { await POST('staff/cheques', body); close(); toast('ثبت شد.'); route(); } catch (e) { fail(e); }
    };
    b.append(h('div', { class: 'grid' }, field('نوع', dir), field('شمارهٔ چک', f.no), field('بانک', f.bank), field('مبلغ (تومان)', f.amount), field('تاریخ صدور', f.issued), field('سررسید', f.due), field('وضعیت', f.status), field('حساب', f.account),
      h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'مشتری (اگر هست)'), cust.el), field('فروشنده (اگر هست)', f.vendor), field('یا نام طرف حساب', f.party), field('شرح', f.notes, '', 'wide'), q ? null : h('div', { class: 'wide' }, pay)),
      h('div', { class: 'row end' }, h('button', { class: 'btn', onclick: close }, 'انصراف'), h('button', { class: 'btn primary', onclick: save }, 'ثبت')));
  }, true);
}

/** Home: the cheques due within the reminder days (and those past due), filled in when the site answers (the home page does not wait for it). */
function duePanel() {
  const box = h('div');
  GET('staff/acc/due').then(r => {
    if (!r.items.length) return;
    if (!S.dueShown) { S.dueShown = true; toast(r.items.length + ' چک نزدیک سررسید است.', 'err'); }
    box.append(h('section', { class: 'panel' }, h('h3', {}, 'چک‌های نزدیک سررسید (' + r.items.length + ')'), chequesTable(r.items)));
  }, () => { });
  return box;
}

/* ---------- a customer's account (on the customer page) ---------- */
async function accountSection(c) {
  let r; try { r = await GET('staff/customers/' + c.id + '/account'); } catch (e) { return h('p', { class: 'err' }, e.message); }
  const bal = r.balance, sec = h('section', { class: 'panel' }, h('div', { class: 'title' }, h('h3', {}, 'حساب مشتری'),
    h('div', { class: 'row' }, pill((bal > 0 ? 'بدهکار ' : bal < 0 ? 'بستانکار ' : 'تسویه ') + money(Math.abs(bal)), bal > 0 ? 'bad' : 'ok'), r.site ? pill('حساب سایت: ' + r.site.login) : null,
      can('accounting', 2) ? h('a', { class: 'btn', href: '#/invoice/new?cid=' + c.id }, '+ فاکتور') : null, can('accounting', 2) || can('payments', 2) ? h('button', { class: 'btn primary', onclick: () => paymentDialog({ customer: c, direction: 'in' }) }, '+ واریزی') : null)),
    table(['تاریخ', 'شرح', 'بدهکار', 'بستانکار', 'مانده'], r.ledger.map(x => h('tr', { class: 'click' + (x.dead ? ' muted' : ''), onclick: () => { if (x.ref === 'invoice') location.hash = '#/invoice/' + x.id; else if (x.ref === 'job') location.hash = '#/job/' + x.id; } },
      h('td', { class: 'lat' }, x.at_j), h('td', {}, x.text), h('td', { class: 'lat' }, x.debit ? money(x.debit) : ''), h('td', { class: 'lat' }, x.credit ? money(x.credit) : ''), h('td', { class: 'lat' }, money(x.balance)))), 'گردش مالی ثبت نشده است.'));
  const out = h('div', {}, sec);
  if (r.invoices.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'فاکتورها و پیش‌فاکتورها'), table(['نوع', 'تاریخ', 'وضعیت', 'مبلغ', 'مانده'], r.invoices.map(v => clickRow('#/invoice/' + v.id, [kindPill(v), h('span', { class: 'lat' }, v.at_j), stPill(v), money(v.total), v.kind === 'sale' && v.status === 'final' ? money(v.remaining) : ''])))));
  if (r.payments.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'واریزی‌ها و پرداخت‌ها'), paymentsTable(r.payments)));
  if (r.cheques.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'چک‌ها'), chequesTable(r.cheques)));
  return out;
}

/* ---------- the period's money ---------- */
async function viewAccSummary(q) {
  const r = await GET('staff/acc/summary' + qs({ period: q.get('period'), from: q.get('from'), to: q.get('to') }));
  const card = (label, v, note) => h('div', { class: 'card' }, h('b', { class: 'lat' }, v), h('span', {}, label), note ? h('small', { class: 'err' }, note) : null);
  const m = v => Number(v).toLocaleString('en-US');
  const ins = r.flow.filter(x => x.direction === 'in'), outs = r.flow.filter(x => x.direction === 'out');
  return h('div', {}, title('خلاصهٔ مالی', h('span', { class: 'muted' }, r.from_j + ' تا ' + r.to_j), h('span', { class: 'pill' }, 'مبالغ به تومان')),
    h('div', { class: 'chips filters' }, Object.entries(r.periods).map(([k, p]) => h('button', { class: 'chip' + (q.get('period') === k || (!q.get('period') && k === 'month') ? ' on' : ''), onclick: () => { location.hash = '#/acc' + qs({ period: k }); } }, p.label))),
    h('div', { class: 'cards' }, card('فروش (' + r.sales.count + ' فاکتور)', m(r.sales.total)), card('برگشت از فروش', m(r.returns.total)), card('فروش خالص', m(r.net)), r.profit != null ? card('سود فاکتورها', m(r.profit), r.missing_cost ? r.missing_cost + ' ردیف قیمت خرید ندارد' : '') : null,
      card('دریافتی', m(ins.reduce((s, x) => s + x.amount, 0))), card('پرداختی', m(outs.reduce((s, x) => s + x.amount, 0))), card('هزینه‌ها', m(r.expenses.reduce((s, x) => s + x.amount, 0)))),
    h('div', { class: 'two' }, h('section', { class: 'panel' }, h('h3', {}, 'دریافت و پرداخت به روش'), table(['روش', 'دریافت', 'پرداخت'], Object.keys(A().methods).filter(k => r.flow.some(x => x.method === k)).map(k => h('tr', {}, h('td', {}, A().methods[k]),
      h('td', { class: 'lat' }, m((ins.find(x => x.method === k) || { amount: 0 }).amount)), h('td', { class: 'lat' }, m((outs.find(x => x.method === k) || { amount: 0 }).amount)))))),
      h('section', { class: 'panel' }, h('h3', {}, 'هزینه‌ها'), table(['سرفصل', 'مبلغ'], r.expenses.map(x => h('tr', {}, h('td', {}, x.category), h('td', { class: 'lat' }, m(x.amount))))))),
    h('div', { class: 'two' }, h('section', { class: 'panel' }, h('h3', {}, 'موجودی حساب‌ها (از ابتدا)'), table(['حساب', 'مانده'], r.accounts.map(x => h('tr', {}, h('td', {}, x.account), h('td', { class: 'lat' }, m(x.balance)))))),
      h('section', { class: 'panel' }, h('h3', {}, 'بیشترین بدهی مشتریان'), table(['مشتری', 'موبایل', 'بدهی'], r.debtors.map(x => clickRow('#/customer/' + x.customer_id, [x.name, h('span', { class: 'lat' }, x.mobile), money(x.balance)]))))),
    r.cheques.length ? h('section', { class: 'panel' }, h('h3', {}, 'چک‌های نزدیک سررسید'), chequesTable(r.cheques)) : null,
    h('p', { class: 'muted' }, 'سود فاکتورها = فروش − قیمت خرید ردیف‌ها − تخفیف (بدون مالیات). موجودی حساب‌ها از همهٔ دریافت‌ها و پرداخت‌ها و پرداخت‌های سرویس است؛ چک تا پاس نشود در حساب نیست.'));
}
