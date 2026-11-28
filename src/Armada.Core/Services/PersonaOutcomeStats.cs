namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Per model profile and persona results from stage outcomes, each attempt weighted by its age with a half-life
    /// (RS-8.6). Uses the scorecard's attribution rules: evaluation attempts, attempts marked NotTheModel, infrastructure,
    /// operator and runtime environment failures and unsettled attempts are not counted, and a Judge verdict is charged to
    /// the attempt whose work it reviewed. Side-effect free.
    /// </summary>
    public static class PersonaOutcomeStats
    {
        #region Public-Methods

        /// <summary>
        /// The dictionary key of a profile and persona.
        /// </summary>
        /// <param name="profileKey">Model profile identity key.</param>
        /// <param name="persona">Persona name.</param>
        /// <returns>The key.</returns>
        public static string Key(string profileKey, string? persona)
        {
            return (profileKey ?? "") + "\n" + PersonaCatalog.NormalizeName(persona).ToLowerInvariant();
        }

        /// <summary>
        /// The weight of an attempt that ended at a time: 1 now, one half after one half-life.
        /// </summary>
        /// <param name="endedUtc">When the attempt ended.</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="halfLifeDays">Half-life in days (at least 1).</param>
        /// <returns>The weight, 0 to 1.</returns>
        public static double Weight(DateTime endedUtc, DateTime nowUtc, int halfLifeDays)
        {
            double age = (nowUtc.ToUniversalTime() - endedUtc.ToUniversalTime()).TotalDays;
            if (age < 0) age = 0;
            return Math.Pow(0.5, age / Math.Max(1, halfLifeDays));
        }

        /// <summary>
        /// Compute the scores of every (profile, persona) pair the outcomes cover.
        /// </summary>
        /// <param name="outcomes">Stage outcomes of one tenant (every persona, so Judge verdicts can be charged).</param>
        /// <param name="nowUtc">Now.</param>
        /// <param name="halfLifeDays">Half-life in days.</param>
        /// <returns>Scores by <see cref="Key"/>.</returns>
        /// <exception cref="ArgumentNullException">The outcomes are null.</exception>
        public static Dictionary<string, PersonaOutcomeScore> Compute(IEnumerable<StageOutcome> outcomes, DateTime nowUtc, int halfLifeDays)
        {
            if (outcomes == null) throw new ArgumentNullException(nameof(outcomes));
            List<StageOutcome> all = outcomes.Where(o => o != null).ToList();
            Dictionary<string, List<StageOutcome>> byMission = new Dictionary<string, List<StageOutcome>>(StringComparer.Ordinal);
            foreach (StageOutcome outcome in all)
            {
                if (!byMission.TryGetValue(outcome.MissionId, out List<StageOutcome>? list))
                {
                    list = new List<StageOutcome>();
                    byMission[outcome.MissionId] = list;
                }
                list.Add(outcome);
            }

            Dictionary<string, Accumulator> sums = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
            Dictionary<string, string> keyOfOutcome = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (StageOutcome outcome in all)
            {
                string persona = PersonaCatalog.NormalizeName(outcome.Persona);
                if (persona.Length == 0) continue;
                if (ModelScorecardService.ExclusionOf(outcome, persona) != null) continue;
                string? profileKey = ProfileKeyOf(outcome);
                if (profileKey == null) continue;
                string key = Key(profileKey, persona);
                if (!sums.TryGetValue(key, out Accumulator? sum))
                {
                    sum = new Accumulator { ProfileKey = profileKey, Persona = persona };
                    sums[key] = sum;
                }
                keyOfOutcome[outcome.Id] = key;
                double w = Weight(outcome.EndedUtc, nowUtc, halfLifeDays);
                sum.Samples++;
                sum.Weight += w;
                if (outcome.Disposition == StageDispositionEnum.Completed && outcome.StageCheckStatus != StageCheckStatusEnum.Failed) sum.Success += w;
                if (outcome.FailureKind == MissionFailureKindEnum.NoOp) sum.NoOp += w;
                if (outcome.SuspectedCause != FailureCauseEnum.ToolUse) sum.ToolValid += w;
                if (outcome.Disposition == StageDispositionEnum.OutputRepair
                    || (outcome.ContractRepairKind.HasValue && outcome.ContractRepairKind.Value != ContractRepairKindEnum.None)) sum.Repair += w;
            }

            foreach (StageOutcome judge in all)
            {
                if (!PersonaCatalog.Matches(judge.Persona, PersonaCatalog.Judge)) continue;
                if (judge.Disposition != StageDispositionEnum.Completed || String.IsNullOrWhiteSpace(judge.JudgeVerdict)) continue;
                if (judge.IsEvaluation || judge.SuspectedCause == FailureCauseEnum.NotTheModel) continue;
                if (String.IsNullOrEmpty(judge.ReviewedMissionId) || !byMission.TryGetValue(judge.ReviewedMissionId, out List<StageOutcome>? reviewedMission)) continue;
                JudgeVerdictEnum verdict = ModelScorecardService.ParseVerdict(judge.JudgeVerdict);
                if (verdict != JudgeVerdictEnum.Pass && verdict != JudgeVerdictEnum.NeedsRevision && verdict != JudgeVerdictEnum.Fail) continue;
                StageOutcome? reviewed = ModelScorecardService.ReviewedAttempt(reviewedMission, judge);
                if (reviewed == null || !keyOfOutcome.TryGetValue(reviewed.Id, out string? key)) continue;
                Accumulator sum = sums[key];
                double w = Weight(judge.EndedUtc, nowUtc, halfLifeDays);
                sum.JudgeWeight += w;
                if (verdict == JudgeVerdictEnum.Pass) sum.JudgePass += w;
            }

            Dictionary<string, PersonaOutcomeScore> scores = new Dictionary<string, PersonaOutcomeScore>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Accumulator> entry in sums)
            {
                Accumulator sum = entry.Value;
                PersonaOutcomeScore score = new PersonaOutcomeScore { ProfileKey = sum.ProfileKey, Persona = sum.Persona, Samples = sum.Samples };
                if (sum.Weight > 0)
                {
                    score.SuccessRate = sum.Success / sum.Weight;
                    score.NoOpRate = sum.NoOp / sum.Weight;
                    score.ToolCallValidity = sum.ToolValid / sum.Weight;
                    score.RepairRate = sum.Repair / sum.Weight;
                }
                if (sum.JudgeWeight > 0) score.JudgePassRate = sum.JudgePass / sum.JudgeWeight;
                List<double> parts = new List<double>();
                if (score.SuccessRate.HasValue) parts.Add(score.SuccessRate.Value);
                if (score.JudgePassRate.HasValue) parts.Add(score.JudgePassRate.Value);
                if (score.NoOpRate.HasValue) parts.Add(1 - score.NoOpRate.Value);
                if (score.ToolCallValidity.HasValue) parts.Add(score.ToolCallValidity.Value);
                if (score.RepairRate.HasValue) parts.Add(1 - score.RepairRate.Value);
                score.Score = parts.Count == 0 ? 0 : parts.Average();
                scores[entry.Key] = score;
            }
            return scores;
        }

        /// <summary>
        /// The model profile identity key of an attempt, or null when it records no runtime.
        /// </summary>
        /// <param name="outcome">Stage outcome.</param>
        /// <returns>The key, or null.</returns>
        public static string? ProfileKeyOf(StageOutcome outcome)
        {
            ModelProfileObservation? observation = ModelProfileService.ObservationFrom(outcome);
            if (observation == null) return null;
            return ModelProfileRules.IdentityKey(outcome.TenantId, ModelProfileRules.IdentityOf(observation));
        }

        #endregion

        #region Private-Types

        private sealed class Accumulator
        {
            public string ProfileKey { get; set; } = "";
            public string Persona { get; set; } = "";
            public int Samples { get; set; } = 0;
            public double Weight { get; set; } = 0;
            public double Success { get; set; } = 0;
            public double NoOp { get; set; } = 0;
            public double ToolValid { get; set; } = 0;
            public double Repair { get; set; } = 0;
            public double JudgeWeight { get; set; } = 0;
            public double JudgePass { get; set; } = 0;
        }

        #endregion
    }
}
