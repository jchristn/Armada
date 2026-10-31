namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Builds the mechanical counts of a Full Evaluate report (docs/MODEL_HETEROGENEITY.md, Evaluate (Full)): one count per
    /// Quick probe, and per persona the battery outcomes in total and per task. These counts are the source of truth; the
    /// evaluator's narrative may only cite them.
    /// </summary>
    public static class ModelEvaluationReportBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Key of a probe count.
        /// </summary>
        /// <param name="probe">Probe.</param>
        /// <returns>Key.</returns>
        public static string ProbeKey(ModelProbeKindEnum probe)
        {
            return "probe:" + probe;
        }

        /// <summary>
        /// Key of a persona total.
        /// </summary>
        /// <param name="persona">Persona.</param>
        /// <returns>Key.</returns>
        public static string PersonaKey(string persona)
        {
            return "persona:" + persona;
        }

        /// <summary>
        /// Key of a battery task count.
        /// </summary>
        /// <param name="packId">Pack.</param>
        /// <param name="taskId">Task.</param>
        /// <returns>Key.</returns>
        public static string TaskKey(string? packId, string? taskId)
        {
            return "task:" + (packId ?? String.Empty) + "/" + (taskId ?? String.Empty);
        }

        /// <summary>
        /// Fill the report's probe counts and per-persona outcomes. Personas listed in the report with no battery result
        /// still get an (empty) outcome, so a persona that could not be scored shows as zero of zero, not missing.
        /// </summary>
        /// <param name="report">Report to fill.</param>
        /// <param name="probes">Quick probe results.</param>
        /// <param name="battery">Battery task results, or empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static void Fill(ModelEvaluationReport report, IEnumerable<ModelProbeResult> probes, IEnumerable<BatteryTaskResult> battery)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (probes == null) throw new ArgumentNullException(nameof(probes));
            if (battery == null) throw new ArgumentNullException(nameof(battery));

            report.Probes = probes
                .OrderBy(p => (int)p.Probe)
                .Select(p =>
                {
                    ModelEvaluationCount count = new ModelEvaluationCount { Key = ProbeKey(p.Probe), Label = "Probe: " + p.Probe };
                    if (p.Outcome == ModelProbeOutcomeEnum.Passed) count.Passed = 1;
                    else if (p.Outcome == ModelProbeOutcomeEnum.Failed) count.Failed = 1;
                    else count.CouldNotRun = 1;
                    return count;
                })
                .ToList();

            Dictionary<string, ModelEvaluationPersonaOutcome> byPersona = new Dictionary<string, ModelEvaluationPersonaOutcome>(StringComparer.OrdinalIgnoreCase);
            foreach (string persona in report.Personas) Outcome(byPersona, persona);

            foreach (BatteryTaskResult result in battery.OrderBy(r => r.PackId, StringComparer.Ordinal).ThenBy(r => r.TaskId, StringComparer.Ordinal).ThenBy(r => r.Attempt))
            {
                ModelEvaluationPersonaOutcome outcome = Outcome(byPersona, PersonaCatalog.NormalizeName(result.Persona));
                string key = TaskKey(result.PackId, result.TaskId);
                ModelEvaluationCount? task = outcome.Tasks.FirstOrDefault(t => String.Equals(t.Key, key, StringComparison.Ordinal));
                if (task == null)
                {
                    task = new ModelEvaluationCount
                    {
                        Key = key,
                        Label = result.PackId + ": " + (String.IsNullOrWhiteSpace(result.TaskTitle) ? result.TaskId : result.TaskTitle),
                        Persona = outcome.Persona
                    };
                    outcome.Tasks.Add(task);
                }

                Add(task, result.Outcome);
                Add(outcome.Totals, result.Outcome);
            }

            report.PersonaOutcomes = byPersona.Values.OrderBy(o => o.Persona, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// Every count in the report by key (probes, persona totals and tasks).
        /// </summary>
        /// <param name="report">Report.</param>
        /// <returns>Counts by key.</returns>
        /// <exception cref="ArgumentNullException">Thrown when report is null.</exception>
        public static Dictionary<string, ModelEvaluationCount> CountsByKey(ModelEvaluationReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            Dictionary<string, ModelEvaluationCount> counts = new Dictionary<string, ModelEvaluationCount>(StringComparer.Ordinal);
            foreach (ModelEvaluationCount probe in report.Probes) counts[probe.Key] = probe;
            foreach (ModelEvaluationPersonaOutcome outcome in report.PersonaOutcomes)
            {
                counts[outcome.Totals.Key] = outcome.Totals;
                foreach (ModelEvaluationCount task in outcome.Tasks) counts[task.Key] = task;
            }

            return counts;
        }

        #endregion

        #region Private-Methods

        private static ModelEvaluationPersonaOutcome Outcome(Dictionary<string, ModelEvaluationPersonaOutcome> byPersona, string persona)
        {
            string name = String.IsNullOrWhiteSpace(persona) ? "Worker" : persona.Trim();
            if (!byPersona.TryGetValue(name, out ModelEvaluationPersonaOutcome? outcome))
            {
                outcome = new ModelEvaluationPersonaOutcome
                {
                    Persona = name,
                    Totals = new ModelEvaluationCount { Key = PersonaKey(name), Label = name + " (all tasks)", Persona = name }
                };
                byPersona[name] = outcome;
            }

            return outcome;
        }

        private static void Add(ModelEvaluationCount count, BatteryTaskOutcomeEnum outcome)
        {
            switch (outcome)
            {
                case BatteryTaskOutcomeEnum.Passed: count.Passed++; break;
                case BatteryTaskOutcomeEnum.Failed: count.Failed++; break;
                case BatteryTaskOutcomeEnum.CouldNotRun: count.CouldNotRun++; break;
                default: count.PendingEvaluation++; break;
            }
        }

        #endregion
    }
}
