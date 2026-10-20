/* The customer's page: mobile number -> SMS code -> history. All text is put in with textContent (nothing from the server is ever read as HTML). */
(function () {
  'use strict';
  var root = document.getElementById('mzc-portal');
  if (!root || !window.MZC_PORTAL) return;
  var API = window.MZC_PORTAL.api, KEY = 'mzc_token', token = '';
  try { token = window.sessionStorage.getItem(KEY) || ''; } catch (e) { token = ''; }

  function el(tag, cls, text) { var n = document.createElement(tag); if (cls) n.className = cls; if (text !== undefined && text !== null) n.textContent = text; return n; }
  function add(parent) { for (var i = 1; i < arguments.length; i++) if (arguments[i]) parent.appendChild(arguments[i]); return parent; }
  function money(n) { return Number(n || 0).toLocaleString('fa-IR') + ' تومان'; }
  function api(path, body, method) {
    var h = { 'Content-Type': 'application/json' }; if (token) h['X-MZC-Token'] = token;
    return fetch(API + path, { method: method || (body ? 'POST' : 'GET'), headers: h, body: body ? JSON.stringify(body) : undefined, credentials: 'omit', cache: 'no-store' })
      .then(function (r) { return r.json().then(function (j) { if (!r.ok) { var e = new Error(j && j.error || j && j.message || 'خطا'); e.status = r.status; throw e; } return j; }); });
  }
  function shell(title, sub) { root.textContent = ''; var card = el('div', 'mzcp-card mzcp-auth'); add(card, el('span', 'mzcp-mark', 'M')); add(card, el('h2', '', title), sub ? el('p', 'mzcp-sub', sub) : null); root.appendChild(card); return card; }

  function login() {
    var card = shell('سوابق من در مازستا', 'شمارهٔ موبایلی که نزد ما ثبت است را وارد کنید؛ کد ورود را با پیامک می‌فرستیم.');
    var input = el('input'); input.type = 'tel'; input.inputMode = 'numeric'; input.placeholder = '09121234567'; input.autocomplete = 'tel'; input.dir = 'ltr';
    var btn = el('button', 'mzcp-btn', 'ارسال کد'), msg = el('p', 'mzcp-msg');
    add(card, input, btn, msg);
    function go() {
      btn.disabled = true; msg.textContent = '';
      api('code', { mobile: input.value }).then(function (r) { code(input.value, r.minutes); }).catch(function (e) { msg.textContent = e.message; btn.disabled = false; });
    }
    btn.addEventListener('click', go); input.addEventListener('keydown', function (e) { if (e.key === 'Enter') go(); });
    input.focus();
  }

  function code(mobile, minutes) {
    var card = shell('کد را وارد کنید', 'اگر این شماره در سوابق ما باشد تا چند لحظهٔ دیگر پیامکی با کد شش‌رقمی می‌رسد (تا ' + (minutes || 5) + ' دقیقه معتبر است).');
    var input = el('input'); input.type = 'text'; input.inputMode = 'numeric'; input.maxLength = 6; input.placeholder = '------'; input.autocomplete = 'one-time-code'; input.dir = 'ltr';
    var btn = el('button', 'mzcp-btn', 'ورود'), back = el('button', 'mzcp-link', 'شمارهٔ دیگر'), msg = el('p', 'mzcp-msg');
    add(card, input, btn, back, msg);
    function go() {
      btn.disabled = true; msg.textContent = '';
      api('verify', { mobile: mobile, code: input.value }).then(function (r) {
        token = r.token; try { window.sessionStorage.setItem(KEY, token); } catch (e) { /* the page still works until it is closed */ } history_();
      }).catch(function (e) { msg.textContent = e.message; btn.disabled = false; });
    }
    btn.addEventListener('click', go); input.addEventListener('keydown', function (e) { if (e.key === 'Enter') go(); });
    back.addEventListener('click', login); input.focus();
  }

  function photos(ids) {
    var wrap = el('div', 'mzcp-photos');
    (ids || []).forEach(function (id) {
      var img = el('img'); img.alt = ''; img.loading = 'lazy'; wrap.appendChild(img);
      fetch(window.MZC_PORTAL.photo + id, { headers: { 'X-MZC-Token': token }, credentials: 'omit' }).then(function (r) { return r.ok ? r.blob() : null; })
        .then(function (b) { if (b) { img.src = URL.createObjectURL(b); } else { img.remove(); } }).catch(function () { img.remove(); });
    });
    return ids && ids.length ? wrap : null;
  }
  function kv(dl, k, v) { if (v === null || v === undefined || v === '') return; add(dl, el('dt', '', k), el('dd', '', v)); }

  /* the programs the customer wants on a system or service that is still open: they open it by its invoice or service number, pick names from the shop's list as they type
     (or write one that is not on it), and add their request in words */
  function softForm(me) {
    var open = me.builds.concat(me.jobs).filter(function (r) { return r.soft_open && r.number; });
    if (!open.length) return null;
    var card = el('div', 'mzcp-card'), dl = document.createElement('datalist'); dl.id = 'mzcp-soft';
    (me.catalog || []).forEach(function (n) { var o = document.createElement('option'); o.value = n; dl.appendChild(o); });
    var num = el('input'); num.type = 'text'; num.dir = 'ltr'; num.placeholder = 'شمارهٔ فاکتور یا سرویس'; num.inputMode = 'numeric';
    var go = el('button', 'mzcp-btn', 'باز کردن فرم'), quick = el('div', 'mzcp-row'), box = el('div'), msg = el('p', 'mzcp-msg');
    open.forEach(function (r) { var b = el('button', 'mzcp-link', (r.kind === 'build' ? 'سیستم ' : 'سرویس ') + r.number); b.addEventListener('click', function () { num.value = r.number; show(); }); quick.appendChild(b); });
    add(card, el('h3', '', 'نرم‌افزارهایی که می‌خواهید نصب شود'), el('p', 'mzcp-sub', 'شمارهٔ فاکتور سیستم یا شمارهٔ سرویس را وارد کنید (یا یکی را انتخاب کنید) و نرم‌افزارها را بنویسید.'), num, go, quick, msg, box, dl);
    function show() {
      msg.textContent = ''; box.textContent = '';
      var v = num.value.replace(/[۰-۹]/g, function (d) { return '۰۱۲۳۴۵۶۷۸۹'.indexOf(d); }).trim().toUpperCase();
      var r = open.filter(function (x) { return x.number.toUpperCase() === v || x.number.toUpperCase() === 'R-' + v; })[0];
      if (!r) { msg.textContent = 'پرونده‌ای باز با این شماره برای شما پیدا نشد.'; return; }
      var names = r.software.items.map(function (x) { return x.name; }), chips = el('div', 'mzcp-row');
      var draw = function () {
        chips.textContent = '';
        names.forEach(function (n, i) { var c = el('span', 'mzcp-pill', n + ' '), x = el('button', 'mzcp-link', '×'); x.setAttribute('aria-label', 'حذف ' + n); x.addEventListener('click', function () { names.splice(i, 1); draw(); }); c.appendChild(x); chips.appendChild(c); });
        if (!names.length) chips.appendChild(el('span', 'mzcp-sub', 'هنوز نرم‌افزاری انتخاب نشده است.'));
      };
      var inp = el('input'); inp.type = 'text'; inp.setAttribute('list', 'mzcp-soft'); inp.placeholder = 'نام نرم‌افزار را بنویسید…'; inp.dir = 'auto'; inp.autocomplete = 'off';
      var addB = el('button', 'mzcp-btn', 'افزودن');
      var push = function () { var n = inp.value.trim(); if (n && !names.some(function (x) { return x.toLowerCase() === n.toLowerCase(); })) { names.push(n); draw(); } inp.value = ''; inp.focus(); };
      addB.addEventListener('click', push); inp.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); push(); } });
      var note = el('textarea'); note.rows = 3; note.placeholder = 'درخواست‌های نرم‌افزاری دیگر (نسخهٔ خاص، زبان، تنظیمات …)'; note.value = r.software.note || '';
      var save = el('button', 'mzcp-btn', 'ذخیره'), done = el('p', 'mzcp-msg');
      save.addEventListener('click', function () {
        save.disabled = true; done.textContent = '';
        api('software', { kind: r.kind, number: r.number, items: names, note: note.value }).then(function (x) { r.software = x.software; done.textContent = 'ذخیره شد. سپاس!'; save.disabled = false; })
          .catch(function (e) { done.textContent = e.message; save.disabled = false; });
      });
      draw();
      add(box, el('p', 'mzcp-sub', (r.kind === 'build' ? 'سیستم ' : 'سرویس ') + r.number + ' — اگر نرم‌افزار در فهرست نیست، نامش را کامل بنویسید.'), chips, inp, addB, note, save, done);
      inp.focus();
    }
    go.addEventListener('click', show); num.addEventListener('keydown', function (e) { if (e.key === 'Enter') show(); });
    return card;
  }
  function softList(s) {
    if (!s || (!s.items.length && !s.note)) return null;
    var p = el('p', 'mzcp-sub', 'نرم‌افزارها: ' + s.items.map(function (x) { return x.name + (x.done ? ' ✓' : ''); }).join('، ') + (s.note ? ' — ' + s.note : ''));
    return p;
  }

  function table(heads, rows) {
    var t = el('table', 'mzcp-table'), th = el('thead'), hr = el('tr'), tb = el('tbody');
    heads.forEach(function (x) { var c = el('th', '', x); c.scope = 'col'; hr.appendChild(c); }); add(t, add(th, hr), tb);
    rows.forEach(function (cells) { var r = el('tr'); cells.forEach(function (x, i) { var td = el('td', x && x.cls || '', x && x.text !== undefined ? x.text : x); td.setAttribute('data-label', heads[i]); r.appendChild(td); }); tb.appendChild(r); });
    return add(el('div', 'mzcp-scroll'), t);
  }
  function link(text, href) { var a = el('a', 'mzcp-btn ghost', text); a.href = href; a.target = '_blank'; a.rel = 'noopener'; return a; }
  function stat(label, value, note, tone) { return add(el('div', 'mzcp-stat' + (tone ? ' ' + tone : '')), el('span', 'k', label), el('b', '', value), note ? el('small', '', note) : null); }

  /* where a service is now: the stages in order, the current one marked (a cancelled service shows its status only) */
  function stepper(j, statuses) {
    var keys = Object.keys(statuses).filter(function (k) { return k !== 'cancelled'; }), at = keys.indexOf(j.status);
    if (at < 0) return null;
    var ol = el('ol', 'mzcp-steps'); ol.setAttribute('aria-label', 'مراحل سرویس');
    keys.forEach(function (k, i) { var li = el('li', i < at ? 'done' : i === at ? 'now' : '', statuses[k]); if (i === at) li.setAttribute('aria-current', 'step'); ol.appendChild(li); });
    return ol;
  }

  function buildCard(b) {
    var c = el('article', 'mzcp-card'), head = el('header', 'mzcp-card-head');
    add(head, add(el('div'), el('h3', '', b.title || 'سیستم'), add(el('div', 'mzcp-meta'), b.invoice ? el('span', '', 'فاکتور ' + b.invoice) : null, b.date ? el('span', '', b.date) : null)), b.status ? el('span', 'mzcp-pill', b.status) : null);
    add(c, head);
    if (b.parts.length) add(c, table(['قطعه', 'مدل', 'سریال', 'گارانتی'], b.parts.map(function (p) {
      var w = { text: '—' };
      if (p.until) w = p.left < 0 ? { text: 'تمام شد (' + p.until + ')', cls: 'mzcp-off' } : { text: p.left.toLocaleString('fa-IR') + ' روز مانده', cls: 'mzcp-on' };
      else if (p.months) w = { text: p.months + ' ماه' }; else if (p.warranty) w = { text: 'دارد' };
      return [p.category, p.model, { text: p.serial || '—', cls: 'mzcp-lat' }, w];
    })));
    add(c, softList(b.software), b.report ? add(el('div', 'mzcp-actions'), link('گزارش تست سیستم', b.report)) : null, photos(b.photos));
    return c;
  }

  function jobCard(j, statuses) {
    var c = el('article', 'mzcp-card'), head = el('header', 'mzcp-card-head');
    add(head, add(el('div'), el('h3', '', j.device || 'سرویس'), add(el('div', 'mzcp-meta'), j.service ? el('span', '', 'سرویس ' + j.service) : null, j.received ? el('span', '', 'پذیرش ' + j.received) : null)), el('span', 'mzcp-pill st-' + j.status, j.statusText));
    add(c, head, stepper(j, statuses));
    var body = el('div', 'mzcp-split'), dl = el('dl', 'mzcp-kv');
    kv(dl, 'موعد تحویل', j.due); kv(dl, 'تاریخ تحویل', j.closed); kv(dl, 'شمارهٔ فاکتور', j.invoice);
    if (j.recv && j.recv.length) kv(dl, 'وسایل تحویل‌گرفته', j.recv.map(function (x) { return x.name + (x.serial ? ' (' + x.serial + ')' : ''); }).join('، '));
    kv(dl, 'درخواست شما', j.complaint); kv(dl, 'کارهای انجام‌شده', j.work);
    add(body, dl.children.length ? dl : null);
    if (j.parts.length || j.labor) {
      var bill = el('div', 'mzcp-bill'), rows = j.parts.map(function (p) { return [p.name + (p.months ? ' (گارانتی ' + p.months + ' ماه)' : ''), p.qty.toLocaleString('fa-IR'), money(p.price), money(p.qty * p.price)]; });
      if (j.labor) rows.push(['هزینهٔ خدمات', '', '', money(j.labor)]);
      var sums = el('dl', 'mzcp-sums'); if (j.discount) kv(sums, 'تخفیف', money(j.discount)); kv(sums, 'جمع کل', money(j.total)); if (j.paid) kv(sums, 'پرداخت‌شده', money(j.paid));
      if (j.total > j.paid) { kv(sums, 'مانده', money(j.total - j.paid)); sums.lastChild.className = sums.lastChild.previousSibling.className = 'due'; }
      add(bill, table(['قطعه / کار', 'تعداد', 'قیمت واحد', 'جمع'], rows), sums); add(body, bill);
    }
    add(c, body.children.length ? body : null, softList(j.software), j.report ? add(el('div', 'mzcp-actions'), link('گزارش تست', j.report)) : null, photos(j.photos));
    return c;
  }

  function empty(text) { return add(el('div', 'mzcp-empty'), el('p', '', text)); }

  function history_() {
    var card = shell('سوابق من', 'در حال دریافت…');
    card.setAttribute('aria-busy', 'true');
    api('me').then(function (me) {
      root.textContent = '';
      var open = me.jobs.filter(function (j) { return j.status !== 'delivered' && j.status !== 'cancelled'; });
      var due = me.jobs.reduce(function (s, j) { return s + Math.max(0, j.total - j.paid); }, 0);
      var live = me.builds.reduce(function (s, b) { return s + b.parts.filter(function (p) { return p.until && p.left >= 0; }).length; }, 0);

      var who = el('header', 'mzcp-profile'), out = el('button', 'mzcp-btn ghost', 'خروج');
      add(who, el('span', 'mzcp-avatar', (me.name || '؟').trim().charAt(0)), add(el('div', 'mzcp-who'), el('h2', '', me.name), me.mobile ? el('span', 'mzcp-lat', me.mobile) : null), out);
      out.addEventListener('click', function () { api('logout', {}).catch(function () {}).then(function () { token = ''; try { window.sessionStorage.removeItem(KEY); } catch (e) { } login(); }); });

      var stats = add(el('section', 'mzcp-stats'), stat('سیستم‌های خریداری‌شده', me.builds.length.toLocaleString('fa-IR')), stat('سرویس در جریان', open.length.toLocaleString('fa-IR'), open.length ? open[0].statusText : 'همه تحویل شده'),
        stat('مانده پرداخت', due ? money(due) : 'تسویه', null, due ? 'due' : 'ok'), stat('قطعه با گارانتی فعال', live.toLocaleString('fa-IR')));
      stats.setAttribute('aria-label', 'خلاصه');

      // tabs: systems, services, and the software form when something is still open
      var soft = softForm(me), tabs = [['jobs', 'سرویس‌ها', me.jobs.length], ['builds', 'سیستم‌ها', me.builds.length]].concat(soft ? [['soft', 'نرم‌افزار', null]] : []);
      var bar = el('div', 'mzcp-tabs'), panes = {}, btns = [];
      bar.setAttribute('role', 'tablist');
      var pick = function (k, focus) {
        btns.forEach(function (b) { var on = b.dataset.k === k; b.setAttribute('aria-selected', on ? 'true' : 'false'); b.tabIndex = on ? 0 : -1; if (on && focus) b.focus(); });
        Object.keys(panes).forEach(function (x) { panes[x].hidden = x !== k; });
      };
      tabs.forEach(function (t, i) {
        var b = el('button', '', t[1]); b.type = 'button'; b.id = 'mzcp-tab-' + t[0]; b.dataset.k = t[0]; b.setAttribute('role', 'tab'); b.setAttribute('aria-controls', 'mzcp-pane-' + t[0]);
        if (t[2] !== null) add(b, el('span', 'n', t[2].toLocaleString('fa-IR')));
        b.addEventListener('click', function () { pick(t[0]); });
        b.addEventListener('keydown', function (e) { var d = e.key === 'ArrowLeft' ? 1 : e.key === 'ArrowRight' ? -1 : 0; if (d) { e.preventDefault(); pick(tabs[(i + d + tabs.length) % tabs.length][0], true); } });
        btns.push(b); bar.appendChild(b);
        var p = el('section', 'mzcp-pane'); p.id = 'mzcp-pane-' + t[0]; p.setAttribute('role', 'tabpanel'); p.setAttribute('aria-labelledby', b.id); panes[t[0]] = p;
      });
      if (me.jobs.length) me.jobs.forEach(function (j) { panes.jobs.appendChild(jobCard(j, me.statuses || {})); }); else panes.jobs.appendChild(empty('هنوز سرویسی برای شما ثبت نشده است.'));
      if (me.builds.length) me.builds.forEach(function (b) { panes.builds.appendChild(buildCard(b)); }); else panes.builds.appendChild(empty('هنوز سیستمی برای شما ثبت نشده است.'));
      if (soft) panes.soft.appendChild(soft);

      add(root, who, stats, bar); tabs.forEach(function (t) { root.appendChild(panes[t[0]]); });
      pick(open.length || !me.builds.length ? 'jobs' : 'builds');
    }).catch(function (e) {
      if (e.status === 401) { token = ''; try { window.sessionStorage.removeItem(KEY); } catch (x) { } login(); } else { var c = shell('خطا', e.message); add(c, el('button', 'mzcp-btn', 'تلاش دوباره')).lastChild.addEventListener('click', history_); }
    });
  }

  if (token) history_(); else login();
})();
