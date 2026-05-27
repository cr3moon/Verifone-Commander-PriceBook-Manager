// -----------------------------------------------------------------------
// <copyright file="ImportRow.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.Core.Import
{
    using System.Collections.Generic;
    using VerifoneCommander.PriceBookManager.Core.Models;

    public sealed class ImportRow
    {
        public int LineNumber { get; set; }

        public string Upc { get; set; }

        public string Modifier { get; set; }

        public string Description { get; set; }

        public string DepartmentName { get; set; }

        public string Price { get; set; }

        public ImportRowStatus Status { get; set; }

        public List<string> Messages { get; } = new List<string>();

        // The PLU built from this row, or null when the row is in error.
        public Plu Plu { get; set; }

        public string Message => string.Join("; ", this.Messages);
    }
}
