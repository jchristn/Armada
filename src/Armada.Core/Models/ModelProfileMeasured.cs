namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Services;

    /// <summary>
    /// The profile's measured block (docs/MODEL_HETEROGENEITY.md, The model profile): counts with provenance from the
    /// latest complete Full Evaluate report, never a derived score. Built on read from the profile's advisory revisions;
    /// an incomplete report (cancelled or timed out) never fills it. Advisory only: nothing reads it for routing.
    /// </summary>
    public class ModelProfileMeasured
    {
        #region Public-Members

        /// <summary>
        /// Full Evaluate run the counts come from (mpr_ prefix).
        /// </summary>
        public string RunId { get; set; } = "";

        /// <summary>
        /// Advisory revision that carries the report (mdv_ prefix).
        /// </summary>
        public string RevisionId { get; set; } = "";

        /// <summary>
        /// Battery run of the evaluation (bat_ prefix), or null.
        /// </summary>
        public string? BatteryRunId { get; set; } = null;

        /// <summary>
        /// When the report was written, in UTC.
        /// </summary>
        public DateTime MeasuredUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Battery packs the run used.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> PackIds
        {
            get => _PackIds;
            set => _PackIds = value ?? new List<string>();
        }

        /// <summary>
        /// Probe counts, one per probe kind.
        /// </summary>
        [CollectionBound(EvaluationLimits.MaxCounts, Reason = "One per probe kind")]
        public List<ModelProfileMeasuredCount> Probes
        {
            get => _Probes;
            set => _Probes = value ?? new List<ModelProfileMeasuredCount>();
        }

        /// <summary>
        /// Battery counts per persona, each with one count per pack, ordered by persona.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "One per persona scored")]
        public List<ModelProfileMeasuredPersona> Personas
        {
            get => _Personas;
            set => _Personas = value ?? new List<ModelProfileMeasuredPersona>();
        }

        #endregion

        #region Private-Members

        private const string _ProbePrefix = "probe:";
        private const string _TaskPrefix = "task:";

        private List<string> _PackIds = new List<string>();
        private List<ModelProfileMeasuredCount> _Probes = new List<ModelProfileMeasuredCount>();
        private List<ModelProfileMeasuredPersona> _Personas = new List<ModelProfileMeasuredPersona>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileMeasured()
        {
        }

        /// <summary>
        /// The measured block from the latest complete Full Evaluate report among the revisions (by report time, then
        /// revision time and id), or null when no revision carries a complete report. Incomplete reports are skipped
        /// whatever their age.
        /// </summary>
        /// <param name="revisions">The profile's revisions, in any order.</param>
        /// <returns>The measured block, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when revisions is null.</exception>
        public static ModelProfileMeasured? From(IEnumerable<ModelProfileRevision> revisions)
        {
            if (revisions == null) throw new ArgumentNullException(nameof(revisions));
            ModelProfileRevision? latest = revisions
                .Where(r => r != null && r.EvaluationReport != null && r.EvaluationReport.Complete)
                .OrderByDescending(r => r.EvaluationReport!.CreatedUtc)
                .ThenByDescending(r => r.FirstSeenUtc)
                .ThenByDescending(r => r.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (latest == null) return null;

            ModelEvaluationReport report = latest.EvaluationReport!;
            ModelProfileMeasured measured = new ModelProfileMeasured
            {
                RunId = !String.IsNullOrEmpty(report.RunId) ? report.RunId : (latest.EvaluationRunId ?? String.Empty),
                RevisionId = latest.Id,
                BatteryRunId = report.BatteryRunId,
                MeasuredUtc = report.CreatedUtc,
                PackIds = new List<string>(report.Packs)
            };

            foreach (ModelEvaluationCount probe in report.Probes)
            {
                string key = probe.Key.StartsWith(_ProbePrefix, StringComparison.Ordinal) ? probe.Key.Substring(_ProbePrefix.Length) : probe.Key;
                ModelProfileMeasuredCount count = new ModelProfileMeasuredCount { Key = key };
                Add(count, probe);
                measured.Probes.Add(count);
            }

            foreach (ModelEvaluationPersonaOutcome outcome in report.PersonaOutcomes.OrderBy(o => o.Persona, StringComparer.Ordinal))
            {
                ModelProfileMeasuredPersona persona = new ModelProfileMeasuredPersona { Persona = outcome.Persona };
                SortedDictionary<string, ModelProfileMeasuredCount> byPack = new SortedDictionary<string, ModelProfileMeasuredCount>(StringComparer.Ordinal);
                foreach (ModelEvaluationCount task in outcome.Tasks)
                {
                    string pack = PackOf(task.Key);
                    if (!byPack.TryGetValue(pack, out ModelProfileMeasuredCount? count))
                    {
                        count = new ModelProfileMeasuredCount { Key = pack };
                        byPack[pack] = count;
                    }

                    Add(count, task);
                }

                persona.Packs = byPack.Values.ToList();
                measured.Personas.Add(persona);
            }

            return measured;
        }

        #endregion

        #region Private-Methods

        private static void Add(ModelProfileMeasuredCount count, ModelEvaluationCount source)
        {
            count.Passed += source.Passed;
            count.Failed += source.Failed;
            count.CouldNotRun += source.CouldNotRun;
            count.PendingEvaluation += source.PendingEvaluation;
            count.Attempted += source.Passed + source.Failed + source.CouldNotRun + source.PendingEvaluation;
        }

        private static string PackOf(string? taskKey)
        {
            // Task keys are "task:<pack>/<task>" (ModelEvaluationReportBuilder.TaskKey).
            string key = taskKey ?? String.Empty;
            if (key.StartsWith(_TaskPrefix, StringComparison.Ordinal)) key = key.Substring(_TaskPrefix.Length);
            int slash = key.IndexOf('/');
            return slash >= 0 ? key.Substring(0, slash) : key;
        }

        #endregion
    }
}
