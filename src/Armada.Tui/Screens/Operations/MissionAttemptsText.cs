namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Armada.Core.Models;

    /// <summary>
    /// Renders a mission's attempt history (RS-12.9): recovery counters, escalation state, and one row per attempt with
    /// its captain, model, disposition, failure kind, repair, escalation, Judge verdict and duration.
    /// </summary>
    public static class MissionAttemptsText
    {
        /// <summary>
        /// Write the attempt history into a document.
        /// </summary>
        /// <param name="doc">Target document.</param>
        /// <param name="history">Attempt history, or null when it has not loaded.</param>
        /// <param name="tr">Localizer.</param>
        /// <returns>The document.</returns>
        /// <exception cref="ArgumentNullException">When doc or tr is null.</exception>
        public static OpsDocument Build(OpsDocument doc, MissionAttemptHistory? history, Func<string, string> tr)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (history == null) return doc;

            doc.Section("Recovery");
            doc.Field("Tier", history.Tier?.ToString() ?? "-");
            string escalations = history.EscalationCount.ToString(CultureInfo.InvariantCulture);
            if (!String.IsNullOrEmpty(history.EscalatedFromModel)) escalations += " (" + tr("from") + " " + history.EscalatedFromModel + ")";
            doc.Field("Escalations", escalations);
            doc.Field("Redispatches", history.RedispatchAttempts.ToString(CultureInfo.InvariantCulture));
            doc.Field("Output repairs", history.OutputRepairAttempts.ToString(CultureInfo.InvariantCulture));
            doc.Field("No-op redispatches", history.NoOpRedispatchAttempts.ToString(CultureInfo.InvariantCulture));
            doc.Field("Stall recoveries", history.StallRecoveryAttempts.ToString(CultureInfo.InvariantCulture));

            doc.Section("Attempts", history.Attempts.Count.ToString(CultureInfo.InvariantCulture));
            if (history.Attempts.Count == 0)
            {
                doc.Note("No attempts recorded yet.");
                return doc;
            }

            List<IList<string>> rows = new List<IList<string>>();
            List<string?> details = new List<string?>();
            foreach (MissionAttempt a in history.Attempts)
            {
                rows.Add(new List<string>
                {
                    a.Attempt.ToString(CultureInfo.InvariantCulture) + (a.ReworkRound > 0 ? " r" + a.ReworkRound.ToString(CultureInfo.InvariantCulture) : ""),
                    a.CaptainName ?? a.CaptainId ?? "-",
                    a.Model ?? "-",
                    a.Disposition.ToString(),
                    a.FailureKind?.ToString() ?? "-",
                    Repair(a),
                    a.Escalated ? tr("Yes") : "",
                    a.JudgeVerdict ?? "",
                    Duration(a.DurationMs)
                });
                List<string> extra = new List<string>();
                if (a.RuntimeFailureKind.HasValue) extra.Add(tr("Runtime exit") + ": " + a.RuntimeFailureKind.Value);
                if (!String.IsNullOrEmpty(a.FailureReason)) extra.Add(a.FailureReason!);
                if (a.Escalated && !String.IsNullOrEmpty(a.NextCaptainId)) extra.Add(tr("Escalated to") + " " + a.NextCaptainId);
                details.Add(extra.Count == 0 ? null : String.Join(Environment.NewLine, extra));
            }

            doc.TableWithDetails(
                new List<string> { tr("#"), tr("Captain"), tr("Model"), tr("Disposition"), tr("Failure"), tr("Repair"), tr("Escalated"), tr("Judge"), tr("Duration") },
                rows, details, 28);
            return doc;
        }

        /// <summary>
        /// The repair label for an attempt: the contract repair kind, OutputRepair for an output-repair relaunch, or empty.
        /// </summary>
        /// <param name="attempt">Attempt.</param>
        /// <returns>The label.</returns>
        /// <exception cref="ArgumentNullException">When attempt is null.</exception>
        public static string Repair(MissionAttempt attempt)
        {
            if (attempt == null) throw new ArgumentNullException(nameof(attempt));
            if (attempt.ContractRepairKind.HasValue && attempt.ContractRepairKind.Value.ToString() != "None") return attempt.ContractRepairKind.Value.ToString();
            return attempt.IsRepair ? "OutputRepair" : "";
        }

        private static string Duration(long? ms)
        {
            if (!ms.HasValue) return "";
            TimeSpan span = TimeSpan.FromMilliseconds(ms.Value);
            return span.TotalHours >= 1
                ? span.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : span.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }
    }
}
