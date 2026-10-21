// Unattended run (MainForm.RunSelfTestAsync, MazestaDesk.exe --selftest with MAZESTA_DESK_SELFTEST="server|user|password"): drives the real screens through the real
// bridge against a real site and returns a text report. Creates one customer, one service and one new computer on that site (with stock rights also a purchase invoice
// and a product) - use a test site.
window.deskSelfTest = async (server, user, pass) => {
  const log = []; let bad = 0;
  const ok = (name, cond, extra) => { if (!cond) bad++; log.push((cond ? 'PASS ' : 'FAIL ') + name + (extra ? '  ' + extra : '')); };
  const wait = async (cond, ms) => { const t = Date.now(); while (Date.now() - t < (ms || 8000)) { try { const v = cond(); if (v) return v; } catch (e) { } await new Promise(r => setTimeout(r, 80)); } return null; };
  const set = (el, v) => { el.value = v; el.dispatchEvent(new Event('input', { bubbles: true })); };
  const submit = form => form.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
  try {
    let r = await bridge.setServer(server); ok('server accepted', r.status === 200, JSON.stringify(r.data || r.error));
    r = await bridge.setServer('http://evil.example'); ok('plain-http foreign server refused', r.status === 400);
    await bridge.setServer(server);
    r = await bridge.api('GET', 'staff/config'); ok('call without sign-in refused (401)', r.status === 401);
    r = await bridge.api('GET', '../wp/v2/users'); ok('path outside /staff/ refused by the C# side', r.status === 0 && /مجاز/.test(r.error || ''), r.error);
    r = await bridge.api('POST', 'staff/jobs?x=1;DROP', {}); ok('odd characters in a path refused', r.status === 0);
    r = await bridge.login(user, pass + 'x'); ok('wrong password refused', r.status === 401, r.error || (r.data && r.data.error));
    // the real login screen
    showLogin(); const form = await wait(() => document.querySelector('form.login')); ok('login screen shown', !!form);
    set(form.querySelector('input[name=username]'), user); set(form.querySelector('input[name=password]'), pass); submit(form);
    ok('sign-in through the screen opens the shell', !!(await wait(() => document.querySelector('aside nav') && S.me)), S.me ? S.me.name : '');
    ok('token never reaches the page', !JSON.stringify(S.me).includes('token'));
    const cards = await wait(() => document.querySelectorAll('.card b').length && [...document.querySelectorAll('.card b')].every(b => b.textContent !== '…') && document.querySelectorAll('.card b'), 20000);
    ok('home counts loaded from the site', !!cards && [...cards].some(b => /^\d+$/.test(b.textContent)), cards ? [...cards].map(b => b.textContent).join(',') : '');
    const jc = document.querySelector('.card[href^="#/jobs?status="]');   // one request for all cards: its number must match the list's own total
    if (jc) { const k = jc.getAttribute('href').split('=')[1], l = await bridge.api('GET', 'staff/jobs?status=' + k + '&per=1'); ok('a home count matches its list', l.status === 200 && String(l.data.total) === jc.querySelector('b').textContent, jc.querySelector('b').textContent + ' / ' + (l.data && l.data.total)); }
    // a customer
    const mobile = '0912' + String(Math.floor(1000000 + Math.random() * 8999999));
    location.hash = '#/customer/new'; const cf = await wait(() => document.querySelector('form.panel input[name=name]') && document.querySelector('form.panel'));
    set(cf.querySelector('input[name=name]'), 'مشتری خودآزمون'); set(cf.querySelector('.rows input[name=number]'), '۰۹' + mobile.slice(2).replace(/\d/g, d => '۰۱۲۳۴۵۶۷۸۹'[d]));
    const extra = ['0935' + mobile.slice(4), '0990' + mobile.slice(4)]; extra.forEach(x => { [...cf.querySelectorAll('button')].find(b => b.textContent.includes('ردیف')).click(); set([...cf.querySelectorAll('.rows input[name=number]')].pop(), x); }); submit(cf);
    const cid = await wait(() => (location.hash.match(/#\/customer\/(\d+)/) || [])[1]); ok('customer saved (Persian digits accepted)', !!cid, 'id=' + cid);
    const gc = cid ? await GET('staff/customers/' + cid) : null; ok('three mobile numbers kept, the first as the main one', gc && JSON.stringify(gc.customer.mobiles) === JSON.stringify([mobile].concat(extra)) && gc.customer.mobile === mobile, gc && JSON.stringify(gc.customer.mobiles));
    const fc = await GET('staff/customers' + qs({ q: extra[1].slice(-7) })); ok('found by the third number', fc.items.some(x => String(x.id) === cid));
    // a service for them: the customer found by typing part of their number, a laptop with its serial, the money, a program to install
    location.hash = '#/job/new'; const jf = await wait(() => document.querySelector('form.panel input[name=c_name]') && document.querySelector('form.panel'));
    set(jf.querySelector('input[name=c_mobile]'), mobile.slice(-6));
    const opt = await wait(() => [...jf.querySelectorAll('.cpick .opt')].find(o => o.textContent.includes(mobile))); ok('typing part of the mobile suggests the customer', !!opt);
    if (opt) opt.click(); ok('customer chosen from the suggestions', !!(await wait(() => jf.querySelector('.cpick .chosen'))));
    const dt = jf.querySelector('select[name=device_type]'), devBox = jf.querySelector('input[name=device_serial]').closest('.two');
    ok('model and serial hidden for a computer case', devBox.style.display === 'none'); dt.value = 'laptop'; dt.dispatchEvent(new Event('change')); ok('model and serial shown for a laptop', devBox.style.display === '');
    set(jf.querySelector('input[name=device]'), 'لپ‌تاپ آزمون'); set(jf.querySelector('input[name=device_serial]'), 'SN-' + mobile); set(jf.querySelector('textarea[name=complaint]'), 'خودآزمون: صفحه‌نمایش نمی‌آید');
    if (can('payments', 2)) {
      set(jf.querySelector('input[name=labor]'), '1000000'); set(jf.querySelector('input[name=paid]'), '300000'); set(jf.querySelector('input[name=discount]'), '100000'); set(jf.querySelector('input[name=paid_note]'), 'پیک');
      const tot = jf.querySelector('.sum.big b'), sp = [...jf.querySelectorAll('.sum')].find(x => x.textContent.startsWith('سود خدمات'));
      ok('customer pays services - discount (paid-out not on the bill); services profit = services - discount - paid-out', tot && tot.textContent === money(900000) && sp && sp.querySelector('b').textContent === money(600000), (tot && tot.textContent) + ' / ' + (sp && sp.textContent));
      const payBox = [...jf.querySelectorAll('.wide')].find(d => d.querySelector('datalist#acclist')); [...payBox.querySelectorAll('button')].find(b => b.textContent.includes('ردیف')).click();
      ok('the payment account suggests the site\'s accounts and takes any text', payBox.querySelector('input[name=account]').getAttribute('list') === 'acclist' && payBox.querySelectorAll('datalist option').length === (S.cfg.accounts || []).length, (S.cfg.accounts || []).join(' / '));
    }
    const softBox = [...jf.querySelectorAll('.wide')].find(d => d.querySelector('datalist#softlist')); [...softBox.querySelectorAll('button')].find(b => b.textContent.includes('ردیف')).click();
    set(softBox.querySelector('input[name=name]'), 'Blender'); ok('the software box suggests from the shop list', softBox.querySelector('input[name=name]').getAttribute('list') === 'softlist' && softBox.querySelectorAll('datalist option').length > 10);
    submit(jf);
    const jid = await wait(() => (location.hash.match(/#\/job\/(\d+)/) || [])[1]); ok('service saved', !!jid, 'id=' + jid);
    const offer = await wait(() => [...document.querySelectorAll('.modal h3')].find(x => x.textContent === 'پذیرش ثبت شد')); ok('print offer after a new service', !!offer);
    document.querySelectorAll('.backdrop').forEach(b => b.remove());
    await wait(() => document.querySelector('h2') && /R-\d+/.test(document.querySelector('h2').textContent)); const no = (document.querySelector('h2').textContent.match(/R-\d+/) || [])[0];
    ok('service number shown', !!no, no);
    const gj = await GET('staff/jobs/' + jid);
    ok('service saved with laptop serial, software and money', gj.device_serial === 'SN-' + mobile && gj.software.length === 1 && gj.software[0].name === 'Blender' && (!can('payments') || (gj.paid === 300000 && gj.paid_note === 'پیک' && gj.total === 900000)), JSON.stringify({ s: gj.device_serial, sw: gj.software, t: gj.total }));
    // label
    await printLabel('job', +jid); const lab = await wait(() => document.querySelector('.modal .label'));
    ok('label preview has the number and a QR', !!lab && lab.textContent.includes(no) && !!lab.querySelector('svg'));
    ok('print copy is in the page for the print stylesheet', !!document.querySelector('#printArea .label'));
    document.querySelectorAll('.backdrop').forEach(b => b.remove());
    // find by service number (what a scanner types) opens the record
    location.hash = '#/'; await wait(() => document.querySelector('.cards') && ![...document.querySelectorAll('.card b')].some(b => b.textContent === '…'), 20000);   // away from the record first: the scan must be what opens it
    set(document.getElementById('gsearch'), no); document.getElementById('gsearch').dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    ok('typing/scanning the number opens the service', !!(await wait(() => location.hash === '#/job/' + jid, 20000)));
    // with stock rights: a purchase invoice entered on its screen brings a new product into the stock
    const stamp = String(Date.now()), pname = 'کالای خودآزمون ' + stamp; let pid = 0;
    if (can('stock', 2)) {
      location.hash = '#/purchase/new'; const pf = await wait(() => document.querySelector('form.panel input[name=vendor_name]') && document.querySelector('form.panel'));
      set(pf.querySelector('input[name=vendor_name]'), 'فروشندهٔ خودآزمون'); set(pf.querySelector('input[name=invoice_no]'), 'P-' + stamp);
      [...pf.querySelectorAll('button')].find(b => b.textContent.includes('ردیف')).click();
      set(pf.querySelector('.rows input[name=name]'), pname); pf.querySelector('.rows select[name=category]').value = 'gpu';
      set(pf.querySelector('.rows input[name=qty]'), '۲'); set(pf.querySelector('.rows input[name=unit_cost]'), '1500000'); set(pf.querySelector('.rows input[name=serials]'), 'SA-' + stamp + ', SB-' + stamp); submit(pf);
      const uid = await wait(() => (location.hash.match(/#\/purchase\/(\d+)/) || [])[1]); ok('purchase invoice saved on its screen', !!uid, 'id=' + uid);
      const pr = await GET('staff/products' + qs({ q: pname })); pid = pr.items.length ? pr.items[0].id : 0;
      ok('its product is in the stock with 2', pr.items.length === 1 && pr.items[0].stock === 2, JSON.stringify(pr.items));
    }
    // a new computer (known by its invoice number) with a part - picked from the stock when there is one
    location.hash = '#/build/new?cid=' + cid; const bf = await wait(() => document.querySelector('form.panel datalist#softlist') && !document.querySelector('form.panel input[name=device]') && document.querySelector('form.panel'));   // the system form: software box, no device box
    if (!bf) throw new Error('build page did not appear: ' + document.querySelector('#main').innerText.slice(0, 300) + ' @' + location.hash);
    ok('the system form has no title, device type or Telegram field', !bf.querySelector('input[name=title], select[name=device_type], textarea[name=media]'));
    set(bf.querySelector('input[name=invoice_no]'), '۹' + stamp.slice(-6).replace(/\d/g, d => '۰۱۲۳۴۵۶۷۸۹'[d]));
    [...bf.querySelectorAll('button')].find(b => b.textContent.includes('ردیف')).click();
    const row = bf.querySelector('.rows tbody tr'); row.querySelector('select[name=category]').value = 'gpu';
    set(row.querySelector('input[name=model]'), pid ? pname : 'قطعهٔ آزمون');
    if (pid) ok('typing the product name picks it from the stock', !!(await wait(() => row.dataset.pid === String(pid))), 'pid=' + row.dataset.pid);
    set(row.querySelector('input[name=serial]'), 'SA-' + stamp); set(row.querySelector('input[name=warranty_months]'), '۱۲'); submit(bf);
    const bid = await wait(() => (location.hash.match(/#\/build\/(\d+)/) || [])[1]); ok('new computer saved with a part', !!bid, 'id=' + bid);
    const got = await GET('staff/builds/' + bid);
    ok('saved by its invoice number (no service number), named after its graphics card', got.invoice_no === '9' + stamp.slice(-6) && got.service_no === '' && got.number === got.invoice_no && got.title === 'سیستم ' + got.parts[0].model, JSON.stringify({ n: got.number, s: got.service_no, t: got.title }));
    ok('the part came back with its warranty', got.parts.length === 1 && got.parts[0].warranty_months === 12 && (!pid || got.parts[0].product_id === pid), JSON.stringify(got.parts));
    if (pid) { const pr = await GET('staff/products/' + pid); ok('the stock went down and the product knows where it went', pr.totals.stock === 1 && pr.used.length === 1 && pr.used[0].id === +bid, JSON.stringify(pr.totals)); }
    // accounting, by the keys: F2 a new sale, the customer, F3-style product search + Enter, F8 save, F9 final, F6 the payment, F10 print, F1 the key list
    if (can('accounting', 2) && pid) {
      const key = k => document.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true, cancelable: true }));
      location.hash = '#/'; await wait(() => document.querySelector('.cards') && ![...document.querySelectorAll('.card b')].some(b => b.textContent === '…'), 20000);   // the home page's counts first: a one-thread test server answers in turn
      key('F2'); const vf = await wait(() => location.hash === '#/invoice/new' && document.querySelector('#main input[type=search]')); ok('F2 opens a new sales invoice', !!vf, location.hash);
      set(document.querySelector('#main input[name=c_mobile]'), mobile.slice(-7));
      const co = await wait(() => [...document.querySelectorAll('#main .cpick .opt')].find(o => o.textContent.includes(mobile)), 15000); if (co) co.click(); ok('invoice customer picked', !!co);
      set(vf, pname.slice(0, -3)); await wait(() => document.querySelector('#main .results .opt'), 15000);
      vf.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
      const line = await wait(() => document.querySelector(`#main .rows tr[data-pid="${pid}"]`)); ok('Enter in the product search adds the line', !!line, line && line.dataset.pid);
      set(line.querySelector('[name=unit_price]'), '2000000'); key('F8');
      const ivid = await wait(() => (location.hash.match(/#\/invoice\/(\d+)/) || [])[1]); ok('F8 saves the draft', !!ivid, location.hash);
      await wait(() => document.querySelector('#main .rows tr[data-pid]')); key('F9');
      const fin = await wait(() => [...document.querySelectorAll('.modal button')].find(b => b.textContent === 'قطعی شود')); if (fin) fin.click();
      let gi = null;
      for (let i = 0; i < 40 && !(gi && gi.status === 'final'); i++) { await new Promise(r => setTimeout(r, 150)); gi = await GET('staff/invoices/' + ivid); }
      ok('F9 makes it final with a number', gi && gi.status === 'final' && gi.no > 0, gi && gi.status + ' ' + gi.no);
      const st = await GET('staff/products/' + pid); ok('the sale took the part out of the stock', st.totals.stock === 0, st.totals.stock);
      await wait(() => document.querySelector('#main h2') && document.querySelector('#main h2').textContent.includes(String(gi.no)));
      key('F6'); const pm = await wait(() => document.querySelector('.modal input[name=amount]')); ok('F6 opens the payment of this invoice with what is left', !!pm && digits(pm.value) === '2000000', pm && pm.value);
      if (pm) { [...document.querySelectorAll('.modal button')].find(b => b.textContent === 'ثبت').click(); }
      let gp = null; for (let i = 0; i < 40 && !(gp && gp.remaining === 0); i++) { await new Promise(r => setTimeout(r, 150)); gp = await GET('staff/invoices/' + ivid); }
      ok('payment recorded: nothing left', gp && gp.paid === 2000000 && gp.remaining === 0, gp && gp.remaining);
      document.querySelectorAll('.backdrop').forEach(b => b.remove());
      await wait(() => document.querySelector('#main h2')); key('F10'); const pv = await wait(() => document.querySelector('.modal .invoice')); ok('F10 prints the invoice (number, line, total)', !!pv && pv.textContent.includes(String(gi.no)) && pv.textContent.includes(money(2000000)));
      document.querySelectorAll('.backdrop').forEach(b => b.remove());
      key('F1'); const hp = await wait(() => [...document.querySelectorAll('.modal h3')].find(x => x.textContent === 'کلیدهای میانبر')); ok('F1 lists the keys', !!hp && document.querySelectorAll('.modal tbody tr').length === 13);
      document.querySelectorAll('.backdrop').forEach(b => b.remove());
      location.hash = '#/customer/' + cid; const acc = await wait(() => [...document.querySelectorAll('#main h3')].find(x => x.textContent === 'حساب مشتری'));
      ok('the customer page shows the account with the invoice and the payment', !!acc && document.querySelector('#main').textContent.includes('فاکتور فروش ' + gi.no), acc ? acc.closest('section').textContent.slice(0, 120) : '');
    }
    // the period report
    location.hash = '#/stats'; const rep = await wait(() => [...document.querySelectorAll('#main h3')].find(x => x.textContent.startsWith('سرویس‌ها')) && document.querySelector('#main .cards'));
    ok('report page shows the cards of the period', !!rep && /\d/.test(rep.textContent), rep ? rep.textContent.slice(0, 80) : '');
    ok('report page says the amounts are in toman', [...document.querySelectorAll('#main .title .pill')].some(x => x.textContent.includes('تومان')));
    // live search: the top box lists the nearest records from 3 characters, not before
    const gs = $('#gsearch'), gres = document.querySelector('.gfind .results');
    set(gs, mobile.slice(-2)); await new Promise(r => setTimeout(r, 700)); ok('top search waits for 3 characters', gres.hidden);
    set(gs, mobile.slice(-7)); const hit = await wait(() => !gres.hidden && [...gres.querySelectorAll('.opt')].find(o => o.textContent.includes(mobile)));
    ok('top search lists the customer while typing', !!hit, gres.textContent.slice(0, 80));
    if (hit) { hit.click(); ok('picking a result opens it', !!(await wait(() => location.hash.startsWith('#/customer/')))); }
    location.hash = '#/customers'; const fb = await wait(() => document.querySelector('#main .filters input[type=search]'));
    fb.focus(); set(fb, mobile.slice(-7)); const one = await wait(() => location.hash.includes('q=') && document.querySelectorAll('#main tbody tr.click').length === 1 && document.activeElement && document.activeElement.matches('#main .filters input[type=search]'));
    ok('list filter follows the box as one types and keeps the focus', !!one, location.hash);
    // permissions seen by this person
    ok('screens follow the permissions (nav items = areas with at least view)', document.querySelectorAll('aside nav a').length === NAV.filter(n => n[2]()).length);
    await bridge.logout(); const s = await bridge.session(); ok('sign-out clears the token', s.data && s.data.signedIn === false);
    r = await bridge.api('GET', 'staff/me'); ok('after sign-out the site refuses', r.status === 401);
  } catch (e) { bad++; log.push('FAIL exception ' + e.message + '\n' + (e.stack || '')); }
  return (bad ? bad + ' FAILED\n' : 'ALL PASSED\n') + log.join('\n');
};

// ExecuteScriptAsync does not wait for a promise: the report is posted back when the run ends.
window.deskSelfTestRun = (server, user, pass) => { window.deskSelfTest(server, user, pass).then(r => window.chrome.webview.postMessage(JSON.stringify({ op: 'selftest-done', result: r }))); return 'started'; };
