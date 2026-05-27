// -----------------------------------------------------------------------
// <copyright file="PluImportParserTests.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.Core.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using VerifoneCommander.PriceBookManager.Core.Import;
    using VerifoneCommander.PriceBookManager.Core.Models;
    using Xunit;

    public class PluImportParserTests
    {
        private const string Header = "upc,modifier,description,department,price";

        [Fact]
        public void Parse_ValidRow_BuildsPluFromDepartment()
        {
            var result = ParseCsv(Header + "\n036000291452,0,Soda,Grocery,1.99");

            Assert.False(result.HasFileErrors);
            var row = Assert.Single(result.Rows);
            Assert.Equal(ImportRowStatus.Valid, row.Status);
            Assert.Equal("00036000291452", row.Upc);

            Assert.NotNull(row.Plu);
            Assert.Equal(36000291452L, row.Plu.Ean13);
            Assert.Equal(0, row.Plu.Modifier);
            Assert.Equal(10, row.Plu.DepartmentId);
            Assert.Equal(5, row.Plu.ProductCodeId);
            Assert.Equal(1.99, row.Plu.Price);
            Assert.Contains(1, row.Plu.TaxRateIds);
            Assert.Contains(PluFlags.FoodStamps, row.Plu.FlagIds); // Grocery allows food stamps
        }

        [Fact]
        public void Parse_NonFoodStampDepartment_RemovesFlag_AndCopiesAgeValidations()
        {
            var result = ParseCsv(Header + "\n0036000291452,5,Lager,Beer,9.49");

            var row = Assert.Single(result.Rows);
            Assert.Equal(ImportRowStatus.Valid, row.Status);
            Assert.DoesNotContain(PluFlags.FoodStamps, row.Plu.FlagIds);
            Assert.Contains(3, row.Plu.AgeValidationIds);
            Assert.Equal(5, row.Plu.Modifier);
        }

        [Fact]
        public void Parse_BlankModifier_DefaultsToZero()
        {
            var result = ParseCsv(Header + "\n036000291452,,Soda,Grocery,1.00");
            var row = Assert.Single(result.Rows);
            Assert.NotEqual(ImportRowStatus.Error, row.Status);
            Assert.Equal(0, row.Plu.Modifier);
        }

        [Fact]
        public void Parse_MissingRequiredColumn_ReportsFileError()
        {
            var result = ParseCsv("upc,description,price\n036000291452,Soda,1.00");
            Assert.True(result.HasFileErrors);
            Assert.Contains(result.FileErrors, e => e.Contains("department", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(result.Rows);
        }

        [Theory]
        [InlineData("036000291452,0,,Grocery,1.00")] // blank description
        [InlineData("036000291452,0,Soda,Nope,1.00")] // unknown department
        [InlineData("036000291452,0,Soda,Grocery,abc")] // bad price
        [InlineData("036000291452,xx,Soda,Grocery,1.00")] // bad modifier
        [InlineData(",0,Soda,Grocery,1.00")] // blank upc
        public void Parse_InvalidRow_IsError_WithNoPlu(string dataLine)
        {
            var result = ParseCsv(Header + "\n" + dataLine);
            var row = Assert.Single(result.Rows);
            Assert.Equal(ImportRowStatus.Error, row.Status);
            Assert.Null(row.Plu);
            Assert.NotEmpty(row.Messages);
        }

        [Fact]
        public void Parse_RiskyNumberSystem_ImportsAsWarning()
        {
            // Number system 2 (random/variable weight) — importable but flagged.
            var result = ParseCsv(Header + "\n200000000000,0,Scale item,Grocery,0");
            var row = Assert.Single(result.Rows);
            Assert.Equal(ImportRowStatus.Warning, row.Status);
            Assert.NotNull(row.Plu);
            Assert.Contains(row.Messages, m => m.Contains("weight", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Parse_InvalidCheckDigit_ImportsAsWarning_NotRecomputed()
        {
            // 12-digit value with a bad check digit: never "fixed" (H2 guard), imported with a warning.
            var result = ParseCsv(Header + "\n036000291451,0,Soda,Grocery,1.00");
            var row = Assert.Single(result.Rows);
            Assert.Equal(ImportRowStatus.Warning, row.Status);
            Assert.Equal(36000291451L, row.Plu.Ean13); // stored verbatim, not recomputed
        }

        [Fact]
        public void Parse_ImportableAndErrorCounts()
        {
            var csv = Header + "\n" +
                "036000291452,0,Soda,Grocery,1.99\n" +
                "036000291452,0,,Grocery,1.99\n" +
                "200000000000,0,Scale,Grocery,0";
            var result = ParseCsv(csv);

            Assert.Equal(3, result.Rows.Count);
            Assert.Equal(2, result.ImportableCount); // valid + warning
            Assert.Equal(1, result.ErrorCount);
            Assert.Equal(1, result.WarningCount);
        }

        private static IReadOnlyDictionary<string, Department> Departments()
        {
            return new Dictionary<string, Department>(StringComparer.Ordinal)
            {
                ["Grocery"] = new Department
                {
                    SystemId = 10,
                    Name = "Grocery",
                    AllowFoodStamps = true,
                    ProductCodeId = 5,
                    TaxRateIds = new HashSet<int> { 1 },
                    AgeValidationIds = new HashSet<int>(),
                },
                ["Beer"] = new Department
                {
                    SystemId = 20,
                    Name = "Beer",
                    AllowFoodStamps = false,
                    ProductCodeId = 7,
                    TaxRateIds = new HashSet<int> { 1, 2 },
                    AgeValidationIds = new HashSet<int> { 3 },
                },
            };
        }

        private static ImportResult ParseCsv(string csv)
        {
            return PluImportParser.Parse(CsvReader.Parse(csv), Departments());
        }
    }
}
