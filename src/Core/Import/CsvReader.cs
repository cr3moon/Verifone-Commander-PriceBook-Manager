// -----------------------------------------------------------------------
// <copyright file="CsvReader.cs" company="Shubham Gogna">
// Copyright (c) Shubham Gogna
// </copyright>
// -----------------------------------------------------------------------

namespace VerifoneCommander.PriceBookManager.Core.Import
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Minimal RFC 4180 CSV reader. Ported from the sibling import/export tool.
    /// Returns every row (header + data) as a list of string fields. Handles
    /// quoted fields, doubled quotes ("" -> "), and CRLF/LF line endings.
    /// </summary>
    public static class CsvReader
    {
        public static List<List<string>> Parse(string content)
        {
            _ = content ?? throw new ArgumentNullException(nameof(content));

            using var reader = new StringReader(content);
            return Parse(reader);
        }

        public static List<List<string>> Parse(TextReader reader)
        {
            _ = reader ?? throw new ArgumentNullException(nameof(reader));

            var rows = new List<List<string>>();
            var sb = new System.Text.StringBuilder();
            var row = new List<string>();
            bool inQuote = false;
            int ch;

            while ((ch = reader.Read()) != -1)
            {
                char c = (char)ch;

                if (inQuote)
                {
                    if (c == '"')
                    {
                        if (reader.Peek() == '"')
                        {
                            sb.Append('"');
                            reader.Read();
                        }
                        else
                        {
                            inQuote = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuote = true;
                    }
                    else if (c == ',')
                    {
                        row.Add(sb.ToString());
                        sb.Clear();
                    }
                    else if (c == '\r')
                    {
                        // Ignore; the line break is handled on '\n'.
                    }
                    else if (c == '\n')
                    {
                        row.Add(sb.ToString());
                        sb.Clear();
                        rows.Add(row);
                        row = new List<string>();
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            if (sb.Length > 0 || row.Count > 0)
            {
                row.Add(sb.ToString());
                rows.Add(row);
            }

            // Drop a trailing blank row (e.g. a file ending in a newline).
            if (rows.Count > 0)
            {
                var last = rows[rows.Count - 1];
                if (last.Count == 1 && last[0].Length == 0)
                {
                    rows.RemoveAt(rows.Count - 1);
                }
            }

            return rows;
        }
    }
}
