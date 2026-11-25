namespace Armada.Gate
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Writes a <see cref="GateReport"/> as JSON and Markdown.
    /// </summary>
    public static class GateReportWriter
    {
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Write the report to <paramref name="directory"/> as gate-&lt;timestamp&gt;.json and .md.
        /// </summary>
        /// <param name="report">The report.</param>
        /// <param name="directory">Output directory, created when missing.</param>
        /// <returns>The Markdown path.</returns>
        /// <exception cref="ArgumentNullException">When an argument is null.</exception>
        public static string Write(GateReport report, string directory)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentNullException(nameof(directory));
            Directory.CreateDirectory(directory);
            string stamp = report.StartedUtc.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
            string json = Path.Combine(directory, "gate-" + stamp + ".json");
            string markdown = Path.Combine(directory, "gate-" + stamp + ".md");
            File.WriteAllText(json, JsonSerializer.Serialize(report, _Json));
            File.WriteAllText(markdown, ToMarkdown(report));
            return markdown;
        }

        /// <summary>
        /// Render the report as Markdown.
        /// </summary>
        /// <param name="report">The report.</param>
        /// <returns>Markdown text.</returns>
        /// <exception cref="ArgumentNullException">When <paramref name="report"/> is null.</exception>
        public static string ToMarkdown(GateReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            StringBuilder text = new StringBuilder();
            text.AppendLine("# Acceptance gate report");
            text.AppendLine();
            text.AppendLine("Armada " + (report.ArmadaVersion ?? "unknown") + ", " + report.StartedUtc.ToString("u", CultureInfo.InvariantCulture)
                + " to " + report.FinishedUtc.ToString("u", CultureInfo.InvariantCulture) + ". Packs: " + String.Join(", ", report.Packs) + ".");
            text.AppendLine();
            text.AppendLine("Overall: **" + (report.Passed ? "PASS" : "NOT PASSED") + "** (NotMeasured measures are checked by the suites named in their detail).");
            text.AppendLine();
            text.AppendLine("| Pipeline | Run | Status | Passed | Failed | Could not run | Pending evaluation |");
            text.AppendLine("|---|---|---|---|---|---|---|");
            foreach (GatePipelineResult pipeline in report.Pipelines)
                text.AppendLine("| " + pipeline.Pipeline + " | " + (pipeline.RunId ?? "") + " | " + pipeline.Status + " | " + pipeline.Passed + " | " + pipeline.Failed + " | " + pipeline.CouldNotRun + " | " + pipeline.PendingEvaluation + " |");
            text.AppendLine();
            text.AppendLine("| # | Measure | Value | Threshold | Result | Detail |");
            text.AppendLine("|---|---|---|---|---|---|");
            foreach (GateMeasure measure in report.Measures)
                text.AppendLine("| " + measure.Id + " | " + measure.Name + " | " + measure.Value + " | " + measure.Threshold + " | " + measure.Status + " | " + measure.Detail.Replace("|", "/") + " |");
            return text.ToString();
        }
    }
}
