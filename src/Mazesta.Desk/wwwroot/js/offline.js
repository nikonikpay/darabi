// The offline bar: while payments or draft invoices wait for the site (no line), a strip says how many and sends them when the site answers; a call the site refused stays listed until dropped.
(function () {
  let bar = null, busy = false;
  async function tick() {
    if (busy || !window.S || !S.me || !window.bridge) return;
    busy = true;
    try {
      let q = await bridge.queue();
      if (q.status === 200 && q.data && q.data.pending > 0) { const f = await bridge.flush(); if (f.status === 200 && f.data && f.data.sent > 0) { toast(f.data.sent + ' مورد از صف آفلاین به سایت فرستاده شد.'); if (typeof route === 'function') route(); } q = await bridge.queue(); }
      // a change made offline that met someone else's change: both are in the record's history; say so (the texts come once)
      if (q.status === 200 && q.data && q.data.conflicts) q.data.conflicts.forEach(t => toast(t, 'err'));
      show(q.status === 200 ? q.data : null);
    } catch (e) { /* the next tick tries again */ } finally { busy = false; }
  }
  function show(d) {
    if (bar) { bar.remove(); bar = null; }
    if (!d || (!d.pending && !(d.refused && d.refused.length))) return;
    bar = h('div', { class: 'offbar' },
      d.pending ? h('span', {}, d.pending + ' مورد در صف فرستادن به سایت است (اتصال برقرار نیست).') : null,
      (d.refused || []).map(x => h('span', { class: 'bad' }, 'سایت نپذیرفت: ' + x.error + ' (' + x.at + ') ', h('button', { class: 'btn', onclick: async () => { await bridge.discard(x.id); tick(); } }, 'حذف از صف'))));
    document.body.prepend(bar);
  }
  setInterval(tick, 20000); setTimeout(tick, 3000);
})();
