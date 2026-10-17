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
const NAV = [['#/', 'خانه', () => true], ['#/customers', 'مشتری‌ها', () => can('customers')], ['#/builds', 'سیستم‌های نو', () => can('builds')], ['#/jobs', 'سرویس‌ها', () => can('jobs')], ['#/stock', 'انبار و کالاها', () => can('stock')], ['#/purchases', 'فاکتورهای خرید', () => can('stock')], ['#/reports', 'گزارش‌های تست', () => can('tests')]];

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
  app.append(h('aside', {}, h('div', { class: 'brand' }, h('span', { class: 'mark' }, 'M'), 'میز کار'), nav, who), h('div', { class: 'col' }, h('header', { class: 'top' }, search, quick), h('main', { id: 'main' })));
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
  [/^\/builds$/, (m, q) => viewList('build', q)], [/^\/jobs$/, (m, q) => viewList('job', q)], [/^\/build\/(new|\d+)$/, (m, q) => viewBuild(m[1], q)], [/^\/job\/(new|\d+)$/, (m, q) => viewJob(m[1], q)], [/^\/reports$/, (m, q) => viewReports(q)],
];
async function route() {
  if (!S.me) return;
  const { path, q } = parseHash(); const main = $('#main'); if (!main) return;
  document.querySelectorAll('.backdrop').forEach(b => b.remove());   // a dialog of the page just left
  document.querySelectorAll('aside nav a').forEach(a => a.classList.toggle('on', a.dataset.r === '#' + path || (a.dataset.r !== '#/' && path.startsWith(a.dataset.r.slice(1).replace(/s$/, '')))));
  const r = routes.find(x => x[0].test(path));
  clear(main).append(h('div', { class: 'loading' }, 'در حال بارگذاری…'));
  try { const m = path.match(r ? r[0] : /^$/); const el = r ? await r[1](m, q) : h('p', {}, 'صفحه پیدا نشد.'); clear(main).append(el); main.scrollTop = 0; if (S.after) { const f = S.after; S.after = null; f(); } }
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
  const box = h('input', { type: 'search', value: q.get('q') || '', placeholder, onkeydown: e => { if (e.key === 'Enter') go({ q: e.target.value, page: 1 }); } });
  const go = o => { const p = new URLSearchParams(q); for (const [k, v] of Object.entries(o)) { if (v === '' || v == null) p.delete(k); else p.set(k, v); } location.hash = base + (p.toString() ? '?' + p : ''); };
  return h('div', { class: 'filters' }, box, statuses ? h('div', { class: 'chips' }, h('button', { class: 'chip' + (q.get('status') ? '' : ' on'), onclick: () => go({ status: '', page: 1 }) }, 'همه'),
    Object.entries(statuses).map(([k, t]) => h('button', { class: 'chip' + (q.get('status') === k ? ' on' : ''), onclick: () => go({ status: k, page: 1 }) }, t))) : null);
};

/* ---------- home ---------- */
async function viewHome() {
  const out = h('div', {}, title('سلام ' + S.me.name));
  const cards = (label, kind, statuses, area) => {
    const row = h('div', { class: 'cards' });
    Object.entries(statuses).forEach(([k, t]) => {
      const n = h('b', {}, '…'); row.append(h('a', { class: 'card', href: `#/${kind}s?status=${k}` }, n, h('span', {}, t)));
      GET(`staff/${kind}s` + qs({ status: k, per: 1 })).then(r => { n.textContent = r.total; }, () => { n.textContent = '—'; });
    });
    return h('section', { class: 'panel' }, h('h3', {}, label), row);
  };
  if (can('builds')) out.append(cards('سیستم‌های نو در جریان', 'build', S.cfg.buildStatuses));
  if (can('jobs')) out.append(cards('سرویس‌ها', 'job', S.cfg.jobStatuses));
  if (!can('builds') && !can('jobs')) out.append(h('p', { class: 'muted' }, 'برای کار از منوی کنار استفاده کنید.'));
  return out;
}

/* ---------- find, lists ---------- */
async function viewFind(q) {
  const r = await GET('staff/find' + qs({ q: q.get('q') || '' })); const out = h('div', {}, title(`نتیجهٔ «${q.get('q')}»`));
  if (r.customers.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'مشتریان'), table(['نام', 'موبایل'], r.customers.map(c => clickRow('#/customer/' + c.id, [c.name, h('span', { class: 'lat' }, c.mobile)])))));
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
const buildsTable = rows => table(['فاکتور', 'مشتری', 'سیستم', 'نوع', 'وضعیت', 'تاریخ'], rows.map(b => clickRow('#/build/' + b.id, [h('b', { class: 'lat' }, b.number), b.customer_name || '', b.title, b.deviceText || '', pill(b.statusText, statusKind(b.status)), h('span', { class: 'lat' }, b.sold_at_j)])));
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
  f.group = select('group', S.cfg.groups, c.group || 'customer', { disabled: ro }); f.address = area('address', c.address, { readOnly: ro }); f.notes = area('notes', c.notes, { readOnly: ro });
  const form = h('form', { class: 'panel grid', onsubmit: async e => {
    e.preventDefault();
    const body = { id: isNew ? 0 : c.id, updated: c.updated }; for (const k of ['name', 'mobile', 'mobile2', 'phone', 'national_id', 'email', 'address', 'notes', 'group']) body[k] = f[k].value;
    try { const s = await POST('staff/customers', body); toast('ذخیره شد.'); location.hash = '#/customer/' + s.id; if (!isNew) route(); } catch (err) { fail(err); }
  } },
    field('نام', mk('name', '', { required: true })), field('موبایل', mk('mobile', '', { dir: 'ltr' })), field('موبایل دوم', mk('mobile2', '', { dir: 'ltr' })), field('تلفن', mk('phone', '', { dir: 'ltr' })), field('کد ملی', mk('national_id', '', { dir: 'ltr' })),
    field('ایمیل', mk('email', '', { dir: 'ltr' })), field('گروه', f.group), field('آدرس', f.address, '', 'wide'), field('یادداشت', f.notes, '', 'wide'),
    ro ? null : h('div', { class: 'row wide' }, h('button', { class: 'btn primary' }, 'ذخیره')));
  const out = h('div', {}, title(isNew ? 'مشتری تازه' : c.name,
    !isNew && can('jobs', 2) ? h('a', { class: 'btn', href: '#/job/new?cid=' + c.id }, '+ سرویس') : null, !isNew && can('builds', 2) ? h('a', { class: 'btn', href: '#/build/new?cid=' + c.id }, '+ سیستم نو') : null), form);
  if (builds.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'سیستم‌های نو'), buildsTable(builds)));
  if (jobs.length) out.append(h('section', { class: 'panel' }, h('h3', {}, 'سرویس‌ها'), jobsTable(jobs)));
  return out;
}

/* ---------- the customer chooser of a build / job form ---------- */
function customerPicker(cur, locked) {
  let sel = cur || null, mode = cur ? 'chosen' : (can('customers') ? 'search' : 'new');
  const box = h('div', { class: 'cpick' }), mobile = text('c_mobile', '', { dir: 'ltr', placeholder: '09121234567' }), name = text('c_name', '', { placeholder: 'نام مشتری (فقط برای مشتری تازه)' });
  const draw = () => {
    clear(box);
    if (mode === 'chosen') { box.append(h('div', { class: 'chosen' }, h('b', {}, sel.name), ' ', h('span', { class: 'lat muted' }, sel.mobile || ''), ' ', h('a', { href: '#/customer/' + sel.id }, 'پرونده'), locked ? null : h('button', { type: 'button', class: 'link', onclick: () => { sel = null; mode = can('customers') ? 'search' : 'new'; draw(); } }, 'تغییر'))); return; }
    if (mode === 'search') {
      const res = h('div', { class: 'results' }), inp = h('input', { type: 'search', placeholder: 'جستجوی مشتری: نام یا موبایل', oninput: debounce(async () => {
        const v = inp.value.trim(); clear(res); if (v.length < 2) return;
        try { const r = await GET('staff/customers' + qs({ q: v })); r.items.slice(0, 8).forEach(c => res.append(h('button', { type: 'button', class: 'opt', onclick: () => { sel = c; mode = 'chosen'; draw(); } }, c.name, ' ', h('span', { class: 'lat muted' }, c.mobile)))); if (!r.items.length) res.append(h('small', { class: 'muted' }, 'مشتری با این مشخصات ثبت نشده است.')); } catch (e) { fail(e); }
      }, 300) });
      box.append(inp, res, can('customers', 2) ? h('button', { type: 'button', class: 'link', onclick: () => { mode = 'new'; draw(); } }, 'مشتری تازه است') : null);
      setTimeout(() => inp.focus(), 0); return;
    }
    box.append(h('div', { class: 'two' }, field('موبایل مشتری', mobile), can('customers', 2) ? field('نام', name) : null), can('customers') ? h('button', { type: 'button', class: 'link', onclick: () => { mode = 'search'; draw(); } }, 'انتخاب از مشتریان ثبت‌شده') : h('small', { class: 'muted' }, 'اگر این شماره ثبت شده باشد همان مشتری انتخاب می‌شود.'));
  };
  draw();
  return { el: box, body: () => mode === 'chosen' ? { customer_id: sel.id } : { c_mobile: digits(mobile.value), c_name: name.value }, error: () => mode === 'search' ? 'یک مشتری انتخاب کنید.' : (mode === 'new' && !digits(mobile.value).trim() ? 'موبایل مشتری را بنویسید.' : '') };
}

/* ---------- editable row lists (parts, payments) ---------- */
function rowsEditor(cols, rows, o) {
  o = o || {}; const body = h('tbody'), ro = !!o.readOnly, picks = cols.some(c => c.type === 'product');
  const add = r => {
    const tr = h('tr', { 'data-pid': r && r.product_id ? r.product_id : '' });
    cols.forEach(c => {
      const v = r ? r[c.k] : (c.def == null ? '' : c.def);
      const inp = c.type === 'product' ? productInput(c, v, tr, ro) : c.type === 'select' ? select(c.k, c.opts, v || c.def, { disabled: ro }) : c.type === 'check' ? h('input', { type: 'checkbox', name: c.k, checked: !!v, disabled: ro }) :
        h('input', { type: 'text', inputmode: c.type === 'num' ? 'numeric' : null, name: c.k, value: v == null ? '' : v, dir: c.ltr || c.type === 'num' ? 'ltr' : null, readOnly: ro || c.readOnly, 'aria-label': c.label });
      tr.append(h('td', { class: c.type === 'check' ? 'c' : '' }, inp));
    });
    if (!ro) tr.append(h('td', { class: 'c' }, h('button', { type: 'button', class: 'x', 'aria-label': 'حذف ردیف', onclick: () => { tr.remove(); o.onchange && o.onchange(); } }, '×')));
    if (o.onchange) tr.addEventListener('input', o.onchange); body.append(tr);
  };
  (rows || []).forEach(add);
  const el = h('div', { class: 'tablewrap rows' }, h('table', {}, h('thead', {}, h('tr', {}, cols.map(c => h('th', {}, c.label)), ro ? null : h('th', {}))), body), ro ? null : h('button', { type: 'button', class: 'btn sm', onclick: () => add(null) }, '+ ردیف'));
  const get = () => [...body.children].map(tr => { const r = {}; cols.forEach((c, i) => { const i2 = tr.children[i].firstChild; r[c.k] = c.type === 'check' ? i2.checked : i2.value; }); if (picks) r.product_id = +tr.dataset.pid || null; return r; });
  return { el, get };
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
    tr.dataset.pid = ''; if (q.length < 2) return;
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
  const isNew = id === 'new'; let b = { title: '', invoice_no: '', sold_at: today(), status: 'queue', notes: '', parts: [], report_id: '', device_type: 'system', media: [] }, cust = null;
  if (!isNew) { b = await GET('staff/builds/' + id); cust = { id: b.customer_id, name: b.customer_name, mobile: b.customer_mobile }; }
  else if (q.get('cid')) { const r = await GET('staff/customers/' + q.get('cid')); cust = r.customer; }
  const ro = !can('builds', 2), picker = customerPicker(cust, !isNew);
  const f = { title: text('title', b.title, { readOnly: ro, placeholder: 'مثلاً: سیستم گیمینگ ۵۶۰۰ / ۳۰۶۰' }), invoice: text('invoice_no', b.invoice_no, { readOnly: ro, dir: 'ltr', required: isNew }), sold: h('input', { type: 'date', name: 'sold_at', value: b.sold_at, readOnly: ro }),
    dtype: select('device_type', S.cfg.deviceTypes || {}, b.device_type || 'system', { disabled: ro }), media: mediaBox(b.media, ro),
    status: select('status', S.cfg.buildStatuses, b.status, { disabled: ro && !(can('tests', 2) && can('builds')) }), notes: area('notes', b.notes, { readOnly: ro }) };
  const cat = Object.assign({}, S.cfg.categories);
  const parts = rowsEditor([{ k: 'category', label: 'نوع', type: 'select', opts: cat, def: 'other' }, { k: 'model', label: 'مدل (از انبار)', type: 'product', always: { category: 'category' }, fill: { sell_price: 'sell_price', buy_price: 'buy_price', warranty_months: 'warranty_months' } },
    { k: 'serial', label: 'سریال', ltr: true }].concat(can('payments') ? [{ k: 'sell_price', label: 'قیمت فروش', type: 'num', readOnly: !can('payments', 2) }] : []).concat(S.cfg.costSeen ? [{ k: 'buy_price', label: 'قیمت خرید', type: 'num', readOnly: !S.cfg.costEdit }] : [])
    .concat([{ k: 'warranty_months', label: 'گارانتی (ماه)', type: 'num' }, { k: 'vendor', label: 'تأمین‌کننده / گارانتی' }, { k: 'has_box', label: 'جعبه', type: 'check' }, { k: 'qc', label: 'تست شد', type: 'check' }]), b.parts, { readOnly: ro });
  let force = false;
  const save = async e => {
    e.preventDefault(); const bad = picker.error(); if (bad) return toast(bad, 'err');
    const body = Object.assign({ id: isNew ? 0 : b.id, updated: b.updated, title: f.title.value, invoice_no: digits(f.invoice.value), sold_at: f.sold.value, status: f.status.value, notes: f.notes.value, device_type: f.dtype.value, media: mediaOf(f.media), force }, picker.body());
    if (isNew || can('builds', 2)) body.parts = parts.get().map(priceNum);
    try {
      const r = await POST('staff/builds', body); toast(isNew ? 'ثبت شد: فاکتور ' + r.number : 'ذخیره شد.'); (r.warnings || []).forEach(w => toast(w, 'err'));
      if (isNew) location.hash = '#/build/' + r.id; else route();
    } catch (err) {
      if (err.status === 409 && err.data && err.data.duplicate) { if (await ask(err.message + ' با همین شماره ثبت شود؟', 'ثبت کن')) { force = true; save(e); } } else if (err.status === 409) { toast(err.message, 'err'); if (await ask('رکورد را دوباره بارگذاری کنم؟ (تغییرات این صفحه از بین می‌رود)', 'بارگذاری')) route(); } else fail(err);
    }
  };
  const actions = isNew ? null : h('div', { class: 'row' }, h('button', { type: 'button', class: 'btn', onclick: () => printLabel('build', b.id) }, 'چاپ برچسب'), h('button', { type: 'button', class: 'btn', onclick: () => sendReceipt('build', b.id) }, 'پیامک رسید'),
    can('tests') ? h('button', { type: 'button', class: 'btn', onclick: () => reportDialog('build', b) }, b.report_id ? 'گزارش تست ✓' : 'گزارش تست') : null);
  return h('div', {}, title(isNew ? 'سیستم نو' : `سیستم ${b.number}`, actions),
    h('form', { class: 'panel grid', onsubmit: save }, h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'مشتری'), picker.el), field('عنوان سیستم', f.title), field('نوع دستگاه', f.dtype), field('شمارهٔ فاکتور حسابداری', f.invoice, 'سیستم نو با شمارهٔ فاکتور و نام خریدار شناخته می‌شود'), field('تاریخ فروش', f.sold), field('وضعیت', f.status),
      b.service_no ? field('شمارهٔ سرویس (قدیمی)', h('input', { value: b.service_no, readOnly: true, dir: 'ltr' })) : null, h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'قطعات'), parts.el),
      field('لینک عکس و فیلم در تلگرام', f.media, '', 'wide'), mediaLinks(b.media), field('یادداشت', f.notes, '', 'wide'), ro && !(can('tests', 2) && can('builds')) ? null : h('div', { class: 'row wide' }, h('button', { class: 'btn primary' }, 'ذخیره'))));
}

/* ---------- job (a service) ---------- */
async function viewJob(id, q) {
  const isNew = id === 'new'; let j = { device: '', device_type: '', media: [], complaint: '', work_done: '', status: 'received', received_at: today(), due_at: '', invoice_no: '', notes: '', parts: [], recv: [], pays: [], labor_price: 0, discount: 0, paid: 0, is_mazesta: false, has_warranty: false }, cust = null;
  if (!isNew) { j = await GET('staff/jobs/' + id); cust = { id: j.customer_id, name: j.customer_name, mobile: j.customer_mobile }; }
  else if (q.get('cid')) { const r = await GET('staff/customers/' + q.get('cid')); cust = r.customer; }
  const ro = !can('jobs', 2), money1 = can('payments'), money2 = can('payments', 2), picker = customerPicker(cust, !isNew);
  const sum = h('b', { class: 'lat' }, ''), recalc = () => {
    const n = v => +digits(v).replace(/[^\d]/g, '') || 0; let t = n(f.labor.value) - n(f.discount.value);
    parts.get().forEach(p => { t += (n(p.qty) || 1) * n(p.unit_price); }); sum.textContent = money(t); };
  const f = { device: text('device', j.device, { readOnly: ro }), dtype: select('device_type', Object.assign({ '': '—' }, S.cfg.deviceTypes || {}), j.device_type || '', { disabled: ro }), media: mediaBox(j.media, ro), complaint: area('complaint', j.complaint, { readOnly: ro, rows: 3 }), work: area('work_done', j.work_done, { rows: 3, readOnly: ro }), status: select('status', S.cfg.jobStatuses, j.status, { disabled: ro }),
    recv: h('input', { type: 'date', value: j.received_at || today(), readOnly: ro }), due: h('input', { type: 'date', value: j.due_at || '', readOnly: ro }), invoice: text('invoice_no', j.invoice_no, { dir: 'ltr', readOnly: ro }),
    labor: text('labor', j.labor_price || '', { dir: 'ltr', readOnly: !money2, oninput: () => recalc() }), discount: text('discount', j.discount || '', { dir: 'ltr', readOnly: !money2, oninput: () => recalc() }), paid: text('paid', j.paid || '', { dir: 'ltr', readOnly: !money2 }),
    notes: area('notes', j.notes, { readOnly: ro }), maz: check('is_mazesta', j.is_mazesta, 'دستگاه از مازستا خریداری شده'), war: check('has_warranty', j.has_warranty, 'در گارانتی') };
  const partCols = [{ k: 'name', label: 'قطعهٔ اضافه‌شده (از انبار)', type: 'product', fill: { unit_price: 'sell_price', buy_price: 'buy_price', warranty_months: 'warranty_months' } }, { k: 'qty', label: 'تعداد', type: 'num', def: 1 }]
    .concat(money1 ? [{ k: 'unit_price', label: 'قیمت واحد', type: 'num', readOnly: !money2 }] : []).concat(S.cfg.costSeen ? [{ k: 'buy_price', label: 'قیمت خرید', type: 'num', readOnly: !S.cfg.costEdit }] : []).concat([{ k: 'serial', label: 'سریال', ltr: true }, { k: 'warranty_months', label: 'گارانتی (ماه)', type: 'num' }]);
  const parts = rowsEditor(partCols, j.parts, { readOnly: ro, onchange: recalc });
  const recv = rowsEditor([{ k: 'name', label: 'قطعه / لوازمی که مشتری آورده' }, { k: 'serial', label: 'سریال', ltr: true }, { k: 'has_box', label: 'جعبه', type: 'check' }], j.recv, { readOnly: ro });
  const pays = money1 ? rowsEditor([{ k: 'amount', label: 'مبلغ', type: 'num' }, { k: 'account', label: 'حساب / کارت' }, { k: 'tx', label: 'شمارهٔ پیگیری', ltr: true }, { k: 'holder', label: 'صاحب کارت' }], j.pays, { readOnly: !money2 }) : null;
  const fixNum = priceNum;
  const save = async e => {
    e.preventDefault(); const bad = picker.error(); if (bad) return toast(bad, 'err');
    const body = Object.assign({ id: isNew ? 0 : j.id, updated: j.updated, device: f.device.value, device_type: f.dtype.value, media: mediaOf(f.media), complaint: f.complaint.value, work_done: f.work.value, status: f.status.value, received_at: f.recv.value, due_at: f.due.value, invoice_no: digits(f.invoice.value), notes: f.notes.value,
      is_mazesta: $('input', f.maz).checked, has_warranty: $('input', f.war).checked, parts: parts.get().map(fixNum), recv: recv.get() }, picker.body());
    if (money2) { Object.assign(body, { labor_price: digits(f.labor.value), discount: digits(f.discount.value), paid: digits(f.paid.value), pays: pays.get().map(p => Object.assign(p, { amount: digits(p.amount) })) }); }
    try { const r = await POST('staff/jobs', body); toast(isNew ? 'ثبت شد: ' + r.service_no : 'ذخیره شد.'); if (isNew) { S.after = () => offerPrint(r); location.hash = '#/job/' + r.id; } else route(); }
    catch (err) { if (err.status === 409) { toast(err.message, 'err'); if (await ask('رکورد را دوباره بارگذاری کنم؟ (تغییرات این صفحه از بین می‌رود)', 'بارگذاری')) route(); } else fail(err); }
  };
  const actions = isNew ? null : h('div', { class: 'row' }, h('button', { type: 'button', class: 'btn', onclick: () => printLabel('job', j.id) }, 'چاپ برچسب'), h('button', { type: 'button', class: 'btn', onclick: () => printReceipt(j) }, 'چاپ رسید'),
    h('button', { type: 'button', class: 'btn', onclick: () => sendReceipt('job', j.id) }, 'پیامک رسید'), can('tests') ? h('button', { type: 'button', class: 'btn', onclick: () => reportDialog('job', j) }, j.report_id ? 'گزارش تست ✓' : 'گزارش تست') : null);
  const el = h('div', {}, title(isNew ? 'پذیرش سرویس' : `سرویس ${j.service_no}`, actions),
    h('form', { class: 'panel grid', onsubmit: save }, h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'مشتری'), picker.el), field('نوع دستگاه', f.dtype), field('دستگاه', f.device, 'مثلاً: لپ‌تاپ ایسوس / کیس / کارت گرافیک'), field('وضعیت', f.status), field('تاریخ پذیرش', f.recv), field('تاریخ قول تحویل', f.due), field('شمارهٔ فاکتور', f.invoice),
      h('div', { class: 'checks' }, f.maz, f.war), field('مشکل اعلام‌شده', f.complaint, '', 'wide'), field('کار انجام‌شده', f.work, '', 'wide'),
      h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'لوازمی که مشتری تحویل داده'), recv.el), h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'قطعات اضافه‌شده'), parts.el),
      money1 ? h('div', { class: 'wide money' }, field('اجرت', f.labor), field('تخفیف', f.discount), field('پرداخت‌شده', f.paid), h('div', { class: 'total' }, 'جمع: ', sum), pays ? h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'پرداخت‌ها'), pays.el) : null) : null,
      field('لینک عکس و فیلم در تلگرام', f.media, '', 'wide'), mediaLinks(j.media), field('یادداشت داخلی', f.notes, '', 'wide'), ro ? null : h('div', { class: 'row wide' }, h('button', { class: 'btn primary' }, 'ذخیره'))));
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

/* ---------- start ---------- */
(async function start() {
  const s = await bridge.session();
  if (s.data && s.data.signedIn) { try { S.me = await GET('staff/me'); return enter(); } catch (e) { /* fall through to the login page */ } }
  showLogin();
})();
