// Shortages (parts the shop lacks, asked for by staff and answered by whoever buys), the warranty shipments, a party's account laid out plainly, and the daily backup page.
NAV.push(['#/shortages', 'کسری انبار', () => true], ['#/ships', 'گارانتی', () => can('ships')], ['#/accounts', 'حساب‌ها', () => can('accounting')], ['#/backup', 'پشتیبان‌گیری', () => !!S.me && (S.me.admin || can('accounting', 2))]);
routes.push([/^\/shortages$/, (m, q) => viewShortages(q)], [/^\/ships$/, (m, q) => viewShips(q)], [/^\/accounts$/, (m, q) => viewAccounts(q)], [/^\/backup$/, () => viewBackup()]);

const dateIn = v => h('input', { type: 'date', value: v || '' });
const latDate = iso => { const d = new Date(iso); return isNaN(d) ? '' : d.toLocaleString('fa-IR-u-nu-latn', { dateStyle: 'short', timeStyle: 'short' }); };
const chipsOf = (q, base, items, def) => h('div', { class: 'chips' }, items.map(([k, label]) => h('button', { class: 'chip' + ((q.get('status') || def) === k ? ' on' : ''), onclick: () => { location.hash = base + qs({ status: k }); } }, label)));

/* ---------- shortages ---------- */
const SHORT_TONE = { open: 'bad', looking: '', bought: 'ok', arrived: 'ok', cancelled: '' };

/** Anyone asks for a part: what, how many, and for which invoice, service or new system, so the buyer knows what it is for. */
function shortageAsk(o) {
  modal('درخواست قطعه (کسری انبار)', (b, close) => {
    const f = { name: text('name', o.name || '', { required: true, autofocus: true, placeholder: 'مثلاً: پردازندهٔ Ryzen 5 7600' }), qty: text('qty', 1, { dir: 'ltr' }),
      kind: select('kind', { invoice: 'فاکتور', job: 'سرویس', build: 'سیستم نو', other: 'سایر' }, o.kind || 'other'), ref: text('ref', o.ref || '', { placeholder: 'شمارهٔ فاکتور، سرویس یا سیستم' }), note: text('note', '', { placeholder: 'مدل دقیق، رنگ، عجله …' }) };
    const save = async () => {
      if (!f.name.value.trim()) return toast('نام قطعه را بنویسید.', 'err');
      try { await POST('staff/shortages', { name: f.name.value, qty: digits(f.qty.value), kind: f.kind.value, ref: f.ref.value, customer_id: o.customer_id || 0, note: f.note.value }); close(); toast('درخواست ثبت شد؛ انبار و حسابداری خبردار می‌شوند.'); if (/^#\/shortages/.test(location.hash)) route(); shortBadge(); } catch (e) { fail(e); }
    };
    b.append(h('div', { class: 'grid' }, field('قطعه', f.name, '', 'wide'), field('تعداد', f.qty), field('برای', f.kind), field('شمارهٔ فاکتور / سرویس / سیستم', f.ref, 'تا خریدار بداند برای کدام کار است.'), field('توضیح', f.note, '', 'wide')),
      h('div', { class: 'row end' }, h('button', { class: 'btn', onclick: close }, 'انصراف'), h('button', { class: 'btn primary', onclick: save }, 'ثبت درخواست')));
    b.addEventListener('keydown', e => { if (e.key === 'Enter' && e.target.tagName === 'INPUT') { e.preventDefault(); save(); } });
  });
}

/** The buyer's answer: how far it is, who it was bought from, and a note ("خریداری شد از قلی‌خانی"). */
async function shortageAnswer(x) {
  let vendors = []; try { vendors = (await GET('staff/suppliers')).items; } catch (e) { }
  modal('کسری: ' + x.name, (b, close) => {
    const st = select('status', { open: 'درخواست تازه', looking: 'در حال پیدا کردن', bought: 'خریداری شد', arrived: 'رسید', cancelled: 'لغو شد' }, x.status);
    const vend = select('vendor_id', Object.assign({ 0: '— هنوز مشخص نیست —' }, Object.fromEntries(vendors.map(v => [v.id, v.name]))), x.vendor_id), note = text('note', x.note, { placeholder: 'مثلاً: از قلی‌خانی گرفته شد، فردا می‌رسد' }), qty = text('qty', x.qty, { dir: 'ltr' });
    const save = async () => { try { await POST('staff/shortages', { id: x.id, updated: x.updated, status: st.value, vendor_id: +vend.value, note: note.value, qty: digits(qty.value) }); close(); toast('ثبت شد.'); route(); shortBadge(); } catch (e) { if (e.status === 409) { close(); route(); } fail(e); } };
    b.append(h('p', {}, h('b', {}, x.qty + ' × ' + x.name), ' — برای ', h('b', {}, (x.kindText || '') + (x.ref ? ' ' + x.ref : '')), x.customer_name ? ' (' + x.customer_name + ')' : '', h('br'), h('small', { class: 'muted' }, 'درخواست ' + x.who + ' در ' + x.created_j)),
      h('div', { class: 'grid' }, field('وضعیت', st), field('تعداد', qty), field('خرید از', vend), field('یادداشت', note, '', 'wide')),
      h('div', { class: 'row end' }, h('button', { class: 'btn', onclick: close }, 'انصراف'), h('button', { class: 'btn primary', onclick: save }, 'ثبت')));
  });
}

async function viewShortages(q) {
  const st = q.get('status') || 'active', page = Math.max(1, +q.get('page') || 1), r = await GET('staff/shortages' + qs({ status: st === 'all' ? '' : st, q: q.get('q'), page }));
  const hrefOf = p => { const x = new URLSearchParams(q); x.set('page', p); return '#/shortages?' + x; };
  return h('div', {}, title('کسری انبار', h('button', { class: 'btn primary', onclick: () => shortageAsk({}) }, '+ درخواست قطعه')),
    h('div', { class: 'row' }, filterBar(q, '#/shortages', null, 'قطعه، شمارهٔ فاکتور، مشتری، درخواست‌دهنده…'), chipsOf(q, '#/shortages', [['active', 'بی‌نتیجه (باز)'], ['open', 'تازه'], ['looking', 'در حال پیدا کردن'], ['bought', 'خریداری شد'], ['arrived', 'رسید'], ['all', 'همه']], 'active')),
    table(['تاریخ', 'قطعه', 'تعداد', 'برای', 'درخواست‌دهنده', 'وضعیت', 'خرید از / یادداشت'], r.items.map(x => h('tr', { class: r.canAnswer ? 'click' : '', onclick: () => r.canAnswer && shortageAnswer(x) },
      h('td', { class: 'lat' }, x.created_j), h('td', {}, h('b', {}, x.name)), h('td', { class: 'lat' }, x.qty), h('td', {}, (x.kindText || '') + (x.ref ? ' ' + x.ref : ''), x.customer_name ? h('small', { class: 'muted' }, ' — ' + x.customer_name) : null),
      h('td', {}, x.who), h('td', {}, pill(x.statusText, SHORT_TONE[x.status])), h('td', {}, x.vendor_name ? h('b', {}, x.vendor_name + ' ') : null, x.note))), 'کسری ثبت نشده.'),
    r.canAnswer ? h('p', { class: 'muted' }, 'روی هر ردیف بزنید تا وضعیت و «خرید از چه کسی» را بنویسید.') : h('p', { class: 'muted' }, 'درخواست شما را انبار و حسابداری می‌بینند و جواب می‌دهند.'), pager(r.total, r.per, page, hrefOf));
}

/** The buyers' badge on the menu and a note on the home screen (the toast once a sign-in). */
async function shortBadge(first) {
  if (!S.me) return;
  try {
    const r = await GET('staff/shortages/count'); if (!r.buyer) return;
    const a = $('nav a[data-r="#/shortages"]'); if (a) a.textContent = 'کسری انبار' + (r.open ? ' (' + r.open + ')' : '');
    if (r.open && first && !S.shortShown) { S.shortShown = true; toast(r.open + ' درخواست قطعه منتظر جواب است.', 'err'); }
  } catch (e) { }
}
function shortPanel() {
  const box = h('div'); shortBadge(true);
  GET('staff/shortages?status=open&per=8').then(r => { if (r.canAnswer && r.items.length) box.append(h('section', { class: 'panel' }, h('h3', {}, 'درخواست قطعه‌های بی‌جواب (' + r.total + ')'), table(['قطعه', 'تعداد', 'برای', 'درخواست‌دهنده'], r.items.map(x => clickRow('#/shortages', [x.name, x.qty, (x.kindText || '') + ' ' + x.ref, x.who]))))); }, () => { });
  return box;
}
setInterval(() => shortBadge(false), 300000);

/* ---------- warranty shipments ---------- */
const SHIP_TONE = { sent: 'bad', back: '', done: 'ok' };

async function shipDialog(s) {
  let vendors = []; try { vendors = (await GET('staff/ships?per=1')).vendors; } catch (e) { return fail(e); }
  const isNew = !s; s = s || { owner: 'mazesta', sent_at: today(), part_name: '' };
  const ro = !can('ships', 2);
  modal(isNew ? 'ارسال تازه به گارانتی' : 'گارانتی: ' + s.part_name, (b, close) => {
    const f = { part: text('part_name', s.part_name, { required: true, readOnly: ro, autofocus: isNew }), serial: text('serial', s.serial, { dir: 'ltr', readOnly: ro }), problem: area('problem', s.problem, { rows: 2, readOnly: ro }),
      vendor: select('vendor_id', Object.assign({ 0: '— انتخاب کنید —' }, Object.fromEntries(vendors.map(v => [v.id, v.name]))), s.vendor_id, { disabled: ro }), vnew: text('vendor_new', '', { placeholder: 'یا نام شرکت گارانتی تازه', readOnly: ro }),
      sent: dateIn(s.sent_at), tracking: text('tracking', s.tracking, { dir: 'ltr', readOnly: ro }), vreply: area('vendor_reply', s.vendor_reply, { rows: 2, readOnly: ro }), recv: dateIn(s.received_at), fix: area('fix_notes', s.fix_notes, { rows: 2, readOnly: ro }),
      bpart: text('back_part', s.back_part, { readOnly: ro }), bserial: text('back_serial', s.back_serial, { dir: 'ltr', readOnly: ro }), shelf: text('shelf', s.shelf, { readOnly: ro }), picked: check('picked_up', s.picked_up, 'به مشتری یا انبار تحویل داده شد'),
      owner: select('owner', { mazesta: 'مال مازستا', customer: 'مال مشتری' }, s.owner, { disabled: ro }), ref: text('ref', s.ref, { dir: 'ltr', readOnly: ro, placeholder: 'شمارهٔ سرویس یا فاکتور' }), creply: area('customer_reply', s.customer_reply, { rows: 2, readOnly: ro }) };
    const cust = customerPicker(s.customer_id ? { id: s.customer_id, name: s.customer_name } : null, ro), custBox = h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'مشتری (اگر شمارهٔ سرویس یا فاکتور نمی‌دانید)'), cust.el);
    const showOwner = () => { custBox.style.display = f.owner.value === 'customer' ? '' : 'none'; }; f.owner.addEventListener('change', showOwner); showOwner();
    $('input', f.picked).disabled = ro;
    const save = async () => {
      const body = { id: isNew ? 0 : s.id, updated: s.updated, part_name: f.part.value, serial: f.serial.value, problem: f.problem.value, vendor_id: +f.vendor.value, vendor_new: f.vnew.value, sent_at: f.sent.value, tracking: f.tracking.value, vendor_reply: f.vreply.value,
        received_at: f.recv.value, fix_notes: f.fix.value, back_part: f.bpart.value, back_serial: f.bserial.value, shelf: f.shelf.value, picked_up: $('input', f.picked).checked, owner: f.owner.value, ref: f.ref.value, customer_reply: f.creply.value };
      if (f.owner.value === 'customer') Object.assign(body, { customer_id: cust.body().customer_id || 0 });
      try { await POST('staff/ships', body); close(); toast('ثبت شد.'); route(); } catch (e) { if (e.status === 409) { close(); route(); } fail(e); }
    };
    b.append(h('div', { class: 'grid' }, h('h4', { class: 'wide' }, 'ارسال'), field('قطعه و مدل', f.part), field('سریال', f.serial), field('خرابی', f.problem, '', 'wide'), field('گارانتی / تعمیرگاه', f.vendor), field('یا شرکت تازه', f.vnew), field('تاریخ ارسال', f.sent), field('شمارهٔ ارسال / پیگیری', f.tracking),
      h('h4', { class: 'wide' }, 'جواب و بازگشت'), field('جواب شرکت', f.vreply, '', 'wide'), field('تاریخ رسیدن از گارانتی', f.recv), field('قطعهٔ برگشتی', f.bpart), field('سریال برگشتی', f.bserial), field('قفسه', f.shelf), field('کار انجام‌شده', f.fix, '', 'wide'),
      h('div', { class: 'wide' }, f.picked), h('h4', { class: 'wide' }, 'مال چه کسی'), field('مالکیت', f.owner), field('شمارهٔ سرویس / فاکتور', f.ref), custBox, field('جواب به مشتری', f.creply, '', 'wide')),
      h('div', { class: 'row end' }, h('button', { class: 'btn', onclick: close }, 'بستن'), ro ? null : h('button', { class: 'btn primary', onclick: save }, 'ثبت')));
  }, true);
}

async function viewShips(q) {
  const st = q.get('status') || 'open', page = Math.max(1, +q.get('page') || 1), r = await GET('staff/ships' + qs({ status: st === 'all' ? '' : st, q: q.get('q'), page }));
  const hrefOf = p => { const x = new URLSearchParams(q); x.set('page', p); return '#/ships?' + x; };
  return h('div', {}, title('ارسال به گارانتی', can('ships', 2) ? h('button', { class: 'btn primary', onclick: () => shipDialog(null) }, '+ ارسال تازه') : null),
    h('div', { class: 'row' }, filterBar(q, '#/ships', null, 'قطعه، سریال، شمارهٔ ارسال، مشتری، شرکت…'), chipsOf(q, '#/ships', [['open', 'تحویل‌نشده'], ['sent', 'نزد گارانتی'], ['back', 'برگشته'], ['all', 'همه']], 'open')),
    table(['ارسال', 'قطعه', 'سریال', 'گارانتی', 'مال', 'شمارهٔ ارسال', 'وضعیت', 'برگشت'], r.items.map(s => h('tr', { class: 'click', onclick: () => shipDialog(s) },
      h('td', { class: 'lat' }, s.sent_at_j), h('td', {}, h('b', {}, s.part_name)), h('td', { class: 'lat' }, s.serial), h('td', {}, s.vendor_name), h('td', {}, s.owner === 'customer' ? (s.customer_name || s.ref) : 'مازستا'), h('td', { class: 'lat' }, s.tracking),
      h('td', {}, pill(s.stateText, SHIP_TONE[s.state])), h('td', { class: 'lat' }, s.received_at_j))), 'ارسالی نیست.'), pager(r.total, r.per, page, hrefOf));
}

/* ---------- a party's account ---------- */
async function viewAccounts(q) {
  const cid = +q.get('customer') || 0, vid = +q.get('vendor') || 0, kind = vid ? 'vendor' : 'customer';
  const go = o => { location.hash = '#/accounts' + qs(o); };
  const who = text('who', '', { placeholder: 'نام یا موبایل مشتری را بنویسید…' }), vwho = text('vwho', '', { placeholder: 'نام فروشنده یا شرکت را بنویسید…' });
  let vendors = []; try { vendors = (await GET('staff/suppliers')).items; } catch (e) { }
  const cbox = suggest(who, async v => (await GET('staff/customers' + qs({ q: v, per: 8 }))).items.map(c => [[h('b', {}, c.name), ' ', h('span', { class: 'lat muted' }, c.mobile || '')], () => go({ customer: c.id })]), 'مشتری پیدا نشد.');
  const vbox = suggest(vwho, async v => vendors.filter(x => x.name.includes(v)).slice(0, 10).map(x => [h('b', {}, x.name), () => go({ vendor: x.id })]), 'فروشنده پیدا نشد.');
  const top = h('div', { class: 'panel grid' }, field('حساب مشتری', cbox), field('حساب فروشنده / طرف حساب', vbox));
  if (!cid && !vid) return h('div', {}, title('حساب‌ها'), top, h('p', { class: 'muted' }, 'یک مشتری یا فروشنده را انتخاب کنید تا همهٔ خرید، فروش، دریافت و پرداخت‌هایش را مرتب ببینید.'));
  const r = await GET(vid ? 'staff/suppliers/' + vid + '/account' : 'staff/customers/' + cid + '/account');
  let name = ''; if (vid) { name = (vendors.find(x => x.id === vid) || {}).name || ''; } else { try { name = (await GET('staff/customers/' + cid)).customer.name; } catch (e) { } }
  const bal = r.balance, sum = vid ? (bal > 0 ? 'ما به ایشان ' + money(bal) + ' بدهکاریم' : bal < 0 ? 'ایشان به ما ' + money(-bal) + ' بدهکارند' : 'حساب تسویه است')
    : (bal > 0 ? 'ایشان ' + money(bal) + ' به ما بدهکارند' : bal < 0 ? 'ما ' + money(-bal) + ' به ایشان بدهکاریم' : 'حساب تسویه است');
  const TONE = t => /دریافت|مشتری به حساب|پرداخت ما/.test(t) ? 'ok' : /پرداخت به مشتری|خرید|فروش|سرویس|برگشت/.test(t) ? '' : '';
  const heads = vid ? ['تاریخ', 'نوع', 'شرح', 'روش / حساب', 'شمارهٔ پیگیری', 'خرید (بدهی ما)', 'پرداخت / کم‌شدن بدهی', 'مانده'] : ['تاریخ', 'نوع', 'شرح', 'روش / حساب', 'شمارهٔ پیگیری', 'بدهکار (طلب ما)', 'بستانکار (پرداخت او)', 'مانده'];
  return h('div', {}, title('حساب ' + name, pill(sum, bal === 0 ? 'ok' : 'bad'), h('span', { class: 'pill' }, 'مبالغ به تومان')), top,
    h('section', { class: 'panel' }, table(heads, r.ledger.map(x => h('tr', { class: 'click' + (x.dead ? ' muted' : ''), onclick: () => { if (x.ref === 'invoice') location.hash = '#/invoice/' + x.id; else if (x.ref === 'job') location.hash = '#/job/' + x.id; else if (x.ref === 'purchase') location.hash = '#/purchase/' + x.id; } },
      h('td', { class: 'lat' }, x.at_j), h('td', {}, pill(x.type || '', TONE(x.type || ''))), h('td', {}, x.text), h('td', {}, x.how || ''), h('td', { class: 'lat' }, x.tx || ''), h('td', { class: 'lat' }, x.debit ? money(x.debit) : ''), h('td', { class: 'lat' }, x.credit ? money(x.credit) : ''), h('td', { class: 'lat' }, h('b', {}, money(x.balance))))), 'گردش مالی ثبت نشده است.')),
    h('p', { class: 'muted' }, vid ? 'پولی که مشتری به حساب این طرف ریخته (به جای ما) در ستون «پرداخت» آمده و از بدهی ما کم می‌شود؛ شمارهٔ پیگیری‌اش کنار آن است.' : 'مانده مثبت یعنی مشتری به ما بدهکار است. واریزی که مشتری به حساب دیگری زده، با نام آن طرف و شمارهٔ پیگیری در همین فهرست آمده.'));
}

/* ---------- backup ---------- */
async function viewBackup() {
  // the copies are kept on THIS PC: the program fetches the database dump from the site once a day (while someone is signed in) and keeps the newest few
  const r0 = await bridge.pcBackup('');
  if (r0.status !== 200) return h('div', {}, title('پشتیبان‌گیری از پایگاه داده'), h('p', { class: 'panel err' }, r0.error || 'پشتیبان‌گیری فقط در خود برنامه کار می‌کند.'));
  const r = r0.data;
  const dir = text('dir', r.dir, { dir: 'ltr', placeholder: 'D:\\Backups', style: 'min-width:320px' }), keep = text('keep', r.keep, { dir: 'ltr' });
  const redraw = () => route();
  const save = async () => { const x = await bridge.pcBackup('-set', { dir: dir.value, keep: +digits(keep.value) || 14 }); if (x.status === 200) { toast('ذخیره شد؛ هر روز یک نسخه گرفته می‌شود (برنامه باید باز و وارد شده باشد).'); redraw(); } else toast(x.error, 'err'); };
  const run = async () => { toast('در حال پشتیبان‌گیری…'); const x = await bridge.pcBackup('-run'); if (x.status === 200) toast('پشتیبان گرفته شد.'); else toast(x.error, 'err'); redraw(); };
  return h('div', {}, title('پشتیبان‌گیری از پایگاه داده'),
    h('section', { class: 'panel grid' }, field('پوشه یا درایو ذخیره روی این کامپیوتر', dir, 'مسیر کامل، مثلاً D:\\Backups'), field('چند نسخهٔ آخر بماند', keep),
      h('div', { class: 'row wide' }, h('button', { class: 'btn primary', onclick: save }, 'ذخیره'), r.dir ? h('button', { class: 'btn', onclick: run }, 'همین حالا پشتیبان بگیر') : null)),
    r.error ? h('p', { class: 'panel err' }, 'آخرین خطا: ' + r.error) : null,
    h('p', { class: 'muted' }, r.last ? 'آخرین پشتیبان موفق: ' + r.last : 'هنوز پشتیبانی گرفته نشده.'),
    table(['فایل', 'اندازه', 'زمان'], r.files.map(f => h('tr', {}, h('td', { class: 'lat' }, f.name), h('td', { class: 'lat' }, (f.size / 1024).toFixed(0) + ' KB'), h('td', { class: 'lat' }, f.at))), 'فایلی نیست.'));
}
