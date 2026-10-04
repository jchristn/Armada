namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using Armada.Client;

    /// <summary>
    /// Writers for the export formats the dashboard offers (JSON, CSV, Markdown). CSV follows RFC 4180 quoting;
    /// Markdown escapes pipes and flattens newlines.
    /// </summary>
    public static class DataExport
    {
        #region Public-Methods

        /// <summary>
        /// Indented JSON with the client's serializer settings.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>JSON text.</returns>
        public static string Json(object? value)
        {
            JsonSerializerOptions options = new JsonSerializerOptions(ArmadaJson.Options);
            options.WriteIndented = true;
            return JsonSerializer.Serialize(value, options);
        }

        /// <summary>
        /// CSV text: a header row and one row per record.
        /// </summary>
        /// <param name="headers">Column headers.</param>
        /// <param name="rows">Rows of cell values.</param>
        /// <returns>CSV text (CRLF line endings).</returns>
        public static string Csv(IEnumerable<string> headers, IEnumerable<IEnumerable<string?>> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(String.Join(",", (headers ?? Enumerable.Empty<string>()).Select(CsvCell))).Append("\r\n");
            foreach (IEnumerable<string?> row in rows ?? Enumerable.Empty<IEnumerable<string?>>())
            {
                sb.Append(String.Join(",", row.Select(CsvCell))).Append("\r\n");
            }

            return sb.ToString();
        }

        /// <summary>
        /// A Markdown document: a heading, optional intro lines, and a table.
        /// </summary>
        /// <param name="title">Heading text.</param>
        /// <param name="intro">Lines under the heading.</param>
        /// <param name="headers">Column headers.</param>
        /// <param name="rows">Rows.</param>
        /// <returns>Markdown text.</returns>
        public static string Markdown(string title, IEnumerable<string> intro, IEnumerable<string> headers, IEnumerable<IEnumerable<string?>> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# ").Append(title ?? "").Append("\n\n");
            foreach (string line in intro ?? Enumerable.Empty<string>()) sb.Append(line).Append("\n");
            sb.Append("\n");
            List<string> head = (headers ?? Enumerable.Empty<string>()).ToList();
            sb.Append("| ").Append(String.Join(" | ", head.Select(MarkdownCell))).Append(" |\n");
            sb.Append("|").Append(String.Join("|", head.Select(h => " --- "))).Append("|\n");
            foreach (IEnumerable<string?> row in rows ?? Enumerable.Empty<IEnumerable<string?>>())
            {
                sb.Append("| ").Append(String.Join(" | ", row.Select(MarkdownCell))).Append(" |\n");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Quote a CSV cell when needed.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Cell text.</returns>
        public static string CsvCell(string? value)
        {
            string v = value ?? "";
            if (v.IndexOfAny(new char[] { ',', '"', '\n', '\r' }) < 0) return v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Escape a Markdown table cell.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Cell text.</returns>
        public static string MarkdownCell(string? value)
        {
            return (value ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        #endregion
    }
}
