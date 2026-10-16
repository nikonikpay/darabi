// Unattended run (MainForm.RunSelfTestAsync, MazestaDesk.exe --selftest with MAZESTA_DESK_SELFTEST="server|user|password"): drives the real screens through the real
// bridge against a real site and returns a text report. Creates one customer, one service and one new computer on that site - use a test site.
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
    const cards = await wait(() => document.querySelectorAll('.card b').length && [...document.querySelectorAll('.card b')].every(b => b.textContent !== '…') && document.querySelectorAll('.card b'));
    ok('home counts loaded from the site', !!cards && [...cards].some(b => /^\d+$/.test(b.textContent)), cards ? [...cards].map(b => b.textContent).join(',') : '');
    // a customer
    const mobile = '0912' + String(Math.floor(1000000 + Math.random() * 8999999));
    location.hash = '#/customer/new'; const cf = await wait(() => document.querySelector('form.panel input[name=name]') && document.querySelector('form.panel'));
    set(cf.querySelector('input[name=name]'), 'مشتری خودآزمون'); set(cf.querySelector('input[name=mobile]'), '۰۹' + mobile.slice(2).replace(/\d/g, d => '۰۱۲۳۴۵۶۷۸۹'[d])); submit(cf);
    const cid = await wait(() => (location.hash.match(/#\/customer\/(\d+)/) || [])[1]); ok('customer saved (Persian digits accepted)', !!cid, 'id=' + cid);
    // a service for them
    location.hash = '#/job/new?cid=' + cid; const jf = await wait(() => document.querySelector('form.panel input[name=device]') && document.querySelector('form.panel'));
    set(jf.querySelector('input[name=device]'), 'لپ‌تاپ آزمون'); set(jf.querySelector('textarea[name=complaint]'), 'خودآزمون: صفحه‌نمایش نمی‌آید'); submit(jf);
    const jid = await wait(() => (location.hash.match(/#\/job\/(\d+)/) || [])[1]); ok('service saved', !!jid, 'id=' + jid);
    const offer = await wait(() => [...document.querySelectorAll('.modal h3')].find(x => x.textContent === 'پذیرش ثبت شد')); ok('print offer after a new service', !!offer);
    document.querySelectorAll('.backdrop').forEach(b => b.remove());
    await wait(() => document.querySelector('h2') && /R-\d+/.test(document.querySelector('h2').textContent)); const no = (document.querySelector('h2').textContent.match(/R-\d+/) || [])[0];
    ok('service number shown', !!no, no);
    // label
    await printLabel('job', +jid); const lab = await wait(() => document.querySelector('.modal .label'));
    ok('label preview has the number and a QR', !!lab && lab.textContent.includes(no) && !!lab.querySelector('svg'));
    ok('print copy is in the page for the print stylesheet', !!document.querySelector('#printArea .label'));
    document.querySelectorAll('.backdrop').forEach(b => b.remove());
    // find by service number (what a scanner types) opens the record
    location.hash = '#/'; await wait(() => document.querySelector('.cards'));   // away from the record first: the scan must be what opens it
    set(document.getElementById('gsearch'), no); document.getElementById('gsearch').dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    ok('typing/scanning the number opens the service', !!(await wait(() => location.hash === '#/job/' + jid)));
    // a new computer with a part
    location.hash = '#/build/new?cid=' + cid; const bf = await wait(() => document.querySelector('form.panel input[name=title]') && document.querySelector('form.panel'));
    if (!bf) throw new Error('build page did not appear: ' + document.querySelector('#main').innerText.slice(0, 300) + ' @' + location.hash);
    set(bf.querySelector('input[name=title]'), 'سیستم خودآزمون'); [...bf.querySelectorAll('button')].find(b => b.textContent.includes('ردیف')).click();
    set(bf.querySelector('.rows input[name=model]'), 'قطعهٔ آزمون'); set(bf.querySelector('.rows input[name=serial]'), 'ST-' + Date.now()); set(bf.querySelector('.rows input[name=warranty_months]'), '۱۲'); submit(bf);
    const bid = await wait(() => (location.hash.match(/#\/build\/(\d+)/) || [])[1]); ok('new computer saved with a part', !!bid, 'id=' + bid);
    const got = await GET('staff/builds/' + bid); ok('the part came back with its warranty', got.parts.length === 1 && got.parts[0].warranty_months === 12 && /^R-\d+$/.test(got.service_no), JSON.stringify(got.parts));
    // permissions seen by this person
    ok('screens follow the permissions (nav items = areas with at least view)', document.querySelectorAll('aside nav a').length === NAV.filter(n => n[2]()).length);
    await bridge.logout(); const s = await bridge.session(); ok('sign-out clears the token', s.data && s.data.signedIn === false);
    r = await bridge.api('GET', 'staff/me'); ok('after sign-out the site refuses', r.status === 401);
  } catch (e) { bad++; log.push('FAIL exception ' + e.message + '\n' + (e.stack || '')); }
  return (bad ? bad + ' FAILED\n' : 'ALL PASSED\n') + log.join('\n');
};

// ExecuteScriptAsync does not wait for a promise: the report is posted back when the run ends.
window.deskSelfTestRun = (server, user, pass) => { window.deskSelfTest(server, user, pass).then(r => window.chrome.webview.postMessage(JSON.stringify({ op: 'selftest-done', result: r }))); return 'started'; };
