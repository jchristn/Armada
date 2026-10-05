namespace Armada.Tui.Screens.Activity
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using Armada.Client;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Kit;

    /// <summary>
    /// Builds the All Activity exports with the dashboard's content: JSON (exportedUtc, query, totalCount, entries),
    /// CSV (twelve fixed columns), and Markdown (a heading, filters, and one section per entry).
    /// </summary>
    public static class ActivityExports
    {
        #region Public-Members

        /// <summary>
        /// CSV header columns, in order.
        /// </summary>
        public static IReadOnlyList<string> CsvHeader { get; } = new List<string>
        {
            "id", "sourceType", "title", "status", "severity", "occurredUtc", "actorDisplay", "vesselId", "missionId", "voyageId", "route", "description",
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// File name for an export ("armada-history-2026-10-04-10-00-00.csv").
        /// </summary>
        /// <param name="nowUtc">Current time.</param>
        /// <param name="extension">Extension without the dot.</param>
        /// <returns>File name.</returns>
        public static string FileName(DateTime nowUtc, string extension)
        {
            return "armada-history-" + nowUtc.ToString("yyyy-MM-dd-HH-mm-ss", CultureInfo.InvariantCulture) + "." + extension;
        }

        /// <summary>
        /// JSON export (camelCase like the dashboard).
        /// </summary>
        /// <param name="query">Query.</param>
        /// <param name="entries">Entries.</param>
        /// <param name="nowUtc">Export time.</param>
        /// <returns>JSON text.</returns>
        public static string Json(HistoricalTimelineQuery query, List<HistoricalTimelineEntry> entries, DateTime nowUtc)
        {
            ActivityExportDocument doc = new ActivityExportDocument();
            doc.ExportedUtc = nowUtc;
            doc.Query = query ?? new HistoricalTimelineQuery();
            doc.Entries = entries ?? new List<HistoricalTimelineEntry>();
            doc.TotalCount = doc.Entries.Count;
            JsonSerializerOptions options = new JsonSerializerOptions(ArmadaJson.Options);
            options.WriteIndented = true;
            options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            return JsonSerializer.Serialize(doc, options);
        }

        /// <summary>
        /// CSV export.
        /// </summary>
        /// <param name="entries">Entries.</param>
        /// <returns>CSV text.</returns>
        public static string Csv(IEnumerable<HistoricalTimelineEntry> entries)
        {
            IEnumerable<IEnumerable<string?>> rows = (entries ?? Enumerable.Empty<HistoricalTimelineEntry>()).Select(e => (IEnumerable<string?>)new List<string?>
            {
                e.Id, e.SourceType, e.Title, e.Status, e.Severity, Iso(e.OccurredUtc), e.ActorDisplay, e.VesselId, e.MissionId, e.VoyageId, e.Route, e.Description,
            });
            return DataExport.Csv(CsvHeader, rows).TrimEnd('\r', '\n');
        }

        /// <summary>
        /// Markdown export.
        /// </summary>
        /// <param name="query">Query (its filters are listed).</param>
        /// <param name="entries">Entries.</param>
        /// <param name="nowUtc">Export time.</param>
        /// <returns>Markdown text.</returns>
        public static string Markdown(HistoricalTimelineQuery query, List<HistoricalTimelineEntry> entries, DateTime nowUtc)
        {
            List<string> filters = new List<string>();
            if (!String.IsNullOrEmpty(query.ObjectiveId)) filters.Add("objective=`" + query.ObjectiveId + "`");
            if (!String.IsNullOrEmpty(query.Text)) filters.Add("text=`" + query.Text + "`");
            if (!String.IsNullOrEmpty(query.Actor)) filters.Add("actor=`" + query.Actor + "`");
            if (!String.IsNullOrEmpty(query.VesselId)) filters.Add("vessel=`" + query.VesselId + "`");
            if (query.PostmortemOnly) filters.Add("postmortemOnly=`true`");
            if (query.SourceTypes != null && query.SourceTypes.Count > 0) filters.Add("sourceTypes=`" + String.Join(", ", query.SourceTypes) + "`");

            List<string> lines = new List<string>
            {
                "# Armada History Export",
                "",
                "Exported: " + Iso(nowUtc),
                "Entries: " + entries.Count.ToString(CultureInfo.InvariantCulture),
            };
            if (filters.Count > 0) lines.Add("Filters: " + String.Join(", ", filters));
            lines.Add("");
            foreach (HistoricalTimelineEntry e in entries)
            {
                lines.Add("## " + e.Title);
                lines.Add("- Source: " + e.SourceType);
                lines.Add("- Time: " + Iso(e.OccurredUtc));
                if (!String.IsNullOrEmpty(e.Status)) lines.Add("- Status: " + e.Status);
                if (!String.IsNullOrEmpty(e.Severity)) lines.Add("- Severity: " + e.Severity);
                if (!String.IsNullOrEmpty(e.ActorDisplay)) lines.Add("- Actor: " + e.ActorDisplay);
                if (!String.IsNullOrEmpty(e.VesselId)) lines.Add("- Vessel: " + e.VesselId);
                if (!String.IsNullOrEmpty(e.Route)) lines.Add("- Route: " + e.Route);
                if (!String.IsNullOrEmpty(e.Description))
                {
                    lines.Add("");
                    lines.Add(e.Description!);
                }

                lines.Add("");
            }

            return String.Join("\n", lines);
        }

        #endregion

        #region Private-Methods

        private static string Iso(DateTime utc)
        {
            DateTime u = utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return u.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
