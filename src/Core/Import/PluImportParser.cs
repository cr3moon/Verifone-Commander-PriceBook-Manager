// -----------------------------------------------------------------------
// <copyright file="PluImportParser.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.Core.Import
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using VerifoneCommander.PriceBookManager.Core.Models;

    /// <summary>
    /// Parses a fixed-template PLU import CSV into validated rows. Columns are
    /// matched by header name (case-insensitive): upc, modifier, description,
    /// department, price. "modifier" is optional (defaults to 0); the rest are
    /// required. Each row is validated and, when clean, turned into a Plu built
    /// the same way the Edit page builds one (the department supplies tax rates,
    /// age validations, product code, and the food-stamps flag).
    /// </summary>
    public static class PluImportParser
    {
        public static readonly IReadOnlyList<string> RequiredColumns =
            new[] { "upc", "description", "department", "price" };

        public static ImportResult Parse(
            IReadOnlyList<List<string>> rows,
            IReadOnlyDictionary<string, Department> departmentsByName)
        {
            _ = rows ?? throw new ArgumentNullException(nameof(rows));
            _ = departmentsByName ?? throw new ArgumentNullException(nameof(departmentsByName));

            var result = new ImportResult();

            if (rows.Count == 0)
            {
                result.FileErrors.Add("The file is empty.");
                return result;
            }

            var columnIndex = MapHeader(rows[0], result);
            if (result.HasFileErrors)
            {
                return result;
            }

            for (int i = 1; i < rows.Count; i++)
            {
                result.Rows.Add(ParseRow(i, rows[i], columnIndex, departmentsByName));
            }

            if (result.Rows.Count == 0)
            {
                result.FileErrors.Add("The file has a header row but no data rows.");
            }

            return result;
        }

        private static Dictionary<string, int> MapHeader(IReadOnlyList<string> header, ImportResult result)
        {
            var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Count; i++)
            {
                var name = (header[i] ?? string.Empty).Trim();
                if (name.Length > 0 && !columnIndex.ContainsKey(name))
                {
                    columnIndex[name] = i;
                }
            }

            foreach (var required in RequiredColumns)
            {
                if (!columnIndex.ContainsKey(required))
                {
                    result.FileErrors.Add($"Missing required column '{required}'. Expected header: upc, modifier, description, department, price.");
                }
            }

            return columnIndex;
        }

        private static ImportRow ParseRow(
            int lineNumber,
            IReadOnlyList<string> cells,
            IReadOnlyDictionary<string, int> columnIndex,
            IReadOnlyDictionary<string, Department> departmentsByName)
        {
            string Field(string column) =>
                columnIndex.TryGetValue(column, out var i) && i < cells.Count
                    ? (cells[i] ?? string.Empty).Trim()
                    : string.Empty;

            var rawUpc = Field("upc");
            var rawModifier = Field("modifier");
            var description = Field("description");
            var departmentName = Field("department");
            var rawPrice = Field("price");

            var row = new ImportRow
            {
                LineNumber = lineNumber,
                Upc = rawUpc,
                Modifier = rawModifier,
                Description = description,
                DepartmentName = departmentName,
                Price = rawPrice,
            };

            var error = false;

            // UPC: reject only the truly unusable classes (blank / non-digit / >14).
            // We zero-pad to 14 and store that value verbatim — we never invent a
            // check digit (H2-safe). A complete UPC-A/EAN-13/GTIN-14 therefore keeps
            // its own check digit; a short stem (<=11 digits, no valid check) is stored
            // as-entered and flagged with a Warning rather than silently "fixed" — the
            // operator should supply complete barcodes for scannable items.
            long ean13 = 0;
            var classification = UpcUtilities.ClassifyUpc(rawUpc);
            if (classification.IsReject)
            {
                row.Messages.Add("UPC: " + classification.Note);
                error = true;
            }
            else
            {
                var normalized = UpcUtilities.Normalize(rawUpc, UpcUtilities.NormalizationStrategy.ZeroPadOnly);
                ean13 = long.Parse(normalized.Normalized, CultureInfo.InvariantCulture);
                row.Upc = normalized.Normalized;

                var riskNote = UpcUtilities.RiskNote(classification.Risk);
                if (riskNote != null)
                {
                    row.Messages.Add(riskNote);
                }
                else if (classification.Class == UpcUtilities.UpcClass.AmbiguousInvalid)
                {
                    row.Messages.Add("UPC check digit does not validate — verify it scans correctly.");
                }
            }

            var modifier = 0;
            if (!string.IsNullOrEmpty(rawModifier) &&
                (!int.TryParse(rawModifier, NumberStyles.Integer, CultureInfo.InvariantCulture, out modifier) || modifier < 0 || modifier > 999))
            {
                row.Messages.Add("Modifier must be a whole number 0-999.");
                error = true;
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                row.Messages.Add("Description is required.");
                error = true;
            }

            Department department = null;
            if (string.IsNullOrWhiteSpace(departmentName))
            {
                row.Messages.Add("Department is required.");
                error = true;
            }
            else if (!departmentsByName.TryGetValue(departmentName, out department))
            {
                row.Messages.Add($"Department '{departmentName}' was not found on the controller.");
                error = true;
            }

            double price = 0;
            if (string.IsNullOrWhiteSpace(rawPrice))
            {
                row.Messages.Add("Price is required.");
                error = true;
            }
            else if (!double.TryParse(rawPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out price) || price < 0)
            {
                row.Messages.Add("Price must be a non-negative number.");
                error = true;
            }

            if (error)
            {
                row.Status = ImportRowStatus.Error;
                return row;
            }

            var flagIds = Plu.GenerateDefaultFlagIds();
            if (department.AllowFoodStamps)
            {
                flagIds.Add(PluFlags.FoodStamps);
            }
            else
            {
                flagIds.Remove(PluFlags.FoodStamps);
            }

            row.Plu = new Plu
            {
                Ean13 = ean13,
                Modifier = modifier,
                Description = description,
                DepartmentId = department.SystemId,
                ProductCodeId = department.ProductCodeId,
                Price = price,
                FlagIds = flagIds,
                TaxRateIds = new HashSet<int>(department.TaxRateIds),
                AgeValidationIds = new HashSet<int>(department.AgeValidationIds),
            };

            row.Status = row.Messages.Count > 0 ? ImportRowStatus.Warning : ImportRowStatus.Valid;
            return row;
        }
    }
}
