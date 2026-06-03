import xml.etree.ElementTree as ET
import sys
from collections import defaultdict

# Department sysid -> name. Source: poscfg.xml from FULL_BACKUP_14 of the
# verifone-commander-import-export sibling project (2026-05-26 backup).
DEPTS = {
    '1000': 'GROCERY',          '1001': 'BEVERAGE NA',     '1002': 'HEALTH AND BEAUTY',
    '1003': 'HOUSEHOLD',        '1004': 'AUTOMOTIVE',      '1005': 'APPAREL',
    '1006': 'FISHING APPAREL',  '1007': 'FLIES AND SUPPLIES', '1008': 'JEWELRY',
    '1009': 'KIDS CORNER',      '1010': 'SOUVENIRS',       '1011': 'ART',
    '1012': 'CANDY',            '1013': 'SNACKS',          '1014': 'FUEL',
    '6541': 'Lottery Sales Winner', '9995': 'OPEN MERCHANDISE',
    '9996': 'TEST B DEPT',      '9997': 'TEST C DEPT',     '9998': 'MANUAL FUEL DEPT',
}

NS = '{urn:vfi-sapphire:np.domain.2001-07-01}'

ISSUE_LABELS = {
    'SHORT':   'Short hand-entered',
    'BADCHK':  'Bad check digit',
    'RW':      'Random-weight (NS=2)',
    'COUPON5': 'Coupon (NS=5)',
    'COUPON9': 'Coupon (NS=9)',
    'NDC':     'NDC (NS=3)',
    'INSTORE': 'In-store (NS=4)',
    'UNUSABLE':'Unusable (non-numeric / too long)',
}

def gtin14_check(s13):
    t = 0
    for i, c in enumerate(s13):
        d = int(c); t += d * 3 if i % 2 == 0 else d
    return (10 - (t % 10)) % 10

def is_valid_gtin14(s14):
    return len(s14) == 14 and s14.isdigit() and gtin14_check(s14[:13]) == int(s14[13])

def classify(upc):
    if not upc or not upc.isdigit():
        return 'UNUSABLE'
    sig = len(upc.lstrip('0'))
    if sig <= 5: return 'SHORT'
    if len(upc) != 14: return 'UNUSABLE'
    ns = upc[2]
    if ns == '2': return 'RW'
    if ns == '5': return 'COUPON5'
    if ns == '9': return 'COUPON9'
    if ns == '3': return 'NDC'
    if ns == '4': return 'INSTORE'
    if not is_valid_gtin14(upc): return 'BADCHK'
    return 'VALID'

# Parse backup
tree = ET.parse(sys.argv[1])
root = tree.getroot()
rows = []
counts = defaultdict(int)
for plu in root.findall(NS + 'PLU'):
    upc = (plu.findtext('upc') or '').strip()
    mod = (plu.findtext('upcModifier') or '').strip()
    desc = (plu.findtext('description') or '').strip()
    dept_id = (plu.findtext('department') or '').strip()
    flags_el = plu.find('flags')
    flag_ids = set()
    if flags_el is not None:
        for f in flags_el.findall(NS + 'flag'):
            sid = f.get('sysid')
            if sid: flag_ids.add(sid)
    not_sold = '2' in flag_ids
    cls = classify(upc)
    counts[cls] += 1
    if cls != 'VALID':
        rows.append({
            'upc': upc, 'mod': mod, 'desc': desc,
            'dept_id': dept_id, 'dept_name': DEPTS.get(dept_id, '?UNKNOWN?'),
            'class': cls, 'label': ISSUE_LABELS[cls],
            'not_sold': not_sold,
        })

# Header + summary
print('=' * 88)
print('ANOMALOUS-BARCODE AUDIT — Verifone Commander PriceBook (sample-data backup)')
print('=' * 88)
print(f'Total PLUs analyzed: {sum(counts.values())}    Anomalous: {len(rows)}')
print()
print('Classification counts:')
for k in ('VALID','SHORT','BADCHK','RW','COUPON5','COUPON9','NDC','INSTORE','UNUSABLE'):
    if k in counts: print(f'  {k:10} {counts[k]:4}')

def fmt_item(r, extra):
    nm = '  [NOT SOLD]' if r['not_sold'] else ''
    return f"  upc={r['upc']}  mod={r['mod']}  {extra}  {r['desc']}{nm}"

# View 1 — Grouped by DEPARTMENT (each item shows issue type)
print()
print('=' * 88)
print('VIEW 1 — GROUPED BY DEPARTMENT (item line includes the barcode issue type)')
print('=' * 88)
by_dept = defaultdict(list)
for r in rows: by_dept[r['dept_id']].append(r)
for dept_id in sorted(by_dept.keys(), key=lambda x: int(x) if x.isdigit() else 0):
    items = by_dept[dept_id]
    name = DEPTS.get(dept_id, '?UNKNOWN?')
    print()
    print(f'-- Dept {dept_id} {name}  ({len(items)} anomal{"y" if len(items)==1 else "ies"}) --')
    for r in sorted(items, key=lambda x: (x['class'], x['upc'])):
        print(fmt_item(r, f"[{r['label']}]"))

# View 2 — Grouped by ISSUE TYPE (each item shows department)
print()
print('=' * 88)
print('VIEW 2 — GROUPED BY ISSUE TYPE (item line includes the department)')
print('=' * 88)
by_class = defaultdict(list)
for r in rows: by_class[r['class']].append(r)
for cls in ('SHORT','BADCHK','RW','COUPON5','COUPON9','NDC','INSTORE','UNUSABLE'):
    items = by_class.get(cls, [])
    if not items: continue
    print()
    print(f'-- {ISSUE_LABELS[cls]}  ({len(items)} item{"" if len(items)==1 else "s"}) --')
    for r in sorted(items, key=lambda x: (x['dept_id'], x['upc'])):
        print(fmt_item(r, f"[Dept {r['dept_id']} {r['dept_name']}]"))
