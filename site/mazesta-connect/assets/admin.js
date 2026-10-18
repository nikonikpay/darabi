/* Mazesta Connect admin: part rows (add / remove), filtering a list of checkboxes, rows that open on click. No libraries. */
(function () {
  'use strict';
  document.addEventListener('click', function (e) {
    var add = e.target.closest('[data-mzc-add]');
    if (add) {
      e.preventDefault();
      var box = document.getElementById(add.getAttribute('data-mzc-add')), tpl = document.getElementById(add.getAttribute('data-mzc-add') + '-tpl');
      if (!box || !tpl) return;
      var n = parseInt(box.getAttribute('data-next') || '0', 10);
      var row = document.createElement('div'); row.className = 'mzc-row';
      row.innerHTML = tpl.innerHTML.replace(/__N__/g, String(n));
      box.appendChild(row); box.setAttribute('data-next', String(n + 1));
      var first = row.querySelector('input,select'); if (first) first.focus();
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
  /* a part row picked from the stock (<input list="mzc-products">): the product id, and the category and prices the row has not been given yet */
  document.addEventListener('input', function (e) {
    var inp = e.target, row = inp.closest && inp.closest('[data-mzc-part]');
    if (!row || inp.getAttribute('data-f') !== 'name') return;
    var opt = null, list = document.getElementById('mzc-products');
    if (list) { for (var i = 0; i < list.options.length; i++) { if (list.options[i].value === inp.value) { opt = list.options[i]; break; } } }
    var f = function (k) { return row.querySelector('[data-f="' + k + '"]'); }, set = function (k, v, always) { var el = f(k); if (el && v != null && v !== '' && (always || !el.value)) el.value = v; };
    if (f('pid')) f('pid').value = opt ? opt.getAttribute('data-id') : '';
    if (!opt) return;
    set('cat', opt.getAttribute('data-cat'), true); set('sell', opt.getAttribute('data-sell')); set('buy', opt.getAttribute('data-buy')); set('w', opt.getAttribute('data-w'));
  });
  document.addEventListener('change', function (e) {
    var t = e.target.closest('[data-mzc-target]');
    if (!t) return;
    var some = document.getElementById('mzc-some'); if (some) some.style.display = t.value === 'some' ? '' : 'none';
  });
  /* the form's customer: typing a name or a number lists the closest customers (any of their numbers); picking one fills customer_id, typing on clears it again */
  var cbox = document.querySelector('[data-mzc-cfind]');
  if (cbox) {
    var cid = cbox.querySelector('[name="customer_id"]'), res = cbox.querySelector('.mzc-cres'), picked = cbox.querySelector('.mzc-cpicked'), timer = 0, seq = 0;
    var inputs = cbox.querySelectorAll('input[type="text"]'), mob = cbox.querySelector('[name="c_mobile"]');
    var unpick = function () { cid.value = ''; picked.hidden = true; mob.required = true; inputs.forEach(function (i) { i.readOnly = false; }); };
    var pick = function (c) {
      cid.value = c.id; res.hidden = true; mob.required = false;
      cbox.querySelector('[name="c_name"]').value = c.name; mob.value = c.mobiles[0] || ''; cbox.querySelector('[name="c_mobile2"]').value = c.mobiles[1] || '';
      inputs.forEach(function (i) { i.readOnly = true; });
      picked.textContent = 'مشتری ثبت‌شده انتخاب شد: ' + c.name + ' '; var x = document.createElement('button'); x.type = 'button'; x.className = 'mzc-btn sm'; x.textContent = 'تغییر';
      x.addEventListener('click', function () { unpick(); inputs[0].focus(); }); picked.appendChild(x); picked.hidden = false;
    };
    cbox.addEventListener('input', function (e) {
      if (!e.target.hasAttribute('data-mzc-cq')) return;
      clearTimeout(timer); var q = e.target.value.trim();
      if (q.length < 2) { res.hidden = true; return; }
      timer = setTimeout(function () {
        var my = ++seq;
        fetch(cbox.getAttribute('data-mzc-cfind') + '&q=' + encodeURIComponent(q), { credentials: 'same-origin' }).then(function (r) { return r.json(); }).then(function (r) {
          if (my !== seq) return;
          res.textContent = '';
          (r.items || []).forEach(function (c) {
            var b = document.createElement('button'); b.type = 'button'; b.className = 'mzc-copt';
            var n = document.createElement('strong'); n.textContent = c.name; var m = document.createElement('span'); m.className = 'num muted'; m.textContent = ' ' + c.mobiles.concat(c.phone ? [c.phone] : []).join(' · ');
            b.appendChild(n); b.appendChild(m); b.addEventListener('click', function () { pick(c); }); res.appendChild(b);
          });
          if (!res.children.length) { var s = document.createElement('small'); s.className = 'muted'; s.textContent = 'مشتری با این مشخصات ثبت نشده؛ با ذخیرهٔ فرم مشتری تازه ساخته می‌شود.'; res.appendChild(s); }
          res.hidden = false;
        }).catch(function () { });
      }, 250);
    });
  }

  /* a customer's computer case is known by its service number; the model and serial fields are for a laptop (or a part) */
  var dtype = document.querySelector('select[name="device_type"]');
  var devShow = function () { var sys = dtype.value === 'system'; document.querySelectorAll('[data-mzc-dev]').forEach(function (l) { l.style.display = sys ? 'none' : ''; }); };
  if (dtype && document.querySelector('[data-mzc-dev]')) { dtype.addEventListener('change', devShow); devShow(); }

  /* the service's money, worked out as one types: parts, the customer's final sum (parts + services - discount), the services' profit (minus what we paid out) and the parts' profit */
  var money = document.querySelector('[data-mzc-money]');
  if (money) {
    var n = function (v) { v = String(v || '').replace(/[۰-۹]/g, function (d) { return '۰۱۲۳۴۵۶۷۸۹'.indexOf(d); }).replace(/[^\d]/g, ''); return v ? parseInt(v, 10) : 0; };
    var fmt = function (x) { return x.toLocaleString('fa-IR'); };
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
})();
