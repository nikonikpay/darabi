// What was said with the customer of a computer or a service (when, why, what came of it, when to call again), today's follow-ups, and the offers whose parts the stock is short of.

const stamp = () => new Date().toLocaleString('fa-IR-u-nu-latn', { dateStyle: 'short', timeStyle: 'short' });

/** The calls of a case, newest first, with the line that writes a new one. kind: 'build' or 'job'. */
function callsPanel(kind, rec) {
  const may = can(kind + 's') && (can(kind + 's', 2) || can('customers', 2));
  const calls = (rec.calls || []).slice(), list = h('div', { class: 'timeline' });
  const row = c => h('div', { class: 'call' }, h('div', { class: 'when' }, h('span', { class: 'lat' }, c.at_j), h('small', { class: 'muted' }, c.who)),
    h('div', {}, h('b', {}, c.resultText), h('span', { class: 'muted' }, ' — ' + c.reasonText), c.note ? h('p', {}, c.note) : null,
      c.next_at_j ? pill('پیگیری بعدی: ' + c.next_at_j, 'warn') : null, c.waiting ? pill('در صف فرستادن') : null));
  const draw = () => { clear(list).append(...(calls.length ? calls.slice().reverse().map(row) : [h('p', { class: 'muted' }, 'هنوز گفتگویی ثبت نشده است.')])); };
  draw();
  const head = h('h3', {}, 'پیگیری و گفتگو با مشتری', rec.customer_mobile ? h('span', { class: 'lat muted' }, '  ' + rec.customer_mobile) : null);
  if (!may) return h('section', { class: 'panel calls' }, head, list);
  const f = { reason: select('reason', S.cfg.callReasons || {}, kind === 'build' ? 'ready' : 'quote'), result: select('result', Object.assign({ '': '— نتیجه —' }, S.cfg.callResults || {}), ''),
    note: text('note', '', { placeholder: 'چه گفته شد؟ مثلاً: هزینه را تأیید کرد، فردا عصر می‌آید' }), next: h('input', { type: 'date', name: 'next_at' }) };
  const save = async e => {
    e.preventDefault(); if (!f.result.value) { f.result.focus(); return toast('نتیجهٔ تماس را انتخاب کنید.', 'err'); }
    const body = { kind, ref: rec.id, reason: f.reason.value, result: f.result.value, note: f.note.value, next_at: f.next.value, at: new Date().toISOString() };
    try {
      const r = await POST('staff/calls', body);
      calls.push(r.queued ? { at_j: stamp(), who: S.me.name, resultText: S.cfg.callResults[body.result], reasonText: S.cfg.callReasons[body.reason], note: body.note, next_at_j: body.next_at, waiting: true } : r);
      f.result.value = ''; f.note.value = ''; f.next.value = ''; draw(); if (!r.queued) toast('ثبت شد.');
    } catch (err) { fail(err); }
  };
  return h('section', { class: 'panel calls' }, head,
    h('form', { class: 'callform', onsubmit: save }, field('موضوع', f.reason), field('نتیجه', f.result), field('توضیح', f.note, '', 'grow'), field('پیگیری بعدی (اگر لازم است)', f.next), h('button', { class: 'btn primary' }, 'ثبت گفتگو')), list);
}

/** The home page's list: whom to call again today (or earlier and not yet done). */
function followupsPanel() {
  const box = h('div');
  if (can('jobs') || can('builds')) GET('staff/followups').then(r => {
    if (r.items.length) box.append(h('section', { class: 'panel accent-warn' }, h('h3', {}, 'پیگیری‌های امروز (' + r.items.length + ')'),
      table(['قرار', 'پرونده', 'مشتری', 'موبایل', 'آخرین گفتگو'], r.items.map(x => clickRow(`#/${x.kind}/${x.record}`, [h('span', { class: 'lat' }, x.next_at_j), (x.kind === 'job' ? 'سرویس ' : 'سیستم ') + (x.number || ''), x.customer_name,
        h('span', { class: 'lat' }, x.customer_mobile), x.resultText + (x.note ? ' — ' + x.note : '')])))));
  }, () => { });
  return box;
}

/** The warning on an offer whose parts are not all on the shelf. */
function shortNote(v) {
  if (!v.short || !v.short.length) return null;
  return h('div', { class: 'panel accent-bad', role: 'alert' }, h('b', {}, 'کمبود انبار: '), v.short.map(s => `${s.name} (لازم ${s.qty}، موجود ${s.stock})`).join('، '),
    h('small', { class: 'muted' }, '  قطعه را در ردیف‌ها عوض کنید یا درخواست خرید بدهید.'));
}

/** On the shortage page: each part the ready-made invoices and open proformas need more of than the stock holds. Worked out each time, so a swapped part leaves the list by itself. */
function offerShortPanel() {
  const box = h('div');
  if (can('accounting')) Promise.all([GET('staff/invoices?kind=template&per=50'), GET('staff/invoices?kind=proforma&status=draft&per=50')]).then(([t, p]) => {
    const parts = new Map();
    for (const v of [...t.items, ...p.items]) for (const s of v.short || []) {
      const e = parts.get(s.product_id) || parts.set(s.product_id, { name: s.name, stock: s.stock, need: 0, offers: [] }).get(s.product_id);
      e.need = Math.max(e.need, s.qty); e.offers.push(v);
    }
    box.append(h('section', { class: 'panel' + (parts.size ? ' accent-bad' : '') }, h('h3', {}, 'کمبود فاکتورهای آماده و پیش‌فاکتورهای باز'),
      table(['قطعه', 'موجودی', 'لازم', 'در این فاکتورها', ''], [...parts.values()].map(x => h('tr', {}, h('td', {}, h('b', {}, x.name)), h('td', { class: 'lat' }, x.stock), h('td', { class: 'lat' }, x.need),
        h('td', {}, x.offers.map(v => h('a', { class: 'pill', href: '#/invoice/' + v.id }, v.title || v.customer_name || ('#' + (v.no || v.id))))),
        h('td', {}, h('button', { class: 'btn sm', onclick: () => shortageAsk({ name: x.name, kind: 'invoice' }) }, 'درخواست خرید')))), 'همهٔ قطعه‌های فاکتورهای آماده و پیش‌فاکتورهای باز موجودند.')));
  }, () => { });
  return box;
}
