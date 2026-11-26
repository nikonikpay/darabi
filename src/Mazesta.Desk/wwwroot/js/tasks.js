// Tasks and workflows: what waits for me, what I sent, one task with its steps and history, and the manager's workflows drawn as cards that are dragged into order.
const hasTasks = () => !!(S.cfg && S.cfg.team);   // a site whose plugin is from before tasks: the program shows none of them
NAV.push(['#/tasks', 'کارهای من', hasTasks], ['#/flows', 'گردش کارها', () => hasTasks() && can('manage', 2)]);
routes.push([/^\/tasks$/, (m, q) => viewTasks(q)], [/^\/task\/(new|\d+)$/, (m, q) => viewTask(m[1], q)], [/^\/flows$/, () => viewFlows()], [/^\/flow\/(new|\d+)$/, m => viewFlow(m[1])]);

const team = () => S.cfg.team || [], teamRoles = () => S.cfg.teamRoles || {};
const whoNames = s => [...(s.roles || []).map(r => r.label || teamRoles()[r] || r), ...(s.users || []).map(u => u.name || (team().find(p => p.id === u) || {}).name || '؟')];
const TASK_TONE = { open: '', blocked: 'bad', review: 'warn', done: 'ok', cancelled: '' };
const overdue = t => t.due_at && !['done', 'cancelled'].includes(t.status) && t.due_at < today();

/** The steps in a row: done ones ticked, the current one standing out. */
function pipeline(t, full) {
  const closed = t.status === 'done' || t.status === 'review';
  return h('ol', { class: 'pipe' + (full ? ' full' : '') }, t.steps.map((s, i) => {
    const state = closed || i < t.step ? 'done' : i === t.step && t.status !== 'cancelled' ? 'now' + (t.status === 'blocked' ? ' bad' : '') : '';
    return h('li', { class: state, 'aria-current': i === t.step && !closed ? 'step' : null }, h('i', {}, state === 'done' ? '✓' : i + 1), h('div', {}, h('b', {}, s.title), h('small', {}, whoNames(s).join('، ') || 'سازندهٔ کار'), full && s.hint ? h('p', { class: 'muted' }, s.hint) : null));
  }));
}

async function taskAct(t, action, more) {
  try {
    const r = await POST('staff/tasks/act', Object.assign({ task: t.id, action, step: t.step }, more || {}));
    if (r.queued) (S.waitingTasks || (S.waitingTasks = new Set())).add(t.id + ':' + t.step);   // its buttons wait too: a second press would only be refused later
    else toast(r.status === 'done' ? 'کار تمام شد.' : action === 'done' && r.step !== t.step && r.steps[r.step] ? 'ثبت شد؛ رفت برای مرحلهٔ «' + r.steps[r.step].title + '».' : 'ثبت شد.');
    route(); taskBadge(); return true;
  } catch (e) { fail(e); if (e.status === 409) route(); return false; }
}

/** The one dialog of every action: a note (required for the ones that must say why), and the percent for progress. */
function actDialog(t, action, label) {
  const needs = ['fail', 'back', 'reject', 'note'].includes(action);
  modal(label + ' — ' + t.title, (b, close) => {
    const note = area('note', '', { rows: 3, autofocus: true, placeholder: needs ? 'توضیح (لازم است)' : 'توضیح (اگر لازم است)' });
    const range = h('input', { type: 'range', min: 0, max: 100, step: 5, value: t.progress, 'aria-label': 'درصد پیشرفت این مرحله' }), shown = h('b', { class: 'lat' }, t.progress + '٪'); range.addEventListener('input', () => { shown.textContent = range.value + '٪'; });
    b.append(action === 'progress' ? field('درصد پیشرفت این مرحله', h('div', { class: 'row' }, range, shown)) : null, field('توضیح', note),
      h('div', { class: 'row end' }, h('button', { class: 'btn', onclick: close }, 'انصراف'), h('button', { class: 'btn primary', onclick: async () => {
        if (needs && !note.value.trim()) return toast('توضیح را بنویسید.', 'err');
        close(); await taskAct(t, action, Object.assign({ note: note.value }, action === 'progress' ? { progress: +range.value } : {}));
      } }, label)));
  });
}

function taskButtons(t) {
  if (S.waitingTasks && S.waitingTasks.has(t.id + ':' + t.step)) return [pill('ثبت شما در صف فرستادن است')];
  const b = (action, label, cls) => h('button', { type: 'button', class: 'btn ' + (cls || ''), onclick: () => actDialog(t, action, label) }, label);
  const out = [];
  if ((t.mine || t.can_act) && t.status === 'open') out.push(b('done', t.step < t.steps.length - 1 ? 'انجام شد، مرحلهٔ بعد' : 'انجام شد', t.mine ? 'primary' : ''), b('progress', 'پیشرفت'), b('fail', 'انجام نشد'), t.step > 0 ? b('back', 'برگشت به مرحلهٔ قبل') : null,
    t.mine ? null : h('small', { class: 'muted' }, 'نوبت دیگران است؛ شما به‌عنوان مدیر هم می‌توانید ثبت کنید.'));
  if (t.status === 'blocked' && (t.mine || t.can_act || t.boss)) out.push(h('button', { type: 'button', class: 'btn primary', onclick: () => taskAct(t, 'resume') }, 'ادامهٔ کار'));
  if (t.status === 'review' && t.boss) out.push(b('confirm', 'تأیید می‌کنم', 'primary'), b('reject', 'تأیید نمی‌کنم'));
  return out.filter(Boolean);
}

function taskCard(t) {
  const step = t.steps[t.step];
  const blocked = t.status === 'blocked' ? [...(t.log || [])].reverse().find(l => l.action === 'fail') : null;
  return h('article', { class: 'task' + (t.priority ? ' urgent' : '') + (t.status === 'blocked' || overdue(t) ? ' bad' : '') },
    h('header', {}, h('h3', {}, h('a', { href: '#/task/' + t.id }, t.title)), h('span', { class: 'row' }, t.priority ? pill('فوری', 'bad') : null, pill(t.statusText, TASK_TONE[t.status]), t.due_at_j ? pill('تا ' + t.due_at_j, overdue(t) ? 'bad' : '') : null)),
    h('p', { class: 'muted' }, t.ref_label ? h('a', { href: `#/${t.ref_kind}/${t.ref_id}` }, t.ref_label) : null, t.customer_name ? ' · ' + t.customer_name : '', ' · از ' + t.who),
    pipeline(t),
    h('div', { class: 'meter', role: 'progressbar', 'aria-valuenow': t.percent, 'aria-valuemin': 0, 'aria-valuemax': 100 }, h('span', { style: `inline-size:${t.percent}%` }), h('b', { class: 'lat' }, t.percent + '٪')),
    step && ['open', 'blocked'].includes(t.status) ? h('p', {}, 'الان: ', h('b', {}, step.title), step.hint ? h('span', { class: 'muted' }, ' — ' + step.hint) : null) : null,
    blocked ? h('p', { class: 'err' }, 'انجام نشد: ' + blocked.note + ' (' + blocked.who + ')') : null,
    h('div', { class: 'row' }, taskButtons(t)));
}

async function viewTasks(q) {
  const box = q.get('box') || 'mine', page = Math.max(1, +q.get('page') || 1);
  const r = await GET('staff/tasks' + qs({ box, q: q.get('q'), page }));
  const boxes = [['mine', 'نوبت من'], ['review', 'منتظر تأیید من'], ['sent', 'فرستاده‌ام'], ['all', can('manage') ? 'همهٔ کارهای باز' : 'همهٔ کارهای من'], ['done', 'تمام‌شده']];
  return h('div', {}, title('کارها', h('a', { class: 'btn primary', href: '#/task/new' }, '+ کار تازه')),
    h('div', { class: 'row' }, h('div', { class: 'chips' }, boxes.map(([k, t]) => h('button', { class: 'chip' + (box === k ? ' on' : ''), onclick: () => { location.hash = '#/tasks?box=' + k; } }, t))),
      filterBar(q, '#/tasks', null, 'جستجو در کارها…')),
    r.items.length ? h('div', { class: 'tasks' }, r.items.map(taskCard)) : h('div', { class: 'panel empty' }, box === 'mine' ? 'کاری منتظر شما نیست.' : 'موردی نیست.'),
    pager(r.total, r.per, page, p => '#/tasks?' + new URLSearchParams({ box, page: p })));
}

/** The home page's first box: what waits for me. */
function myTasksPanel() {
  const box = h('div');
  if (hasTasks()) GET('staff/tasks?box=mine&per=6').then(r => {
    if (r.items.length) box.append(h('section', { class: 'panel accent-task' }, h('div', { class: 'title' }, h('h3', {}, 'کارهای نوبت من (' + r.total + ')'), h('a', { href: '#/tasks' }, 'همهٔ کارها')), h('div', { class: 'tasks' }, r.items.map(taskCard))));
  }, () => { });
  return box;
}

/** The menu's number, and a word when a task newly waits for this person. */
async function taskBadge() {
  if (!S.me || !hasTasks()) return;
  try {
    const r = await GET('staff/tasks?box=mine&per=50'); navBadge('#/tasks', r.total);
    const now = new Set(r.items.map(t => t.id + ':' + t.step));
    if (S.mineSeen) r.items.filter(t => !S.mineSeen.has(t.id + ':' + t.step) && !t.by_me).forEach(t => toast('کار تازه برای شما: ' + t.title + ' — ' + (t.steps[t.step] || {}).title));
    S.mineSeen = now;
  } catch (e) { }
}

async function viewTask(id, q) {
  if (id === 'new') return taskForm(null, q);
  const t = await GET('staff/tasks/' + id);
  if (q.get('edit') && t.boss) return taskForm(t, q);
  const log = h('div', { class: 'timeline' }, [...t.log].reverse().map(l => h('div', { class: 'call' }, h('div', { class: 'when' }, h('span', { class: 'lat' }, l.at_j), h('small', { class: 'muted' }, l.who)),
    h('div', {}, h('b', {}, l.actionText), l.stepTitle ? h('span', { class: 'muted' }, ' — ' + l.stepTitle) : null, l.progress != null ? pill(l.progress + '٪') : null, l.note ? h('p', {}, l.note) : null))));
  const closed = ['done', 'cancelled'].includes(t.status);
  return h('div', {}, title(t.title, pill(t.statusText, TASK_TONE[t.status]), t.boss ? h('a', { class: 'btn', href: `#/task/${t.id}?edit=1` }, 'ویرایش') : null,
    t.boss ? h('button', { class: 'btn', onclick: async () => { if (await ask(closed ? 'این کار دوباره باز شود؟' : 'این کار لغو شود؟', closed ? 'باز شود' : 'لغو شود')) taskAct(t, closed ? 'reopen' : 'cancel'); } }, closed ? 'باز کردن دوباره' : 'لغو کار') : null),
    h('div', { class: 'panel' }, h('p', { class: 'muted' }, 'از ' + t.who + ' در ' + t.created_j, t.due_at_j ? ' · تا ' + t.due_at_j : '', t.flow_name ? ' · گردش کار «' + t.flow_name + '»' : '', t.confirm ? ' · پایان با تأیید سازنده' : ''),
      t.ref_label ? h('p', {}, 'مربوط به: ', h('a', { href: `#/${t.ref_kind}/${t.ref_id}` }, t.ref_label)) : null, t.customer_name ? h('p', {}, 'مشتری: ' + t.customer_name + ' ', h('span', { class: 'lat muted' }, t.customer_mobile)) : null, t.body ? h('p', { class: 'body' }, t.body) : null,
      pipeline(t, true), h('div', { class: 'meter' }, h('span', { style: `inline-size:${t.percent}%` }), h('b', { class: 'lat' }, t.percent + '٪')),
      t.marks.length ? h('p', {}, 'تا اینجا انجام داده‌اند: ' + t.marks.map(m => m.name).join('، ')) : null,
      h('div', { class: 'row' }, taskButtons(t), h('button', { type: 'button', class: 'btn', onclick: () => actDialog(t, 'note', 'یادداشت') }, '+ یادداشت'))),
    h('section', { class: 'panel' }, h('h3', {}, 'آنچه گذشت'), log));
}

/**
 * The steps drawn as cards in a row. A card is dragged by its handle to another place; a role or a person is dragged from the strip onto a card (or picked in the card's own list);
 * the arrows do the same moves from the keyboard. kind() tells which record statuses a step may set.
 */
function stepsEditor(initial, kind) {
  const steps = (initial && initial.length ? initial : [{ title: '', users: [], roles: [] }]).map(s => ({ title: s.title || '', users: (s.users || []).map(u => u.id || u), roles: (s.roles || []).map(r => r.key || r), all: !!s.all, hint: s.hint || '', status: s.status || '' }));
  const row = h('div', { class: 'flowrow' });
  const label = w => w[0] === 'r' ? teamRoles()[w.slice(2)] || w.slice(2) : (team().find(p => p.id === +w.slice(2)) || {}).name || '؟';
  const add = (s, w) => { const list = w[0] === 'r' ? s.roles : s.users, v = w[0] === 'r' ? w.slice(2) : +w.slice(2); if (!list.includes(v)) { list.push(v); draw(); } };
  const move = (from, to) => { if (to < 0 || to >= steps.length || from === to) return; steps.splice(to, 0, steps.splice(from, 1)[0]); draw(); };
  const options = () => Object.assign({ '': '+ گیرنده…' }, Object.fromEntries(Object.entries(teamRoles()).map(([k, t]) => ['r:' + k, 'نقش: ' + t])), Object.fromEntries(team().map(p => ['u:' + p.id, p.name])));
  const card = (s, i) => {
    const statuses = kind() === 'build' ? S.cfg.buildStatuses : kind() === 'job' ? S.cfg.jobStatuses : null;
    const name = text('title', s.title, { placeholder: 'نام مرحله، مثلاً: اسمبل', 'aria-label': 'نام مرحله ' + (i + 1), oninput: e => { s.title = e.target.value; } });
    const pick = select('who', options(), '', { 'aria-label': 'افزودن گیرنده', onchange: e => { if (e.target.value) add(s, e.target.value); } });
    const all = check('all', s.all, 'همهٔ گیرنده‌ها باید انجام دهند'); $('input', all).addEventListener('change', e => { s.all = e.target.checked; });
    const handle = h('span', { class: 'handle', draggable: 'true', title: 'بکشید تا جابه‌جا شود', ondragstart: e => { e.dataTransfer.setData('text/plain', 's:' + i); e.dataTransfer.setDragImage(el, 20, 20); } }, '⠿');
    const el = h('div', { class: 'flowcard', ondragover: e => { e.preventDefault(); el.classList.add('over'); }, ondragleave: () => el.classList.remove('over'),
      ondrop: e => { e.preventDefault(); el.classList.remove('over'); const d = e.dataTransfer.getData('text/plain'); if (d.startsWith('s:')) move(+d.slice(2), i); else if (/^[ru]:/.test(d)) add(s, d); } },
      h('header', {}, handle, h('b', { class: 'n' }, i + 1), h('span', { class: 'moves' }, h('button', { type: 'button', title: 'یک مرحله زودتر', disabled: i === 0, onclick: () => move(i, i - 1) }, '→'), h('button', { type: 'button', title: 'یک مرحله دیرتر', disabled: i === steps.length - 1, onclick: () => move(i, i + 1) }, '←'),
        h('button', { type: 'button', class: 'x', title: 'برداشتن مرحله', disabled: steps.length === 1, onclick: () => { steps.splice(i, 1); draw(); } }, '×'))),
      name,
      h('div', { class: 'whos' }, [...s.roles.map(r => 'r:' + r), ...s.users.map(u => 'u:' + u)].map(w => h('button', { type: 'button', class: 'who-chip ' + w[0], title: 'برداشتن', onclick: () => { const list = w[0] === 'r' ? s.roles : s.users; list.splice(list.indexOf(w[0] === 'r' ? w.slice(2) : +w.slice(2)), 1); draw(); } }, label(w), ' ×')),
        !s.roles.length && !s.users.length ? h('small', { class: 'muted' }, 'بدون گیرنده: خودِ سازنده') : null),
      pick, all, area('hint', s.hint, { rows: 2, placeholder: 'چه کاری باید انجام شود؟', 'aria-label': 'توضیح مرحله', oninput: e => { s.hint = e.target.value; } }),
      statuses ? field('وضعیت پرونده در شروع این مرحله', select('status', Object.assign({ '': 'بدون تغییر' }, statuses), s.status, { onchange: e => { s.status = e.target.value; } })) : null);
    return el;
  };
  const draw = () => {
    clear(row).append(...steps.flatMap((s, i) => [i ? h('span', { class: 'arrow', 'aria-hidden': 'true' }, '←') : null, card(s, i)]).filter(Boolean),
      h('button', { type: 'button', class: 'flowadd', onclick: () => { steps.push({ title: '', users: [], roles: [], all: false, hint: '', status: '' }); draw(); const all = row.querySelectorAll('input[name=title]'); all[all.length - 1].focus(); } }, '+ مرحله'));
  };
  draw();
  const chip = (w, t) => h('span', { class: 'who-chip ' + w[0], draggable: 'true', ondragstart: e => e.dataTransfer.setData('text/plain', w) }, t);
  const palette = h('div', { class: 'palette' }, h('small', { class: 'muted' }, 'نقش یا نفر را روی کارت مرحله بکشید:'), Object.entries(teamRoles()).map(([k, t]) => chip('r:' + k, t)), team().map(p => chip('u:' + p.id, p.name)));
  return { el: h('div', {}, palette, row), get: () => steps, redraw: draw };
}

async function taskForm(t, q) {
  const [rk, rid] = t ? [t.ref_kind, t.ref_id] : (q.get('ref') || ':').split(':');
  let flows = []; if (!t) { try { flows = (await GET('staff/flows')).items.filter(f => f.active && (f.kind === 'general' || f.kind === rk)); } catch (e) { } }
  const f = { title: text('title', t ? t.title : '', { placeholder: 'مثلاً: گرفتن برنامه‌های مشتری' }), body: area('body', t ? t.body : '', { rows: 3 }), due: h('input', { type: 'date', value: t ? t.due_at : '' }),
    priority: check('priority', t && t.priority, 'فوری'), confirm: check('confirm', t && t.confirm, 'پس از انجام، من باید تأیید کنم'),
    flow: select('flow_id', Object.assign({ 0: 'مرحله‌ها را خودم می‌نویسم' }, Object.fromEntries(flows.map(x => [x.id, x.name]))), +q.get('flow') || 0) };
  const editor = stepsEditor(t ? t.steps : [], () => rk), box = h('div', { class: 'wide' }, h('label', { class: 'lbl' }, 'مرحله‌ها و گیرنده‌ها'), editor.el);
  const own = h('div', { class: 'checks' }, f.confirm, f.priority);
  const sync = () => { const saved = !t && +f.flow.value > 0; box.hidden = saved; own.hidden = saved; f.title.placeholder = saved ? 'خالی بماند تا نام گردش کار بیاید' : 'مثلاً: گرفتن برنامه‌های مشتری'; };
  f.flow.addEventListener('change', sync); sync();
  const save = async e => {
    e.preventDefault();
    const saved = !t && +f.flow.value > 0;
    if (!saved && !f.title.value.trim()) return toast('عنوان کار را بنویسید.', 'err');
    if (!saved && editor.get().length === 1 && !editor.get()[0].title.trim()) editor.get()[0].title = f.title.value.trim().slice(0, 120);   // a simple task: its one step is the task
    const body = { title: f.title.value, body: f.body.value, due_at: f.due.value };
    if (saved) body.flow_id = +f.flow.value; else Object.assign(body, { steps: editor.get(), priority: $('input', f.priority).checked, confirm: $('input', f.confirm).checked });
    if (t) Object.assign(body, { id: t.id, updated: t.updated }); else if (rk) Object.assign(body, { ref_kind: rk, ref_id: +rid });
    try { const r = await POST('staff/tasks', body); if (r.queued) location.hash = '#/tasks'; else { toast(t ? 'ذخیره شد.' : 'کار ساخته و فرستاده شد.'); location.hash = '#/task/' + r.id; } taskBadge(); } catch (err) { fail(err); }
  };
  return h('div', {}, title(t ? 'ویرایش کار' : 'کار تازه'), rk ? h('p', { class: 'muted' }, 'برای ', h('a', { href: `#/${rk}/${rid}` }, rk === 'build' ? 'این سیستم' : 'این سرویس')) : null,
    h('form', { class: 'panel grid', onsubmit: save }, flows.length ? field('از روی گردش کار آماده', f.flow) : null, field('عنوان', f.title, '', 'wide'), field('توضیح', f.body, '', 'wide'), field('مهلت', f.due), own, box,
      h('div', { class: 'row wide' }, h('button', { class: 'btn primary' }, t ? 'ذخیره' : 'ساختن و فرستادن'), h('a', { class: 'btn', href: t ? '#/task/' + t.id : '#/tasks' }, 'انصراف'))));
}

/** On a computer's or a service's page: where its work is now, and starting a workflow or a task for it. */
function workflowPanel(kind, rec) {
  if (!hasTasks()) return h('div');
  const box = h('section', { class: 'panel accent-task' }, h('h3', {}, 'گردش کار و کارهای این پرونده'));
  Promise.all([GET(`staff/tasks?ref_kind=${kind}&ref_id=${rec.id}&per=50`), GET('staff/flows')]).then(([t, f]) => {
    const running = new Set(t.items.filter(x => !['done', 'cancelled'].includes(x.status)).map(x => x.flow_id));
    const flows = f.items.filter(x => x.active && (x.kind === kind || x.kind === 'general') && !running.has(x.id));
    box.append(t.items.length ? h('div', { class: 'tasks one' }, t.items.map(taskCard)) : h('p', { class: 'muted' }, 'برای این پرونده کاری ثبت نشده است.'),
      h('div', { class: 'row' }, flows.map(x => h('button', { type: 'button', class: 'btn', onclick: async () => { if (!await ask('گردش کار «' + x.name + '» برای این پرونده شروع شود؟', 'شروع')) return; try { await POST('staff/tasks', { flow_id: x.id, ref_kind: kind, ref_id: rec.id }); toast('شروع شد.'); route(); taskBadge(); } catch (e) { fail(e); } } }, 'شروع «' + x.name + '»')),
        h('a', { class: 'btn', href: `#/task/new?ref=${kind}:${rec.id}` }, '+ کار برای این پرونده')));
  }, e => box.append(h('p', { class: 'muted' }, e.message)));
  return box;
}

/* ---------- the manager's workflows ---------- */
async function viewFlows() {
  const r = await GET('staff/flows');
  return h('div', {}, title('گردش کارها', h('a', { class: 'btn primary', href: '#/flow/new' }, '+ گردش کار تازه')),
    h('p', { class: 'muted' }, 'هر گردش کار، مرحله‌های پشت سر هم است. تغییر آن روی کارهایی که از قبل در جریان‌اند اثری ندارد.'),
    r.items.length ? r.items.map(f => h('section', { class: 'panel' }, h('div', { class: 'title' }, h('h3', {}, f.name), h('span', { class: 'row' }, pill(f.kindText), f.auto ? pill('خودکار', 'ok') : null, f.active ? null : pill('خاموش', 'bad'),
      h('a', { class: 'btn', href: '#/flow/' + f.id }, 'ویرایش'), h('button', { class: 'btn', onclick: async () => { if (!await ask('گردش کار «' + f.name + '» پاک شود؟ کارهای در جریان ادامه پیدا می‌کنند.', 'پاک شود')) return; try { await POST('staff/flows/delete', { id: f.id }); toast('پاک شد.'); route(); } catch (e) { fail(e); } } }, 'حذف'))),
      pipeline({ steps: f.steps, step: -1, status: 'open' }))) : h('div', { class: 'panel empty' }, 'هنوز گردش کاری ساخته نشده است.'));
}

async function viewFlow(id) {
  let f = { name: '', kind: 'general', auto: false, active: true, steps: [] };
  if (id !== 'new') { f = (await GET('staff/flows')).items.find(x => x.id === +id); if (!f) return h('p', {}, 'گردش کار پیدا نشد.'); }
  const name = text('name', f.name, { required: true, placeholder: 'مثلاً: اسمبل سیستم نو' }), kind = select('kind', { build: 'سیستم نو (اسمبل)', job: 'سرویس', general: 'کار عمومی' }, f.kind);
  const auto = check('auto', f.auto, 'برای هر پروندهٔ تازهٔ این نوع خودکار شروع شود'), active = check('active', f.active, 'روشن');
  const editor = stepsEditor(f.steps, () => kind.value);
  const sync = () => { auto.hidden = kind.value === 'general'; }; kind.addEventListener('change', () => { sync(); editor.redraw(); }); sync();
  const save = async e => {
    e.preventDefault(); const bad = editor.get().findIndex(s => !s.title.trim()); if (bad >= 0) return toast('نام مرحلهٔ ' + (bad + 1) + ' را بنویسید.', 'err');
    try { await POST('staff/flows', { id: f.id || 0, updated: f.updated, name: name.value, kind: kind.value, auto: $('input', auto).checked, active: $('input', active).checked, steps: editor.get() }); toast('گردش کار ذخیره شد.'); location.hash = '#/flows'; } catch (err) { fail(err); }
  };
  return h('div', {}, title(id === 'new' ? 'گردش کار تازه' : 'ویرایش «' + f.name + '»'),
    h('form', { class: 'panel grid', onsubmit: save }, field('نام', name), field('برای', kind), h('div', { class: 'checks' }, auto, active), h('div', { class: 'wide' }, editor.el),
      h('div', { class: 'row wide' }, h('button', { class: 'btn primary' }, 'ذخیرهٔ گردش کار'), h('a', { class: 'btn', href: '#/flows' }, 'انصراف'))));
}
