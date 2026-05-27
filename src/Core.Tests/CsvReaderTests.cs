// -----------------------------------------------------------------------
// <copyright file="CsvReaderTests.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.Core.Tests
{
    using VerifoneCommander.PriceBookManager.Core.Import;
    using Xunit;

    public class CsvReaderTests
    {
        [Fact]
        public void Parse_SimpleRows()
        {
            var rows = CsvReader.Parse("a,b,c\n1,2,3");
            Assert.Equal(2, rows.Count);
            Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
            Assert.Equal(new[] { "1", "2", "3" }, rows[1]);
        }

        [Fact]
        public void Parse_QuotedFieldWithComma()
        {
            var rows = CsvReader.Parse("\"x,y\",z");
            Assert.Single(rows);
            Assert.Equal(new[] { "x,y", "z" }, rows[0]);
        }

        [Fact]
        public void Parse_DoubledQuoteEscape()
        {
            var rows = CsvReader.Parse("\"she said \"\"hi\"\"\",z");
            Assert.Equal(new[] { "she said \"hi\"", "z" }, rows[0]);
        }

        [Fact]
        public void Parse_HandlesCrLf()
        {
            var rows = CsvReader.Parse("a\r\nb");
            Assert.Equal(2, rows.Count);
            Assert.Equal(new[] { "a" }, rows[0]);
            Assert.Equal(new[] { "b" }, rows[1]);
        }

        [Fact]
        public void Parse_DropsTrailingBlankRow()
        {
            var rows = CsvReader.Parse("a,b\n1,2\n");
            Assert.Equal(2, rows.Count);
        }
    }
}
