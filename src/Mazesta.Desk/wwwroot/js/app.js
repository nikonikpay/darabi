// The staff program's screens. The site decides what each person may do; the screens only hide what the site would refuse (S.me.perms comes from the site).
const S = { me: null, cfg: null };
class ApiError extends Error { constructor(msg, status, data) { super(msg); this.status = status; this.data = data; } }

async function api(method, path, body) {
  const r = await bridge.api(method, path, body);
  if (r.status >= 200 && r.status < 300 && r.data) return r.data;
  if (r.status === 401 && S.me) { S.me = null; showLogin('نشست شما تمام شد؛ دوباره وارد شوید.'); }
  throw new ApiError((r.data && r.data.error) || r.error || 'خطای ناشناخته', r.status, r.data);
}
const GET = p => api('GET', p), POST = (p, b) => api('POST', p, b || {});
const qs = o => { const s = Object.entries(o).filter(([, v]) => v !== '' && v != null).map(([k, v]) => k + '=' + encodeURIComponent(v).replace(/[!'()*]/g, c => '%' + c.charCodeAt(0).toString(16).toUpperCase())).join('&'); return s ? '?' + s : ''; };
const can = (area, level) => !!S.me && (S.me.perms[area] || 0) >= (level || 1);
const fail = e => toast(e && e.message ? e.message : String(e), 'err');

/* ---------- local settings (this PC only) ---------- */
const prefs = {
  get() { let o = {}; try { o = JSON.parse(localStorage.getItem('mzd') || '{}'); } catch (e) { } return Object.assign({ labelW: 70, labelH: 50, shop: 'مازستا', phone: '', foot: 'لطفاً این رسید را هنگام تحویل همراه داشته باشید.', paper: 'A5' }, o); },
  set(o) { try { localStorage.setItem('mzd', JSON.stringify(Object.assign(prefs.get(), o))); } catch (e) { } },
};

/* ---------- shell and routing ---------- */
const NAV = [['#/', 'خانه', () => true], ['#/customers', 'مشتری‌ها', () => can('customers')], ['#/builds', 'سیستم‌های نو', () => can('builds')], ['#/jobs', 'سرویس‌ها', () => can('jobs')], ['#/stock', 'انبار و کالاها', () => can('stock')], ['#/purchases', 'فاکتورهای خرید', () => can('stock')], ['#/stats', 'گزارش مالی و آمار', () => can('reports') && (can('jobs') || can('builds'))], ['#/reports', 'گزارش‌های تست', () => can('tests')]];

function showLogin(msg) {
  const app = clear($('#app')); document.body.classList.remove('in');
  const user = text('username', '', { autofocus: true, dir: 'ltr', autocapitalize: 'off' }), pass = h('input', { type: 'password', name: 'password', dir: 'ltr', autocomplete: 'current-password' });
  const err = h('div', { class: 'err', role: 'alert' }, msg || '');
  const go = async e => {
    e.preventDefault(); err.textContent = ''; btn.disabled = true;
    const r = await bridge.login(user.value.trim(), pass.value);
    btn.disabled = false;
    if (r.status === 200 && r.data) { S.me = r.data; await enter(); } else err.textContent = (r.data && r.data.error) || r.error || 'ورود ناموفق بود.';
  };
  const btn = h('button', { class: 'btn primary big', type: 'submit' }, 'ورود');
  app.append(h('form', { class: 'login', onsubmit: go }, h('div', { class: 'mark' }, 'M'), h('h1', {}, 'میز کار مازستا'), h('p', { class: 'muted' }, 'با نام کاربری و گذرواژهٔ خودتان وارد شوید.'),
    field('نام کاربری', user), field('گذرواژه', pass), err, btn, h('button', { class: 'link', type: 'button', onclick: () => settingsDialog() }, 'تنظیمات اتصال به سایت')));
}

async function enter() {
  try { S.cfg = await GET('staff/config'); } catch (e) { return fail(e); }
  document.body.classList.add('in'); drawShell(); route();
}

function drawShell() {
  const app = clear($('#app'));
  const search = h('input', { type: 'search', id: 'gsearch', placeholder: 'جستجو یا اسکن: شمارهٔ سرویس، موبایل، نام، سریال…', autocomplete: 'off', onkeydown: e => { if (e.key === 'Enter') globalFind(e.target.value.trim()); } });
  const nav = h('nav', {}, NAV.filter(n => n[2]()).map(n => h('a', { href: n[0], 'data-r': n[0] }, n[1])));
  const quick = h('div', { class: 'quick' }, can('jobs', 2) ? h('a', { class: 'btn primary', href: '#/job/new' }, '+ پذیرش سرویس') : null, can('builds', 2) ? h('a', { class: 'btn primary', href: '#/build/new' }, '+ سیستم نو') : null, can('customers', 2) ? h('a', { class: 'btn', href: '#/customer/new' }, '+ مشتری') : null);
  const who = h('div', { class: 'who' }, h('strong', {}, S.me.name), h('small', {}, S.me.admin ? 'مدیر سایت' : S.me.roles.map(r => r.label).join(' + ')),
    h('div', {}, h('button', { class: 'link', onclick: () => settingsDialog() }, 'تنظیمات'), ' · ', h('button', { class: 'link', onclick: async () => { await bridge.logout(); S.me = null; showLogin(); } }, 'خروج')));
  app.append(h('aside', {}, h('div', { class: 'brand' }, h('span', { class: 'mark' }, 'M'), 'میز کار'), nav, who), h('div', { class: 'col' }, h('header', { class: 'top' }, liveFind(search), quick), h('main', { id: 'main' })));
}

// the top search: from 3 characters the nearest customers, systems and services show under it as one types; Enter still opens an exact number at once
function liveFind(input) {
  const res = h('div', { class: 'results', hidden: true }); let seq = 0;
  const go = href => { input.value = ''; res.hidden = true; location.hash = href; };
  input.addEventListener('input', debounce(async () => {
    const qv = input.value.trim(), my = ++seq; if (qv.length < 3) { res.hidden = true; return; }
    try {
      const r = await GET('staff/find' + qs({ q: qv })); if (my !== seq) return; clear(res);
      r.customers.forEach(c => res.append(h('button', { type: 'button', class: 'opt', onclick: () => go('#/customer/' + c.id) }, h('b', {}, c.name), ' ', h('span', { class: 'lat muted' }, c.mobile || ''))));
      r.jobs.forEach(j => res.append(h('button', { type: 'button', class: 'opt', onclick: () => go('#/job/' + j.id) }, 'سرویس ', h('b', { class: 'lat' }, j.service_no || j.number), ' ', j.customer_name || '', ' ', h('span', { class: 'muted' }, j.device || ''))));
      r.builds.forEach(b => res.append(h('button', { type: 'button', class: 'opt', onclick: () => go('#/build/' + b.id) }, 'سیستم ', h('b', { class: 'lat' }, b.number || ''), ' ', b.customer_name || '', ' ', h('span', { class: 'muted' }, b.title || ''))));
      if (!res.children.length) res.append(h('div', { class: 'opt muted' }, 'چیزی پیدا نشد.'));
      res.hidden = false;
    } catch (e) { }
  }, 250));
  input.addEventListener('blur', () => setTimeout(() => { res.hidden = true; }, 200));
  input.addEventListener('keydown', e => { if (e.key === 'Escape') res.hidden = true; if (e.key === 'Enter') res.hidden = true; });
  return h('div', { class: 'gfind' }, input, res);
}

async function globalFind(q) {
  if (q.length < 2) return;
  try {
    const r = await GET('staff/find' + qs({ q }));
    const exact = [...r.jobs.map(x => ['job', x]), ...r.builds.map(x => ['build', x])].filter(([, x]) => [x.service_no, x.number].some(n => n && n.toLowerCase() === q.toLowerCase()));
    if (exact.length === 1) { $('#gsearch').value = ''; location.hash = '#/' + exact[0][0] + '/' + exact[0][1].id; return; }
    if (!exact.length && r.customers.length === 1 && !r.jobs.length && !r.builds.length) { $('#gsearch').value = ''; location.hash = '#/customer/' + r.customers[0].id; return; }
    location.hash = '#/find' + qs({ q });
  } catch (e) { fail(e); }
}

function parseHash() { const raw = location.hash.replace(/^#/, '') || '/'; const i = raw.indexOf('?'); return { path: i < 0 ? raw : raw.slice(0, i), q: new URLSearchParams(i < 0 ? '' : raw.slice(i + 1)) }; }

const routes = [
  [/^\/$/, () => viewHome()], [/^\/find$/, (m, q) => viewFind(q)], [/^\/customers$/, (m, q) => viewCustomers(q)], [/^\/customer\/(new|\d+)$/, (m, q) => viewCustomer(m[1], q)],
  [/^\/stock$/, (m, q) => viewStock(q)], [/^\/product\/(new|\d+)$/, (m, q) => viewProduct(m[1], q)], [/^\/purchases$/, (m, q) => viewPurchases(q)], [/^\/purchase\/(new|\d+)$/, (m, q) => viewPurchase(m[1], q)],
  [/^\/builds$/, (m, q) => viewList('build', q)], [/^\/jobs$/, (m, q) => viewList('job', q)], [/^\/build\/(new|\d+)$/, (m, q) => viewBuild(m[1], q)], [/^\/job\/(new|\d+)$/, (m, q) => viewJob(m[1], q)], [/^\/reports$/, (m, q) => viewReports(q)], [/^\/stats$/, (m, q) => viewStats(q)],
];
// only the newest navigation draws: a hashchange already overtaken by another, or a page whose data came back after a later click, is dropped (else the page is drawn twice and loses the focus)
let routeSeq = 0;
async function route(ev) {
  if (!S.me) return;
  if (ev instanceof HashChangeEvent && ev.newURL !== location.href) return;
  const my = ++routeSeq;
  const { path, q } = parseHash(); const main = $('#main'); if (!main) return;
  document.querySelectorAll('.backdrop').forEach(b => b.remove());   // a dialog of the page just left
  document.querySelectorAll('aside nav a').forEach(a => a.classList.toggle('on', a.dataset.r === '#' + path || (a.dataset.r !== '#/' && path.startsWith(a.dataset.r.slice(1).replace(/s$/, '')))));
  const r = routes.find(x => x[0].test(path));
  clear(main).append(h('div', { class: 'loading' }, 'در حال بارگذاری…'));
  try { const m = path.match(r ? r[0] : /^$/); const el = r ? await r[1](m, q) : h('p', {}, 'صفحه پیدا نشد.'); if (my !== routeSeq) return; clear(main).append(el); main.scrollTop = 0; if (S.after) { const f = S.after; S.after = null; f(); } }
  catch (e) { clear(main).append(h('div', { class: 'panel' }, h('p', { class: 'err' }, e.message), h('button', { class: 'btn', onclick: route }, 'دوباره'))); }
}
window.addEventListener('hashchange', route);

/* ---------- small shared pieces ---------- */
const pill = (t, kind) => h('span', { class: 'pill ' + (kind || '') }, t);
const statusKind = s => ({ delivered: 'ok', tested: 'ok', ready: 'ok', cancelled: 'bad' }[s] || '');
const title = (t, ...actions) => h('div', { class: 'title' }, h('h2', {}, t), h('div', { class: 'row' }, actions));
function pager(total, per, page, hrefOf) {
  const n = Math.ceil(total / per); if (n < 2) return null;
  return h('div', { class: 'pager' }, page > 1 ? h('a', { href: hrefOf(page - 1) }, '‹ قبلی') : null, h('span', {}, `صفحهٔ ${page} از ${n} (${total} مورد)`), page < n ? h('a', { href: hrefOf(page + 1) }, 'بعدی ›') : null);
}
const table = (heads, rows, empty) => h('div', { class: 'tablewrap' }, h('table', {}, h('thead', {}, h('tr', {}, heads.map(x => h('th', {}, x)))), h('tbody', {}, rows.length ? rows : h('tr', {}, h('td', { class: 'empty', colspan: heads.length }, empty || 'موردی نیست.')))));
const clickRow = (href, cells) => h('tr', { class: 'click', tabindex: 0, onclick: () => { location.hash = href; }, onkeydown: e => { if (e.key === 'Enter') location.hash = href; } }, cells.map(c => h('td', {}, c)));
const filterBar = (q, base, statuses, placeholder) => {
  const box = h('input', { type: 'search', value: q.get('q') || '', placeholder, onkeydown: e => { if (e.key === 'Enter') { S.refocus = true; go({ q: e.target.value, page: 1 }); } },
    // a box already left (a row opened while the timer ran) must not pull the page back to the list
    oninput: debounce(e => { const v = e.target.value.trim(); if (box.isConnected && (v.length >= 3 || !v) && v !== (q.get('q') || '')) { S.refocus = true; go({ q: v, page: 1 }); } }, 350) });
  if (S.refocus) S.after = () => { S.refocus = false; const b = $('#main .filters input[type=search]'); if (b) { b.focus(); b.setSelectionRange(b.value.length, b.value.length); } };
  const go = o => { const p = new URLSearchParams(q); for (const [k, v] of Object.entries(o)) { if (v === '' || v == null) p.delete(k); else p.set(k, v); } location.hash = base + (p.toString() ? '?' + p : ''); };
  return h('div', { class: 'filters' }, box, statuses ? h('div', { class: 'chips' }, h('button', { class: 'chip' + (q.get('status') ? '' : ' on'), onclick: () => go({ status: '', page: 1 }) }, 'همه'),
    Object.entries(statuses).map(([k, t]) => h('button', { class: 'chip' + (q.get('status') === k ? ' on' : ''), onclick: () => go({ status: k, page: 1 }) }, t))) : null);
};

/* ---------- home ---------- */
async function viewHome() {
  const out = h('div', {}, title('سلام ' + S.me.name));
  const counts = GET('staff/counts');   // every card's number in one request, not one per status
  const cards = (label, kind, statuses) => {
    const row = h('div', { class: 'cards' });
    Object.entries(statuses).forEach(([k, t]) => {
      const n = h('b', {}, '…'); row.append(h('a', { class: 'card', href: `#/${kind}s?status=${k}` }, n, h('span', {}, t)));
      counts.then(r => { const v = (r[kind + 's'] || {})[k]; n.textContent = v == null ? '—' : v; }, () => { n.textContent = '—'; });
    });
    return h('section', { class: 'panel' }, h('h3', {}, label), row);
  };
  if (can('builds')) out.append(cards('سیستم‌های نو در جریان', 'build', S.cfg.buildStatuses));
  if (can('jobs')) out.append(cards('سرویس‌ها', 'job', S.cfg.jobStatuses));
  if (can('accounting')) out.append(duePanel());   // acc.js: cheques near their due date
  if (!can('builds') && !can('jobs') && !can('accounting')) out.append(h('p', { class: 'muted' }, 'برای کار از منوی کنار استفاده کنید.'));
  return out;
}

/* ---------- find, lists ---------- */
async function viewFind(q) {
  const r = await GET('staff/find' + qs({ q: q.get('q') || '' })); const out = h('div', {}, title(`نتیجهٔ «${q.get('q')}»`));
  if (r.customers.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'مشتریان'), table(['نام', 'موبایل'], r.customers.map(c => clickRow('#/customer/' + c.id, [c.name, h('span', { class: 'lat' }, (c.mobiles || [c.mobile]).join(' · '))])))));
  if (r.builds.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'سیستم‌های نو'), buildsTable(r.builds)));
  if (r.jobs.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'سرویس‌ها'), jobsTable(r.jobs)));
  const sq = q.get('q') || ''; let tr = null;
  if (sq.length >= 3) { try { tr = await GET('staff/serial' + qs({ q: sq })); } catch (e) { } }
  const trace = tr ? [...tr.bought.map(x => clickRow('#/purchase/' + x.purchase_id, ['خرید', h('span', { class: 'lat' }, x.serial), x.product_name, (x.vendor_name || '') + ' — ' + (x.invoice_no || '')])),
    ...tr.builds.map(x => clickRow('#/build/' + x.id, ['سیستم نو', h('span', { class: 'lat' }, x.serial), x.model, x.customer_name + ' — ' + (x.invoice_no || x.service_no || '')])),
    ...tr.jobs.map(x => clickRow('#/job/' + x.id, [x.kind === 'in' ? 'قطعهٔ مشتری' : 'سرویس', h('span', { class: 'lat' }, x.serial), x.part, x.customer_name + ' — ' + x.service_no])),
    ...tr.ships.map(x => h('tr', {}, h('td', {}, 'گارانتی'), h('td', {}, h('span', { class: 'lat' }, x.serial)), h('td', {}, x.part_name), h('td', {}, x.vendor_name || '')))] : [];
  if (trace.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'ردیابی سریال'), table(['کجا', 'سریال', 'قطعه', 'پرونده'], trace)));
  if (!r.customers.length && !r.builds.length && !r.jobs.length && !trace.length) out.append(h('p', { class: 'muted' }, 'چیزی پیدا نشد.'));
  return out;
}
const buildsTable = rows => table(['فاکتور', 'مشتری', 'سیستم', 'وضعیت', 'تاریخ'], rows.map(b => clickRow('#/build/' + b.id, [h('b', { class: 'lat' }, b.number), b.customer_name || '', b.title, pill(b.statusText, statusKind(b.status)), h('span', { class: 'lat' }, b.sold_at_j)])));
const jobsTable = rows => table(['شماره', 'مشتری', 'دستگاه', 'وضعیت', 'پذیرش', 'مبلغ'], rows.map(j => clickRow('#/job/' + j.id, [h('b', { class: 'lat' }, j.service_no), j.customer_name || '', j.device, pill(j.statusText, statusKind(j.status)), h('span', { class: 'lat' }, j.received_at_j), j.total != null ? money(j.total) : ''])));

async function viewList(kind, q) {
  const page = Math.max(1, +q.get('page') || 1), isJob = kind === 'job', base = isJob ? '#/jobs' : '#/builds';
  const r = await GET(`staff/${kind}s` + qs({ q: q.get('q'), status: q.get('status'), page }));
  const hrefOf = p => { const x = new URLSearchParams(q); x.set('page', p); return base + '?' + x; };
  return h('div', {}, title(isJob ? 'سرویس‌ها' : 'سیستم‌های نو', can(isJob ? 'jobs' : 'builds', 2) ? h('a', { class: 'btn primary', href: isJob ? '#/job/new' : '#/build/new' }, isJob ? '+ پذیرش سرویس' : '+ سیستم نو') : null),
    filterBar(q, base, isJob ? S.cfg.jobStatuses : S.cfg.buildStatuses, 'جستجو…'), isJob ? jobsTable(r.items) : buildsTable(r.items), pager(r.total, r.per, page, hrefOf));
}

async function viewCustomers(q) {
  const page = Math.max(1, +q.get('page') || 1), r = await GET('staff/customers' + qs({ q: q.get('q'), page }));
  const hrefOf = p => { const x = new URLSearchParams(q); x.set('page', p); return '#/customers?' + x; };
  return h('div', {}, title('مشتری‌ها', can('customers', 2) ? h('a', { class: 'btn primary', href: '#/customer/new' }, '+ مشتری تازه') : null), filterBar(q, '#/customers', null, 'نام، موبایل، کد ملی…'),
    table(['نام', 'موبایل', 'گروه', 'سیستم', 'سرویس'], r.items.map(c => clickRow('#/customer/' + c.id, [c.name, h('span', { class: 'lat' }, c.mobile), c.groupText, c.builds, c.jobs]))), pager(r.total, r.per, page, hrefOf));
}

/* ---------- customer ---------- */
async function viewCustomer(id, q) {
  const isNew = id === 'new'; let c = { name: '', mobile: '', group: 'customer' }, builds = [], jobs = [];
  if (!isNew) { const r = await GET('staff/customers/' + id); c = r.customer; builds = r.builds; jobs = r.jobs; }
  const ro = !can('customers', 2), f = {};
  const mk = (k, label, o) => (f[k] = text(k, c[k], Object.assign({ readOnly: ro }, o)));
  const mobs = rowsEditor([{ k: 'number', label: 'شمارهٔ موبایل', ltr: true }], (c.mobiles && c.mobiles.length ? c.mobiles : [c.mobile || '']).map(number => ({ number })), { readOnly: ro });
  f.group = select('group', S.cfg.groups, c.group || 'customer', { disabled: ro }); f.address = area('address', c.address, { readOnly: ro }); f.notes = area('notes', c.notes, { readOnly: ro });
  const form = h('form', { class: 'panel grid', onsubmit: async e => {
    e.preventDefault();
    const body = { id: isNew ? 0 : c.id, updated: c.updated, mobiles: mobs.get().map(r => digits(r.number).trim()).filter(Boolean) }; for (const k of ['name', 'phone', 'national_id', 'email', 'address', 'notes', 'group']) body[k] = f[k].value;
    try { const s = await POST('staff/customers', body); toast('ذخیره شد.'); location.hash = '#/customer/' + s.id; if (!isNew) route(); } catch (err) { fail(err); }
  } },
    field('نام', mk('name', '', { required: true })), h('div', { class: 'field' }, h('span', {}, 'موبایل‌ها'), mobs.el, h('small', {}, 'اولی شمارهٔ اصلی و پیامک است؛ مشتری با هر کدام وارد صفحه‌اش می‌شود')), field('تلفن', mk('phone', '', { dir: 'ltr' })), field('کد ملی', mk('national_id', '', { dir: 'ltr' })),
    field('ایمیل', mk('email', '', { dir: 'ltr' })), field('گروه', f.group), field('آدرس', f.address, '', 'wide'), field('یادداشت', f.notes, '', 'wide'),
    ro ? null : h('div', { class: 'row wide' }, h('button', { class: 'btn primary' }, 'ذخیره')));
  const out = h('div', {}, title(isNew ? 'مشتری تازه' : c.name,
    !isNew && can('jobs', 2) ? h('a', { class: 'btn', href: '#/job/new?cid=' + c.id }, '+ سرویس') : null, !isNew && can('builds', 2) ? h('a', { class: 'btn', href: '#/build/new?cid=' + c.id }, '+ سیستم نو') : null), form);
  if (builds.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'سیستم‌های نو'), buildsTable(builds)));
  if (jobs.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'سرویس‌ها'), jobsTable(jobs)));
  if (!isNew && (can('accounting') || can('payments'))) out.append(await accountSection(c));   // acc.js: invoices, deposits, the running balance
  return out;
}

/* ---------- the customer chooser of a build / job form ---------- */
/** Name, mobile and a second mobile in one place: typing in the name or a number lists the closest registered customers (any of their numbers matches), and picking one
 *  chooses them; with none picked the form makes a new customer from what was typed. Without the right to see customers the site matches the mobile itself. */
function customerPicker(cur, locked) {
  let sel = cur || null, seq = 0;
  const box = h('div', { class: 'cpick' }), res = h('div', { class: 'results' }), look = can('customers');
  const name = text('c_name', '', { placeholder: 'نام یا موبایل را بنویسید…' }), mobile = text('c_mobile', '', { dir: 'ltr', placeholder: '09121234567' }), mobile2 = text('c_mobile2', '', { dir: 'ltr' });
  const find = debounce(async v => {
    const my = ++seq; clear(res); if (!look || v.length < 3) return;
    try {
      const r = await GET('staff/customers' + qs({ q: v, per: 8 })); if (my !== seq) return;
      r.items.forEach(c => res.append(h('button', { type: 'button', class: 'opt', onclick: () => { sel = c; draw(); } }, h('b', {}, c.name), ' ', h('span', { class: 'lat muted' }, (c.mobiles || [c.mobile, c.mobile2]).concat(c.phone ? [c.phone] : []).filter(Boolean).join(' · ')))));
      if (!r.items.length) res.append(h('small', { class: 'muted' }, 'مشتری با این مشخصات ثبت نشده؛ با ذخیره، مشتری تازه ساخته می‌شود.'));
    } catch (e) { fail(e); }
  }, 250);
  [name, mobile].forEach(i => i.addEventListener('input', () => find(digits(i.value).trim())));
  const draw = () => {
    clear(box); clear(res);
    if (sel) { box.append(h('div', { class: 'chosen' }, h('b', {}, sel.name), ' ', h('span', { class: 'lat muted' }, (sel.mobiles || [sel.mobile, sel.mobile2]).filter(Boolean).join(' · ')), ' ', h('a', { href: '#/customer/' + sel.id }, 'پرونده'), locked ? null : h('button', { type: 'button', class: 'link', onclick: () => { sel = null; draw(); setTimeout(() => name.focus(), 0); } }, 'تغییر'))); return; }
    box.append(h('div', { class: 'three' }, field('نام مشتری', name), field('موبایل', mobile), field('موبایل دوم', mobile2, 'اختیاری')), res);
  };
  draw();
  return { el: box, body: () => sel ? { customer_id: sel.id } : { c_mobile: digits(mobile.value), c_mobile2: digits(mobile2.value), c_name: name.value }, error: () => !sel && !digits(mobile.value).trim() ? 'مشتری را از فهرست انتخاب کنید یا موبایلش را بنویسید.' : '' };
}

/** The programs the customer asked for: rows picked from the shop's list as one types (or any other name), an "installed" mark, and the request in words. */
function softEditor(rec, ro) {
  const dl = h('datalist', { id: 'softlist' }, (S.cfg.software || []).map(n => h('option', { value: n })));
  const rows = rowsEditor([{ k: 'name', label: 'نرم‌افزار (از فهرست یا نام تازه)', list: 'softlist' }, { k: 'done', label: 'نصب و اجرا شد', type: 'check' }], rec.software || [], { readOnly: ro });
  const note = area('software_note', rec.software_note, { readOnly: ro, rows: 2, placeholder: 'درخواست‌های نرم‌افزاری (نسخه، زبان، تنظیمات …)' });
  const el = h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'نرم‌افزارهای درخواستی', rec.software_by_customer ? h('span', { class: 'pill ok' }, ' مشتری خودش پر کرده') : null), rows.el, note, dl);
  return { el, body: () => ({ software: rows.get().filter(r => r.name.trim()).map(r => ({ name: r.name.trim(), done: r.done })), software_note: note.value }) };
}

/* ---------- editable row lists (parts, payments) ---------- */
function rowsEditor(cols, rows, o) {
  o = o || {}; const body = h('tbody'), ro = !!o.readOnly, picks = cols.some(c => c.type === 'product');
  const add = r => {
    const tr = h('tr', { 'data-pid': r && r.product_id ? r.product_id : '' });
    cols.forEach(c => {
      const v = r ? r[c.k] : (c.def == null ? '' : c.def);
      const inp = c.type === 'product' ? productInput(c, v, tr, ro) : c.type === 'select' ? select(c.k, c.opts, v || c.def, { disabled: ro }) : c.type === 'check' ? h('input', { type: 'checkbox', name: c.k, checked: !!v, disabled: ro }) :
        h('input', { type: 'text', inputmode: c.type === 'num' ? 'numeric' : null, name: c.k, value: v == null ? '' : v, dir: c.ltr || c.type === 'num' ? 'ltr' : null, list: c.list, autocomplete: c.list ? 'off' : null, readOnly: ro || c.readOnly, 'aria-label': c.label });
      tr.append(h('td', { class: c.type === 'check' ? 'c' : '' }, inp));
    });
    if (!ro) tr.append(h('td', { class: 'c' }, h('button', { type: 'button', class: 'x', 'aria-label': 'حذف ردیف', onclick: () => { tr.remove(); o.onchange && o.onchange(); } }, '×')));
    if (o.onchange) tr.addEventListener('input', o.onchange); body.append(tr);
  };
  (rows || []).forEach(add);
  const el = h('div', { class: 'tablewrap rows' }, h('table', {}, h('thead', {}, h('tr', {}, cols.map(c => h('th', {}, c.label)), ro ? null : h('th', {}))), body), ro ? null : h('button', { type: 'button', class: 'btn sm', onclick: () => add(null) }, '+ ردیف'));
  const get = () => [...body.children].map(tr => { const r = {}; cols.forEach((c, i) => { const i2 = tr.children[i].firstChild; r[c.k] = c.type === 'check' ? i2.checked : i2.value; }); if (picks) r.product_id = +tr.dataset.pid || null; return r; });
  return { el, get, add };
}

/** The name box of a part row that picks from the stock: it suggests the site's products as one types; the chosen one's id rides on the row, and the row's empty fields
 *  named in c.fill (row field: product field) are filled from it (c.always: even when not empty). Typing a name that is not a product leaves the row free (no stock link). */
function productInput(c, v, tr, ro) {
  const dl = h('datalist', { id: 'pl' + Math.random().toString(36).slice(2) }), found = new Map();
  const inp = h('input', { type: 'text', name: c.k, value: v == null ? '' : v, list: dl.id, readOnly: ro, 'aria-label': c.label, placeholder: 'از انبار…', autocomplete: 'off' });
  const pick = () => {
    const p = found.get(inp.value.trim()); tr.dataset.pid = p ? p.id : '';
    if (!p) return;
    const put = (map, always) => Object.entries(map || {}).forEach(([k, pk]) => { const el = tr.querySelector(`[name="${k}"]`); if (el && p[pk] != null && (always || !el.value)) el.value = p[pk]; });
    put(c.always, true); put(c.fill, false); tr.dispatchEvent(new Event('input', { bubbles: true }));
  };
  inp.addEventListener('input', debounce(async () => {
    const q = inp.value.trim(); if (found.has(q)) return pick();
    tr.dataset.pid = ''; if (q.length < 3) return;
    try {
      const r = await GET('staff/products' + qs({ q, per: 15 })); clear(dl); found.clear();
      r.items.forEach(p => { found.set(p.name, p); dl.append(h('option', { value: p.name }, 'موجودی ' + p.stock + (p.sell_price != null ? ' — ' + money(p.sell_price) : ''))); });
      if (found.has(inp.value.trim())) pick();
    } catch (e) { }
  }, 250));
  return [inp, dl];
}

const mediaBox = (links, ro) => area('media', (links || []).join('\n'), { readOnly: ro, dir: 'ltr', rows: 2, placeholder: 'https://t.me/…  (هر لینک در یک خط)' });
const mediaOf = el => el.value.split(/\s+/).filter(Boolean);
const mediaLinks = links => (links || []).length ? h('div', { class: 'row' }, links.map((u, i) => h('a', { href: u, target: '_blank', rel: 'noopener' }, 'لینک ' + (i + 1)))) : null;
const priceNum = r => { for (const k of ['sell_price', 'buy_price', 'unit_price', 'unit_cost', 'qty', 'warranty_months']) if (k in r) r[k] = digits(r[k]).replace(/[^\d]/g, ''); return r; };

/* ---------- build (a new computer) ---------- */
async function viewBuild(id, q) {
  const isNew = id === 'new'; let b = { invoice_no: '', sold_at: today(), status: 'queue', notes: '', parts: [], report_id: '', software: [], software_note: '' }, cust = null;
  if (!isNew) { b = await GET('staff/builds/' + id); cust = { id: b.customer_id, name: b.customer_name, mobile: b.customer_mobile }; }
  else if (q.get('cid')) { const r = await GET('staff/customers/' + q.get('cid')); cust = r.customer; }
  const ro = !can('builds', 2), picker = customerPicker(cust, !isNew), soft = softEditor(b, ro);
  const f = { invoice: text('invoice_no', b.invoice_no, { readOnly: ro, dir: 'ltr', required: isNew }), sold: h('input', { type: 'date', name: 'sold_at', value: b.sold_at, readOnly: ro }),
    status: select('status', S.cfg.buildStatuses, b.status, { disabled: ro && !(can('tests', 2) && can('builds')) }), notes: area('notes', b.notes, { readOnly: ro }) };
  const cat = Object.assign({}, S.cfg.categories);
  const parts = rowsEditor([{ k: 'category', label: 'نوع', type: 'select', opts: cat, def: 'other' }, { k: 'model', label: 'مدل (از انبار)', type: 'product', always: { category: 'category' }, fill: { sell_price: 'sell_price', buy_price: 'buy_price', warranty_months: 'warranty_months' } },
    { k: 'serial', label: 'سریال', ltr: true }].concat(can('payments') ? [{ k: 'sell_price', label: 'قیمت فروش', type: 'num', readOnly: !can('payments', 2) }] : []).concat(S.cfg.costSeen ? [{ k: 'buy_price', label: 'قیمت خرید', type: 'num', readOnly: !S.cfg.costEdit }] : [])
    .concat([{ k: 'warranty_months', label: 'گارانتی (ماه)', type: 'num' }, { k: 'vendor', label: 'تأمین‌کننده / گارانتی' }, { k: 'has_box', label: 'جعبه', type: 'check' }, { k: 'qc', label: 'تست شد', type: 'check' }]), b.parts, { readOnly: ro });
  let force = false;
  const save = async e => {
    e.preventDefault(); const bad = picker.error(); if (bad) return toast(bad, 'err');
    const body = Object.assign({ id: isNew ? 0 : b.id, updated: b.updated, invoice_no: digits(f.invoice.value), sold_at: f.sold.value, status: f.status.value, notes: f.notes.value, force }, picker.body(), ro ? {} : soft.body());
    if (isNew || can('builds', 2)) body.parts = parts.get().map(priceNum);
    try {
      const r = await POST('staff/builds', body); toast(isNew ? 'ثبت شد: فاکتور ' + r.number : 'ذخیره شد.'); (r.warnings || []).forEach(w => toast(w, 'err'));
      if (isNew) location.hash = '#/build/' + r.id; else route();
    } catch (err) {
      if (err.status === 409 && err.data && err.data.duplicate) { if (await ask(err.message + ' با همین شماره ثبت شود؟', 'ثبت کن')) { force = true; save(e); } } else if (err.status === 409) { toast(err.message, 'err'); if (await ask('رکورد را دوباره بارگذاری کنم؟ (تغییرات این صفحه از بین می‌رود)', 'بارگذاری')) route(); } else fail(err);
    }
  };
  const actions = isNew ? null : h('div', { class: 'row' }, h('button', { type: 'button', class: 'btn', onclick: () => printLabel('build', b.id) }, 'چاپ برچسب'), h('button', { type: 'button', class: 'btn', onclick: () => sendReceipt('build', b.id) }, 'پیامک رسید'),
    can('tests') ? h('button', { type: 'button', class: 'btn', onclick: () => reportDialog('build', b) }, b.report_id ? 'گزارش تست ✓' : 'گزارش تست') : null,
    can('accounting', 2) ? h('button', { type: 'button', class: 'btn', onclick: async () => { try { const v = await POST('staff/invoices/from-build', { build_id: b.id }); location.hash = '#/invoice/' + v.id; } catch (e) { fail(e); } } }, 'فاکتور فروش') : null);
  return h('div', {}, title(isNew ? 'سیستم نو' : `سیستم ${b.number}`, actions),
    h('form', { class: 'panel grid', onsubmit: save }, h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'مشتری'), picker.el), field('شمارهٔ فاکتور حسابداری', f.invoice, 'سیستم نو با شمارهٔ فاکتور و نام خریدار شناخته می‌شود'), field('تاریخ فروش', f.sold), field('وضعیت', f.status),
      b.service_no ? field('شمارهٔ سرویس (قدیمی)', h('input', { value: b.service_no, readOnly: true, dir: 'ltr' })) : null, h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'قطعات'), parts.el),
      soft.el, mediaLinks(b.media), field('یادداشت', f.notes, '', 'wide'), ro && !(can('tests', 2) && can('builds')) ? null : h('div', { class: 'row wide' }, h('button', { class: 'btn primary' }, 'ذخیره'))));
}

/* ---------- job (a service) ---------- */
async function viewJob(id, q) {
  const isNew = id === 'new'; let j = { device: '', device_serial: '', device_type: 'system', media: [], complaint: '', work_done: '', status: 'received', received_at: today(), due_at: '', invoice_no: '', notes: '', parts: [], recv: [], pays: [], labor_price: 0, discount: 0, paid: 0, paid_note: '', is_mazesta: false, has_warranty: false, software: [], software_note: '' }, cust = null;
  if (!isNew) { j = await GET('staff/jobs/' + id); cust = { id: j.customer_id, name: j.customer_name, mobile: j.customer_mobile }; }
  else if (q.get('cid')) { const r = await GET('staff/customers/' + q.get('cid')); cust = r.customer; }
  const ro = !can('jobs', 2), money1 = can('payments'), money2 = can('payments', 2), picker = customerPicker(cust, !isNew), soft = softEditor(j, ro);
  // the money: the customer pays parts + services - discount; the costs we paid out are ours (the customer never sees them) and only lower the services' profit
  const out = { parts: h('b', { class: 'lat' }), svc: h('b', { class: 'lat' }), total: h('b', { class: 'lat' }), profit: h('b', { class: 'lat' }) };
  const recalc = () => {
    if (!money1) return;
    const n = v => +digits(v).replace(/[^\d]/g, '') || 0; let p = 0, c = 0;
    parts.get().forEach(x => { const qn = n(x.qty) || 1; p += qn * n(x.unit_price); c += qn * n(x.buy_price); });
    const lab = n(f.labor.value) - n(f.discount.value);
    out.parts.textContent = money(p); out.svc.textContent = money(lab - n(f.paid.value)); out.total.textContent = money(p + lab); out.profit.textContent = money(p - c);
  };
  const f = { device: text('device', j.device, { readOnly: ro, placeholder: 'مثلاً: ASUS TUF F15' }), serial: text('device_serial', j.device_serial, { readOnly: ro, dir: 'ltr' }), dtype: select('device_type', S.cfg.deviceTypes || {}, j.device_type || 'system', { disabled: ro }),
    media: mediaBox(j.media, ro), complaint: area('complaint', j.complaint, { readOnly: ro, rows: 3 }), work: area('work_done', j.work_done, { rows: 3, readOnly: ro }), status: select('status', S.cfg.jobStatuses, j.status, { disabled: ro }),
    recv: h('input', { type: 'date', value: j.received_at || today(), readOnly: ro }), due: h('input', { type: 'date', value: j.due_at || '', readOnly: ro }), invoice: text('invoice_no', j.invoice_no, { dir: 'ltr', readOnly: ro }),
    labor: text('labor', j.labor_price || '', { dir: 'ltr', readOnly: !money2, oninput: () => recalc() }), discount: text('discount', j.discount || '', { dir: 'ltr', readOnly: !money2, oninput: () => recalc() }),
    paid: text('paid', j.paid || '', { dir: 'ltr', readOnly: !money2, oninput: () => recalc() }), paidNote: text('paid_note', j.paid_note, { readOnly: !money2 }),
    notes: area('notes', j.notes, { readOnly: ro }), maz: check('is_mazesta', j.is_mazesta, 'دستگاه از مازستا خریداری شده'), war: check('has_warranty', j.has_warranty, 'در گارانتی') };
  // a customer's computer case is known by its service number; a laptop (or a part) needs its model and serial number
  const devBox = h('div', { class: 'two wide' }, field('دستگاه (مدل)', f.device), field('شمارهٔ سریال دستگاه', f.serial));
  const devShow = () => { devBox.style.display = f.dtype.value === 'system' ? 'none' : ''; }; f.dtype.addEventListener('change', devShow); devShow();
  const partCols = [{ k: 'name', label: 'قطعهٔ اضافه‌شده (از انبار)', type: 'product', fill: { unit_price: 'sell_price', buy_price: 'buy_price', warranty_months: 'warranty_months' } }, { k: 'qty', label: 'تعداد', type: 'num', def: 1 }]
    .concat(money1 ? [{ k: 'unit_price', label: 'قیمت واحد', type: 'num', readOnly: !money2 }] : []).concat(S.cfg.costSeen ? [{ k: 'buy_price', label: 'قیمت خرید', type: 'num', readOnly: !S.cfg.costEdit }] : []).concat([{ k: 'serial', label: 'سریال', ltr: true }, { k: 'warranty_months', label: 'گارانتی (ماه)', type: 'num' }]);
  const parts = rowsEditor(partCols, j.parts, { readOnly: ro, onchange: recalc });
  const recv = rowsEditor([{ k: 'name', label: 'قطعه / لوازمی که مشتری آورده' }, { k: 'serial', label: 'سریال', ltr: true }, { k: 'has_box', label: 'جعبه', type: 'check' }], j.recv, { readOnly: ro });
  const pays = money1 ? rowsEditor([{ k: 'amount', label: 'مبلغ واریزی', type: 'num' }, { k: 'account', label: 'واریز به حساب', list: 'acclist' }, { k: 'tx', label: 'شمارهٔ تراکنش', ltr: true }, { k: 'holder', label: 'صاحب حساب' }], j.pays, { readOnly: !money2 }) : null;
  const save = async e => {
    e.preventDefault(); const bad = picker.error(); if (bad) return toast(bad, 'err');
    const body = Object.assign({ id: isNew ? 0 : j.id, updated: j.updated, device: f.device.value, device_serial: f.serial.value, device_type: f.dtype.value, media: mediaOf(f.media), complaint: f.complaint.value, work_done: f.work.value, status: f.status.value, received_at: f.recv.value, due_at: f.due.value,
      invoice_no: digits(f.invoice.value), notes: f.notes.value, is_mazesta: $('input', f.maz).checked, has_warranty: $('input', f.war).checked, parts: parts.get().map(priceNum), recv: recv.get() }, picker.body(), ro ? {} : soft.body());
    if (money2) { Object.assign(body, { labor_price: digits(f.labor.value), discount: digits(f.discount.value), paid: digits(f.paid.value), paid_note: f.paidNote.value, pays: pays.get().map(p => Object.assign(p, { amount: digits(p.amount) })) }); }
    try { const r = await POST('staff/jobs', body); toast(isNew ? 'ثبت شد: ' + r.service_no : 'ذخیره شد.'); if (isNew) { S.after = () => offerPrint(r); location.hash = '#/job/' + r.id; } else route(); }
    catch (err) { if (err.status === 409) { toast(err.message, 'err'); if (await ask('رکورد را دوباره بارگذاری کنم؟ (تغییرات این صفحه از بین می‌رود)', 'بارگذاری')) route(); } else fail(err); }
  };
  const actions = isNew ? null : h('div', { class: 'row' }, h('button', { type: 'button', class: 'btn', onclick: () => printLabel('job', j.id) }, 'چاپ برچسب'), h('button', { type: 'button', class: 'btn', onclick: () => printReceipt(j) }, 'چاپ رسید'),
    h('button', { type: 'button', class: 'btn', onclick: () => sendReceipt('job', j.id) }, 'پیامک رسید'), can('tests') ? h('button', { type: 'button', class: 'btn', onclick: () => reportDialog('job', j) }, j.report_id ? 'گزارش تست ✓' : 'گزارش تست') : null);
  const costs = money1 ? h('div', { class: 'wide money' }, h('h3', { class: 'wide' }, 'هزینه‌ها'), field('هزینهٔ خدمات', f.labor), field('تخفیف', f.discount), field('هزینه‌های پرداخت‌شده', f.paid, 'خرجی که خودمان برای این سرویس دادیم؛ مشتری نمی‌بیند و از سود خدمات کم می‌شود'), field('توضیحات هزینهٔ پرداخت‌شده', f.paidNote),
    h('div', { class: 'sum' }, h('span', {}, 'هزینهٔ قطعات'), out.parts), h('div', { class: 'sum big' }, h('span', {}, 'هزینهٔ نهایی (از مشتری)'), out.total), h('div', { class: 'sum' }, h('span', {}, 'سود خدمات'), out.svc),
    S.cfg.costSeen ? h('div', { class: 'sum' }, h('span', {}, 'سود قطعات'), out.profit) : null, field('شمارهٔ فاکتور', f.invoice),
    pays ? h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'پرداخت‌های مشتری'), pays.el, h('datalist', { id: 'acclist' }, (S.cfg.accounts || []).map(n => h('option', { value: n })))) : null) : field('شمارهٔ فاکتور', f.invoice);
  const el = h('div', {}, title(isNew ? 'پذیرش سرویس' : `سرویس ${j.service_no}`, actions),
    h('form', { class: 'panel grid', onsubmit: save }, h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'مشتری'), picker.el), field('نوع دستگاه', f.dtype), field('وضعیت', f.status), field('تاریخ پذیرش', f.recv), field('تاریخ قول تحویل', f.due), devBox,
      h('div', { class: 'checks' }, f.maz, f.war), field('مشکل اعلام‌شده', f.complaint, '', 'wide'), field('کار انجام‌شده', f.work, '', 'wide'),
      h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'لوازمی که مشتری تحویل داده'), recv.el), h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'قطعات اضافه‌شده'), parts.el),
      soft.el, costs, field('لینک عکس و فیلم در تلگرام', f.media, '', 'wide'), mediaLinks(j.media), field('یادداشت داخلی', f.notes, '', 'wide'), ro ? null : h('div', { class: 'row wide' }, h('button', { class: 'btn primary' }, 'ذخیره'))));
  recalc(); return el;
}
function offerPrint(job) {
  modal('پذیرش ثبت شد', (b, close) => { b.append(h('p', {}, 'شمارهٔ پذیرش: ', h('b', { class: 'lat' }, job.service_no)), h('div', { class: 'row' },
    h('button', { class: 'btn primary', onclick: () => { close(); printLabel('job', job.id); } }, 'چاپ برچسب'), h('button', { class: 'btn', onclick: () => { close(); printReceipt(job); } }, 'چاپ رسید'),
    S.cfg.receiptReady ? h('button', { class: 'btn', onclick: () => { close(); sendReceipt('job', job.id); } }, 'پیامک رسید') : null, h('button', { class: 'btn', onclick: close }, 'بعداً'))); });
}

/* ---------- SMS receipt, label and receipt printing ---------- */
async function sendReceipt(kind, id) {
  if (!S.cfg.receiptReady) return toast('پیامک روی سایت فعال نیست یا اجازهٔ فرستادن ندارید.', 'err');
  if (!await ask('پیامک رسید برای مشتری فرستاده شود؟', 'بفرست')) return;
  try { const r = await POST('staff/sms/receipt', { kind, id }); toast('پیامک به ' + r.to + ' فرستاده شد.'); } catch (e) { fail(e); }
}

function printOverlay(titleText, node, css) {
  document.getElementById('printcss')?.remove(); document.head.append(h('style', { id: 'printcss' }, css));
  document.getElementById('printArea')?.remove(); document.body.append(h('div', { id: 'printArea' }, node.cloneNode(true)));
  modal(titleText, (b, close) => { b.append(h('div', { class: 'preview' }, node), h('div', { class: 'row end' }, h('button', { class: 'btn', onclick: close }, 'بستن'), h('button', { class: 'btn primary', onclick: () => bridge.print() }, 'چاپ…'))); }, true);
}

async function printLabel(kind, id) {
  try {
    const d = await GET(`staff/label/${kind}/${id}`), p = prefs.get();
    const node = h('div', { class: 'label', style: `width:${p.labelW}mm;height:${p.labelH}mm` }, h('div', { class: 'lhead' }, h('div', { class: 'lnum lat' }, d.number), h('div', { class: 'lqr', html: qrSvg(d.qr) })),
      h('dl', {}, d.lines.filter(l => l.key !== 'service_no' && l.value).map(l => [h('dt', {}, l.label), h('dd', {}, l.value)])));
    printOverlay('برچسب ' + d.number, node, `@page{size:${p.labelW}mm ${p.labelH}mm;margin:0}`);
  } catch (e) { fail(e); }
}

function printReceipt(j) {
  const p = prefs.get(), qr = j.service_no;
  const node = h('div', { class: 'receipt' }, h('header', {}, h('div', {}, h('h1', {}, p.shop), p.phone ? h('div', { class: 'lat' }, p.phone) : null), h('div', { class: 'lqr big', html: qrSvg(qr, 5) })),
    h('h2', {}, 'رسید پذیرش'), h('div', { class: 'rnum lat' }, j.service_no),
    h('dl', {}, [['مشتری', j.customer_name], ['موبایل', j.customer_mobile], ['دستگاه', j.device], ['تاریخ پذیرش', j.received_at_j], ['قول تحویل', j.due_at_j], ['مشکل اعلام‌شده', j.complaint],
      ['لوازم تحویل‌گرفته‌شده', (j.recv || []).map(x => x.name + (x.serial ? ' (' + x.serial + ')' : '')).join('، ')]].filter(x => x[1]).map(x => [h('dt', {}, x[0]), h('dd', {}, x[1])])), h('p', { class: 'foot' }, p.foot));
  printOverlay('رسید ' + j.service_no, node, `@page{size:${p.paper};margin:12mm}`);
}

/* ---------- the period report: money and counts ---------- */
async function viewStats(q) {
  const r = await GET('staff/stats' + qs({ period: q.get('period'), from: q.get('from'), to: q.get('to') }));
  const go = o => { location.hash = '#/stats' + qs(o); };
  const from = text('from', r.from_j, { dir: 'ltr', placeholder: '1405/07/01' }), to = text('to', r.to_j, { dir: 'ltr' });
  const card = (label, v, note) => h('div', { class: 'card' }, h('b', { class: 'lat' }, v), h('span', {}, label), note ? h('small', { class: 'err' }, note) : null);
  const m = v => v == null ? null : Number(v).toLocaleString('en-US'), miss = n => n ? `ناقص: ${n} قطعه قیمت خرید ندارد` : '';
  const counts = (by, names) => h('div', { class: 'chips' }, Object.entries(by).map(([k, n]) => h('span', { class: 'pill' }, (names[k] || k) + ': ' + n)));
  const out = h('div', {}, title('گزارش مالی و آمار', h('span', { class: 'muted' }, r.from_j + ' تا ' + r.to_j), h('span', { class: 'pill' }, 'مبالغ به تومان')),
    h('div', { class: 'filters' }, h('div', { class: 'chips' }, Object.entries(r.periods).map(([k, p]) => h('button', { class: 'chip' + (q.get('period') === k || (!q.get('period') && !q.get('from') && k === 'month') ? ' on' : ''), onclick: () => go({ period: k }) }, p.label))),
      h('label', { class: 'check' }, 'از ', from), h('label', { class: 'check' }, 'تا ', to), h('button', { class: 'btn', onclick: () => go({ from: digits(from.value), to: digits(to.value) }) }, 'نمایش')));
  const J = r.jobs, B = r.builds;
  if (J) out.append(h('section', { class: 'panel' }, h('h3', {}, 'سرویس‌ها (بر اساس تاریخ پذیرش)'), h('div', { class: 'cards' }, [card('تعداد', J.count), J.total != null ? card('هزینهٔ نهایی', m(J.total)) : null, J.received != null ? card('دریافت‌شده', m(J.received), J.total > J.received ? 'مانده ' + m(J.total - J.received) : '') : null,
    J.labor != null ? card('هزینهٔ خدمات', m(J.labor)) : null, J.parts_sell != null ? card('فروش قطعات', m(J.parts_sell)) : null, J.paid_out != null ? card('هزینه‌های پرداخت‌شده', m(J.paid_out)) : null, J.discount != null ? card('تخفیف', m(J.discount)) : null,
    J.parts_buy != null ? card('خرید قطعات', m(J.parts_buy)) : null, J.service_profit != null ? card('سود خدمات', m(J.service_profit)) : null, J.parts_profit != null ? card('سود قطعات', m(J.parts_profit), miss(J.missing_cost)) : null]), counts(J.by_status, S.cfg.jobStatuses)));
  if (B) out.append(h('section', { class: 'panel' }, h('h3', {}, 'سیستم‌های نو (بر اساس تاریخ فروش)'), h('div', { class: 'cards' }, [card('تعداد', B.count), B.sell != null ? card('فروش', m(B.sell), B.missing_price ? B.missing_price + ' قطعه قیمت فروش ندارد' : '') : null,
    B.buy != null ? card('خرید قطعات', m(B.buy)) : null, B.profit != null ? card('سود قطعات', m(B.profit), miss(B.missing_cost)) : null]), counts(B.by_status, S.cfg.buildStatuses)));
  if (r.sum) out.append(h('section', { class: 'panel' }, h('h3', {}, 'جمع سود این بازه'), h('div', { class: 'cards' }, card('سود خدمات', m(r.sum.service_profit)), card('سود قطعات (سرویس و سیستم نو)', m(r.sum.parts_profit), [miss(r.sum.missing_cost), r.sum.missing_price ? r.sum.missing_price + ' قطعه قیمت فروش ندارد' : ''].filter(Boolean).join(' · ')), card('سود کل', m(r.sum.profit)))));
  const mo = r.months; const hasMoney = mo.some(x => x.jobs_total != null || x.builds_sell != null);
  out.append(h('section', { class: 'panel' }, h('h3', {}, 'ماه به ماه'), table(['ماه'].concat(J ? ['سرویس'] : [], J && hasMoney ? ['مبلغ سرویس‌ها'] : [], J && mo.some(x => x.jobs_profit != null) ? ['سود سرویس‌ها'] : [], B ? ['سیستم نو'] : [], B && hasMoney ? ['فروش سیستم‌ها'] : [], B && mo.some(x => x.builds_profit != null) ? ['سود سیستم‌ها'] : []),
    mo.map(x => h('tr', {}, [x.month, x.jobs, m(x.jobs_total), m(x.jobs_profit), x.builds, m(x.builds_sell), m(x.builds_profit)].filter((v, i) => [true, !!J, J && hasMoney, J && x.jobs_profit != null, !!B, B && hasMoney, B && x.builds_profit != null][i]).map(v => h('td', { class: 'lat' }, v == null ? '—' : v)))), 'در این بازه چیزی ثبت نشده است.')));
  if (r.top.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'پرفروش‌ترین قطعات'), table(['قطعه', 'تعداد'].concat(r.top[0].revenue != null ? ['مبلغ فروش'] : []), r.top.map(x => h('tr', {}, h('td', {}, x.name), h('td', {}, x.qty), x.revenue != null ? h('td', { class: 'lat' }, m(x.revenue)) : null)))));
  out.append(h('p', { class: 'muted' }, 'مشتری فقط خدمات و قطعات (منهای تخفیف) را می‌پردازد. سود خدمات = هزینهٔ خدمات − تخفیف − هزینه‌های پرداخت‌شده. سود قطعات = فروش − خرید، در سرویس‌ها و سیستم‌های نو. اگر قطعه‌ای قیمت خرید نداشته باشد، سود قطعات «ناقص» است و سود واقعی کمتر است.'));
  return out;
}

/* ---------- test reports ---------- */
async function reportDialog(kind, rec) {
  modal('گزارش تست ' + (rec.number || rec.service_no), async (body, close) => {
    const list = h('div', { class: 'results tall' }), view = h('div', { class: 'rview' }, h('p', { class: 'muted' }, 'گزارشی را انتخاب کنید.'));
    const load = async (qv) => {
      clear(list).append(h('small', { class: 'muted' }, '…'));
      try {
        const r = await GET('staff/reports' + qs({ service: rec.number || rec.service_no, q: qv })); clear(list);
        if (!r.items.length) list.append(h('small', { class: 'muted' }, 'گزارشی پیدا نشد.'));
        r.items.forEach(x => list.append(h('button', { type: 'button', class: 'opt' + (x.id === rec.report_id ? ' on' : ''), onclick: () => show(x) }, x.match ? '★ ' : '', h('b', { class: 'lat' }, x.service || '—'), ' ', x.device || x.machine, h('small', { class: 'muted' }, ' ' + (x.created || '').slice(0, 10) + ' · ' + x.verdict))));
      } catch (e) { fail(e); }
    };
    const show = async x => {
      clear(view).append(h('p', { class: 'muted' }, '…'));
      try {
        const r = await GET('staff/reports/' + x.id);
        clear(view).append(h('div', { class: 'row' }, can('tests', 2) ? h('button', { class: 'btn primary', onclick: async () => { try { await POST('staff/attach', { kind, id: rec.id, report_id: x.id }); toast('گزارش به پرونده چسبید.'); close(); route(); } catch (e) { fail(e); } } }, 'چسباندن به این پرونده') : null,
          rec.report_id === x.id && can('tests', 2) ? h('button', { class: 'btn', onclick: async () => { await POST('staff/attach', { kind, id: rec.id, report_id: '' }); toast('گزارش برداشته شد.'); close(); route(); } }, 'برداشتن گزارش') : null),
          h('iframe', { sandbox: '', srcdoc: r.html, title: 'گزارش' }));
      } catch (e) { fail(e); }
    };
    body.append(h('input', { type: 'search', placeholder: 'جستجوی گزارش…', oninput: debounce(ev => load(ev.target.value), 300) }), h('div', { class: 'split' }, list, view)); load('');
  }, true);
}

async function viewReports(q) {
  const r = await GET('staff/reports' + qs({ q: q.get('q') })); const view = h('div', { class: 'rview' }, h('p', { class: 'muted' }, 'گزارشی را انتخاب کنید.'));
  const list = h('div', { class: 'results tall' }, r.items.map(x => h('button', { class: 'opt', onclick: async () => { try { const d = await GET('staff/reports/' + x.id); clear(view).append(h('iframe', { sandbox: '', srcdoc: d.html, title: 'گزارش' })); } catch (e) { fail(e); } } }, h('b', { class: 'lat' }, x.service || '—'), ' ', x.device || x.machine, h('small', { class: 'muted' }, ' ' + (x.created || '').slice(0, 10) + ' · ' + x.verdict))));
  return h('div', {}, title('گزارش‌های تست'), filterBar(q, '#/reports', null, 'شمارهٔ سرویس، دستگاه…'), h('div', { class: 'split panel' }, list, view));
}

/* ---------- stock: products, their history, purchase invoices ---------- */
const stockPill = n => pill(n + ' عدد', n > 0 ? 'ok' : n < 0 ? 'bad' : '');
async function viewStock(q) {
  const page = Math.max(1, +q.get('page') || 1), r = await GET('staff/products' + qs({ q: q.get('q'), instock: q.get('instock'), page }));
  const hrefOf = p => { const x = new URLSearchParams(q); x.set('page', p); return '#/stock?' + x; };
  const only = h('label', { class: 'check' }, h('input', { type: 'checkbox', checked: q.get('instock') === '1', onchange: e => { const x = new URLSearchParams(q); if (e.target.checked) x.set('instock', '1'); else x.delete('instock'); x.delete('page'); location.hash = '#/stock?' + x; } }), 'فقط موجودها');
  return h('div', {}, title('انبار و کالاها', can('stock', 2) ? h('a', { class: 'btn', href: '#/purchase/new' }, '+ فاکتور خرید') : null, can('stock', 2) ? h('a', { class: 'btn primary', href: '#/product/new' }, '+ کالا') : null),
    h('div', { class: 'row' }, filterBar(q, '#/stock', null, 'نام، برند، پارت‌نامبر…'), only),
    table(['کالا', 'دسته', 'موجودی', 'قیمت فروش'].concat(S.cfg.costSeen ? ['قیمت خرید'] : []), r.items.map(p => clickRow('#/product/' + p.id, [p.name, p.categoryText, stockPill(p.stock), money(p.sell_price)].concat(S.cfg.costSeen ? [money(p.buy_price)] : [])))),
    pager(r.total, r.per, page, hrefOf));
}

async function viewProduct(id) {
  const isNew = id === 'new'; let r = { product: { name: '', category: 'other', brand: '', sku: '', sell_price: null, buy_price: null, warranty_months: null, notes: '' }, bought: [], used: [], totals: null };
  if (!isNew) r = await GET('staff/products/' + id);
  const p = r.product, ro = !can('stock', 2), f = {
    name: text('name', p.name, { readOnly: ro, required: true }), category: select('category', S.cfg.categories, p.category, { disabled: ro }), brand: text('brand', p.brand, { readOnly: ro }), sku: text('sku', p.sku, { readOnly: ro, dir: 'ltr' }),
    sell: text('sell_price', p.sell_price, { readOnly: ro, dir: 'ltr' }), buy: S.cfg.costSeen ? text('buy_price', p.buy_price, { readOnly: ro || !S.cfg.costEdit, dir: 'ltr' }) : null, w: text('warranty_months', p.warranty_months, { readOnly: ro, dir: 'ltr' }), notes: text('notes', p.notes, { readOnly: ro }) };
  const save = async e => {
    e.preventDefault();
    const body = { id: isNew ? 0 : p.id, updated: p.updated, name: f.name.value, category: f.category.value, brand: f.brand.value, sku: f.sku.value, sell_price: digits(f.sell.value), warranty_months: digits(f.w.value), notes: f.notes.value };
    if (f.buy && S.cfg.costEdit) body.buy_price = digits(f.buy.value);
    try { const s = await POST('staff/products', body); toast('ذخیره شد.'); if (isNew) location.hash = '#/product/' + s.id; else route(); } catch (err) { fail(err); }
  };
  const out = h('div', {}, title(isNew ? 'کالای تازه' : p.name, !isNew && can('stock', 2) ? h('a', { class: 'btn', href: '#/purchase/new?pid=' + p.id }, '+ خرید این کالا') : null),
    r.totals ? h('div', { class: 'cards' }, [['موجودی', stockPill(r.totals.stock)], ['خریده‌شده', r.totals.bought], ['به‌کاررفته', r.totals.used]].concat(r.totals.spent != null ? [['جمع خرید', money(r.totals.spent)]] : []).concat(r.totals.sold != null ? [['جمع فروش', money(r.totals.sold)]] : [])
      .map(([l, v]) => h('div', { class: 'card' }, h('b', {}, v), h('span', {}, l)))) : null,
    h('form', { class: 'panel grid', onsubmit: save }, field('نام کالا', f.name, '', 'wide'), field('دسته', f.category), field('برند', f.brand), field('پارت‌نامبر / بارکد', f.sku), field('قیمت فروش', f.sell), f.buy ? field('قیمت خرید', f.buy, 'با هر فاکتور خرید به‌روز می‌شود') : null,
      field('گارانتی (ماه)', f.w), field('یادداشت', f.notes, '', 'wide'), h('p', { class: 'muted wide' }, 'موجودی دستی وارد نمی‌شود: از فاکتورهای خرید و قطعه‌های به‌کاررفته حساب می‌شود.'), ro ? null : h('div', { class: 'row wide' }, h('button', { class: 'btn primary' }, 'ذخیره'))));
  if (r.bought.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'از کجا خریده‌ایم'), table(['تاریخ', 'فروشنده', 'فاکتور', 'تعداد', 'قیمت واحد', 'سریال‌ها'],
    r.bought.map(x => clickRow('#/purchase/' + x.purchase_id, [h('span', { class: 'lat' }, x.bought_at_j), x.vendor_name, h('span', { class: 'lat' }, x.invoice_no), x.qty, x.unit_cost != null ? money(x.unit_cost) : '—', h('span', { class: 'lat' }, x.serials.join(', '))])))));
  if (!isNew) out.append(h('section', { class: 'panel' }, h('h3', {}, 'به چه کسی رفته'), table(['تاریخ', 'مشتری', 'پرونده', 'تعداد', 'قیمت', 'سریال'],
    r.used.map(x => clickRow('#/' + x.kind + '/' + x.id, [h('span', { class: 'lat' }, x.at_j), x.customer_name, (x.kind === 'build' ? 'سیستم ' : 'سرویس ') + x.number, x.qty, x.price != null ? money(x.price) : '—', h('span', { class: 'lat' }, x.serial)])), 'هنوز جایی به کار نرفته است.')));
  return out;
}

async function viewPurchases(q) {
  const page = Math.max(1, +q.get('page') || 1), r = await GET('staff/purchases' + qs({ q: q.get('q'), vendor: q.get('vendor'), page }));
  const hrefOf = p => { const x = new URLSearchParams(q); x.set('page', p); return '#/purchases?' + x; };
  return h('div', {}, title('فاکتورهای خرید', can('stock', 2) ? h('a', { class: 'btn primary', href: '#/purchase/new' }, '+ فاکتور خرید') : null), filterBar(q, '#/purchases', null, 'شمارهٔ فاکتور، فروشنده، کالا یا سریال…'),
    table(['تاریخ', 'فروشنده', 'شمارهٔ فاکتور', 'ردیف'].concat(S.cfg.costSeen ? ['جمع'] : []), r.items.map(u => clickRow('#/purchase/' + u.id, [h('span', { class: 'lat' }, u.bought_at_j), u.vendor_name, h('span', { class: 'lat' }, u.invoice_no), u.lines].concat(S.cfg.costSeen ? [money(u.total)] : [])))),
    pager(r.total, r.per, page, hrefOf));
}

async function viewPurchase(id, q) {
  const isNew = id === 'new'; let u = { vendor_id: 0, invoice_no: '', bought_at: today(), notes: '', items: [] };
  if (!isNew) u = await GET('staff/purchases/' + id);
  else if (q.get('pid')) { const r = await GET('staff/products/' + q.get('pid')); u.items = [{ product_id: r.product.id, name: r.product.name, category: r.product.category, qty: 1, unit_cost: r.product.buy_price, serials: [] }]; }
  const ro = !can('stock', 2), sup = await GET('staff/suppliers');
  const vendor = select('vendor_id', Object.assign({ 0: '— انتخاب کنید —' }, Object.fromEntries(sup.items.map(v => [v.id, v.name + (v.kind !== 'supplier' ? ' (' + v.kindText + ')' : '')]))), u.vendor_id, { disabled: ro });
  const f = { vendor, vnew: text('vendor_name', '', { readOnly: ro, placeholder: 'اگر در فهرست نیست' }), invoice: text('invoice_no', u.invoice_no, { readOnly: ro, dir: 'ltr' }), date: h('input', { type: 'date', value: u.bought_at || today(), readOnly: ro }), notes: area('notes', u.notes, { readOnly: ro, rows: 2 }) };
  const items = rowsEditor([{ k: 'name', label: 'کالا (از انبار یا نام تازه)', type: 'product', always: { category: 'category' }, fill: { unit_cost: 'buy_price' } }, { k: 'category', label: 'دسته', type: 'select', opts: S.cfg.categories, def: 'other' },
    { k: 'qty', label: 'تعداد', type: 'num', def: 1 }, { k: 'unit_cost', label: 'قیمت خرید واحد', type: 'num' }, { k: 'sell_price', label: 'قیمت فروش تازه', type: 'num' }, { k: 'serials', label: 'سریال‌ها (با , جدا)', ltr: true }],
    u.items.map(x => Object.assign({}, x, { serials: (x.serials || []).join(', ') })), { readOnly: ro });
  let force = false;
  const save = async e => {
    e.preventDefault();
    const body = { id: isNew ? 0 : u.id, updated: u.updated, vendor_id: +f.vendor.value || 0, vendor_name: f.vnew.value, invoice_no: digits(f.invoice.value), bought_at: f.date.value, notes: f.notes.value, force, items: items.get().map(priceNum) };
    try { const r = await POST('staff/purchases', body); toast('فاکتور خرید ثبت شد.'); if (isNew) location.hash = '#/purchase/' + r.id; else route(); }
    catch (err) { if (err.status === 409 && err.data && err.data.duplicate) { if (await ask(err.message + ' باز هم ثبت شود؟', 'ثبت کن')) { force = true; save(e); } } else fail(err); }
  };
  return h('div', {}, title(isNew ? 'فاکتور خرید تازه' : 'فاکتور خرید ' + (u.invoice_no || '#' + u.id)),
    h('form', { class: 'panel grid', onsubmit: save }, field('فروشنده', f.vendor), field('یا فروشندهٔ تازه', f.vnew), field('شمارهٔ فاکتور خرید', f.invoice), field('تاریخ خرید', f.date),
      h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'کالاها'), items.el), field('یادداشت', f.notes, '', 'wide'), u.total != null ? h('div', { class: 'wide total' }, 'جمع: ', h('b', {}, money(u.total))) : null,
      ro ? null : h('div', { class: 'row wide' }, h('button', { class: 'btn primary' }, 'ذخیره'))));
}

/* ---------- settings ---------- */
function settingsDialog() {
  const p = prefs.get(), srv = text('server', '', { dir: 'ltr', placeholder: 'https://www.dfmrendering.com' });
  bridge.session().then(r => { if (r.data) srv.value = r.data.server; });
  modal('تنظیمات', (b, close) => {
    const w = text('w', p.labelW, { dir: 'ltr' }), hh = text('h', p.labelH, { dir: 'ltr' }), shop = text('shop', p.shop), phone = text('phone', p.phone, { dir: 'ltr' }), foot = text('foot', p.foot), paper = select('paper', { A5: 'A5', A4: 'A4' }, p.paper);
    b.append(h('div', { class: 'grid' }, field('نشانی سایت', srv, bridge.dev ? 'در حالت آزمایشی مرورگر این بخش اثری ندارد.' : 'با عوض شدن سایت باید دوباره وارد شوید.', 'wide'), field('عرض برچسب (mm)', w), field('ارتفاع برچسب (mm)', hh), field('نام روی رسید', shop), field('تلفن روی رسید', phone), field('کاغذ رسید', paper), field('متن پایین رسید', foot, '', 'wide')),
      h('div', { class: 'row end' }, h('button', { class: 'btn primary', onclick: async () => {
        const cw = Math.min(200, Math.max(20, +digits(w.value) || 70)), ch = Math.min(200, Math.max(20, +digits(hh.value) || 50));
        prefs.set({ labelW: cw, labelH: ch, shop: shop.value, phone: phone.value, foot: foot.value, paper: paper.value });
        const r = await bridge.setServer(srv.value); if (r.status !== 200) return toast(r.error || 'نشانی سایت درست نیست.', 'err');
        close(); toast('ذخیره شد.');
      } }, 'ذخیره')));
  });
}

/* ---------- start (once every script is in: acc.js adds its menu items and routes) ---------- */
document.addEventListener('DOMContentLoaded', async function start() {
  const s = await bridge.session();
  if (s.data && s.data.signedIn) { try { S.me = await GET('staff/me'); return enter(); } catch (e) { /* fall through to the login page */ } }
  showLogin();
});
