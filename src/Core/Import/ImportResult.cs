// -----------------------------------------------------------------------
// <copyright file="ImportResult.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.Core.Import
{
    using System.Collections.Generic;
    using System.Linq;

    public sealed class ImportResult
    {
        public List<ImportRow> Rows { get; } = new List<ImportRow>();

        public List<string> FileErrors { get; } = new List<string>();

        public bool HasFileErrors => this.FileErrors.Count > 0;

        public int ImportableCount => this.Rows.Count(r => r.Status != ImportRowStatus.Error);

        public int WarningCount => this.Rows.Count(r => r.Status == ImportRowStatus.Warning);

        public int ErrorCount => this.Rows.Count(r => r.Status == ImportRowStatus.Error);
    }
}
