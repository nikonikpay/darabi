// The way to the C# side. In the app (WebView2) every call is a message and the token stays in C#. Opened in an ordinary browser with ?dev=<site> (testing only) the same calls
// go straight to that site with fetch, the token kept in sessionStorage.
(function () {
  const wv = window.chrome && window.chrome.webview;
  const dev = new URLSearchParams(location.search).get('dev');
  let n = 0; const waiting = new Map();
  if (wv) wv.addEventListener('message', e => { const m = JSON.parse(e.data); const w = waiting.get(m.id); if (w) { waiting.delete(m.id); w(m); } });
  const send = msg => new Promise(res => { const id = ++n; waiting.set(id, res); wv.postMessage(JSON.stringify(Object.assign({ id }, msg))); });

  async function devCall(method, path, body) {
    const t = sessionStorage.getItem('mzt') || '';
    try {
      const r = await fetch(dev.replace(/\/$/, '') + '/wp-json/mazesta/v1/' + path, { method, headers: Object.assign({ 'X-MZC-Token': t }, method === 'POST' ? { 'Content-Type': 'application/json' } : {}), body: method === 'POST' ? (body || '{}') : undefined });
      const data = await r.json().catch(() => null);
      if (r.status === 401 && path !== 'staff/login') sessionStorage.removeItem('mzt');
      return { status: r.status, data, error: data ? null : 'پاسخ نامعتبر از سایت (' + r.status + ')' };
    } catch (e) { return { status: 0, data: null, error: 'اتصال به سایت برقرار نشد: ' + e.message }; }
  }

  window.bridge = {
    dev: !wv && !!dev,
    api: (method, path, body) => wv ? send({ op: 'api', method, path, body: body == null ? '' : JSON.stringify(body) }) : devCall(method, path, body == null ? '' : JSON.stringify(body)),
    login: async (username, password) => {
      if (wv) return send({ op: 'login', username, password });
      const r = await devCall('POST', 'staff/login', JSON.stringify({ username, password, device: 'browser' }));
      if (r.status === 200 && r.data && r.data.token) { sessionStorage.setItem('mzt', r.data.token); delete r.data.token; }
      return r;
    },
    logout: async () => { if (wv) return send({ op: 'logout' }); await devCall('POST', 'staff/logout', '{}'); sessionStorage.removeItem('mzt'); return { status: 200 }; },
    session: async () => wv ? send({ op: 'session' }) : { status: 200, data: { server: dev || '', signedIn: !!sessionStorage.getItem('mzt'), machine: 'browser' } },
    setServer: async server => wv ? send({ op: 'server', server }) : { status: 200, data: { server: dev || server } },
    print: async () => wv ? send({ op: 'print' }) : (window.print(), { status: 200 }),
  };
})();
