/* Mazesta Connect admin: part rows (add / remove), live searches under their fields, the invoice form, filtering a list of checkboxes, rows that open on click. No libraries. */
(function () {
  'use strict';
  var digits = function (v) { return String(v == null ? '' : v).replace(/[۰-۹]/g, function (d) { return '۰۱۲۳۴۵۶۷۸۹'.indexOf(d); }); };
  var num = function (v) { v = digits(v).replace(/[^\d]/g, ''); return v ? parseInt(v, 10) : 0; };
  var fmt = function (x) { return x.toLocaleString('fa-IR'); };
  var el = function (tag, cls, text) { var e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; };
  var getJson = function (url, q) { return fetch(url + '&q=' + encodeURIComponent(digits(q)), { credentials: 'same-origin' }).then(function (r) { return r.json(); }); };

  /* a template row added to its list (the row's own markup is the template's, so the row keeps its class); returns the row */
  var addRow = function (id) {
    var box = document.getElementById(id), tpl = document.getElementById(id + '-tpl');
    if (!box || !tpl) return null;
    var n = parseInt(box.getAttribute('data-next') || '0', 10), w = document.createElement('div');
    w.innerHTML = tpl.innerHTML.replace(/__N__/g, String(n)).trim();
    var row = w.firstElementChild && w.children.length === 1 && w.firstElementChild.classList.contains('mzc-row') ? w.firstElementChild : w;
    if (row === w) w.className = 'mzc-row';
    box.appendChild(row); box.setAttribute('data-next', String(n + 1));
    return row;
  };

  /* a live search under its field: from 3 characters the closest matches drop down right under the input as one types (arrows and Enter pick, Esc closes); the page is not
     reloaded. The box is fixed to the window so a panel's or table's overflow never cuts it off. load(q) resolves to [{fill(button), pick()}]. */
  var suggest = function (inp, load, empty) {
    var box = el('div', 'mzc-dd'), t = 0, seq = 0, opts = [], at = -1;
    box.hidden = true; inp.setAttribute('autocomplete', 'off'); inp.insertAdjacentElement('afterend', box);
    var place = function () { var r = inp.getBoundingClientRect(); box.style.top = (r.bottom + 2) + 'px'; box.style.right = (document.documentElement.clientWidth - r.right) + 'px'; box.style.minWidth = Math.max(r.width, 300) + 'px'; };
    var hide = function () { box.hidden = true; window.removeEventListener('scroll', place, true); };
    var mark = function (i) { at = i; opts.forEach(function (o, k) { o.classList.toggle('on', k === i); }); if (opts[i]) opts[i].scrollIntoView({ block: 'nearest' }); };
    inp.addEventListener('input', function () {
      clearTimeout(t); var q = inp.value.trim();
      if (q.length < 3) { hide(); return; }
      t = setTimeout(function () {
        var my = ++seq;
        load(q).then(function (items) {
          if (my !== seq || document.activeElement !== inp) return;
          box.textContent = ''; at = -1;
          opts = items.map(function (it) {
            var b = el('button', 'mzc-copt'); b.type = 'button'; b.tabIndex = -1; it.fill(b);
            b.addEventListener('mousedown', function (e) { e.preventDefault(); }); b.addEventListener('click', function () { hide(); it.pick(); });
            box.appendChild(b); return b;
          });
          if (!opts.length && empty) box.appendChild(el('small', 'muted', empty));
          if (!box.children.length) { hide(); return; }
          place(); box.hidden = false; window.addEventListener('scroll', place, true);
        }).catch(function () { });
      }, 250);
    });
    inp.addEventListener('keydown', function (e) {
      if (box.hidden) return;
      if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); mark(Math.max(0, Math.min(opts.length - 1, at + (e.key === 'ArrowDown' ? 1 : -1)))); }
      else if (e.key === 'Enter' && opts.length) { e.preventDefault(); opts[Math.max(0, at)].click(); }
      else if (e.key === 'Escape') { e.preventDefault(); hide(); }
    });
    inp.addEventListener('blur', function () { setTimeout(hide, 150); });
  };

  /* the stock products (stock and price on each) for a part row's name box or the invoice's quick add */
  var plist = document.getElementById('mzc-products'), purl = plist && plist.getAttribute('data-url');
  var products = function (pick) {
    return function (q) {
      return getJson(purl, q).then(function (r) {
        return (r.items || []).map(function (p) {
          return { pick: function () { pick(p); }, fill: function (b) {
            b.appendChild(el('strong', '', p.name)); b.appendChild(el('span', 'mzc-pill ' + (p.stock > 0 ? 'ok' : 'bad'), 'موجودی ' + fmt(p.stock)));
            b.appendChild(el('span', 'num muted', p.sell_price != null ? fmt(p.sell_price) + ' تومان' : 'بی‌قیمت'));
          } };
        });
      });
    };
  };
  /* a picked product on its row: the name and id, the category always, the prices and warranty only where the row has none yet */
  var fillRow = function (row, p) {
    var f = function (k) { return row.querySelector('[data-f="' + k + '"]'); }, set = function (k, v, always) { var e = f(k); if (e && v != null && v !== '' && (always || !e.value)) e.value = v; };
    set('name', p.name, true); set('pid', p.id, true); set('cat', p.category, true); set('sell', p.sell_price); set('buy', p.buy_price); set('w', p.warranty_months);
    row.dispatchEvent(new Event('input', { bubbles: true }));
  };
  // a row's name box gets its search on first focus (rows added later too); typing over a picked name unlinks the row from the stock
  document.addEventListener('focusin', function (e) {
    var i = e.target; if (!purl || !i.matches || !i.matches('[data-mzc-pq]') || i.mzcS) return;
    i.mzcS = 1; var row = i.closest('[data-mzc-part]'); suggest(i, products(function (p) { fillRow(row, p); }), 'در انبار نیست؛ همین نام نوشته می‌شود.');
  });

  document.addEventListener('click', function (e) {
    var add = e.target.closest('[data-mzc-add]');
    if (add) {
      e.preventDefault();
      var row = addRow(add.getAttribute('data-mzc-add')); if (!row) return;
      var first = row.querySelector('input:not([type=hidden]),select'); if (first) first.focus();
      return;
    }
    var rm = e.target.closest('[data-mzc-remove]');
    if (rm) { e.preventDefault(); var r = rm.closest('.mzc-row'); if (r) r.remove(); return; }
    var go = e.target.closest('tr[data-href]');
    if (go && !e.target.closest('a,button,input,select')) { window.location = go.getAttribute('data-href'); }
  });
  document.addEventListener('input', function (e) {
    var f = e.target.closest('[data-mzc-filter]');
    if (!f) return;
    var q = f.value.trim().toLowerCase(), list = document.getElementById(f.getAttribute('data-mzc-filter'));
    if (!list) return;
    list.querySelectorAll('label').forEach(function (l) { l.style.display = l.textContent.toLowerCase().indexOf(q) >= 0 ? '' : 'none'; });
  });
  document.addEventListener('input', function (e) {
    var inp = e.target, row = inp.closest && inp.closest('[data-mzc-part]');
    if (row && inp.getAttribute('data-f') === 'name') { var pid = row.querySelector('[data-f="pid"]'); if (pid) pid.value = ''; }
  });
  document.addEventListener('change', function (e) {
    var t = e.target.closest('[data-mzc-target]');
    if (!t) return;
    var some = document.getElementById('mzc-some'); if (some) some.hidden = t.value !== 'some';
    var grp = document.getElementById('mzc-group'); if (grp) grp.hidden = t.value !== 'group';
  });
  /* the form's customer: typing a name or a number drops the closest customers (any of their numbers) under that box; picking one fills customer_id, "change" clears it again */
  var cbox = document.querySelector('[data-mzc-cfind]');
  if (cbox) {
    var cid = cbox.querySelector('[name="customer_id"]'), picked = cbox.querySelector('.mzc-cpicked');
    var inputs = cbox.querySelectorAll('input[type="text"]'), mob = cbox.querySelector('[name="c_mobile"]');
    var unpick = function () { cid.value = ''; picked.hidden = true; mob.required = cbox.hasAttribute('data-mzc-req'); inputs.forEach(function (i) { i.readOnly = false; i.value = ''; }); };
    var pick = function (c) {
      cid.value = c.id; mob.required = false;
      cbox.querySelector('[name="c_name"]').value = c.name; mob.value = c.mobiles[0] || ''; cbox.querySelector('[name="c_mobile2"]').value = c.mobiles[1] || '';
      inputs.forEach(function (i) { i.readOnly = true; });
      picked.textContent = 'مشتری ثبت‌شده انتخاب شد: ' + c.name + ' '; var x = el('button', 'mzc-btn sm', 'تغییر'); x.type = 'button';
      x.addEventListener('click', function () { unpick(); inputs[0].focus(); }); picked.appendChild(x); picked.hidden = false;
    };
    var customers = function (q) {
      return getJson(cbox.getAttribute('data-mzc-cfind'), q).then(function (r) {
        return (r.items || []).map(function (c) { return { pick: function () { pick(c); }, fill: function (b) { b.appendChild(el('strong', '', c.name)); b.appendChild(el('span', 'num muted', c.mobiles.concat(c.phone ? [c.phone] : []).join(' · '))); } }; });
      });
    };
    cbox.querySelectorAll('[data-mzc-cq]').forEach(function (i) { suggest(i, customers, 'مشتری با این مشخصات ثبت نشده؛ با ذخیرهٔ فرم مشتری تازه ساخته می‌شود.'); });
    if (cbox.getAttribute('data-mzc-picked')) pick(JSON.parse(cbox.getAttribute('data-mzc-picked')));
  }

  /* the invoice form: each line's sum and the invoice's totals as one types; the quick add puts a product in (or one more of a line already there); Enter goes to the next box
     instead of sending a half-written invoice */
  var inv = document.querySelector('[data-mzc-inv]');
  if (inv) {
    var lines = document.getElementById('mzc-lines'), out = function (k) { return inv.querySelector('[data-o="' + k + '"]'); };
    var recalc = function () {
      var sub = 0;
      lines.querySelectorAll('.mzc-row').forEach(function (r) { var t = Math.max(1, num(r.querySelector('[data-f="qty"]').value)) * num(r.querySelector('[data-f="sell"]').value); sub += t; r.querySelector('[data-o="line"]').textContent = fmt(t); });
      var d = Math.min(sub, num(inv.querySelector('[name="discount"]').value)), pct = Math.min(100, Math.max(0, parseFloat(digits(inv.querySelector('[name="tax_pct"]').value)) || 0)), tax = Math.round((sub - d) * pct / 100);
      out('sub').textContent = fmt(sub); out('tax').textContent = fmt(tax); out('total').textContent = fmt(sub - d + tax);
    };
    inv.addEventListener('input', recalc);
    inv.addEventListener('click', function (e) { if (e.target.closest('[data-mzc-remove], [data-mzc-add]')) setTimeout(recalc, 0); });
    var adder = inv.querySelector('[data-mzc-padd]');
    if (adder && purl) {
      suggest(adder, products(function (p) {
        var have = null; lines.querySelectorAll('[data-f="pid"]').forEach(function (x) { if (x.value === String(p.id)) have = x.closest('.mzc-row'); });
        if (have) { var q = have.querySelector('[data-f="qty"]'); q.value = Math.max(1, num(q.value)) + 1; } else { var row = addRow('mzc-lines'); fillRow(row, p); }
        adder.value = ''; adder.focus(); recalc();
      }), 'کالایی با این نام در انبار نیست؛ با «+ ردیف آزاد» بنویسید.');
    }
    inv.addEventListener('keydown', function (e) {
      if (e.key !== 'Enter' || e.defaultPrevented || e.target.tagName !== 'INPUT') return;
      e.preventDefault();
      var f = Array.prototype.filter.call(inv.querySelectorAll('input:not([type=hidden]):not([readonly]), select, textarea, button'), function (x) { return x.offsetParent; }), i = f.indexOf(e.target);
      if (f[i + 1]) f[i + 1].focus();
    });
    recalc();
    if (!lines.children.length) adder.focus();
  }

  /* a customer's computer case is known by its service number; the model and serial fields are for a laptop (or a part) */
  var dtype = document.querySelector('select[name="device_type"]');
  var devShow = function () { var sys = dtype.value === 'system'; document.querySelectorAll('[data-mzc-dev]').forEach(function (l) { l.style.display = sys ? 'none' : ''; }); };
  if (dtype && document.querySelector('[data-mzc-dev]')) { dtype.addEventListener('change', devShow); devShow(); }
  /* the tick lists change with the device type: a computer case, a laptop or a part */
  var listShow = function () { document.querySelectorAll('[data-mzc-dtype]').forEach(function (l) { l.hidden = l.getAttribute('data-mzc-dtype') !== dtype.value; }); };
  if (dtype && document.querySelector('[data-mzc-dtype]')) { dtype.addEventListener('change', listShow); listShow(); }

  /* the service's money, worked out as one types: parts, the customer's final sum (parts + services - discount), the services' profit (minus what we paid out) and the parts' profit */
  var money = document.querySelector('[data-mzc-money]');
  if (money) {
    var n = num;
    var calc = function () {
      var g = function (k) { return n(money.querySelector('[data-m="' + k + '"]').value); }, parts = 0, cost = 0;
      document.querySelectorAll('#mzc-parts [data-mzc-part]').forEach(function (r) {
        var q = r.querySelector('[name$="[qty]"]'), u = r.querySelector('[name$="[unit_price]"]'), b = r.querySelector('[name$="[buy_price]"]'), qty = Math.max(1, n(q && q.value));
        parts += qty * n(u && u.value); cost += qty * n(b && b.value);
      });
      money.querySelector('[data-o="parts"]').textContent = fmt(parts); money.querySelector('[data-o="svc"]').textContent = fmt(g('labor') - g('discount') - g('paid'));
      money.querySelector('[data-o="total"]').textContent = fmt(parts + g('labor') - g('discount')) + ' تومان'; money.querySelector('[data-o="profit"]').textContent = fmt(parts - cost);
    };
    document.addEventListener('input', function (e) { if (e.target.closest('[data-mzc-money], #mzc-parts')) calc(); });
    document.addEventListener('click', function (e) { if (e.target.closest('[data-mzc-remove], [data-mzc-add]')) setTimeout(calc, 0); });
    calc();
  }

  document.addEventListener('submit', function (e) {
    var c = e.target.getAttribute && e.target.getAttribute('data-mzc-confirm');
    if (c && !window.confirm(c)) e.preventDefault();
  });

  /* the old-data import: asks the server for one small step at a time until it says it is done */
  var imp = document.getElementById('mzc-imp');
  if (imp) {
    var bar = imp.querySelector('.mzc-bar i'), txt = document.getElementById('mzc-imp-text'), go = document.getElementById('mzc-imp-go');
    var show = function (r) {
      bar.style.width = r.pct + '%';
      var c = r.counts || {}, parts = Object.keys(c).map(function (k) { return k + ': ' + c[k]; });
      txt.textContent = r.lines + ' رکورد — ' + (parts.join(' · ') || '…') + (r.skipped ? ' — ردشده (قبلاً بوده): ' + r.skipped : '') + (r.errors && r.errors.length ? ' — خطا: ' + r.errors.join(' | ') : '');
    };
    var step = function () {
      var fd = new FormData(); fd.append('action', 'mzc_import_step'); fd.append('n', imp.getAttribute('data-nonce'));
      fetch(imp.getAttribute('data-url'), { method: 'POST', body: fd, credentials: 'same-origin' }).then(function (r) { return r.json(); }).then(function (r) {
        if (r.error) { txt.textContent = r.error; if (go) go.disabled = false; return; }
        show(r);
        if (r.done) { txt.textContent += ' — تمام شد.'; if (go) go.remove(); } else { step(); }
      }).catch(function () { txt.textContent = 'ارتباط قطع شد؛ دوباره «شروع واردسازی» را بزنید (از همان‌جا ادامه می‌دهد).'; if (go) go.disabled = false; });
    };
    if (go) go.addEventListener('click', function () { go.disabled = true; step(); });
  }

  // a list's search box searches as one types (3+ characters, or emptied): the page is fetched again and everything around the box swapped, so the box keeps the focus
  var lt, lseq = 0;
  document.addEventListener('input', function (e) {
    var box = e.target, form = box.closest && box.closest('form.mzc-filter[method="get"]');
    if (!form || box.type !== 'search' || box.name !== 's') return;
    var v = box.value.trim(); clearTimeout(lt);
    if (v.length && v.length < 3) return;
    lt = setTimeout(function () {
      var p = new URLSearchParams();   // the list's own fields only: the column picker sits inside the same form and its nonce must not go into the address
      Array.prototype.forEach.call(form.elements, function (el) { if (el.name && !el.closest('.mzc-cols') && (el.type !== 'checkbox' || el.checked) && el.type !== 'submit') p.set(el.name, el.value); });
      p.set('s', v); p.delete('paged');
      var url = location.pathname + '?' + p.toString(), my = ++lseq, wrap = form.closest('.mzc');
      fetch(url, { credentials: 'same-origin' }).then(function (r) { return r.text(); }).then(function (html) {
        if (my !== lseq || !wrap) return;
        var nw = new DOMParser().parseFromString(html, 'text/html').querySelector('.mzc'); if (!nw) { location.href = url; return; }
        var host = form; while (host.parentNode !== wrap) host = host.parentNode;
        var i = Array.prototype.indexOf.call(wrap.children, host), kids = Array.prototype.slice.call(nw.children);
        Array.prototype.slice.call(wrap.children).forEach(function (c) { if (c !== host) c.remove(); });
        kids.slice(0, i).forEach(function (c) { wrap.insertBefore(c, host); }); kids.slice(i + 1).forEach(function (c) { wrap.appendChild(c); });
        history.replaceState(null, '', url);
      }).catch(function () { });
    }, 350);
  });

  /* an edit page holds its record (MZC_Rev): renewed every 45 s while open, let go when left; a page someone else holds cannot save */
  var lock = document.querySelector('.mzc-lock');
  if (document.querySelector('.mzc-locked')) document.querySelectorAll('.mzc-form button, .mzc-form input[type=submit]').forEach(function (b) { b.disabled = true; });
  if (lock) {
    var send = function (release) {
      var f = new FormData(); f.append('action', 'mzc_lock'); f.append('_ajax_nonce', lock.dataset.nonce); f.append('entity', lock.dataset.entity); f.append('id', lock.dataset.id); if (release) f.append('release', '1');
      if (release && navigator.sendBeacon) { navigator.sendBeacon(lock.dataset.ajax, f); return; }
      fetch(lock.dataset.ajax, { method: 'POST', body: f, credentials: 'same-origin' }).catch(function () { });
    };
    setInterval(function () { if (!document.hidden) send(false); }, 45000);
    var saving = false; document.querySelectorAll('.mzc-form').forEach(function (fm) { fm.addEventListener('submit', function () { saving = true; }); });
    window.addEventListener('pagehide', function () { if (!saving) send(true); });   // a save lets it go on the server's side once it is written
  }
})();
