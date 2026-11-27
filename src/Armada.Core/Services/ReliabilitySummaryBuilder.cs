namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Groups stage outcomes (one per attempt) into reliability rows per (model, persona), then sorts and pages them.
    /// Pure: the caller reads and scopes the outcomes.
    /// </summary>
    public static class ReliabilitySummaryBuilder
    {
        #region Public-Members

        /// <summary>
        /// Model shown for an attempt that recorded none.
        /// </summary>
        public const string UnknownModel = "(unknown)";

        /// <summary>
        /// Persona shown for a mission without one.
        /// </summary>
        public const string NoPersona = "(none)";

        /// <summary>
        /// Model and persona of the totals row.
        /// </summary>
        public const string All = "(all)";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the summary.
        /// </summary>
        /// <param name="outcomes">Outcomes in the window, already scoped to the caller.</param>
        /// <param name="query">Sort and paging (filters are applied by the caller).</param>
        /// <param name="fromUtc">Start of the window.</param>
        /// <param name="toUtc">End of the window.</param>
        /// <param name="truncated">True when the outcomes were cut to the newest ones.</param>
        /// <returns>The summary.</returns>
        /// <exception cref="ArgumentNullException">Thrown when outcomes or query is null.</exception>
        public static ReliabilitySummary Build(IReadOnlyList<StageOutcome> outcomes, ReliabilitySummaryQuery query, DateTime fromUtc, DateTime toUtc, bool truncated)
        {
            if (outcomes == null) throw new ArgumentNullException(nameof(outcomes));
            if (query == null) throw new ArgumentNullException(nameof(query));

            Dictionary<string, List<StageOutcome>> groups = new Dictionary<string, List<StageOutcome>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string[]> names = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (StageOutcome outcome in outcomes)
            {
                string model = String.IsNullOrWhiteSpace(outcome.Model) ? UnknownModel : outcome.Model!.Trim();
                string persona = String.IsNullOrWhiteSpace(outcome.Persona) ? NoPersona : outcome.Persona!.Trim();
                string key = model + "\n" + persona;
                if (!groups.TryGetValue(key, out List<StageOutcome>? list))
                {
                    list = new List<StageOutcome>();
                    groups[key] = list;
                    names[key] = new[] { model, persona };
                }

                list.Add(outcome);
            }

            List<ReliabilitySummaryRow> rows = groups.Select(g => Row(names[g.Key][0], names[g.Key][1], g.Value)).ToList();
            rows = Sort(rows, query.Sort, query.Descending);

            int pageSize = Math.Clamp(query.PageSize, 1, 1000);
            int pageNumber = Math.Max(1, query.PageNumber);
            ReliabilitySummary summary = new ReliabilitySummary
            {
                FromUtc = fromUtc,
                ToUtc = toUtc,
                OutcomesRead = outcomes.Count,
                Truncated = truncated,
                Totals = Row(All, All, outcomes),
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = rows.Count,
                TotalPages = rows.Count == 0 ? 0 : (rows.Count + pageSize - 1) / pageSize,
                Rows = rows.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList()
            };
            return summary;
        }

        #endregion

        #region Private-Methods

        private static ReliabilitySummaryRow Row(string model, string persona, IEnumerable<StageOutcome> outcomes)
        {
            ReliabilitySummaryRow row = new ReliabilitySummaryRow { Model = model, Persona = persona };
            List<long> durations = new List<long>();
            foreach (StageOutcome outcome in outcomes)
            {
                row.Attempts++;
                switch (outcome.Disposition)
                {
                    case StageDispositionEnum.Completed: row.Completed++; break;
                    case StageDispositionEnum.Failed: row.Failed++; break;
                    case StageDispositionEnum.Relaunched: row.Relaunched++; break;
                    case StageDispositionEnum.Escalated: row.Escalations++; break;
                }

                bool contractRepair = outcome.ContractRepairKind.HasValue && outcome.ContractRepairKind.Value != ContractRepairKindEnum.None;
                if (outcome.Disposition == StageDispositionEnum.OutputRepair || contractRepair) row.Repairs++;
                if (outcome.RuntimeFailureKind == RuntimeFailureKindEnum.Unclassified) row.Unclassified++;
                if (outcome.FailureKind.HasValue) Increment(row.FailureKinds, outcome.FailureKind.Value.ToString());
                if (outcome.RuntimeFailureKind.HasValue && outcome.RuntimeFailureKind.Value != RuntimeFailureKindEnum.Clean)
                    Increment(row.RuntimeFailureKinds, outcome.RuntimeFailureKind.Value.ToString());
                if (outcome.DurationMs.HasValue && outcome.DurationMs.Value >= 0) durations.Add(outcome.DurationMs.Value);
            }

            int finished = row.Completed + row.Failed + row.Escalations;
            row.CompletionRate = finished == 0 ? 0 : Math.Round((double)row.Completed / finished, 4);
            row.UnclassifiedRate = row.Attempts == 0 ? 0 : Math.Round((double)row.Unclassified / row.Attempts, 4);
            if (durations.Count > 0)
            {
                durations.Sort();
                row.MedianDurationMs = durations[durations.Count / 2];
            }

            return row;
        }

        private static void Increment(Dictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int current);
            counts[key] = current + 1;
        }

        private static List<ReliabilitySummaryRow> Sort(List<ReliabilitySummaryRow> rows, ReliabilitySortEnum sort, bool descending)
        {
            IOrderedEnumerable<ReliabilitySummaryRow> ordered = sort switch
            {
                ReliabilitySortEnum.CompletionRate => descending ? rows.OrderByDescending(r => r.CompletionRate) : rows.OrderBy(r => r.CompletionRate),
                ReliabilitySortEnum.UnclassifiedRate => descending ? rows.OrderByDescending(r => r.UnclassifiedRate) : rows.OrderBy(r => r.UnclassifiedRate),
                ReliabilitySortEnum.Repairs => descending ? rows.OrderByDescending(r => r.Repairs) : rows.OrderBy(r => r.Repairs),
                ReliabilitySortEnum.Escalations => descending ? rows.OrderByDescending(r => r.Escalations) : rows.OrderBy(r => r.Escalations),
                ReliabilitySortEnum.Failures => descending ? rows.OrderByDescending(r => r.Failed) : rows.OrderBy(r => r.Failed),
                ReliabilitySortEnum.Model => descending ? rows.OrderByDescending(r => r.Model, StringComparer.OrdinalIgnoreCase) : rows.OrderBy(r => r.Model, StringComparer.OrdinalIgnoreCase),
                ReliabilitySortEnum.Persona => descending ? rows.OrderByDescending(r => r.Persona, StringComparer.OrdinalIgnoreCase) : rows.OrderBy(r => r.Persona, StringComparer.OrdinalIgnoreCase),
                _ => descending ? rows.OrderByDescending(r => r.Attempts) : rows.OrderBy(r => r.Attempts)
            };

            // Stable tie-break so pages never overlap.
            return ordered.ThenBy(r => r.Model, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Persona, StringComparer.OrdinalIgnoreCase).ToList();
        }

        #endregion
    }
}
