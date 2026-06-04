#!/usr/bin/env python3
"""Name-duplicate analysis: do flagged bad-barcode PLUs have a clean twin?"""
import re, sys, difflib
from collections import defaultdict

XML = "backup-2026-06-03T09-23-38.xml"
HE  = "likely-handentered-2026-06-03.txt"

# ---------- parse XML ----------
raw = open(XML, encoding="utf-8").read()
plus = []
for blk in re.findall(r"<domain:PLU>(.*?)</domain:PLU>", raw, re.S):
    def g(tag):
        m = re.search(rf"<{tag}>(.*?)</{tag}>", blk, re.S)
        return m.group(1).strip() if m else ""
    plus.append({
        "upc": g("upc"), "mod": g("upcModifier"),
        "desc": g("description"), "dept": g("department"),
        "price": g("price"),
    })

# ---------- barcode classification ----------
def gtin_check_ok(u):
    if len(u) != 14 or not u.isdigit():
        return False
    s = 0
    for i, ch in enumerate(u[:13]):
        w = 3 if (12 - i) % 2 == 0 else 1
        s += int(ch) * w
    return (10 - s % 10) % 10 == int(u[13])

def sig_digits(u):
    return len(u.lstrip("0")) or 1

def classify(u):
    if not (len(u) == 14 and u.isdigit()):
        return "MALFORMED"
    sig = sig_digits(u)
    if sig < 11:
        return "SHORT"          # too few digits -> hand-entered stub
    ns = u[2] if u[:2] == "00" else u[0]
    if ns in "2345" or (u[:2] == "00" and u[2] == "9"):
        return f"NS{ns}"        # random-weight / NDC / in-store / coupon prefix
    if not gtin_check_ok(u):
        return "BADCHK"
    return "VALID"

for p in plus:
    p["cls"] = classify(p["upc"])
    p["clean"] = p["cls"] == "VALID"

# ---------- name normalization ----------
def norm(d):
    d = d.upper()
    d = re.sub(r"\(.*?\)", " ", d)          # drop (ALT-xls) etc
    d = re.sub(r"\bZ?NOT SOLD\b", " ", d)
    d = re.sub(r"^Z+\b", " ", d)
    d = re.sub(r"[^A-Z0-9]+", " ", d)
    return re.sub(r"\s+", " ", d).strip()

for p in plus:
    p["n"] = norm(p["desc"])

# ---------- flagged set (HIGH+MEDIUM only) ----------
flagged = set()
intext = open(HE, encoding="utf-8").read()
cut = intext.find("LOW CONFIDENCE")
hm = intext[:cut]
for m in re.finditer(r"^\s*(\d{14})\b", hm, re.M):
    flagged.add(m.group(1))

# ---------- group by exact normalized name ----------
by_name = defaultdict(list)
for p in plus:
    by_name[p["n"]].append(p)

upc2plu = {p["upc"]: p for p in plus}

def fuzzy_twins(p, thresh=0.82):
    """valid-barcode PLUs whose normalized name is close but not exactly equal."""
    out = []
    for q in plus:
        if q is p or q["n"] == p["n"]:
            continue
        if not q["clean"]:
            continue
        r = difflib.SequenceMatcher(None, p["n"], q["n"]).ratio()
        if r >= thresh:
            out.append((round(r, 2), q))
    return sorted(out, reverse=True, key=lambda x: x[0])

# ---------- price helper ----------
def pf(p):
    try: return float(p["price"])
    except: return None

def price_match(a, b, tol=0.06):
    x, y = pf(a), pf(b)
    if x is None or y is None: return False
    if x == y: return True
    hi = max(x, y)
    return hi > 0 and abs(x - y) / hi <= tol

# ---------- verdict per flagged item (only BAD-own-barcode items are import debris) ----------
rows = []
for u in sorted(flagged):
    p = upc2plu.get(u)
    if not p:
        rows.append((u, None, "MISSING-IN-XML", [], [])); continue

    if p["clean"]:
        # own barcode already scans fine -> not botched-import debris; flagged only by sequence heuristic
        rows.append((u, p, "OWN BARCODE OK (not debris)", [], [])); continue

    exact = [q for q in by_name[p["n"]] if q["upc"] != u]
    clean_exact = [q for q in exact if q["clean"]]
    bad_exact   = [q for q in exact if not q["clean"]]
    # near-name clean twins, corroborated by matching price
    near = [q for r, q in fuzzy_twins(p) if price_match(p, q)] if not clean_exact else []

    twins = clean_exact + near
    if twins:
        verdict = "DUPLICATE -> delete bad (clean twin exists)"
    elif bad_exact:
        verdict = "NEEDS FIX (twin exists but ALL bad)"
    else:
        verdict = "NEEDS FIX (unique - only record)"
    rows.append((u, p, verdict, twins, bad_exact))

# ---------- report ----------
order = ["DUPLICATE -> delete bad (clean twin exists)",
         "NEEDS FIX (twin exists but ALL bad)",
         "NEEDS FIX (unique - only record)",
         "OWN BARCODE OK (not debris)", "MISSING-IN-XML"]
buckets = defaultdict(list)
for r in rows:
    buckets[r[2]].append(r)

print(f"Flagged (HIGH+MEDIUM) items analyzed: {len(rows)}\n")
for v in order:
    print(f"  {len(buckets[v]):3d}  {v}")
print("=" * 90)

for v in order:
    if not buckets[v]:
        continue
    print(f"\n########## {v}  ({len(buckets[v])}) ##########")
    # group by department
    bydept = defaultdict(list)
    for r in buckets[v]:
        bydept[r[1]["dept"] if r[1] else "?"].append(r)
    for dept in sorted(bydept):
        print(f"\n  -- Dept {dept} --")
        for u, p, verdict, twins, bad_sibs in bydept[dept]:
            print(f"    {p['desc']!r}")
            print(f"        bad : {u}  {p['cls']:7s} ${p['price']}")
            for q in twins:
                print(f"        KEEP: {q['upc']}  {q['cls']:7s} ${q['price']}  -> {q['desc']!r}")
            for q in bad_sibs:
                print(f"        (also bad): {q['upc']}  {q['cls']:7s} ${q['price']}  -> {q['desc']!r}")

# ---------- CSV artifacts ----------
import csv
with open("delete_candidates.csv","w",newline="") as f:
    w=csv.writer(f); w.writerow(["dept","description","delete_upc","delete_cls","price","keep_upc","keep_desc","keep_price"])
    for u,p,v,twins,bad in rows:
        if v.startswith("DUPLICATE"):
            q=twins[0]
            w.writerow([p["dept"],p["desc"],u,p["cls"],p["price"],q["upc"],q["desc"],q["price"]])
with open("needs_fix.csv","w",newline="") as f:
    w=csv.writer(f); w.writerow(["dept","description","bad_upc","bad_cls","price","other_bad_upc"])
    seen=set()
    for u,p,v,twins,bad in rows:
        if v.startswith("NEEDS FIX (twin"):
            key=tuple(sorted([u]+[b["upc"] for b in bad]))
            if key in seen: continue
            seen.add(key)
            w.writerow([p["dept"],p["desc"],u,p["cls"],p["price"],";".join(b["upc"] for b in bad)])
print("\nWROTE delete_candidates.csv, needs_fix.csv")
