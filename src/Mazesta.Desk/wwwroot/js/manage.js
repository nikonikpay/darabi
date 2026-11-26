// What the manager decides: two saves of one record that overlapped (made without the site). Each shows both sides; the manager takes fields from the other one, or keeps the record as it is.
NAV.push(['#/conflicts', 'تداخل‌ها', () => can('manage', 2)]);
routes.push([/^\/conflicts$/, () => viewConflicts()]);

const ENTITIES = { customer: ['مشتری', 'customer'], build: ['سیستم', 'build'], job: ['سرویس', 'job'], invoice: ['فاکتور', 'invoice'], product: ['کالا', 'product'], ship: ['ارسال به گارانتی'], shortage: ['درخواست قطعه'] };

/** The number beside a menu line. */
function navBadge(href, n) {
  const a = $(`aside nav a[data-r="${href}"]`); if (!a) return;
  let b = $('.badge', a); if (!n) { if (b) b.remove(); return; }
  if (!b) a.append(b = h('span', { class: 'badge' }));
  b.textContent = n;
}

/** Called once the program opens and whenever the copy brought something new. */
function onSynced() { conflictBadge(); if (typeof taskBadge === 'function') taskBadge(); }

async function conflictBadge() {
  if (!can('manage', 2)) return;
  try { const r = await GET('staff/conflicts?count=1'); navBadge('#/conflicts', r.count); } catch (e) { }
}

async function viewConflicts() {
  const r = await GET('staff/conflicts');
  navBadge('#/conflicts', r.items.length);
  return h('div', {}, title('تداخل‌ها'),
    h('p', { class: 'muted' }, 'وقتی دو نفر بدون اتصال یک پرونده را عوض کنند، ثبتِ دیرتر در پرونده می‌ماند و دیگری اینجا منتظر تصمیم شماست. هیچ‌کدام پاک نمی‌شود؛ هر دو در تاریخچهٔ پرونده هست.'),
    r.items.length ? r.items.map(conflictCard) : h('div', { class: 'panel empty' }, 'تداخلی در انتظار تصمیم نیست.'));
}

function conflictCard(x) {
  const [label, route_] = ENTITIES[x.entity] || [x.entity];
  const picks = x.fields.map(f => ({ key: f.key, box: h('input', { type: 'checkbox', 'aria-label': 'برداشتن ' + f.label }) }));
  const done = text => { toast(text); route(); conflictBadge(); };
  return h('section', { class: 'panel conflict' },
    h('header', {}, h('h3', {}, label, ' ', route_ ? h('a', { href: `#/${route_}/${x.id}` }, x.name || '#' + x.id) : h('b', {}, x.name || '#' + x.id)),
      pill(x.older ? 'ثبت آفلاینِ قدیمی‌تر اعمال نشد' : 'ثبت آفلاینِ دیرتر جایگزین شد', 'bad')),
    table(['فیلد', `الان در پرونده: ${x.now_who || '—'} (${x.now_at || '—'})`, `ثبت دیگر: ${x.who} (${x.at})`, 'این را بردار'],
      x.fields.map((f, i) => h('tr', {}, h('td', {}, h('b', {}, f.label)), h('td', {}, f.now), h('td', { class: 'other' }, f.theirs), h('td', { class: 'c' }, picks[i].box)))),
    h('div', { class: 'row end' },
      h('button', { class: 'btn', onclick: async () => { try { await POST('staff/conflicts/resolve', { rev: x.rev }); done('همان حالت فعلی ماند.'); } catch (e) { fail(e); } } }, 'حالت فعلی درست است'),
      h('button', { class: 'btn primary', onclick: async () => {
        const keys = picks.filter(p => p.box.checked).map(p => p.key); if (!keys.length) return toast('حداقل یک فیلد را تیک بزنید.', 'err');
        try { await POST('staff/revisions/pick', { rev: x.rev, fields: keys }); done('فیلدهای انتخاب‌شده در پرونده نشست.'); } catch (e) { fail(e); }
      } }, 'ثبت فیلدهای تیک‌خورده')));
}
