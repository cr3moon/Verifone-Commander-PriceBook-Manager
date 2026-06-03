#!/usr/bin/env python3
"""
Hunt for hand-entered UPCs — including ones that PASS the GTIN-14 audit
because ConfigClient computed the check digit when the operator typed the
significant digits.

Confidence buckets:

HIGH (almost certainly hand-entered, not derived from real barcodes):
  H1. Significant-digits < 10 — real UPC-As have 11 sig digits; anything
      shorter cannot have come from a scan.
  H2. Tight-gap runs (gap == 1 between adjacent UPCs in the same dept) —
      real consecutive manufacturer SKUs almost never land at +1 increments.

MEDIUM (suspect — eyeball for context):
  M1. Tight cluster (3+ items, gap <= 5).
  M2. Two items, gap <= 30, sharing the first word of description.

LOW (informational; many of these are legitimate manufacturer SKU sequences):
  L1. Looser clusters (3+ items, 5 < gap <= 100).
"""
import xml.etree.ElementTree as ET
import re
import sys
from collections import defaultdict

NS = '{urn:vfi-sapphire:np.domain.2001-07-01}'

DEPTS = {
    '1000': 'GROCERY',          '1001': 'BEVERAGE NA',     '1002': 'HEALTH AND BEAUTY',
    '1003': 'HOUSEHOLD',        '1004': 'AUTOMOTIVE',      '1005': 'APPAREL',
    '1006': 'FISHING APPAREL',  '1007': 'FLIES AND SUPPLIES', '1008': 'JEWELRY',
    '1009': 'KIDS CORNER',      '1010': 'SOUVENIRS',       '1011': 'ART',
    '1012': 'CANDY',            '1013': 'SNACKS',          '1014': 'FUEL',
    '6541': 'Lottery Sales Winner', '9995': 'OPEN MERCHANDISE',
    '9996': 'TEST B DEPT',      '9997': 'TEST C DEPT',     '9998': 'MANUAL FUEL DEPT',
}

LOW_SIG = 10
MID_GAP = 5
LOOSE_GAP = 100

tree = ET.parse(sys.argv[1])
items = []
for plu in tree.getroot().findall(NS + 'PLU'):
    upc = (plu.findtext('upc') or '').strip()
    if not upc.isdigit(): continue
    desc = (plu.findtext('description') or '').strip()
    dept_id = (plu.findtext('department') or '').strip()
    flags_el = plu.find('flags')
    flag_ids = set()
    if flags_el is not None:
        for f in flags_el.findall(NS + 'flag'):
            sid = f.get('sysid')
            if sid: flag_ids.add(sid)
    items.append({
        'upc': upc, 'val': int(upc),
        'sig': len(upc.lstrip('0')) if upc.lstrip('0') else 0,
        'desc': desc, 'dept_id': dept_id,
        'dept_name': DEPTS.get(dept_id, '?'),
        'not_sold': '2' in flag_ids,
        'confidence': None, 'reasons': [],
    })

by_dept = defaultdict(list)
for it in items: by_dept[it['dept_id']].append(it)
for d in by_dept: by_dept[d].sort(key=lambda x: x['val'])

def first_word(s):
    s = s.strip()
    if not s: return ''
    return re.split(r'[\s\-\.,/_]+', s, maxsplit=1)[0].upper()

# H1
for it in items:
    if it['sig'] < LOW_SIG:
        it['confidence'] = 'HIGH'
        it['reasons'].append(f'only {it["sig"]} significant digits (real UPC-As have 11+)')

def runs(lst, max_gap):
    if not lst: return []
    out, cur = [], [lst[0]]
    for x in lst[1:]:
        if x['val'] - cur[-1]['val'] <= max_gap:
            cur.append(x)
        else:
            if len(cur) >= 2: out.append(cur)
            cur = [x]
    if len(cur) >= 2: out.append(cur)
    return out

for d, lst in by_dept.items():
    # H2: pair with gap == 1
    for i in range(len(lst) - 1):
        if lst[i+1]['val'] - lst[i]['val'] == 1:
            for x in (lst[i], lst[i+1]):
                if x['confidence'] != 'HIGH': x['confidence'] = 'HIGH'
                x['reasons'].append('adjacent UPC at +1 (gap=1 sequence)')

    # M1: tight cluster 3+
    for run in runs(lst, MID_GAP):
        if len(run) >= 3:
            gaps = [run[i+1]['val'] - run[i]['val'] for i in range(len(run)-1)]
            for x in run:
                if x['confidence'] is None: x['confidence'] = 'MEDIUM'
                if x['confidence'] != 'HIGH':
                    x['reasons'].append(f'tight cluster of {len(run)} (gaps {gaps})')

    # M2: pair with shared first word
    for run in runs(lst, 30):
        if len(run) == 2:
            fw0, fw1 = first_word(run[0]['desc']), first_word(run[1]['desc'])
            if fw0 and fw0 == fw1:
                for x in run:
                    if x['confidence'] is None: x['confidence'] = 'MEDIUM'
                    if x['confidence'] != 'HIGH':
                        x['reasons'].append(f"pair with shared description prefix '{fw0}'")

    # L1: looser cluster
    for run in runs(lst, LOOSE_GAP):
        if len(run) >= 3:
            gaps = [run[i+1]['val'] - run[i]['val'] for i in range(len(run)-1)]
            if min(gaps) > MID_GAP:
                for x in run:
                    if x['confidence'] is None:
                        x['confidence'] = 'LOW'
                        x['reasons'].append(f'looser cluster of {len(run)} (gaps {gaps})')

flagged = [x for x in items if x['confidence']]
print('=' * 88)
print('SEQUENTIAL / PATTERN AUDIT — likely hand-entered UPCs')
print('=' * 88)
counts = defaultdict(int)
for x in flagged: counts[x['confidence']] += 1
for c in ('HIGH','MEDIUM','LOW'):
    if counts[c]: print(f'  {c}: {counts[c]}')
print(f'  Total flagged: {len(flagged)}  /  Total PLUs analyzed: {len(items)}')

for conf in ('HIGH','MEDIUM','LOW'):
    bucket = [x for x in flagged if x['confidence'] == conf]
    if not bucket: continue
    title = {
        'HIGH':   'HIGH CONFIDENCE — almost certainly hand-entered',
        'MEDIUM': 'MEDIUM CONFIDENCE — eyeball for context',
        'LOW':    'LOW CONFIDENCE — may be legitimate manufacturer SKU runs',
    }[conf]
    print()
    print('=' * 88); print(title); print('=' * 88)
    by_d = defaultdict(list)
    for x in bucket: by_d[x['dept_id']].append(x)
    for d in sorted(by_d.keys(), key=lambda x: int(x) if x.isdigit() else 0):
        rows = sorted(by_d[d], key=lambda x: x['val'])
        print(f'\n-- Dept {d} {DEPTS.get(d, "?")}  ({len(rows)} item(s)) --')
        for x in rows:
            ns = '  [NOT SOLD]' if x['not_sold'] else ''
            print(f"   {x['upc']}  sig={x['sig']:2}  {x['desc'][:46]:46}{ns}")
            for r in x['reasons'][:2]:
                print(f"      · {r}")
