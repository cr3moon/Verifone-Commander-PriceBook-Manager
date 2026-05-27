// -----------------------------------------------------------------------
// <copyright file="UpcUtilitiesTests.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.Core.Tests
{
    using System;
    using Xunit;
    using static VerifoneCommander.PriceBookManager.Core.UpcUtilities;

    public class UpcUtilitiesTests
    {
        // ---- check-digit math --------------------------------------------------
        [Theory]
        [InlineData("03600029145", 2)] // UPC-A 036000291452
        [InlineData("01234567890", 5)] // UPC-A 012345678905
        public void ComputeUpcACheckDigit_KnownStems(string stem11, int expected)
        {
            Assert.Equal(expected, ComputeUpcACheckDigit(stem11));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("1234")] // too short
        [InlineData("123456789012")] // too long (12)
        public void ComputeUpcACheckDigit_BadLength_Throws(string stem)
        {
            Assert.Throws<ArgumentException>(() => ComputeUpcACheckDigit(stem));
        }

        [Theory]
        [InlineData("036000291452", true)]
        [InlineData("012345678905", true)]
        [InlineData("036000291451", false)] // wrong check digit
        [InlineData("03600029145", false)] // 11 digits, not UPC-A length
        [InlineData("0360002914a2", false)] // non-digit
        public void IsValidUpcA_Cases(string code, bool expected)
        {
            Assert.Equal(expected, IsValidUpcA(code));
        }

        [Theory]
        [InlineData("10012345678902", true)] // Verifone GTIN-14 example
        [InlineData("00000000003452", true)] // "345" payload (check digit 2), zero-padded to 14
        [InlineData("00036000291452", true)] // UPC-A 036000291452 zero-padded to 14
        [InlineData("10012345678903", false)] // wrong check digit
        public void IsValidGtin14_Cases(string code14, bool expected)
        {
            Assert.Equal(expected, IsValidGtin14(code14));
        }

        [Fact]
        public void Gtin14CheckDigit_ShortPayloadIsLeftPadded()
        {
            // "345" left-padded to 13 -> 0000000000345; documented to yield check digit 2.
            Assert.Equal(2, Gtin14CheckDigit("345"));
        }

        [Theory]
        [InlineData("12345678901234")] // > 13 payload
        [InlineData("12a")] // non-digit
        public void Gtin14CheckDigit_Invalid_Throws(string payload)
        {
            Assert.Throws<ArgumentException>(() => Gtin14CheckDigit(payload));
        }

        // ---- classification ----------------------------------------------------
        [Fact]
        public void ClassifyUpc_Blank_IsEmptyReject()
        {
            var c = ClassifyUpc("   ");
            Assert.Equal(UpcClass.Empty, c.Class);
            Assert.True(c.IsReject);
        }

        [Fact]
        public void ClassifyUpc_NonNumeric_IsReject()
        {
            var c = ClassifyUpc("12A45");
            Assert.Equal(UpcClass.NonNumeric, c.Class);
            Assert.True(c.IsReject);
        }

        [Fact]
        public void ClassifyUpc_TooLong_IsReject()
        {
            var c = ClassifyUpc("123456789012345"); // 15 digits
            Assert.Equal(UpcClass.TooLong, c.Class);
            Assert.True(c.IsReject);
        }

        [Fact]
        public void ClassifyUpc_ShortValue_IsShortNonBarcode_AndPayloadEligible()
        {
            var c = ClassifyUpc("345");
            Assert.Equal(UpcClass.ShortNonBarcode, c.Class);
            Assert.True(c.PayloadEligible);
        }

        [Fact]
        public void ClassifyUpc_CompleteUpcA_IsCompleteValid_PaddedNotRecomputed()
        {
            var c = ClassifyUpc("036000291452"); // valid UPC-A
            Assert.Equal(UpcClass.CompleteValid, c.Class);
            Assert.Equal("00036000291452", c.Candidate14);
            Assert.True(c.NeedsPadding);
            Assert.False(c.PayloadEligible); // 12 digits -> never a payload
        }

        [Fact]
        public void ClassifyUpc_InvalidCheck12Digit_IsAmbiguous_NotPayloadEligible()
        {
            var c = ClassifyUpc("036000291451"); // bad check digit, 12 digits
            Assert.Equal(UpcClass.AmbiguousInvalid, c.Class);
            Assert.False(c.PayloadEligible); // >= 12 digits is never payload-eligible
        }

        [Theory]
        [InlineData("200000000000", UpcRisk.RandomWeight)] // 12-digit UPC-A; NS digit (first) lands at Candidate14 index 2
        [InlineData("500000000000", UpcRisk.Coupon)]
        [InlineData("900000000000", UpcRisk.Coupon)]
        [InlineData("300000000000", UpcRisk.Ndc)]
        [InlineData("400000000000", UpcRisk.InStore)]
        [InlineData("100000000000", UpcRisk.None)]
        public void ClassifyUpc_RiskFromNumberSystem(string code, UpcRisk expectedRisk)
        {
            var c = ClassifyUpc(code);
            Assert.Equal(expectedRisk, c.Risk);
            if (expectedRisk == UpcRisk.None)
            {
                Assert.Null(RiskNote(expectedRisk));
            }
            else
            {
                Assert.NotNull(RiskNote(expectedRisk));
            }
        }

        // ---- normalization -----------------------------------------------------
        [Theory]
        [InlineData("345", "00000000000345")]
        [InlineData("00036000291452", "00036000291452")]
        public void Normalize_ZeroPadOnly_PadsToFourteen(string input, string expected)
        {
            var r = Normalize(input, NormalizationStrategy.ZeroPadOnly);
            Assert.True(r.Success);
            Assert.Equal(expected, r.Normalized);
        }

        [Fact]
        public void Normalize_ZeroPadOnly_TooLong_Fails()
        {
            var r = Normalize("123456789012345", NormalizationStrategy.ZeroPadOnly);
            Assert.False(r.Success);
        }

        [Fact]
        public void Normalize_Smart_CompleteBarcode_PadsAndPreservesCheckDigit()
        {
            var r = Normalize("036000291452", NormalizationStrategy.SmartCheckDigit);
            Assert.True(r.Success);
            Assert.Equal("00036000291452", r.Normalized);
        }

        [Fact]
        public void Normalize_Smart_ShortPayload_ComputesCheckDigit()
        {
            var r = Normalize("345", NormalizationStrategy.SmartCheckDigit);
            Assert.True(r.Success);
            Assert.Equal("0000000000345" + Gtin14CheckDigit("345"), r.Normalized);
            Assert.True(IsValidGtin14(r.Normalized));
        }

        [Fact]
        public void Normalize_Smart_AmbiguousInvalid_DoesNotRecompute()
        {
            // 12-digit value with a bad check digit must NOT be "fixed" (H2-corruption guard).
            var r = Normalize("036000291451", NormalizationStrategy.SmartCheckDigit);
            Assert.False(r.Success);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("12A45")]
        public void Normalize_NonDigitOrBlank_Fails(string input)
        {
            var r = Normalize(input, NormalizationStrategy.SmartCheckDigit);
            Assert.False(r.Success);
        }

        // ---- the H2 corruption guard -------------------------------------------
        [Theory]
        [InlineData("036000291452")] // 12 (UPC-A)
        [InlineData("0036000291452")] // 13 (EAN-13)
        [InlineData("00036000291452")] // 14 (GTIN-14)
        public void ComputePayloadGtin14_RefusesCompleteBarcodes(string code)
        {
            Assert.Throws<InvalidOperationException>(() => ComputePayloadGtin14(code));
        }

        [Fact]
        public void ComputePayloadGtin14_ShortStem_Succeeds()
        {
            var result = ComputePayloadGtin14("345");
            Assert.Equal(14, result.Length);
            Assert.True(IsValidGtin14(result));
        }
    }
}
