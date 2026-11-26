// This PC's copy of the site's records: brought up to date before the program opens, its age shown at the top, and a warning once it is more than an hour old.
const STALE_MINUTES = 60;
const LIST_PAGES = ['/', '/customers', '/builds', '/jobs', '/stock', '/purchases', '/invoices', '/payments', '/cheques', '/ships', '/shortages', '/tasks'];
const SYNC = { seq: 0, bar: null, chip: null };

const syncState = async () => { const r = await bridge.syncState(); return r.data || { supported: false }; };

/** Between signing in and the first page: waits for the copy. Opens anyway when the site cannot be reached and a whole copy is already here; never opens on half a first copy. */
async function syncGate() {
  for (;;) {
    const before = await syncState();
    if (!before.supported) return true;   // the site's plugin keeps no changes yet: everything is read from the site as before
    const note = h('p', { class: 'muted', role: 'status' }, 'در حال گرفتن از سایت…');
    document.body.classList.remove('in');
    clear($('#app')).append(h('div', { class: 'login' }, h('div', { class: 'mark' }, 'M'), h('h1', {}, before.ready ? 'به‌روزرسانی اطلاعات' : 'دریافت اطلاعات برای اولین بار'),
      h('p', { class: 'muted' }, before.ready ? 'تغییرهای تازه از سایت گرفته می‌شود.' : 'پرونده‌ها یک بار کامل گرفته و روی همین کامپیوتر نگه داشته می‌شوند؛ دفعه‌های بعد فقط تغییرها می‌آیند.'), h('progress', {}), note));
    const timer = setInterval(async () => { const s = await syncState(); if (s.received) note.textContent = s.received + ' پرونده دریافت شد…'; }, 400);
    // with a whole copy already here, a slow or hanging site is not waited for: the program opens and the changes come in behind it
    let s;
    try {
      const running = bridge.sync(), r = before.ready ? await Promise.race([running, new Promise(done => setTimeout(done, 6000, null))]) : await running;
      s = r ? r.data || before : before;
      if (!r) toast('پاسخ سایت دیر شد؛ برنامه باز شد و همگام‌سازی در پس‌زمینه ادامه دارد.');
    } finally { clearInterval(timer); }
    SYNC.seq = s.seq;
    if (!s.supported || s.ready) { if (s.ready && s.error) toast('به سایت وصل نشدیم؛ اطلاعات تا ' + s.lastSync + ' روی این کامپیوتر است.', 'err'); return true; }
    if (!s.known) return true;   // the site never answered with a copy on this PC: the program works from the site, as before
    const again = await new Promise(done => {
      clear($('#app')).append(h('div', { class: 'login' }, h('div', { class: 'mark' }, 'M'), h('h1', {}, 'اطلاعات کامل نرسید'), h('p', { class: 'err', role: 'alert' }, s.error || 'دریافت اطلاعات قطع شد.'),
        h('p', { class: 'muted' }, 'اولین دریافت باید کامل شود تا فهرست‌ها درست باشند. اتصال اینترنت را بررسی کنید؛ از همان‌جا که مانده ادامه پیدا می‌کند.'),
        h('button', { class: 'btn primary big', onclick: () => done(true) }, 'دوباره'), h('button', { class: 'link', onclick: () => done(false) }, 'خروج')));
    });
    if (!again) { await bridge.logout(); S.me = null; showLogin(); return false; }
  }
}

/** The button at the top: how fresh the copy is; a press asks the site now. */
function syncChip() {
  const b = SYNC.chip = h('button', { class: 'syncchip', type: 'button', hidden: true, title: 'همگام‌سازی با سایت', onclick: async () => {
    b.disabled = true; b.textContent = 'در حال همگام‌سازی…';
    try { const s = (await bridge.sync()).data; if (s && s.error) toast(s.error, 'err'); } finally { b.disabled = false; await syncWatch(); }
  } });
  setTimeout(syncWatch, 0);
  return b;
}

async function syncWatch() {
  if (!S.me || !window.bridge) return;
  const s = await syncState();
  const on = s.supported && s.ready, stale = on && s.ageMinutes >= STALE_MINUTES;
  if (SYNC.chip) {
    SYNC.chip.hidden = !on;
    SYNC.chip.className = 'syncchip' + (stale || s.error ? ' old' : '');
    const when = s.ageMinutes <= 1 ? 'همین حالا' : s.ageMinutes < 90 ? s.ageMinutes + ' دقیقه پیش' : s.lastSync;
    SYNC.chip.textContent = s.syncing ? 'در حال همگام‌سازی…' : s.error ? 'بدون اتصال · آخرین بار ' + when : s.ageMinutes <= 1 ? '✓ به‌روز' : 'به‌روزرسانی ' + when;
  }
  if (SYNC.bar) { SYNC.bar.remove(); SYNC.bar = null; }
  if (stale) {
    SYNC.bar = h('div', { class: 'offbar bad', role: 'alert' }, h('span', {}, 'اطلاعات این کامپیوتر ' + (s.ageMinutes < 180 ? s.ageMinutes + ' دقیقه' : Math.floor(s.ageMinutes / 60) + ' ساعت') + ' است که با سایت همگام نشده (آخرین بار ' + s.lastSync + '). اتصال اینترنت را بررسی کنید.'),
      h('button', { class: 'btn', onclick: () => SYNC.chip && SYNC.chip.click() }, 'همگام‌سازی'));
    document.body.prepend(SYNC.bar);
  }
  // others changed something: a list (not a form being typed into) is drawn again
  if (on && s.seq !== SYNC.seq) {
    SYNC.seq = s.seq;
    if (typeof onSynced === 'function') onSynced();
    const busy = $('.backdrop') || (document.activeElement && /^(TEXTAREA|SELECT)$/.test(document.activeElement.tagName));
    if (!busy && LIST_PAGES.includes(parseHash().path)) { S.soft = true; route(); }
  }
}
