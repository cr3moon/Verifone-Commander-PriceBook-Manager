# UPC handling — the AS-IS rule

**Authoritative summary. Read this before changing any code that touches a `<upc>` value.**

The full research, empirical-test results, and HAR-trace analysis live in the sibling
project `verifone-commander-import-export`:
- `docs/design/upc-normalization-and-confirmation.md` — the canonical pipeline spec.
- `docs/reference/Handling Shorter Barcodes — Empirical Review and Corrections (read after the research doc).md` — what we actually learned in production.
- `docs/reference/UPC (Barcode) PLU Field - Check Digit type & algorithm.md` — algorithm derivation.
- `docs/reference/Rename-Update_and_Delete_operations/ANALYSIS_update-delete-operations_findings.md` — HAR trace of the wire operations (rename, update, delete, Not Sold).

What follows is the load-bearing minimum.

## What `<upc>` is

In Verifone Commander the PLU `<upc>` field is a misnomer: it is the **literal scan
code** at the register — the value the barcode scanner emits — not "a number to be
canonicalized." Commander stores it right-aligned in a 14-wide field, so
`14100085530` and `00014100085530` refer to the **same record**. Leading zeros are
moot.

The Commander 14-digit PLU is a **GTIN-14**: `[leading zeros][≤13 data digits][1 check digit]`.

## Check digit algorithm — GS1 / GTIN-14 Mod-10

**NOT Luhn.** Empirically validated 93/93 against this customer's proven-sold codes
(`_work-output/PLU-AUDIT_2026-05-24/checkdigit_algorithm_test.py` in the sibling).
Implemented here in `Core/UpcUtilities.cs::Gtin14CheckDigit`.

Validate barcodes as **GTIN-14** (check over the full 14), not UPC-A on 12 — they
coincide for codes ≤12 digits but GTIN-14 is the authoritative form.

## The AS-IS rule (production-validated)

When emitting `<upc>`:

1. Pass the operator's value through **verbatim**. Zero-pad to 14 (preserves the
   value; Commander stores it right-aligned anyway).
2. **Never recompute, append, or alter a check digit.** A real scanned barcode
   already ends in its own check digit; appending a *second* one shifts the digits
   into a different 14-wide GTIN that no physical barcode emits → **un-scannable**.
   This is the "H2 corruption" that broke real items in production (2026-05-23);
   the operator manually re-entered 7 items AS-IS to fix the catalog.
3. **File / XML import does NOT validate.** Commander stores whatever you send;
   invalid check digits persist forever. The 156 invalid-GTIN codes already in this
   customer's catalog have never auto-corrected across any export. **Commander is
   not a safety net — validation must live in our app.**
4. **Never even *suggest* reformatting a `upc`.** Operator-feedback memory
   `upc-is-literal-scan-code`: normalize/pad/compute is OFF by default.

The validity of a check digit is **not** a scannability test: a valid UPC-A may be
the wrong (forced/computed) code that doesn't scan; an "invalid" code may be the
real scannable barcode. Only **physical scan / sales data** is authoritative.

## What this app does

- `Core/UpcUtilities.cs` — `ClassifyUpc`, `Normalize(ZeroPadOnly|SmartCheckDigit)`,
  `IsValidGtin14`, `Gtin14CheckDigit`. **Safe by construction:** `Normalize` refuses
  to compute a check digit for ≥12-digit values, and `ComputePayloadGtin14` throws
  on the same condition — the H2 corruption is unreachable.
- **Edit page** — accepts 1–14 digit codes; on save, runs `ClassifyUpc` and shows a
  non-blocking advisory for risky number systems or a bad check digit. Stored
  AS-IS (zero-padded only).
- **Import page** — fixed-template CSV; per-row `ClassifyUpc` rejects truly unusable
  inputs (Empty / NonNumeric / TooLong) and surfaces risk / bad-check warnings.
  Stored AS-IS.
- **Items page** — read-only audit: each PLU's stored `upc` is classified; rows
  with a risk number system or a non-validating check digit are tagged
  "UPC issue" (filterable). **Never offers to change a code.** Short internal PLUs
  (≤5 significant digits) are recognized as legitimate, not flagged.

## Number-system (NS) risk flags

NS digit (index 2 of the 14-digit code) drives the risk classification:

| NS | Class | Action |
|----|-------|--------|
| 0, 1, 6, 7, 8 | normal product | accept |
| **2** | random / variable weight | warn — do not store as a fixed PLU |
| 3 | NDC / pharmacy | note |
| 4 | in-store code | note — not globally unique |
| **5, 9** | coupon | reject — not a sellable product UPC |

## Fixing non-scannable items — the operator workflow

When a stored `upc` won't scan (operator confirms by trying to scan the physical
item), the **only safe fix** is **replacement, not mutation**:

1. Create a **new** PLU with the correct scan code (operator types or scans it).
2. Set the **old** PLU's `<flags>` to include `<domain:flag sysid="2"/>` — the
   **Not Sold** flag (`Core/Models/PluFlags.cs::NotSold`). The old PLU stays in the
   catalog but won't ring up; the fix is reversible.

This is exactly what the operator did manually in production, and it's why the
Items page surfaces Not Sold as a first-class state (with a filter toggle). The
deactivate-and-recreate workflow itself is not yet built into the UI; today the
operator performs both steps from ConfigClient.
