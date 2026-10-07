namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Predicts landing behavior and blockers for vessels and missions.
    /// </summary>
    public class LandingPreviewService
    {
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly ArmadaSettings? _Settings;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="settings">Settings, for the Admiral's default landing mode (optional).</param>
        public LandingPreviewService(DatabaseDriver database, LoggingModule logging, ArmadaSettings? settings = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Settings = settings;
        }

        /// <summary>
        /// Preview landing behavior for a vessel and optional branch.
        /// </summary>
        public async Task<LandingPreviewResult> PreviewForVesselAsync(
            AuthContext auth,
            Vessel vessel,
            string? sourceBranch = null,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));

            LandingPreviewResult result = new LandingPreviewResult
            {
                VesselId = vessel.Id,
                SourceBranch = NormalizeEmpty(sourceBranch),
                TargetBranch = !String.IsNullOrWhiteSpace(vessel.DefaultBranch) ? vessel.DefaultBranch : "main",
                LandingMode = vessel.LandingMode,
                BranchCleanupPolicy = vessel.BranchCleanupPolicy,
                RequirePassingChecksToLand = vessel.RequirePassingChecksToLand,
                RequirePullRequestForProtectedBranches = vessel.RequirePullRequestForProtectedBranches,
                RequireMergeQueueForReleaseBranches = vessel.RequireMergeQueueForReleaseBranches
            };

            result.BranchCategory = DetermineBranchCategory(result.SourceBranch, result.TargetBranch, vessel.ReleaseBranchPrefix, vessel.HotfixBranchPrefix);
            result.ProtectedBranchMatch = DetermineProtectedBranchMatch(vessel, result.TargetBranch);
            result.TargetBranchProtected = !String.IsNullOrWhiteSpace(result.ProtectedBranchMatch);
            await PopulateCheckSummaryAsync(auth, result, token).ConfigureAwait(false);
            EvaluateCommonIssues(result);
            EvaluateBranchPolicyIssues(result);
            FinalizeResult(result);
            return result;
        }

        /// <summary>
        /// Preview landing behavior for a mission.
        /// </summary>
        public async Task<LandingPreviewResult> PreviewForMissionAsync(
            AuthContext auth,
            Vessel vessel,
            Mission mission,
            CancellationToken token = default)
        {
            if (auth == null) throw new ArgumentNullException(nameof(auth));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (mission == null) throw new ArgumentNullException(nameof(mission));

            LandingPreviewResult result = new LandingPreviewResult
            {
                VesselId = vessel.Id,
                MissionId = mission.Id,
                SourceBranch = NormalizeEmpty(mission.BranchName),
                TargetBranch = !String.IsNullOrWhiteSpace(vessel.DefaultBranch) ? vessel.DefaultBranch : "main",
                LandingMode = vessel.LandingMode,
                BranchCleanupPolicy = vessel.BranchCleanupPolicy,
                RequirePassingChecksToLand = vessel.RequirePassingChecksToLand,
                RequirePullRequestForProtectedBranches = vessel.RequirePullRequestForProtectedBranches,
                RequireMergeQueueForReleaseBranches = vessel.RequireMergeQueueForReleaseBranches
            };

            result.BranchCategory = DetermineBranchCategory(result.SourceBranch, result.TargetBranch, vessel.ReleaseBranchPrefix, vessel.HotfixBranchPrefix);
            result.ProtectedBranchMatch = DetermineProtectedBranchMatch(vessel, result.TargetBranch);
            result.TargetBranchProtected = !String.IsNullOrWhiteSpace(result.ProtectedBranchMatch);
            await PopulateCheckSummaryAsync(auth, result, token).ConfigureAwait(false);
            EvaluateCommonIssues(result);
            EvaluateBranchPolicyIssues(result);
            // The mode the landing handler will actually use: voyage, then vessel, then the Admiral default.
            Voyage? voyage = String.IsNullOrEmpty(mission.VoyageId) ? null : await _Database.Voyages.ReadAsync(mission.VoyageId, token).ConfigureAwait(false);
            result.EffectiveLandingMode = voyage?.LandingMode ?? vessel.LandingMode ?? _Settings?.LandingMode ?? LandingModeEnum.MergeAndPush;
            result.ManualLandingOnly = result.EffectiveLandingMode == LandingModeEnum.None;
            result.MissionStatus = mission.Status;
            bool landable = EvaluateMissionIssues(result, mission);
            FinalizeResult(result);

            // Only a mission that is waiting to land can be ready to land, and never when landing is manual only (Land
            // would be refused). A Pending mission no longer shows Ready To Land next to "not in a landing state".
            if (!landable || result.ManualLandingOnly) result.IsReadyToLand = false;
            return result;
        }

        private async Task PopulateCheckSummaryAsync(AuthContext auth, LandingPreviewResult result, CancellationToken token)
        {
            CheckRunQuery query = new CheckRunQuery
            {
                TenantId = auth.IsAdmin ? null : auth.TenantId,
                UserId = auth.IsAdmin || auth.IsTenantAdmin ? null : auth.UserId,
                VesselId = result.VesselId,
                MissionId = result.MissionId,
                PageNumber = 1,
                PageSize = 1000
            };

            EnumerationResult<CheckRun> runs = await _Database.CheckRuns.EnumerateAsync(query, token).ConfigureAwait(false);
            IEnumerable<CheckRun> filtered = runs.Objects;
            if (!String.IsNullOrWhiteSpace(result.SourceBranch))
            {
                filtered = filtered.Where(run =>
                    String.Equals(run.BranchName, result.SourceBranch, StringComparison.OrdinalIgnoreCase));
            }

            List<CheckRun> ordered = filtered
                .OrderByDescending(run => run.CompletedUtc ?? run.CreatedUtc)
                .ThenByDescending(run => run.CreatedUtc)
                .ToList();
            CheckRun? latest = ordered.FirstOrDefault();
            if (latest != null)
            {
                result.LatestCheckRunId = latest.Id;
                result.LatestCheckStatus = latest.Status;
                result.LatestCheckSummary = latest.Summary;
            }

            result.HasPassingChecks = ordered.Any(run =>
                run.Status == CheckRunStatusEnum.Passed
                || (run.CompletedUtc.HasValue && run.ExitCode.HasValue && run.ExitCode.Value == 0));
        }

        private static void EvaluateCommonIssues(LandingPreviewResult result)
        {
            result.ExpectedLandingAction = DescribeLandingAction(result.LandingMode);

            if (String.IsNullOrWhiteSpace(result.SourceBranch))
            {
                AddIssue(
                    result,
                    "source_branch_missing",
                    ReadinessSeverityEnum.Warning,
                    "Source branch is unknown",
                    "Armada cannot fully predict landing behavior without a source branch.");
            }

            if (!result.LandingMode.HasValue)
            {
                AddIssue(
                    result,
                    "landing_mode_inherited",
                    ReadinessSeverityEnum.Warning,
                    "Landing mode is inherited",
                    "This vessel does not declare a landing mode directly, so global or voyage settings may change the final behavior.");
            }
            else if (result.LandingMode.Value == LandingModeEnum.None)
            {
                AddIssue(
                    result,
                    "manual_landing_only",
                    ReadinessSeverityEnum.Warning,
                    "Landing is manual",
                    "This vessel is configured for manual landing only. Armada will not merge or open a pull request automatically.");
            }

            if (!String.IsNullOrWhiteSpace(result.SourceBranch)
                && String.Equals(result.SourceBranch, result.TargetBranch, StringComparison.OrdinalIgnoreCase))
            {
                AddIssue(
                    result,
                    "source_matches_target",
                    ReadinessSeverityEnum.Error,
                    "Source branch matches target branch",
                    "Landing would target the same branch name that is already checked out as the source.");
            }

            if (result.RequirePassingChecksToLand && !result.HasPassingChecks)
            {
                AddIssue(
                    result,
                    "passing_checks_required",
                    ReadinessSeverityEnum.Error,
                    "Passing checks are required",
                    "This vessel requires at least one passing structured check before landing may proceed.");
            }
            else if (!result.HasPassingChecks)
            {
                AddIssue(
                    result,
                    "no_passing_checks",
                    ReadinessSeverityEnum.Warning,
                    "No passing checks found",
                    "No passing structured checks were found for the current branch or mission context.");
            }
        }

        private static void EvaluateBranchPolicyIssues(LandingPreviewResult result)
        {
            if (result.TargetBranchProtected)
            {
                AddIssue(
                    result,
                    "protected_branch_target",
                    ReadinessSeverityEnum.Info,
                    "Target branch matches protected policy",
                    "The target branch matches protected policy '" + result.ProtectedBranchMatch + "'.");
            }

            if (result.TargetBranchProtected && result.RequirePullRequestForProtectedBranches)
            {
                if (result.LandingMode != LandingModeEnum.PullRequest && result.LandingMode != LandingModeEnum.MergeQueue)
                {
                    AddIssue(
                        result,
                        "protected_branch_requires_pr",
                        ReadinessSeverityEnum.Error,
                        "Protected branch requires PR-oriented landing",
                        "This target branch is protected and requires pull-request or merge-queue landing.");
                }
            }

            if (String.Equals(result.BranchCategory, "Release", StringComparison.OrdinalIgnoreCase)
                && result.RequireMergeQueueForReleaseBranches
                && result.LandingMode != LandingModeEnum.MergeQueue)
            {
                AddIssue(
                    result,
                    "release_branch_requires_merge_queue",
                    ReadinessSeverityEnum.Error,
                    "Release branches require merge queue",
                    "This vessel requires release branches to land through merge queue.");
            }

            if (String.Equals(result.BranchCategory, "Hotfix", StringComparison.OrdinalIgnoreCase)
                && result.TargetBranchProtected
                && result.RequirePullRequestForProtectedBranches
                && (result.LandingMode == LandingModeEnum.LocalMerge || result.LandingMode == LandingModeEnum.MergeAndPush))
            {
                AddIssue(
                    result,
                    "hotfix_branch_local_merge_warning",
                    ReadinessSeverityEnum.Warning,
                    "Hotfix branch is configured for direct local merge",
                    "This hotfix will land directly into a protected branch unless the landing mode is changed.");
            }
        }

        private static bool EvaluateMissionIssues(LandingPreviewResult result, Mission mission)
        {
            if (mission.Status == MissionStatusEnum.Complete)
            {
                AddIssue(
                    result,
                    "mission_already_landed",
                    ReadinessSeverityEnum.Info,
                    "Mission is already complete",
                    "This mission is Complete; there is nothing left to land.");
                return false;
            }

            if (mission.Status != MissionStatusEnum.WorkProduced
                && mission.Status != MissionStatusEnum.PullRequestOpen
                && mission.Status != MissionStatusEnum.LandingFailed)
            {
                AddIssue(
                    result,
                    "mission_not_landable",
                    ReadinessSeverityEnum.Warning,
                    "Mission is not in a landing state",
                    "This mission is currently '" + mission.Status + "' and may not yet be ready for landing.");
                return false;
            }

            return true;
        }

        private static void FinalizeResult(LandingPreviewResult result)
        {
            result.IsReadyToLand = !result.Issues.Any(issue => issue.Severity == ReadinessSeverityEnum.Error);
        }

        private static string DetermineBranchCategory(string? sourceBranch, string targetBranch, string? releasePrefix, string? hotfixPrefix)
        {
            if (String.IsNullOrWhiteSpace(sourceBranch))
                return "Unknown";
            if (String.Equals(sourceBranch, targetBranch, StringComparison.OrdinalIgnoreCase))
                return "Default";
            string normalizedReleasePrefix = NormalizeBranchPrefix(releasePrefix, "release/");
            string normalizedHotfixPrefix = NormalizeBranchPrefix(hotfixPrefix, "hotfix/");
            if (sourceBranch.StartsWith(normalizedReleasePrefix, StringComparison.OrdinalIgnoreCase))
                return "Release";
            if (sourceBranch.StartsWith(normalizedHotfixPrefix, StringComparison.OrdinalIgnoreCase))
                return "Hotfix";
            return "Feature";
        }

        private static string? DetermineProtectedBranchMatch(Vessel vessel, string targetBranch)
        {
            List<string> patterns = vessel.ProtectedBranchPatterns ?? new List<string>();
            foreach (string pattern in patterns.Where(item => !String.IsNullOrWhiteSpace(item)))
            {
                if (MatchesBranchPattern(targetBranch, pattern))
                    return pattern;
            }

            if (vessel.RequirePullRequestForProtectedBranches
                && patterns.Count == 0
                && String.Equals(targetBranch, vessel.DefaultBranch, StringComparison.OrdinalIgnoreCase))
            {
                return vessel.DefaultBranch;
            }

            return null;
        }

        private static bool MatchesBranchPattern(string branchName, string pattern)
        {
            string trimmedPattern = pattern.Trim();
            if (String.IsNullOrWhiteSpace(trimmedPattern))
                return false;
            if (String.Equals(branchName, trimmedPattern, StringComparison.OrdinalIgnoreCase))
                return true;

            string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(trimmedPattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(branchName, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        private static string NormalizeBranchPrefix(string? value, string fallback)
        {
            return String.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string DescribeLandingAction(LandingModeEnum? landingMode)
        {
            if (!landingMode.HasValue)
                return "Inherited or manual landing";

            switch (landingMode.Value)
            {
                case LandingModeEnum.PullRequest:
                    return "Open or update a pull request";
                case LandingModeEnum.MergeQueue:
                    return "Enqueue the branch in merge queue";
                case LandingModeEnum.LocalMerge:
                    return "Merge the branch into the target branch in the working directory, without pushing";
                case LandingModeEnum.MergeAndPush:
                    return "Merge the branch into the target branch in the working directory, then push it";
                case LandingModeEnum.None:
                    return "Manual landing only";
                default:
                    return landingMode.Value.ToString();
            }
        }

        private static void AddIssue(
            LandingPreviewResult result,
            string code,
            ReadinessSeverityEnum severity,
            string title,
            string message)
        {
            result.Issues.Add(new LandingPreviewIssue
            {
                Code = code,
                Severity = severity,
                Title = title,
                Message = message
            });
        }

        private static string? NormalizeEmpty(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
