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
  document.addEventListener('change', function (e) {
    var t = e.target.closest('[data-mzc-target]');
    if (!t) return;
    var some = document.getElementById('mzc-some'); if (some) some.style.display = t.value === 'some' ? '' : 'none';
  });
  document.addEventListener('submit', function (e) {
    var c = e.target.getAttribute && e.target.getAttribute('data-mzc-confirm');
    if (c && !window.confirm(c)) e.preventDefault();
  });
})();
