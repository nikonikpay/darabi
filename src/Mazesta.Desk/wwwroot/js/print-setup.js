// What every printed page of this PC carries: the shop's header (name, numbers, logo, where to pay) and the ready texts, each ticked by itself on the pages it was made for.

const SHOP_FIELDS = [['name', 'نام فروشگاه'], ['slogan', 'زیر نام (فعالیت یا شعار)'], ['phone', 'تلفن'], ['web', 'وب‌سایت'], ['address', 'نشانی', 'wide'], ['postal', 'کد پستی'], ['nid', 'شناسه / کد ملی'], ['eco', 'کد اقتصادی'], ['reg', 'شمارهٔ ثبت'],
  ['pay', 'اطلاعات واریز (کارت یا شبا و به نام چه کسی)', 'wide'], ['foot', 'متن پایین فاکتور', 'wide']];

/** A picture made small enough to keep on this PC and to print sharply (at most 600 dots on its long side). */
function smallImage(file) {
  return new Promise((res, rej) => {
    const r = new FileReader(); r.onerror = () => rej(new Error('فایل خوانده نشد.'));
    r.onload = () => {
      const img = new Image(); img.onerror = () => rej(new Error('این فایل تصویر نیست.'));
      img.onload = () => {
        const k = Math.min(1, 600 / Math.max(img.width, img.height)), c = document.createElement('canvas');
        c.width = Math.max(1, Math.round(img.width * k)); c.height = Math.max(1, Math.round(img.height * k)); c.getContext('2d').drawImage(img, 0, 0, c.width, c.height);
        res(c.toDataURL('image/png'));
      };
      img.src = r.result;
    };
    r.readAsDataURL(file);
  });
}

function printSetup(done) {
  const cfg = printCfg(), site = Object.assign({}, cfg, { shop: {} });
  // an empty box prints what the site's settings page says (shown greyed in it)
  const a = (S.cfg && S.cfg.acc) || {}, p = prefs.get(), fallback = { name: a.shop || p.shop, phone: a.phone || p.phone, address: a.address || '', foot: a.foot || '' };
  modal('سرفاکتور و توضیحات آمادهٔ چاپ', (b, close) => {
    const inputs = Object.fromEntries(SHOP_FIELDS.map(([k]) => [k, text(k, cfg.shop[k] || '', { placeholder: fallback[k] || '' })]));
    let logo = cfg.shop.logo || '';
    const shown = h('div', { class: 'logobox' }), drawLogo = () => { clear(shown).append(logo ? h('img', { src: logo, alt: 'لوگو' }) : h('small', { class: 'muted' }, 'لوگویی انتخاب نشده.'), logo ? h('button', { type: 'button', class: 'btn sm', onclick: () => { logo = ''; drawLogo(); } }, 'برداشتن') : ''); };
    const file = h('input', { type: 'file', name: 'logo', accept: 'image/*', onchange: async e => { const f = e.target.files[0]; if (!f) return; try { logo = await smallImage(f); drawLogo(); } catch (err) { fail(err); } } });
    const list = h('div', { class: 'notelist' });
    const addNote = n => {
      const title = text('title', n.title, { placeholder: 'نام این توضیح (مثلاً: شرایط گارانتی)' }), body = area('text', n.text, { rows: 2, placeholder: 'متنی که روی برگه چاپ می‌شود' });
      const on = Object.entries(NOTE_KINDS).map(([k, label]) => { const c = check(k, (n.on || []).includes(k), label); return [k, c]; });
      const row = h('div', { class: 'noterow' }, h('div', { class: 'row' }, title, h('button', { type: 'button', class: 'btn sm', onclick: () => row.remove() }, 'حذف')), body, h('div', { class: 'row' }, h('small', { class: 'muted' }, 'خودش تیک بخورد روی:'), on.map(x => x[1])));
      row.read = () => ({ id: n.id, title: title.value.trim(), text: body.value.trim(), on: on.filter(([, c]) => $('input', c).checked).map(([k]) => k) });
      list.append(row);
    };
    printNotes().forEach(addNote); drawLogo();
    const save = () => {
      const shop = Object.fromEntries(SHOP_FIELDS.map(([k]) => [k, inputs[k].value.trim()]).filter(x => x[1])); if (logo) shop.logo = logo;
      const notes = [...list.children].map(r => r.read()).filter(n => n.text);
      setPrint({ shop, notes });
      // the browser's storage is small: say so when a save did not take
      if (JSON.stringify(printCfg().shop) !== JSON.stringify(shop)) return toast('ذخیره نشد؛ لوگوی کوچک‌تری انتخاب کنید.', 'err');
      close(); toast('ذخیره شد.'); if (done) done();
    };
    b.append(h('p', { class: 'muted' }, 'این تنظیمات روی همین کامپیوتر می‌ماند. خانهٔ خالی، مقدار تنظیمات سایت را چاپ می‌کند.'),
      h('div', { class: 'grid' }, SHOP_FIELDS.map(([k, label, cls]) => field(label, inputs[k], '', cls)), h('div', { class: 'wide' }, h('span', { class: 'lbl' }, 'لوگو'), h('label', { class: 'btn sm filebtn' }, 'انتخاب تصویر…', file), shown)),
      h('h3', {}, 'توضیحات آماده'), list, h('button', { type: 'button', class: 'btn sm', onclick: () => addNote({ id: 'n' + Date.now(), title: '', text: '', on: [] }) }, '+ توضیح تازه'),
      h('div', { class: 'row end' }, h('button', { type: 'button', class: 'btn', onclick: close }, 'انصراف'), h('button', { type: 'button', class: 'btn primary', onclick: save }, 'ذخیره')));
  }, true);
}
