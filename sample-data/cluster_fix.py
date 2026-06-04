#!/usr/bin/env python3
"""Cluster-level repair plan for flagged bad-barcode PLUs.

Corruption anatomy (confirmed against catalog):
  real UPC-A      = body11 + check                  e.g. 011210000018  (Tabasco)
  mode-A debris   = 00 + body11 + '0'               check digit zeroed   -> BADCHK
  mode-B debris   = 000 + body11 (no check)         right-aligned, short -> SHORT
  shift garbage   = 00 + (body11<<1) + recomputed   passes mod10 but unscannable
                    (these are the NS2/3/4/5 "coupon/NDC" entries and some pseudo-VALIDs)

Repair:
  candA = 00 + u[2:13] + chk(u[2:13])   fix-the-check (mode A)
  candB = 00 + u[3:14] + chk(u[3:14])   append-check  (mode B)  [also reproduces shift garbage]
  plausible(body11): body[0]=='0' and body[1]!='0'  (NS-0 retail UPC, <=1 leading zero)
"""
import re, csv, difflib
from collections import defaultdict

XML = "backup-2026-06-03T09-23-38.xml"
HE  = "likely-handentered-2026-06-03.txt"

# ---------- parse ----------
raw = open(XML, encoding="utf-8").read()
plus = []
for blk in re.findall(r"<domain:PLU>(.*?)</domain:PLU>", raw, re.S):
    def g(tag):
        m = re.search(rf"<{tag}>(.*?)</{tag}>", blk, re.S)
        return m.group(1).strip() if m else ""
    plus.append({"upc": g("upc"), "desc": g("description"),
                 "dept": g("department"), "price": g("price")})

def upca_check(body11):
    s = sum(int(c) * (3 if i % 2 == 0 else 1) for i, c in enumerate(body11))
    return str((10 - s % 10) % 10)

def gtin_check_ok(u):
    s = sum(int(c) * (3 if (12 - i) % 2 == 0 else 1) for i, c in enumerate(u[:13]))
    return (10 - s % 10) % 10 == int(u[13])

def classify(u):
    sig = len(u.lstrip("0")) or 1
    if sig < 11: return "SHORT"
    ns = u[2] if u[:2] == "00" else u[0]
    if ns in "23459" and u[:2] == "00": return f"NS{ns}"
    if u[:2] != "00": return "NS9" if u[0] == "9" else "ODD"
    if not gtin_check_ok(u): return "BADCHK"
    return "VALID"

for p in plus:
    p["cls"] = classify(p["upc"])
    p["valid"] = p["cls"] == "VALID"

def norm(d):
    d = re.sub(r"\(.*?\)", " ", d.upper())
    d = re.sub(r"[^A-Z0-9]+", " ", d)
    return re.sub(r"\s+", " ", d).strip()

for p in plus:
    p["n"] = norm(p["desc"])

upc2 = {p["upc"]: p for p in plus}
by_name = defaultdict(list)
for p in plus:
    by_name[p["n"]].append(p)

def pf(p):
    try: return float(p["price"])
    except: return None

def price_close(a, b, tol):
    x, y = pf(a), pf(b)
    if x is None or y is None: return False
    return x == y or abs(x - y) / max(x, y) <= tol

def plausible(body11):
    return body11[0] == "0" and body11[1] != "0"

def candA(u): b = u[2:13]; return "00" + b + upca_check(b)
def candB(u): b = u[3:14]; return "00" + b + upca_check(b)

# ---------- flagged HIGH+MEDIUM ----------
text = open(HE, encoding="utf-8").read()
flagged = set(re.findall(r"^\s*(\d{14})\b", text[:text.find("LOW CONFIDENCE")], re.M))

JUNK = {"99999999999998", "99999999999999", "00000000099998", "00000000987332"}
work = []
seen_work = set()
for u in sorted(flagged):
    e = upc2.get(u)
    if not e or e["valid"] or u in JUNK or u in seen_work:
        continue
    work.append(e); seen_work.add(u)
    # pull in exact-name bad siblings (same import mess, even if not flagged)
    for s in by_name[e["n"]]:
        if not s["valid"] and s["upc"] not in seen_work and s["upc"] not in JUNK:
            work.append(s); seen_work.add(s["upc"])

# ---------- per-bad keep derivation ----------
def real_via_barcode(e):
    """existing, VALID, plausible candidate derived from e's own digits"""
    u = e["upc"]
    for cand in (candA(u), candB(u)):
        q = upc2.get(cand)
        if q and q["upc"] != u and q["valid"] and plausible(cand[2:13]):
            return q, "BARCODE-DERIVED"
    # substring rescue (lost leading digit, e.g. Boulder 708163...)
    sig = u.lstrip("0")
    if len(sig) >= 9:
        for q in plus:
            if q["valid"] and sig in q["upc"].lstrip("0") and q["upc"] != u:
                return q, "BARCODE-SUBSTRING"
    return None, None

def brand_candidates(e):
    """VALID entries whose name shares a fuzzy brand token (>=4 chars) & price within 15%"""
    toks = [t for t in e["n"].split() if len(t) >= 4 and t.isalpha()]
    out = []
    if not toks: return out
    brand = toks[0]
    for q in plus:
        if not q["valid"] or q["upc"] == e["upc"]: continue
        for qt in q["n"].split():
            if len(qt) >= 4 and difflib.SequenceMatcher(None, brand, qt).ratio() >= 0.8:
                if price_close(e, q, 0.15):
                    out.append(q)
                break
    return out

safe_rows, review_rows, fix_rows = [], [], []
consumed = set()

def co_bads(e):
    """the shift/append derivative of e if it exists as another bad entry (same product debris)"""
    g = upc2.get(candB(e["upc"]))
    return [g] if g and not g["valid"] and g["upc"] != e["upc"] else []

for e in work:
    if e["upc"] in consumed:
        continue
    consumed.add(e["upc"])
    keep, how = real_via_barcode(e)

    # 1) keep derived from e's own digits -> conclusive
    if keep:
        note = ("price differs but barcode-conclusive"
                if not price_close(e, keep, 0.06) else "")
        safe_rows.append((e, keep, how, note))
        # pull in shift-recompute debris (NSx siblings / pseudo-VALID garbage)
        g = upc2.get(candB(e["upc"]))
        if g and g["upc"] not in (e["upc"], keep["upc"]) and g["upc"] not in consumed:
            consumed.add(g["upc"])
            reason = "SHIFT-GARBAGE" if g["valid"] else "DEBRIS-SIBLING"
            safe_rows.append((g, keep, reason, "= shift-recompute of " + e["upc"]))
        continue

    cb = co_bads(e)
    for g in cb:
        consumed.add(g["upc"])

    # 2) exact-name + price twin (e.g. ALT-xls candy, large-bag Funyuns)
    twins = [q for q in by_name[e["n"]] if q["valid"]]
    pm = [q for q in twins if price_close(e, q, 0.06)]
    if pm:
        for x in [e] + cb:
            safe_rows.append((x, pm[0], "EXACT-NAME+PRICE", ""))
        continue
    if twins:   # exact name but price off -> size collision
        review_rows.append(([e] + cb, twins, "exact name, price differs (size collision?)"))
        continue

    # 3) near-name brand candidates -> review
    cands = list({q["upc"]: q for q in brand_candidates(e)}.values())
    if cands:
        review_rows.append(([e] + cb, cands, "no barcode-derived twin; brand/price candidates listed"))
        continue

    # 4) fix: compute corrected barcode from e's digits
    u = e["upc"]; fixes = []
    a, b = candA(u), candB(u)
    if plausible(a[2:13]) and a not in upc2: fixes.append((a, "fix check digit (mode A)"))
    if plausible(b[2:13]) and b not in upc2: fixes.append((b, "append check digit (mode B)"))
    if fixes:
        fix_rows.append(([e] + cb, fixes[0][0], fixes[0][1], "CONFIDENT"))
    else:
        fix_rows.append(([e] + cb, "", "digits lost/typo - cannot reconstruct", "RE-SCAN"))

# ---------- write ----------
with open("delete_safe.csv", "w", newline="") as f:
    w = csv.writer(f)
    w.writerow(["dept","description","delete_upc","cls","price","reason","keep_upc","keep_desc","keep_price","note"])
    for e, k, reason, note in sorted(safe_rows, key=lambda r: (r[0]["dept"], r[0]["n"])):
        w.writerow([e["dept"], e["desc"], e["upc"], e["cls"], e["price"],
                    reason, k["upc"], k["desc"], k["price"], note])

with open("delete_review.csv", "w", newline="") as f:
    w = csv.writer(f)
    w.writerow(["dept","description","bad_upcs","price","candidates","note"])
    for es, cands, note in review_rows:
        e = es[0]
        w.writerow([e["dept"], e["desc"], ";".join(x["upc"] for x in es), e["price"],
                    " | ".join(f"{q['upc']} {q['desc']!r} ${q['price']}" for q in cands), note])

with open("fix_worklist.csv", "w", newline="") as f:
    w = csv.writer(f)
    w.writerow(["dept","description","bad_upcs","price","corrected_gtin14","derivation","confidence"])
    for es, fix, deriv, conf in fix_rows:
        e = es[0]
        w.writerow([e["dept"], e["desc"], ";".join(x["upc"] for x in es), e["price"],
                    fix, deriv, conf])

print(f"bad entries processed: {len(work)}")
print(f"SAFE deletes : {len(safe_rows)} rows -> delete_safe.csv")
print(f"REVIEW       : {len(review_rows)} clusters -> delete_review.csv")
print(f"FIX          : {len(fix_rows)} clusters -> fix_worklist.csv")
print("\n--- FIX WORKLIST ---")
for es, fix, deriv, conf in fix_rows:
    e = es[0]
    print(f"  [{e['dept']}] {e['desc']!r} ${e['price']}")
    for x in es: print(f"      bad: {x['upc']} ({x['cls']})")
    print(f"      -> {fix or '(re-scan product)'}   {deriv}   [{conf}]")
print("\n--- REVIEW ---")
for es, cands, note in review_rows:
    e = es[0]
    print(f"  [{e['dept']}] {e['desc']!r} ${e['price']}  ({note})")
    for x in es: print(f"      bad: {x['upc']} ({x['cls']})")
    for q in cands: print(f"      cand: {q['upc']} {q['desc']!r} ${q['price']}")
