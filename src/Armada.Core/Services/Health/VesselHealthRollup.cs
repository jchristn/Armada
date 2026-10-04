namespace Armada.Core.Services.Health
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Applies manual overrides to raw findings and rolls the effective statuses up into the overall status.
    /// Rollup rules: the overall status is the worst of Pass, Warn, and Fail among the scored criteria (Fail is worse
    /// than Warn, which is worse than Pass). Unknown and NotApplicable are neutral: they never count as Pass and never
    /// count as Fail. When no scored criterion graded Pass, Warn, or Fail, the overall status is NotApplicable if every
    /// scored criterion is NotApplicable, otherwise Unknown (so a vessel whose every scored criterion is Unknown shows
    /// Unknown, never Pass). An override on the Overall criterion replaces the computed overall status. Stateless and
    /// thread-safe.
    /// </summary>
    public static class VesselHealthRollup
    {
        #region Public-Methods

        /// <summary>
        /// Compute the effective status of every criterion: the override status when an override exists, otherwise
        /// the raw finding status. Criteria with neither are absent from the result.
        /// </summary>
        /// <param name="findings">Raw findings (null is treated as empty).</param>
        /// <param name="overrides">Manual overrides (null is treated as empty).</param>
        /// <returns>Effective status per criterion.</returns>
        public static Dictionary<VesselHealthCriterionEnum, VesselHealthStatusEnum> ComputeEffective(
            IEnumerable<VesselHealthFinding>? findings,
            IEnumerable<VesselHealthOverride>? overrides)
        {
            Dictionary<VesselHealthCriterionEnum, VesselHealthStatusEnum> effective = new Dictionary<VesselHealthCriterionEnum, VesselHealthStatusEnum>();
            if (findings != null)
            {
                foreach (VesselHealthFinding finding in findings)
                {
                    if (finding == null) continue;
                    effective[finding.Criterion] = finding.Status;
                }
            }

            if (overrides != null)
            {
                foreach (VesselHealthOverride item in overrides)
                {
                    if (item == null) continue;
                    effective[item.Criterion] = item.Status;
                }
            }

            return effective;
        }

        /// <summary>
        /// Roll effective statuses up into the overall status over the scored criteria (see the class remarks). The
        /// Overall entry of <paramref name="effective"/>, if any, is ignored here; callers apply an Overall override
        /// separately (see <see cref="ApplyEffectiveStatuses"/>).
        /// </summary>
        /// <param name="effective">Effective status per criterion; a scored criterion that is absent counts as Unknown.</param>
        /// <param name="scoredCriteria">Criteria that feed the rollup (null or empty yields Unknown).</param>
        /// <returns>The overall status.</returns>
        public static VesselHealthStatusEnum ComputeOverall(
            IReadOnlyDictionary<VesselHealthCriterionEnum, VesselHealthStatusEnum> effective,
            IEnumerable<VesselHealthCriterionEnum>? scoredCriteria)
        {
            if (effective == null) throw new ArgumentNullException(nameof(effective));
            if (scoredCriteria == null) return VesselHealthStatusEnum.Unknown;

            List<VesselHealthStatusEnum> statuses = new List<VesselHealthStatusEnum>();
            foreach (VesselHealthCriterionEnum criterion in scoredCriteria.Distinct())
            {
                if (criterion == VesselHealthCriterionEnum.Overall) continue;
                statuses.Add(effective.TryGetValue(criterion, out VesselHealthStatusEnum status) ? status : VesselHealthStatusEnum.Unknown);
            }

            if (statuses.Count == 0) return VesselHealthStatusEnum.Unknown;
            if (statuses.Contains(VesselHealthStatusEnum.Fail)) return VesselHealthStatusEnum.Fail;
            if (statuses.Contains(VesselHealthStatusEnum.Warn)) return VesselHealthStatusEnum.Warn;
            if (statuses.Contains(VesselHealthStatusEnum.Pass)) return VesselHealthStatusEnum.Pass;
            if (statuses.All(s => s == VesselHealthStatusEnum.NotApplicable)) return VesselHealthStatusEnum.NotApplicable;
            return VesselHealthStatusEnum.Unknown;
        }

        /// <summary>
        /// Write the effective (override-aware) per-criterion status columns and the overall status onto a health row.
        /// Criteria without a finding or override get Unknown. An Overall override replaces the computed overall status.
        /// </summary>
        /// <param name="health">Health row to update.</param>
        /// <param name="findings">Raw findings.</param>
        /// <param name="overrides">Manual overrides.</param>
        /// <param name="scoredCriteria">Criteria that feed the rollup.</param>
        /// <exception cref="ArgumentNullException">Thrown when health is null.</exception>
        public static void ApplyEffectiveStatuses(
            VesselHealth health,
            IEnumerable<VesselHealthFinding>? findings,
            IEnumerable<VesselHealthOverride>? overrides,
            IEnumerable<VesselHealthCriterionEnum>? scoredCriteria)
        {
            if (health == null) throw new ArgumentNullException(nameof(health));

            Dictionary<VesselHealthCriterionEnum, VesselHealthStatusEnum> effective = ComputeEffective(findings, overrides);
            health.DivergenceStatus = Get(effective, VesselHealthCriterionEnum.GitDivergence);
            health.WorkingTreeStatus = Get(effective, VesselHealthCriterionEnum.WorkingTree);
            health.BranchStatus = Get(effective, VesselHealthCriterionEnum.Branches);
            health.DependencyStatus = Get(effective, VesselHealthCriterionEnum.Dependencies);
            health.VulnerabilityStatus = Get(effective, VesselHealthCriterionEnum.Vulnerabilities);
            health.TestInfraStatus = Get(effective, VesselHealthCriterionEnum.TestInfrastructure);
            health.CiStatus = Get(effective, VesselHealthCriterionEnum.ContinuousIntegration);
            health.ReadinessStatus = Get(effective, VesselHealthCriterionEnum.ArmadaReadiness);
            health.MissionOutcomeStatus = Get(effective, VesselHealthCriterionEnum.MissionOutcomes);

            VesselHealthOverride? overallOverride = overrides?.FirstOrDefault(o => o != null && o.Criterion == VesselHealthCriterionEnum.Overall);
            health.OverallStatus = overallOverride != null
                ? overallOverride.Status
                : ComputeOverall(effective, scoredCriteria);
        }

        #endregion

        #region Private-Methods

        private static VesselHealthStatusEnum Get(Dictionary<VesselHealthCriterionEnum, VesselHealthStatusEnum> effective, VesselHealthCriterionEnum criterion)
        {
            return effective.TryGetValue(criterion, out VesselHealthStatusEnum status) ? status : VesselHealthStatusEnum.Unknown;
        }

        #endregion
    }
}
