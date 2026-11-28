namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Emits the per-attempt reliability metrics (RS-1.4, RS-12.5) for one stored <see cref="StageOutcome"/>: the outcome
    /// counter, the attempt duration, repairs, and escalations. Labels are bounded: persona and model names longer than
    /// <see cref="MaxLabelLength"/> or containing whitespace become <see cref="OtherLabel"/>, and no identifiers are tagged.
    /// Never throws.
    /// </summary>
    public static class StageOutcomeMetrics
    {
        #region Public-Members

        /// <summary>
        /// Label used for a missing or unbounded value.
        /// </summary>
        public const string OtherLabel = "Other";

        /// <summary>
        /// Longest persona or model label kept as is.
        /// </summary>
        public const int MaxLabelLength = 64;

        /// <summary>
        /// Repair kind label for an output-repair relaunch (a full rerun).
        /// </summary>
        public const string RerunRepairKind = "Rerun";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record the metrics for one stored outcome. A null outcome is ignored.
        /// </summary>
        /// <param name="outcome">Stored outcome.</param>
        public static void Record(StageOutcome? outcome)
        {
            if (outcome == null) return;
            try
            {
                string persona = Label(outcome.Persona, "none");
                string runtime = outcome.Runtime?.ToString() ?? "none";
                string model = Label(outcome.Model, "none");
                string disposition = outcome.Disposition.ToString();
                string failureKind = outcome.FailureKind?.ToString() ?? "None";

                ArmadaMetrics.StageOutcomes.Add(1,
                    new KeyValuePair<string, object?>("persona", persona),
                    new KeyValuePair<string, object?>("runtime", runtime),
                    new KeyValuePair<string, object?>("model", model),
                    new KeyValuePair<string, object?>("disposition", disposition),
                    new KeyValuePair<string, object?>("failure_kind", failureKind),
                    new KeyValuePair<string, object?>("runtime_failure_kind", outcome.RuntimeFailureKind?.ToString() ?? "None"),
                    new KeyValuePair<string, object?>("contract_repair", outcome.ContractRepairKind?.ToString() ?? "None"));

                if (outcome.DurationMs.HasValue && outcome.DurationMs.Value >= 0)
                {
                    ArmadaMetrics.StageAttemptSeconds.Record(outcome.DurationMs.Value / 1000.0,
                        new KeyValuePair<string, object?>("persona", persona),
                        new KeyValuePair<string, object?>("runtime", runtime),
                        new KeyValuePair<string, object?>("model", model),
                        new KeyValuePair<string, object?>("disposition", disposition));
                }

                if (outcome.ContractRepairKind.HasValue && outcome.ContractRepairKind.Value != ContractRepairKindEnum.None) Repair(persona, runtime, model, outcome.ContractRepairKind.Value.ToString());
                if (outcome.Disposition == StageDispositionEnum.OutputRepair) Repair(persona, runtime, model, RerunRepairKind);

                if (outcome.Disposition == StageDispositionEnum.Escalated)
                {
                    ArmadaMetrics.StageEscalations.Add(1,
                        new KeyValuePair<string, object?>("persona", persona),
                        new KeyValuePair<string, object?>("runtime", runtime),
                        new KeyValuePair<string, object?>("model", model),
                        new KeyValuePair<string, object?>("failure_kind", failureKind));
                }
            }
            catch (Exception)
            {
                // Telemetry is best effort and never disturbs outcome recording.
            }
        }

        /// <summary>
        /// Bound a persona or model name for use as a label.
        /// </summary>
        /// <param name="value">Value, or null.</param>
        /// <param name="missing">Label for a null or blank value.</param>
        /// <returns>The value, the missing label, or <see cref="OtherLabel"/> when the value is unbounded.</returns>
        public static string Label(string? value, string missing)
        {
            if (String.IsNullOrWhiteSpace(value)) return missing;
            string trimmed = value.Trim();
            if (trimmed.Length > MaxLabelLength) return OtherLabel;
            foreach (char c in trimmed)
                if (Char.IsWhiteSpace(c) || Char.IsControl(c)) return OtherLabel;
            return trimmed;
        }

        #endregion

        #region Private-Methods

        private static void Repair(string persona, string runtime, string model, string kind)
        {
            ArmadaMetrics.StageRepairs.Add(1,
                new KeyValuePair<string, object?>("persona", persona),
                new KeyValuePair<string, object?>("runtime", runtime),
                new KeyValuePair<string, object?>("model", model),
                new KeyValuePair<string, object?>("kind", kind));
        }

        #endregion
    }
}
