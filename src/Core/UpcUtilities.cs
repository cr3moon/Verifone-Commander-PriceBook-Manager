// -----------------------------------------------------------------------
// <copyright file="UpcUtilities.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.Core
{
    using System;
    using System.Linq;

    // UPC / GTIN normalization helpers. Ported from the sibling import/export tool;
    // this is the authority on how a raw scan/CSV code maps to what Commander stores.
    //
    // Background: Commander stores PLU codes as 14-digit zero-padded GTIN-14
    // strings. The canonical retail barcode is GTIN-12 (UPC-A) where the 12th
    // digit is a Mod-10 check digit computed from the first 11. A back-office
    // CSV will commonly arrive with the value formatted in any of these ways:
    //
    //   14-digit GTIN-14 (correct)                       — use as-is
    //   13-digit EAN-13                                  — pad with 1 leading zero
    //   12-digit UPC-A (already includes check digit)    — pad with 2 leading zeros
    //   11-digit "stem" (no check digit)                 — compute check digit, pad
    //    1-10 digit value (short stem)                   — left-pad stem to 11,
    //                                                       compute check digit, pad
    //
    // The two strategies offered to the operator:
    //
    //   ZeroPadOnly       — naive left-pad to 14. Preserves the input bytes
    //                       exactly. Produces a value that Commander will accept
    //                       byte-for-byte but whose check digit will not validate
    //                       against scanned UPC-A barcodes for stems shorter than
    //                       12 digits.
    //   SmartCheckDigit   — interpret the input length per GS1 conventions and
    //                       compute the check digit when missing. This is what
    //                       the upstream UPC-lookup analysis recommended.
    public static class UpcUtilities
    {
        public enum NormalizationStrategy
        {
            ZeroPadOnly,
            SmartCheckDigit,
        }

        // Risk class derived from the UPC-A number-system digit. These do not affect
        // check-digit validity; they flag values that should not be stored as an
        // ordinary, fixed product PLU.
        public enum UpcRisk
        {
            None,
            RandomWeight,
            Coupon,
            Ndc,
            InStore,
        }

        // Outcome of classifying one raw CSV upc value (per the forward-import
        // pipeline §8.1 of the empirical reference doc).
        public enum UpcClass
        {
            Empty,            // blank                                 -> reject
            NonNumeric,       // contains non-digits                   -> reject
            TooLong,          // > 14 digits                           -> reject
            ShortNonBarcode,  // <= 5 digits (produce PLU / internal)  -> exclude or payload mode
            CompleteValid,    // zfill(14) is a valid GTIN-14          -> standardize (zero-pad), Interpretation A
            AmbiguousInvalid, // zfill(14) check invalid               -> quarantine (exclude / payload / fix)
        }

        public static NormalizationResult Normalize(string input, NormalizationStrategy strategy)
        {
            var r = new NormalizationResult { Original = input ?? string.Empty };

            if (string.IsNullOrWhiteSpace(input))
            {
                r.Notes = "blank";
                return r;
            }

            var trimmed = input.Trim();

            if (!trimmed.All(char.IsDigit))
            {
                r.Notes = "non-digit characters present";
                return r;
            }

            if (strategy == NormalizationStrategy.ZeroPadOnly)
            {
                if (trimmed.Length > 14)
                {
                    r.Notes = "too long (>14 digits)";
                    return r;
                }

                if (trimmed.Length == 14)
                {
                    r.Normalized = trimmed;
                    r.Notes = "already 14 digits";
                    r.Success = true;
                    return r;
                }

                r.Normalized = trimmed.PadLeft(14, '0');
                r.Notes = "padded with leading zeros to 14 digits";
                r.Success = true;
                return r;
            }

            // SmartCheckDigit — classifier-driven and H2-safe. A complete barcode (valid
            // GTIN-14 after zero-padding, including a leading-zero-stripped UPC-A/EAN-13) is
            // ONLY zero-padded; its existing check digit is preserved, never recomputed. A
            // check digit is computed+appended ONLY for a genuinely short value (<=11 digits
            // with no valid check) and NEVER for a 12/13/14-digit value — so appending a
            // digit onto a complete barcode (the "H2" corruption that broke scanning) is
            // unreachable here. See ClassifyUpc / ComputePayloadGtin14.
            if (trimmed.Length > 14)
            {
                r.Notes = "too long (>14 digits)";
                return r;
            }

            var cls = ClassifyUpc(trimmed);

            if (cls.Class == UpcClass.CompleteValid)
            {
                r.Normalized = cls.Candidate14;
                r.Notes = cls.NeedsPadding
                    ? "complete barcode → zero-padded to 14 (existing check digit preserved, not recomputed)"
                    : "already a valid 14-digit GTIN-14";
                r.Success = true;
            }
            else if (cls.PayloadEligible)
            {
                r.Normalized = ComputePayloadGtin14(trimmed);
                r.Notes = "short payload (≤11 digits, no valid check digit) → computed GTIN-14 check digit appended";
                r.Success = true;
            }
            else
            {
                r.Notes = "cannot safely normalize (12–14 digits with an invalid check digit, or not a barcode) — verify by scanning the product";
            }

            return r;
        }

        // UPC-A / GTIN check-digit calculation, Mod-10 (GS1 standard).
        // Input: 11-digit stem string (the value before the check digit).
        public static int ComputeUpcACheckDigit(string digits11)
        {
            if (digits11 == null || digits11.Length != 11)
            {
                throw new ArgumentException("Stem must be exactly 11 digits, got " + (digits11?.Length ?? 0));
            }

            int sumOdd = 0, sumEven = 0;
            for (int i = 0; i < 11; i++)
            {
                int d = digits11[i] - '0';
                if (d < 0 || d > 9)
                {
                    throw new ArgumentException("Non-digit at position " + i);
                }

                if (i % 2 == 0)
                {
                    sumOdd += d;   // 1-indexed odd positions (i = 0,2,4,...) get the x3 weight
                }
                else
                {
                    sumEven += d;
                }
            }

            int total = (sumOdd * 3) + sumEven;
            int mod = total % 10;
            return mod == 0 ? 0 : 10 - mod;
        }

        // Verify a 12-digit UPC-A's check digit.
        public static bool IsValidUpcA(string digits12)
        {
            if (digits12 == null || digits12.Length != 12)
            {
                return false;
            }

            if (!digits12.All(char.IsDigit))
            {
                return false;
            }

            var stem = digits12.Substring(0, 11);
            var providedCheck = digits12[11] - '0';
            return ComputeUpcACheckDigit(stem) == providedCheck;
        }

        // ------------------------------------------------------------- GTIN-14
        //
        // Commander stores PLU codes as 14-digit GTIN-14: 13 payload digits + 1
        // GS1 Mod-10 check digit (the LAST digit). This is the algorithm the live
        // catalog was empirically proven to use (93/93 sold codes validate, and
        // Verifone's own examples — 10012345678902, 345->...3452 — match). It is
        // NOT Luhn. See memory upc-converter-normalizer-spec and
        // docs/design/upc-normalization-and-confirmation.md.
        //
        // Weighting: number the 13 payload digits from the RIGHT; the rightmost
        // gets x3, then alternating x1/x3. Equivalently, indexing the 13-wide
        // (left-zero-padded) payload from the left, even indices get x3 (since 13
        // is odd, both ends get x3).

        // GS1 / GTIN-14 Mod-10 check digit for the 13 payload digits. The payload is
        // left-zero-padded to 13 first, so callers may pass a shorter stem.
        public static int Gtin14CheckDigit(string first13)
        {
            if (first13 == null)
            {
                throw new ArgumentNullException(nameof(first13));
            }

            var p = first13.Trim();
            if (p.Length > 13 || !p.All(char.IsDigit))
            {
                throw new ArgumentException("Expected up to 13 numeric payload digits; got \"" + first13 + "\".");
            }

            p = p.PadLeft(13, '0');
            int sum = 0;
            for (int i = 0; i < 13; i++)
            {
                int d = p[i] - '0';
                sum += (i % 2 == 0) ? d * 3 : d;   // even index (incl. rightmost) = x3
            }

            return (10 - (sum % 10)) % 10;
        }

        // True when a 14-digit string is a valid GTIN-14 (last digit is the correct
        // GS1 Mod-10 check digit over the first 13). Leading zeros are part of the
        // value; this is the leading-zero-invariant test used to recognise a
        // complete barcode after padding.
        public static bool IsValidGtin14(string code14)
        {
            if (code14 == null || code14.Length != 14)
            {
                return false;
            }

            if (!code14.All(char.IsDigit))
            {
                return false;
            }

            return Gtin14CheckDigit(code14.Substring(0, 13)) == (code14[13] - '0');
        }

        // Classify one raw CSV upc value. Pure/side-effect-free so it can be used by
        // both the per-row analysis and the confirmation dialog. Does NOT mutate the
        // value — the caller decides what to emit based on Class.
        public static UpcClassification ClassifyUpc(string raw)
        {
            var c = new UpcClassification { Original = raw ?? string.Empty, Risk = UpcRisk.None, NumberSystemDigit = string.Empty };
            var t = (raw ?? string.Empty).Trim();

            if (t.Length == 0)
            {
                c.Class = UpcClass.Empty;
                c.Note = "Blank UPC — cannot be emitted.";
                return c;
            }

            if (!t.All(char.IsDigit))
            {
                c.Class = UpcClass.NonNumeric;
                c.Note = "Contains non-digit characters — cannot be emitted.";
                return c;
            }

            if (t.Length > 14)
            {
                c.Class = UpcClass.TooLong;
                c.Note = t.Length + " digits — exceeds the 14-digit maximum; cannot be emitted.";
                return c;
            }

            c.Candidate14 = t.PadLeft(14, '0');
            c.NeedsPadding = !string.Equals(c.Candidate14, t, StringComparison.Ordinal);

            if (t.Length <= 5)
            {
                c.Class = UpcClass.ShortNonBarcode;
                c.Note = t.Length + "-digit value — too short for a retail barcode (produce PLU or internal code). " +
                         "Exclude, or treat as a payload only if you intend an internal PLU.";
                return c;
            }

            // Number-system digit (UPC-A position) for risk flagging. After padding a
            // <=12-digit UPC-A to 14, the NS digit sits at index 2. Soft flag only.
            char ns = c.Candidate14[2];
            c.NumberSystemDigit = ns.ToString();
            c.Risk = RiskFromNumberSystem(ns);

            if (IsValidGtin14(c.Candidate14))
            {
                c.Class = UpcClass.CompleteValid;
                c.Note = c.NeedsPadding
                    ? "Complete barcode — zero-pad to 14 (its existing check digit is preserved)."
                    : "Already a valid 14-digit GTIN-14.";
            }
            else
            {
                c.Class = UpcClass.AmbiguousInvalid;
                c.Note = "Not a valid GTIN-14 after zero-padding — ambiguous: a payload missing its check digit, " +
                         "a short internal PLU, or a wrong/corrupt source value. Verify by scanning the product.";
            }

            var rn = RiskNote(c.Risk);
            if (rn != null)
            {
                c.Note += "  " + rn;
            }

            return c;
        }

        // Short status label for UI display (column tag / inline indicator). Returns
        // null when the code is fine (CompleteValid + no risk). Combines risk and a
        // non-validating check digit with " · ". The longer tooltip text comes from
        // RiskNote and the classifier's own Note.
        public static string GetIssueLabel(UpcClassification classification)
        {
            if (classification == null)
            {
                return null;
            }

            string riskLabel = null;
            switch (classification.Risk)
            {
                case UpcRisk.RandomWeight:
                    riskLabel = "Random-weight";
                    break;
                case UpcRisk.Coupon:
                    riskLabel = "Coupon (NS=" + classification.NumberSystemDigit + ")";
                    break;
                case UpcRisk.Ndc:
                    riskLabel = "NDC";
                    break;
                case UpcRisk.InStore:
                    riskLabel = "In-store";
                    break;
            }

            bool badCheck = classification.Class == UpcClass.AmbiguousInvalid;

            if (riskLabel == null && !badCheck)
            {
                return null;
            }

            if (riskLabel == null)
            {
                return "Bad check digit";
            }

            if (!badCheck)
            {
                return riskLabel;
            }

            return riskLabel + " · Bad check digit";
        }

        // Human-readable risk note (null when no risk). Phrasing matches the
        // confirmation dialog / run report.
        public static string RiskNote(UpcRisk r)
        {
            switch (r)
            {
                case UpcRisk.RandomWeight: return "WARNING: random-/variable-weight code (number system 2) — price-embedded; do NOT store as a fixed PLU.";
                case UpcRisk.Coupon: return "WARNING: coupon code (number system 5/9) — not a sellable product UPC.";
                case UpcRisk.Ndc: return "Note: NDC / pharmacy code (number system 3).";
                case UpcRisk.InStore: return "Note: in-store code (number system 4) — not globally unique.";
                default: return null;
            }
        }

        // Opt-in ONLY: treat the input as a checkless PAYLOAD — left-zero-pad to 13,
        // compute the GTIN-14 check digit, append it -> 14 digits. This is the single
        // place in the app that computes a check digit.
        //
        // Guard: refuses any value >= 12 digits. UPC-A (12), EAN-13 (13) and GTIN-14
        // (14) already carry their own check digit as the last digit; recomputing one
        // onto them treats that check digit as payload and produces a different,
        // non-scannable number (the "H2" corruption proven to break scanning).
        public static string ComputePayloadGtin14(string payload)
        {
            var t = (payload ?? string.Empty).Trim();
            if (t.Length == 0 || !t.All(char.IsDigit))
            {
                throw new ArgumentException("Payload must be all digits.");
            }

            if (t.Length >= 12)
            {
                throw new InvalidOperationException(
                    "Refusing to compute a check digit for a " + t.Length + "-digit value: UPC-A/EAN-13/GTIN-14 " +
                    "already carry their own check digit. Use it as-is (zero-pad only) — recomputing corrupts a complete barcode.");
            }

            var first13 = t.PadLeft(13, '0');
            int check = Gtin14CheckDigit(first13);
            return first13 + check.ToString();
        }

        private static UpcRisk RiskFromNumberSystem(char ns)
        {
            switch (ns)
            {
                case '2': return UpcRisk.RandomWeight;
                case '5':
                case '9': return UpcRisk.Coupon;
                case '3': return UpcRisk.Ndc;
                case '4': return UpcRisk.InStore;
                default: return UpcRisk.None; // 0,1,6,7,8
            }
        }

        public sealed class NormalizationResult
        {
            public string Original { get; set; }

            public string Normalized { get; set; }

            public string Notes { get; set; }

            public bool Success { get; set; }

            public override string ToString()
                => this.Success
                    ? this.Original + " → " + this.Normalized + "  (" + this.Notes + ")"
                    : this.Original + "  (skipped: " + this.Notes + ")";
        }

        public sealed class UpcClassification
        {
            public string Original { get; set; }

            // value left-zero-padded to 14 (null when not numeric/oversized)
            public string Candidate14 { get; set; }

            public UpcClass Class { get; set; }

            // Candidate14 differs from Original (i.e. not already a bare 14-digit value)
            public bool NeedsPadding { get; set; }

            // UPC-A number-system digit used for the risk flag (may be "")
            public string NumberSystemDigit { get; set; }

            public UpcRisk Risk { get; set; }

            // human-readable explanation for the dialog / report
            public string Note { get; set; }

            // Convenience: would this value be REJECTED outright (cannot be emitted at all)?
            public bool IsReject => this.Class == UpcClass.Empty || this.Class == UpcClass.NonNumeric || this.Class == UpcClass.TooLong;

            // Eligible for the opt-in "treat as payload -> compute check digit" action?
            // Only genuinely short values (<= 11 significant digits) may be payloads; a
            // 12/13/14-digit value already carries its own check digit.
            public bool PayloadEligible =>
                (this.Class == UpcClass.ShortNonBarcode || this.Class == UpcClass.AmbiguousInvalid)
                && !string.IsNullOrEmpty(this.Original) && this.Original.Trim().Length <= 11
                && this.Original.Trim().All(char.IsDigit);
        }
    }
}
