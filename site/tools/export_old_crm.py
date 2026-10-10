"""Turns the old CRM's SQL Server backup (restored as database `oldcrm`) into one import file for Mazesta Connect: gzip'd JSON lines, one record per line.

    python export_old_crm.py out.jsonl.gz

Needs pyodbc and an ODBC driver for SQL Server. Lines (in this order): customer, build (assembled system, old form 1), job (service, old form 3), form (the other forms:
test 2, warranty parts 6, hand-over 8, online services 10, solutions 11). Whatever the plugin has no column for travels in "data" (the form's other sections, as read).
"""
import collections, gzip, json, re, sys
import pyodbc

CONN = r'DRIVER={ODBC Driver 18 for SQL Server};SERVER=.\SQLEXPRESS;DATABASE=oldcrm;Trusted_Connection=yes;TrustServerCertificate=yes;'


def q(sql, *a):
    c = pyodbc.connect(CONN, timeout=15); cur = c.cursor(); cur.execute(sql, *a); rows = cur.fetchall(); c.close(); return rows


def digits(s):
    return (s or '').translate(str.maketrans('۰۱۲۳۴۵۶۷۸۹٠١٢٣٤٥٦٧٨٩', '01234567890123456789'))


def mobiles(s):
    out = []
    for m in re.findall(r'(?<!\d)(?:0098|\+98|98|0)?(9\d{9})(?!\d)', digits(s)):
        n = '0' + m
        if n not in out: out.append(n)
    return out


def iso_date(s):
    m = re.match(r'(\d{1,2})/(\d{1,2})/(\d{4})', (s or '').strip())
    return f'{m.group(3)}-{int(m.group(1)):02d}-{int(m.group(2)):02d}' if m else None


def num(s):
    d = re.sub(r'[^\d]', '', digits(str(s or '')))
    return int(d) if d else 0


# ---------- dynamic forms ----------
def load_forms(ids):
    field = {r[0]: r for r in q('SELECT id,formId,parentId,type,title FROM TFormField')}
    il = ','.join(str(i) for i in ids)
    recs = collections.OrderedDict()
    for r in q(f'SELECT id,formId,custCode,createDate,formattedId FROM TFormValue WHERE formId IN ({il}) ORDER BY id'):
        recs[r[0]] = {'id': r[0], 'form': r[1], 'cust': r[2], 'at': r[3], 'no': r[4], 'rows': collections.OrderedDict()}
    for vid, fid, grp, title, val in q(f'SELECT formValueId,fieldId,[group],title,value FROM TFormValueParam WHERE formValueId IN (SELECT id FROM TFormValue WHERE formId IN ({il})) ORDER BY id'):
        r = recs.get(vid); fd = field.get(fid)
        if r is None or fd is None: continue
        sec = field.get(fd[2]); sec_title = sec[4] if sec else ''
        key = (sec_title, (grp or '').split('_')[-1])
        shown = (title or '').strip() or (val or '').strip()
        r['rows'].setdefault(key, collections.OrderedDict()).setdefault(fd[4], []).append((fd[3], shown))
    return recs


def get(rec, label):
    """first value of a field anywhere in the record ('' when absent)."""
    for row in rec['rows'].values():
        if label in row: return row[label][0][1]
    return ''


def has(rec, label):
    return get(rec, label) not in ('', '0')


def rows_with(rec, label):
    return [row for row in rec['rows'].values() if label in row]


def extra(rec, skip):
    """the form's remaining sections: [{s: title, r: {label: value | [values]}}], without empty values and switched-off toggles."""
    out = []
    for (sec, _), row in rec['rows'].items():
        cells = {}
        for label, vs in row.items():
            if label in skip: continue
            vals = [v for t, v in vs if v != '' and not (t == 'toggle' and v == '0') and not (t == 'checkbox' and v == '0')]
            if vals: cells[label] = vals[0] if len(vals) == 1 else vals
        if cells: out.append({'s': sec, 'r': cells})
    return out


# a best-effort label for filtering (the model name stays as typed); brands known to make only one kind of part are listed by name, anything else stays "other"
CATS = [('monitor', r'\bMONITOR\b|BENQ|\bDISPLAY\b|مانیتور'),
        ('gpu', r'\b(RTX|GTX|GEFORCE|RADEON|ARC ?[AB]\d{3})\b|\bRX ?\d{3,4}\b|\b[2-5][0-9]{2}0 ?(TI|SUPER)\b|PRO ?ART \d{4}|کارت گرافیک'),
        ('cpu', r'\bCPU\b|RYZEN|CORE ?I\d|CORE ULTRA|\bULTRA \d|THREADRIPPER|XEON|\bTRAY\b|\b\d{4,5}K[FS]?\b|PENTIUM|CELERON|پردازنده'),
        ('motherboard', r'\bMB\b|MOTHER ?BOARD|MAINBOARD|\b[ABHXZ]\d{3}[EM]?\b|مادربرد|مادر برد'),
        ('ram', r'\b(RAM|DDR\d|D[45])\b|DOMINATOR|VENGEANCE|FURY|TRIDENT|TFORCE|\bRIPJAWS|\d{2,3}G(B)? ?\d{4}|رم'),
        ('ssd', r'\bSSD\b|NVME|\bM\.?2\b|SPATIUM|\bMP\d{2}|\bP\d{3}\b|\bM[34]\d{2}\b|\b9[0-9]{2} ?(PRO|EVO)|FIRECUDA|CARDEA|KC\d{3}'),
        ('hdd', r'\bHDD\b|WESTERN DIGITAL|\bWD ?(BLUE|PURPLE|RED|BLACK)|SKYHAWK|BARRACUDA|IRONWOLF|هارد'),
        ('cooler', r'COOLER|LIQUID|\bAIO\b|KRAKEN|GAMMAXX|ASSASSIN|\bAK\d{3}|\bL[STP]\d{3}|HYPER 212|HEATSINK|HYDROGON|\bLC ?II|خنک'),
        ('psu', r'\bPSU\b|POWER|\b\d{3,4} ?W(ATT)?\b|80 ?PLUS|BRONZE|GOLD|TITANIUM|\bRM\d{3}|\bBX\d|\bGF\d|پاور'),
        ('case', r'\bCASE\b|CHASSIS|AWEST|FARA ?\d|MATREXX|\bCC\d{3}|ASTRIA|MASTER ?TECH (HUNTER|APACHI)|ANTEC|LANCOOL|\bTD ?500|MASTER ?BOX|کیس'),
        ('fan', r'\bFAN\b|فن')]


def category(name):
    for cat, pat in CATS:
        if re.search(pat, name or '', re.I): return cat
    return 'other'


def dt(d):
    return d.strftime('%Y-%m-%d %H:%M:%S') if d else None


def main(out):
    lines = []

    # customers
    props = collections.defaultdict(list)
    for link3, par, val in q("SELECT Link3,Parameter,Value_ FROM CustomerProperty WHERE Value_ IS NOT NULL AND LTRIM(RTRIM(Value_))<>''"):
        props[link3].append((par, val.strip()))
    # the old CRM's customer groups (customers, agents, staff, ...); a few customers sit in two, the more specific one wins
    GROUPS = [('نمایندگان', 'agent'), ('پرسنل', 'staff'), ('انبار ها', 'store'), ('پیک و باربری', 'courier'), ('گارانتی ها', 'warranty'), ('مشتریان بالقوه', 'lead'), ('عمومی', 'general'), ('مشتریان', 'customer')]
    have = collections.defaultdict(set)
    for cc, gn in q('SELECT customer_code, group_name FROM Tcustomer_group'):
        have[cc].add(re.sub(r'\s+', ' ', (gn or '')).strip())
    def group_of(cc): return next((slug for label, slug in GROUPS if label in have.get(cc, ())), 'customer')
    n_cust = 0
    for code, name, comment, created in q('SELECT code,Name,comment,CreateDate FROM Tcustomer ORDER BY code'):
        p = props.get(code, []); mob = []; phones = []; email = nid = addr = ''
        for par, val in p:
            if par == 'موبایل':
                ms = mobiles(val)
                mob += [m for m in ms if m not in mob]
                if not ms: phones.append(val)
            elif par == 'تلفن': phones.append(val)
            elif par == 'ایمیل': email = email or val
            elif par == 'کد ملی': nid = nid or digits(val)
            elif par == 'آدرس': addr = addr or val
        d = re.match(r'(\d{2})/(\d{2})/(\d{4}) (\d{2}):(\d{2})', created or '')
        lines.append({'t': 'customer', 'old': code, 'name': re.sub(r'\s+', ' ', name or '').strip(), 'mobile': mob[0] if mob else '', 'mobile2': mob[1] if len(mob) > 1 else '', 'phone': ' / '.join(phones)[:60],
                      'national_id': nid[:20], 'email': email[:190], 'address': addr, 'notes': (comment or '').strip(), 'group': group_of(code), 'created': f'{d[3]}-{d[1]}-{d[2]} {d[4]}:{d[5]}:00' if d else None})
        n_cust += 1

    # assembled systems (form 1)
    recs = load_forms([1])
    for r in recs.values():
        parts = []
        for row in rows_with(r, 'نام کالا'):
            name = row['نام کالا'][0][1]; flags = [v for t, v in row.get('مشخصات کالا', [])]
            parts.append({'cat': category(name), 'model': name, 'serial': row.get('سریال کالا', [('', '')])[0][1], 'warranty': 'گارانتی' in flags, 'box': 'جعبه' in flags, 'qc': 'چک QC' in flags,
                          'note': row.get('توضیحات', [('', '')])[0][1] if 'توضیحات' in row else ''})
        gpu = next((p['model'] for p in parts if p['cat'] == 'gpu'), ''); cpu = next((p['model'] for p in parts if p['cat'] == 'cpu'), '')
        skip = {'شماره فاکتور', 'مشتری', 'نام کالا', 'سریال کالا', 'مشخصات کالا'}
        lines.append({'t': 'build', 'old': r['id'], 'cust': r['cust'], 'no': r['no'], 'invoice': get(r, 'شماره فاکتور'), 'title': ('سیستم ' + (gpu or cpu)).strip() if (gpu or cpu) else 'سیستم نو',
                      'at': dt(r['at'])[:10] if r['at'] else None, 'delivered': has(r, 'سیستم تحویل داده شد'), 'parts': parts, 'data': extra(r, skip), 'created': dt(r['at'])})

    # service jobs (form 3)
    recs = load_forms([3])
    for r in recs.values():
        recv, add, pays = [], [], []
        for row in rows_with(r, 'نام قطعه'):
            fl = [v for t, v in row.get('-', [])]
            recv.append({'name': row['نام قطعه'][0][1], 'serial': row.get('سریال', [('', '')])[0][1], 'box': 'جعبه' in fl, 'warranty': 'گارانتی' in fl, 'note': row.get('توضیحات / مشکل ظاهری', [('', '')])[0][1]})
        for row in rows_with(r, 'کالا') + [x for x in rows_with(r, 'توضیحات') if 'قیمت کالا' in x and 'کالا' not in x]:
            fl = [v for t, v in row.get('-', [])]
            add.append({'name': row.get('کالا', row.get('توضیحات', [('', '')]))[0][1], 'serial': row.get('سریال', [('', '')])[0][1], 'price': num(row.get('قیمت کالا', [('', '')])[0][1]), 'box': 'جعبه' in fl, 'warranty': 'گارانتی' in fl,
                        'note': row.get('توضیحات', [('', '')])[0][1] if 'کالا' in row else ''})
        for row in rows_with(r, 'مبلغ واریزی (تومان)'):
            pays.append({'amount': num(row['مبلغ واریزی (تومان)'][0][1]), 'account': row.get('واریز به حساب', [('', '')])[0][1], 'tx': row.get('شماره تراکنش', [('', '')])[0][1], 'holder': row.get('نام شماره حساب متفرقه', [('', '')])[0][1]})
        done = has(r, 'سرویس انجام شد'); handed = has(r, 'سیستم تحویل داده شد')
        ship = {k: get(r, k) for k in ('نام باربری', 'شماره پیگیری', 'آدرس') if get(r, k)}
        if ship and get(r, 'تاریخ'): ship['تاریخ'] = iso_date(get(r, 'تاریخ')) or get(r, 'تاریخ')
        skip = {'مشتری', 'تاریخ دریافت', 'ساعت دریافت', 'تاریخ تحویل', 'نام قطعه', 'سریال', '-', 'توضیحات / مشکل ظاهری', 'کالا', 'قیمت کالا', 'مبلغ واریزی (تومان)', 'واریز به حساب', 'شماره تراکنش',
                'نام شماره حساب متفرقه', 'مشکلات سیستم و درخواست مشتری', 'کارهای انجام شده روی سیستم', 'توضیحات شرکت (خصوصی)', 'هزینه خدمات (تومان)', 'تخفیف', 'هزینه های پرداخت شده', 'شماره فاکتور',
                'سرویس انجام شد', 'سیستم تحویل داده شد', 'نام باربری', 'شماره پیگیری', 'آدرس', 'تاریخ', 'نام شماره حساب متفرقه', 'هزینه قطعات', 'خدمات + پرداختی ها', 'هزینه نهایی', 'هزینه نهایی (تومان)'}
        lines.append({'t': 'job', 'old': r['id'], 'cust': r['cust'], 'no': r['no'], 'received': iso_date(get(r, 'تاریخ دریافت')) or (dt(r['at'])[:10] if r['at'] else None), 'due': iso_date(get(r, 'تاریخ تحویل')),
                      'mazesta': has(r, 'سیستم مازستا ؟'), 'warranty': has(r, 'گارانتی دارد؟'), 'complaint': get(r, 'مشکلات سیستم و درخواست مشتری'), 'work': get(r, 'کارهای انجام شده روی سیستم'),
                      'private': get(r, 'توضیحات شرکت (خصوصی)'), 'labor': num(get(r, 'هزینه خدمات (تومان)')), 'discount': num(get(r, 'تخفیف')), 'paid': num(get(r, 'هزینه های پرداخت شده')),
                      'invoice': get(r, 'شماره فاکتور'), 'done': done, 'handed': handed, 'recv': recv, 'add': add, 'pays': pays, 'ship': ship, 'data': extra(r, skip), 'created': dt(r['at'])})

    # the other forms
    kinds = {2: 'test', 6: 'warranty', 8: 'handover', 10: 'online', 11: 'solution'}
    recs = load_forms(list(kinds))
    for r in recs.values():
        ref = get(r, 'شماره فاکتور') or get(r, 'مربوط به فاکتور :')
        title = get(r, 'نام و مدل قطعه') or get(r, 'نام کالا') or ''
        lines.append({'t': 'form', 'kind': kinds[r['form']], 'old': r['id'], 'cust': r['cust'] or None, 'no': r['no'], 'ref': ref, 'at': dt(r['at'])[:10] if r['at'] else None, 'title': title,
                      'data': extra(r, {'مشتری'}), 'created': dt(r['at'])})

    with gzip.open(out, 'wt', encoding='utf-8', compresslevel=9) as f:
        for l in lines: f.write(json.dumps(l, ensure_ascii=False, separators=(',', ':')) + '\n')
    c = collections.Counter(l['t'] + (':' + l['kind'] if l['t'] == 'form' else '') for l in lines)
    print(dict(c))


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    main(sys.argv[1])
