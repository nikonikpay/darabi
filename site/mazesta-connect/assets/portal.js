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
  function shell(title, sub) { root.textContent = ''; var card = el('div', 'mzcp-card'); add(card, el('h2', '', title), sub ? el('p', 'mzcp-sub', sub) : null); root.appendChild(card); return card; }

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

  function history_() {
    var card = shell('سوابق من');
    card.firstChild.textContent = 'در حال دریافت…';
    api('me').then(function (me) {
      root.textContent = '';
      var top = el('div', 'mzcp-top'); add(top, el('h2', '', 'سلام ' + me.name), el('button', 'mzcp-link', 'خروج'));
      top.lastChild.addEventListener('click', function () { api('logout', {}).catch(function () {}).then(function () { token = ''; try { window.sessionStorage.removeItem(KEY); } catch (e) { } login(); }); });
      root.appendChild(top);
      add(root, softForm(me));

      add(root, el('h3', 'mzcp-h', 'سیستم‌های خریداری‌شده'));
      if (!me.builds.length) add(root, el('p', 'mzcp-sub', 'هنوز سیستمی برای شما ثبت نشده است.'));
      me.builds.forEach(function (b) {
        var c = el('div', 'mzcp-card'), head = el('div', 'mzcp-row'); add(head, el('strong', '', b.title), b.date ? el('span', 'mzcp-pill', b.date) : null, b.invoice ? el('span', 'mzcp-pill', 'فاکتور ' + b.invoice) : null, b.status ? el('span', 'mzcp-pill', b.status) : null);
        add(c, head);
        if (b.parts.length) {
          var t = el('table', 'mzcp-table'), hd = el('tr'); ['قطعه', 'مدل', 'سریال', 'گارانتی'].forEach(function (x) { hd.appendChild(el('th', '', x)); }); t.appendChild(hd);
          b.parts.forEach(function (p) {
            var r = el('tr'), w = '—';
            if (p.until) w = (p.left < 0 ? 'تمام شده در ' : 'تا ') + p.until + (p.left >= 0 ? ' (' + p.left.toLocaleString('fa-IR') + ' روز مانده)' : '');
            else if (p.months) w = p.months + ' ماه'; else if (p.warranty) w = 'دارد';
            [p.category, p.model, p.serial || '—', w].forEach(function (x, i) { var td = el('td', i === 2 ? 'mzcp-lat' : '', x); td.setAttribute('data-label', hd.children[i].textContent); r.appendChild(td); });
            t.appendChild(r);
          });
          add(c, el('div', 'mzcp-scroll', null)).lastChild.appendChild(t);
        }
        add(c, softList(b.software));
        if (b.report) { var a = el('a', 'mzcp-link', 'گزارش تست سیستم'); a.href = b.report; a.target = '_blank'; a.rel = 'noopener'; add(c, a); }
        add(c, photos(b.photos)); root.appendChild(c);
      });

      add(root, el('h3', 'mzcp-h', 'سرویس‌ها'));
      if (!me.jobs.length) add(root, el('p', 'mzcp-sub', 'هنوز سرویسی برای شما ثبت نشده است.'));
      me.jobs.forEach(function (j) {
        var c = el('div', 'mzcp-card'), head = el('div', 'mzcp-row');
        add(head, el('strong', '', j.device || 'سرویس'), el('span', 'mzcp-pill st-' + j.status, j.statusText), j.service ? el('span', 'mzcp-pill', 'شمارهٔ سرویس ' + j.service) : null);
        add(c, head);
        var dl = el('dl', 'mzcp-kv'); kv(dl, 'تاریخ پذیرش', j.received); kv(dl, 'موعد تحویل', j.due); kv(dl, 'تاریخ تحویل', j.closed); kv(dl, 'شمارهٔ فاکتور', j.invoice);
        if (j.recv && j.recv.length) kv(dl, 'قطعات تحویل‌گرفته‌شده', j.recv.map(function (x) { return x.name + (x.serial ? ' (' + x.serial + ')' : ''); }).join('، ')); kv(dl, 'درخواست شما', j.complaint); kv(dl, 'کارهای انجام‌شده', j.work); add(c, dl);
        if (j.parts.length || j.labor || j.other) {
          var t = el('table', 'mzcp-table'), hd = el('tr'); ['قطعه / کار', 'تعداد', 'قیمت واحد', 'جمع'].forEach(function (x) { hd.appendChild(el('th', '', x)); }); t.appendChild(hd);
          j.parts.forEach(function (p) { var r = el('tr'); [p.name + (p.months ? ' (گارانتی ' + p.months + ' ماه)' : ''), p.qty.toLocaleString('fa-IR'), money(p.price), money(p.qty * p.price)].forEach(function (x, i) { var td = el('td', '', x); td.setAttribute('data-label', hd.children[i].textContent); r.appendChild(td); }); t.appendChild(r); });
          if (j.other) { var ro = el('tr'); ['سایر هزینه‌ها', '', '', money(j.other)].forEach(function (x, i) { var td = el('td', '', x); td.setAttribute('data-label', hd.children[i].textContent); ro.appendChild(td); }); t.appendChild(ro); }
          if (j.labor) { var r = el('tr'); ['هزینهٔ خدمات', '', '', money(j.labor)].forEach(function (x, i) { var td = el('td', '', x); td.setAttribute('data-label', hd.children[i].textContent); r.appendChild(td); }); t.appendChild(r); }
          add(c, el('div', 'mzcp-scroll', null)).lastChild.appendChild(t);
          if (j.discount) add(c, el('p', 'mzcp-sub', 'تخفیف: ' + money(j.discount)));
          add(c, el('p', 'mzcp-total', 'جمع کل: ' + money(j.total)));
          if (j.paid) add(c, el('p', 'mzcp-sub', 'پرداخت‌شده: ' + money(j.paid) + (j.total > j.paid ? ' — مانده: ' + money(j.total - j.paid) : '')));
        }
        add(c, softList(j.software));
        if (j.report) { var a = el('a', 'mzcp-link', 'گزارش تست'); a.href = j.report; a.target = '_blank'; a.rel = 'noopener'; add(c, a); }
        add(c, photos(j.photos)); root.appendChild(c);
      });
    }).catch(function (e) {
      if (e.status === 401) { token = ''; try { window.sessionStorage.removeItem(KEY); } catch (x) { } login(); } else { var c = shell('خطا', e.message); add(c, el('button', 'mzcp-btn', 'تلاش دوباره')).lastChild.addEventListener('click', history_); }
    });
  }

  if (token) history_(); else login();
})();
