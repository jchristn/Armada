namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Computes the read-only scorecard of one (model profile, persona) from stage outcomes, token usage and timing
    /// (docs/MODEL_HETEROGENEITY.md, The scorecard). It reads existing tables only and changes nothing.
    /// <para>
    /// Attribution rules: outcomes of evaluation and battery missions, outcomes whose suspected cause is NotTheModel, and
    /// (unless a person classified the cause by hand) Infra, OperatorAction, cancelled, interrupted and runtime-environment
    /// failures are excluded; an outcome of this profile in another persona is charged to that stage, not this one; a
    /// Judge verdict counts only when the work it reviewed (the reviewed mission's last completed attempt) is an attempt
    /// of this profile in this persona; Unknown and Mixed causes are their own buckets. Each rule reports how many items it
    /// removed.
    /// </para>
    /// <para>
    /// The window is the last WindowDays days or the last WindowMissions missions (by their latest counted attempt),
    /// whichever is smaller. Revisions form separate groups unless marked compatible; an outcome belongs to the newest
    /// revision it pools into that existed when it was recorded.
    /// </para>
    /// </summary>
    public class ModelScorecardService
    {
        #region Public-Members

        /// <summary>
        /// Rule: evaluation and battery missions.
        /// </summary>
        public const string RuleEvaluation = "Evaluation";

        /// <summary>
        /// Rule: suspected cause NotTheModel.
        /// </summary>
        public const string RuleNotTheModel = "NotTheModel";

        /// <summary>
        /// Rule: failure kind Infra.
        /// </summary>
        public const string RuleInfra = "Infra";

        /// <summary>
        /// Rule: failure kind OperatorAction or a cancelled attempt.
        /// </summary>
        public const string RuleOperatorAction = "OperatorAction";

        /// <summary>
        /// Rule: interrupted attempts, crashes and runtime environment failures.
        /// </summary>
        public const string RuleRuntimeEnvironment = "RuntimeEnvironment";

        /// <summary>
        /// Rule: attempts whose result is not settled yet.
        /// </summary>
        public const string RuleNotSettled = "NotSettled";

        /// <summary>
        /// Rule: attempts of this profile in another persona.
        /// </summary>
        public const string RuleOtherStage = "OtherStage";

        /// <summary>
        /// Rule: Judge verdicts on work finished by another profile or persona.
        /// </summary>
        public const string RuleJudgeVerdictOtherCaptain = "JudgeVerdictOtherCaptain";

        /// <summary>
        /// Rule: Judge verdicts from evaluation missions or classified NotTheModel.
        /// </summary>
        public const string RuleJudgeVerdictExcluded = "JudgeVerdictExcluded";

        #endregion

        #region Private-Members

        private const int OutcomePageSize = 1000;
        private const int MaxOutcomePages = 20;
        private const int TokenPageSize = 500;
        private const int MaxReworkHops = 10;
        private static readonly TimeSpan _RevisionSlack = TimeSpan.FromMinutes(10);

        private string _Header = "[ModelScorecardService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ModelScorecardService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Compute the scorecard of a profile for one persona.
        /// </summary>
        /// <param name="profile">Model profile.</param>
        /// <param name="request">Persona, vessel and window.</param>
        /// <param name="nowUtc">End of the window, or null for now.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The scorecard.</returns>
        /// <exception cref="ArgumentNullException">Thrown when profile or request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the persona is missing or a window value is out of range.</exception>
        public async Task<ModelScorecard> ComputeAsync(ModelProfile profile, ModelScorecardRequest request, DateTime? nowUtc = null, CancellationToken token = default)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (request == null) throw new ArgumentNullException(nameof(request));
            string persona = Blank(request.Persona) ?? throw new ArgumentException("persona is required.", nameof(request));
            int days = request.WindowDays ?? ModelScorecardRequest.DefaultWindowDays;
            if (days < 1 || days > ModelScorecardRequest.MaxWindowDays)
                throw new ArgumentException("windowDays must be 1 to " + ModelScorecardRequest.MaxWindowDays + ".", nameof(request));
            int missionLimit = request.WindowMissions ?? ModelScorecardRequest.DefaultWindowMissions;
            if (missionLimit < 1 || missionLimit > ModelScorecardRequest.MaxWindowMissions)
                throw new ArgumentException("windowMissions must be 1 to " + ModelScorecardRequest.MaxWindowMissions + ".", nameof(request));
            string? vesselId = Blank(request.VesselId);

            DateTime now = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
            DateTime start = now.AddDays(-days);
            ModelScorecard card = new ModelScorecard
            {
                ProfileId = profile.Id,
                TenantId = profile.TenantId,
                Identity = profile.Identity,
                Persona = persona,
                VesselId = vesselId,
                WindowDays = days,
                WindowMissions = missionLimit,
                WindowStartUtc = start,
                WindowEndUtc = now
            };
            Dictionary<string, ModelScorecardExclusion> exclusions = AddExclusions(card);

            List<ModelProfileRevision> revisions = (await _Database.ModelProfiles.ReadRevisionsAsync(profile.Id, token).ConfigureAwait(false))
                .OrderBy(r => r.FirstSeenUtc).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
            string profileKey = ModelProfileRules.IdentityKey(profile.TenantId, profile.Identity);

            // This profile's attempts in the day window, then the attribution rules.
            List<StageOutcome> read = new List<StageOutcome>();
            StageOutcomeQuery outcomeQuery = new StageOutcomeQuery
            {
                TenantId = Blank(profile.TenantId),
                Model = Blank(profile.Identity.Model),
                VesselId = vesselId,
                FromUtc = start,
                ToUtc = now
            };
            if (await ReadPagesAsync(outcomeQuery, read, token).ConfigureAwait(false)) card.Truncated = true;

            List<StageOutcome> counted = new List<StageOutcome>();
            foreach (StageOutcome outcome in read)
            {
                if (!MatchesProfile(outcome, profileKey)) continue;
                string? rule = ExclusionOf(outcome, persona);
                if (rule != null)
                {
                    exclusions[rule].Removed++;
                    continue;
                }
                counted.Add(outcome);
            }

            // The mission window: newest missions first, at most the mission limit.
            Dictionary<string, List<StageOutcome>> countedByMission = new Dictionary<string, List<StageOutcome>>(StringComparer.Ordinal);
            foreach (StageOutcome outcome in counted)
            {
                if (!countedByMission.TryGetValue(outcome.MissionId, out List<StageOutcome>? list))
                {
                    list = new List<StageOutcome>();
                    countedByMission[outcome.MissionId] = list;
                }
                list.Add(outcome);
            }
            List<string> missionIds = countedByMission
                .OrderByDescending(kv => kv.Value.Max(o => o.EndedUtc))
                .ThenByDescending(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => kv.Key)
                .ToList();
            if (missionIds.Count > missionLimit)
            {
                card.WindowLimitedBy = "Missions";
                missionIds = missionIds.Take(missionLimit).ToList();
            }
            HashSet<string> selected = new HashSet<string>(missionIds, StringComparer.Ordinal);

            // Every attempt on each counted mission, by any captain (escalations, fallbacks, the finishing attempt).
            Dictionary<string, List<StageOutcome>> allByMission = new Dictionary<string, List<StageOutcome>>(StringComparer.Ordinal);
            foreach (string missionId in missionIds)
                await MissionOutcomesAsync(missionId, allByMission, token).ConfigureAwait(false);

            // Revision groups.
            Dictionary<string, string> groupOfRevision = PoolRevisions(revisions);
            List<string> groupKeys = new List<string>();
            foreach (ModelProfileRevision revision in revisions)
            {
                string key = groupOfRevision[revision.Id];
                if (!groupKeys.Contains(key)) groupKeys.Add(key);
            }

            Dictionary<string, string> groupOfOutcome = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string missionId in missionIds)
            {
                foreach (StageOutcome outcome in countedByMission[missionId])
                {
                    string? revisionId = AssignRevision(outcome, revisions);
                    groupOfOutcome[outcome.Id] = revisionId == null ? String.Empty : groupOfRevision[revisionId];
                }
            }
            if (groupOfOutcome.Values.Any(v => v.Length == 0)) groupKeys.Add(String.Empty);

            // Judge verdicts charged to the attempt whose work was reviewed.
            Dictionary<string, List<JudgeVerdictEnum>> verdicts = new Dictionary<string, List<JudgeVerdictEnum>>(StringComparer.Ordinal);
            HashSet<string> rejected = new HashSet<string>(StringComparer.Ordinal);
            if (selected.Count > 0)
            {
                List<StageOutcome> judges = new List<StageOutcome>();
                StageOutcomeQuery judgeQuery = new StageOutcomeQuery
                {
                    TenantId = Blank(profile.TenantId),
                    Persona = PersonaCatalog.Judge,
                    VesselId = vesselId,
                    FromUtc = start
                };
                if (await ReadPagesAsync(judgeQuery, judges, token).ConfigureAwait(false)) card.Truncated = true;
                foreach (StageOutcome judge in judges.OrderBy(j => j.EndedUtc))
                {
                    if (String.IsNullOrEmpty(judge.ReviewedMissionId) || !selected.Contains(judge.ReviewedMissionId)) continue;
                    if (judge.Disposition != StageDispositionEnum.Completed || String.IsNullOrWhiteSpace(judge.JudgeVerdict)) continue;
                    if (judge.IsEvaluation || judge.SuspectedCause == FailureCauseEnum.NotTheModel)
                    {
                        exclusions[RuleJudgeVerdictExcluded].Removed++;
                        continue;
                    }
                    StageOutcome? reviewed = ReviewedAttempt(allByMission[judge.ReviewedMissionId], judge);
                    if (reviewed == null || !groupOfOutcome.TryGetValue(reviewed.Id, out string? group))
                    {
                        exclusions[RuleJudgeVerdictOtherCaptain].Removed++;
                        continue;
                    }
                    JudgeVerdictEnum verdict = ParseVerdict(judge.JudgeVerdict);
                    if (!verdicts.TryGetValue(group, out List<JudgeVerdictEnum>? list))
                    {
                        list = new List<JudgeVerdictEnum>();
                        verdicts[group] = list;
                    }
                    list.Add(verdict);
                    if (verdict == JudgeVerdictEnum.NeedsRevision || verdict == JudgeVerdictEnum.Fail) rejected.Add(judge.ReviewedMissionId);
                }
            }

            foreach (string groupKey in groupKeys)
            {
                List<StageOutcome> attempts = missionIds
                    .SelectMany(m => countedByMission[m])
                    .Where(o => String.Equals(groupOfOutcome[o.Id], groupKey, StringComparison.Ordinal))
                    .ToList();
                ModelScorecardGroup group = new ModelScorecardGroup
                {
                    RevisionIds = revisions.Where(r => String.Equals(groupOfRevision[r.Id], groupKey, StringComparison.Ordinal)).Select(r => r.Id).ToList(),
                    Attempts = attempts.Count,
                    Missions = attempts.Select(o => o.MissionId).Distinct(StringComparer.Ordinal).Count()
                };
                group.Pooled = group.RevisionIds.Count > 1;
                if (groupKey.Length == 0) group.Note = "Attempts that match no recorded revision of this profile.";
                else if (group.Pooled) group.Note = "Revisions marked compatible; their results are pooled.";

                verdicts.TryGetValue(groupKey, out List<JudgeVerdictEnum>? groupVerdicts);
                await AddMetricsAsync(group, groupKey, attempts, missionIds, countedByMission, allByMission, groupOfOutcome, groupVerdicts ?? new List<JudgeVerdictEnum>(), rejected, persona, profileKey, profile.TenantId, token).ConfigureAwait(false);
                card.Groups.Add(group);
            }

            card.Missions = missionIds.Count;
            card.Attempts = missionIds.Sum(m => countedByMission[m].Count);
            if (card.Attempts > 0)
                card.OldestCountedUtc = missionIds.SelectMany(m => countedByMission[m]).Min(o => o.StartedUtc ?? o.EndedUtc);
            return card;
        }

        #endregion

        #region Private-Methods

        private async Task AddMetricsAsync(
            ModelScorecardGroup group,
            string groupKey,
            List<StageOutcome> attempts,
            List<string> missionIds,
            Dictionary<string, List<StageOutcome>> countedByMission,
            Dictionary<string, List<StageOutcome>> allByMission,
            Dictionary<string, string> groupOfOutcome,
            List<JudgeVerdictEnum> verdicts,
            HashSet<string> rejected,
            string persona,
            string profileKey,
            string? tenantId,
            CancellationToken token)
        {
            List<ModelScorecardMetric> metrics = group.Metrics;
            int n = attempts.Count;

            // First pass: the mission's first counted attempt is this group's, nobody else tried before it, it is not a
            // rework round, its stage checks passed (not skipped) and no counted Judge verdict asked for rework.
            int firstPassOf = 0;
            int firstPass = 0;
            int missionsInGroup = 0;
            List<string> acceptedMissions = new List<string>();
            foreach (string missionId in missionIds)
            {
                List<StageOutcome> ours = countedByMission[missionId];
                List<StageOutcome> all = allByMission[missionId];
                if (ours.Any(o => InGroup(groupOfOutcome, o, groupKey))) missionsInGroup++;

                StageOutcome first = ours.OrderBy(o => o.Attempt).ThenBy(o => o.EndedUtc).First();
                if (InGroup(groupOfOutcome, first, groupKey)
                    && first.ReworkRound == 0
                    && all.Where(o => o.Attempt < first.Attempt).All(o => MatchesProfile(o, profileKey)))
                {
                    firstPassOf++;
                    if (first.Disposition == StageDispositionEnum.Completed
                        && first.StageCheckStatus == StageCheckStatusEnum.Passed
                        && !rejected.Contains(missionId))
                        firstPass++;
                }

                StageOutcome final = all.OrderBy(o => o.Attempt).ThenBy(o => o.EndedUtc).Last();
                if (groupOfOutcome.ContainsKey(final.Id)
                    && InGroup(groupOfOutcome, final, groupKey)
                    && final.Disposition == StageDispositionEnum.Completed
                    && final.StageCheckStatus != StageCheckStatusEnum.Failed
                    && !rejected.Contains(missionId))
                    acceptedMissions.Add(missionId);
            }
            metrics.Add(Rate("firstPassSuccess", "Missions whose first attempt was by this group and passed its stage checks (not skipped) with no rework requested, of missions whose first attempt was by this group (rework rounds excluded).", firstPass, firstPassOf));
            metrics.Add(Rate("acceptedResults", "Missions whose final attempt was by this group, completed, did not fail its stage checks and drew no Judge rejection, of missions with an attempt in this group.", acceptedMissions.Count, missionsInGroup));

            // Stage checks.
            metrics.Add(Rate("stageCheck.passed", "Attempts whose stage checks passed, of attempts.", attempts.Count(o => o.StageCheckStatus == StageCheckStatusEnum.Passed), n));
            metrics.Add(Rate("stageCheck.failed", "Attempts whose stage checks failed, of attempts.", attempts.Count(o => o.StageCheckStatus == StageCheckStatusEnum.Failed), n));
            List<StageOutcome> skipped = attempts.Where(o => o.StageCheckStatus == StageCheckStatusEnum.Skipped).ToList();
            metrics.Add(Rate("stageCheck.skipped", "Attempts whose stage checks were skipped, of attempts.", skipped.Count, n));
            foreach (IGrouping<string, StageOutcome> reason in skipped
                .GroupBy(o => o.StageCheckReason.HasValue && o.StageCheckReason.Value != StageCheckReasonEnum.None ? o.StageCheckReason.Value.ToString() : "Unspecified")
                .OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                metrics.Add(Rate("stageCheck.skipped." + reason.Key, "Skipped attempts with skip reason " + reason.Key + ", of skipped attempts.", reason.Count(), skipped.Count));
            }
            metrics.Add(Rate("stageCheck.couldNotRun", "Attempts whose stage checks could not run, of attempts.", attempts.Count(o => o.StageCheckStatus == StageCheckStatusEnum.CouldNotRun), n));
            metrics.Add(Rate("stageCheck.notRecorded", "Attempts with no recorded stage-check status, of attempts.", attempts.Count(o => !o.StageCheckStatus.HasValue), n));

            // Judge verdicts on this group's work.
            int verdictCount = verdicts.Count;
            metrics.Add(Rate("judge.pass", "Judge verdicts PASS on this group's work, of counted Judge verdicts.", verdicts.Count(v => v == JudgeVerdictEnum.Pass), verdictCount));
            metrics.Add(Rate("judge.needsRevision", "Judge verdicts NEEDS_REVISION on this group's work, of counted Judge verdicts.", verdicts.Count(v => v == JudgeVerdictEnum.NeedsRevision), verdictCount));
            metrics.Add(Rate("judge.fail", "Judge verdicts FAIL on this group's work, of counted Judge verdicts.", verdicts.Count(v => v == JudgeVerdictEnum.Fail), verdictCount));
            metrics.Add(Rate("judge.other", "Other Judge verdicts (spec conflict, unparsed) on this group's work, of counted Judge verdicts.", verdicts.Count(v => v != JudgeVerdictEnum.Pass && v != JudgeVerdictEnum.NeedsRevision && v != JudgeVerdictEnum.Fail), verdictCount));
            metrics.Add(Unavailable("judge.findingCategories", "Judge findings by category (behavior, scope, style, hygiene).",
                "Judge findings are recorded with a severity only; no finding category is recorded today."));

            // Protocol repairs.
            metrics.Add(Rate("repairs.protocol", "Attempts that needed a protocol (output contract) repair, of attempts.",
                attempts.Count(o => o.Disposition == StageDispositionEnum.OutputRepair || (o.ContractRepairKind.HasValue && o.ContractRepairKind.Value != ContractRepairKindEnum.None)), n));
            metrics.Add(Rate("repairs.exhausted", "Attempts that failed or were escalated with InvalidOutput (output repairs ran out), of attempts.",
                attempts.Count(o => o.FailureKind == MissionFailureKindEnum.InvalidOutput && (o.Disposition == StageDispositionEnum.Failed || o.Disposition == StageDispositionEnum.Escalated)), n));

            // Tool failures.
            metrics.Add(Rate("toolFailures", "Attempts whose suspected cause is ToolUse, of attempts.", attempts.Count(o => o.SuspectedCause == FailureCauseEnum.ToolUse), n));
            metrics.Add(Unavailable("toolFailures.kinds", "Tool failures by kind (malformed calls, loops, iteration limits).",
                "Stage outcomes record the ToolUse cause, not the kind of tool failure."));

            // Resumes and stalls.
            metrics.Add(Rate("resumesInPlace", "Attempts after which the same captain was relaunched in the same dock (resume in place), of attempts.", attempts.Count(o => o.Disposition == StageDispositionEnum.Relaunched), n));
            metrics.Add(Rate("stalls.exhausted", "Attempts that ended with StallRecoveryExhausted, of attempts.", attempts.Count(o => o.FailureKind == MissionFailureKindEnum.StallRecoveryExhausted), n));
            metrics.Add(Unavailable("stalls.recovered", "Stalls detected and recovered within an attempt.",
                "Stall detections that recovered are recovery events, not recorded on stage outcomes."));

            // Escalations away and what followed.
            List<StageOutcome> escalated = attempts.Where(o => o.Disposition == StageDispositionEnum.Escalated).ToList();
            List<StageOutcome> known = escalated.Where(o => o.PriorWorkKept.HasValue).ToList();
            metrics.Add(Rate("escalations.away", "Attempts escalated to a different captain, of attempts.", escalated.Count, n));
            metrics.Add(Rate("escalations.nextFinished", "Escalations whose mission was later finished by the next captain (FinishedByOutcomeId set), of escalations.", escalated.Count(o => !String.IsNullOrEmpty(o.FinishedByOutcomeId)), escalated.Count));
            metrics.Add(Rate("escalations.priorWorkKept", "Finished escalations where the finishing captain kept this work (PriorWorkKept true), of escalations where it is known.", known.Count(o => o.PriorWorkKept == true), known.Count));
            metrics.Add(Rate("escalations.priorWorkReplaced", "Finished escalations where the finishing captain replaced this work (PriorWorkKept false), of escalations where it is known.", known.Count(o => o.PriorWorkKept == false), known.Count));
            metrics.Add(Rate("escalations.priorWorkUnknown", "Escalations where keeping the work is not known (not finished, or the commits could not be read), of escalations.", escalated.Count - known.Count, escalated.Count));

            // Suspected causes of the attempts that fell short; Unknown and Mixed stay their own buckets.
            List<StageOutcome> shortfalls = attempts.Where(IsShortfall).ToList();
            foreach (FailureCauseEnum cause in new FailureCauseEnum[]
            {
                FailureCauseEnum.Protocol, FailureCauseEnum.InstructionFollowing, FailureCauseEnum.ToolUse, FailureCauseEnum.Context,
                FailureCauseEnum.Capability, FailureCauseEnum.Unknown, FailureCauseEnum.Mixed, FailureCauseEnum.None
            })
            {
                string name = cause == FailureCauseEnum.None ? "Unclassified" : cause.ToString();
                metrics.Add(Rate("cause." + name, "Attempts that fell short (failed, relaunched, repaired, escalated or with a failure kind) with suspected cause " + name + ", of attempts that fell short.",
                    shortfalls.Count(o => o.SuspectedCause == cause), shortfalls.Count));
            }

            // Cost and time per accepted result, across every attempt that led to it (rework rounds and fallbacks).
            long tokens = 0;
            int withTokens = 0;
            long wallMs = 0;
            int withTime = 0;
            foreach (string missionId in acceptedMissions)
            {
                List<string> chain = await ReworkChainAsync(missionId, persona, allByMission, token).ConfigureAwait(false);
                long missionTokens = 0;
                bool anyTokens = false;
                long missionMs = 0;
                bool anyTime = false;
                foreach (string chainMission in chain)
                {
                    TokenUsageQuery usageQuery = new TokenUsageQuery { TenantId = Blank(tenantId), Source = "mission", SourceId = chainMission, PageSize = TokenPageSize };
                    EnumerationResult<TokenUsageRecord> usage = await _Database.TokenUsage.EnumerateAsync(usageQuery, token).ConfigureAwait(false);
                    foreach (TokenUsageRecord record in usage.Objects)
                    {
                        missionTokens += record.TotalTokens;
                        anyTokens = true;
                    }
                    foreach (StageOutcome outcome in allByMission[chainMission].Where(o => PersonaCatalog.Matches(o.Persona, persona)))
                    {
                        long? ms = DurationOf(outcome);
                        if (!ms.HasValue) continue;
                        missionMs += ms.Value;
                        anyTime = true;
                    }
                }
                if (anyTokens)
                {
                    tokens += missionTokens;
                    withTokens++;
                }
                if (anyTime)
                {
                    wallMs += missionMs;
                    withTime++;
                }
            }
            metrics.Add(Cost("cost.tokensPerAcceptedResult", "Total tokens per accepted result, summed over every attempt (rework rounds and fallbacks included) that led to it; Count is the accepted results with recorded token usage, Of all accepted results.",
                "tokens", tokens, withTokens, acceptedMissions.Count, "No token usage is recorded for the accepted results."));
            metrics.Add(Cost("cost.wallTimePerAcceptedResult", "Wall time per accepted result, summed over every attempt (rework rounds and fallbacks included) that led to it; Count is the accepted results with recorded timing, Of all accepted results.",
                "ms", wallMs, withTime, acceptedMissions.Count, "No attempt timing is recorded for the accepted results."));
        }

        private async Task<List<string>> ReworkChainAsync(string missionId, string persona, Dictionary<string, List<StageOutcome>> allByMission, CancellationToken token)
        {
            // A rework mission depends on the Judge mission that rejected the previous round; that Judge's reviewed mission
            // is the previous round. Walk back while the previous round is the same persona.
            List<string> chain = new List<string> { missionId };
            string current = missionId;
            for (int hop = 0; hop < MaxReworkHops; hop++)
            {
                List<StageOutcome> outcomes = await MissionOutcomesAsync(current, allByMission, token).ConfigureAwait(false);
                if (outcomes.Count == 0 || outcomes.Max(o => o.ReworkRound) <= 0) break;
                Mission? mission = await _Database.Missions.ReadAsync(current, token).ConfigureAwait(false);
                if (mission == null || String.IsNullOrEmpty(mission.DependsOnMissionId)) break;
                List<StageOutcome> judgeOutcomes = await MissionOutcomesAsync(mission.DependsOnMissionId, allByMission, token).ConfigureAwait(false);
                string? previous = judgeOutcomes.Where(o => !String.IsNullOrEmpty(o.ReviewedMissionId)).OrderBy(o => o.Attempt).Select(o => o.ReviewedMissionId).LastOrDefault();
                if (previous == null || chain.Contains(previous)) break;
                List<StageOutcome> previousOutcomes = await MissionOutcomesAsync(previous, allByMission, token).ConfigureAwait(false);
                if (!previousOutcomes.Any(o => PersonaCatalog.Matches(o.Persona, persona))) break;
                chain.Add(previous);
                current = previous;
            }
            return chain;
        }

        private async Task<List<StageOutcome>> MissionOutcomesAsync(string missionId, Dictionary<string, List<StageOutcome>> cache, CancellationToken token)
        {
            if (cache.TryGetValue(missionId, out List<StageOutcome>? cached)) return cached;
            List<StageOutcome> outcomes = new List<StageOutcome>();
            if (await ReadPagesAsync(new StageOutcomeQuery { MissionId = missionId }, outcomes, token).ConfigureAwait(false))
                _Logging.Warn(_Header + "mission " + missionId + " has more stage outcomes than are read");
            cache[missionId] = outcomes;
            return outcomes;
        }

        private async Task<bool> ReadPagesAsync(StageOutcomeQuery query, List<StageOutcome> into, CancellationToken token)
        {
            query.PageSize = OutcomePageSize;
            for (int page = 1; page <= MaxOutcomePages; page++)
            {
                query.PageNumber = page;
                EnumerationResult<StageOutcome> result = await _Database.StageOutcomes.EnumerateAsync(query, token).ConfigureAwait(false);
                into.AddRange(result.Objects);
                if (result.Objects.Count == 0 || into.Count >= result.TotalRecords) return false;
                if (page == MaxOutcomePages) return true;
            }
            return false;
        }

        private static Dictionary<string, ModelScorecardExclusion> AddExclusions(ModelScorecard card)
        {
            string[][] rules = new string[][]
            {
                new string[] { RuleEvaluation, "Evaluation and battery missions (IsEvaluation)." },
                new string[] { RuleNotTheModel, "Attempts whose suspected cause is NotTheModel: Infra, OperatorAction, tooling papercuts and orchestration bugs." },
                new string[] { RuleInfra, "Attempts that failed with kind Infra (not yet classified; skipped when a person classified the cause)." },
                new string[] { RuleOperatorAction, "Attempts that failed with kind OperatorAction or were cancelled (skipped when a person classified the cause)." },
                new string[] { RuleRuntimeEnvironment, "Interrupted attempts, crashes, and runtime usage-limit, auth, model-unavailable or transient failures (skipped when a person classified the cause)." },
                new string[] { RuleNotSettled, "Attempts whose result is not settled yet (Pending)." },
                new string[] { RuleOtherStage, "Attempts of this profile in another persona: a defect is charged to the stage that introduced it." },
                new string[] { RuleJudgeVerdictOtherCaptain, "Judge verdicts on a counted mission whose reviewed work was finished by another captain or profile: a rejection counts only for the slice this profile produced." },
                new string[] { RuleJudgeVerdictExcluded, "Judge verdicts from evaluation missions or classified NotTheModel." }
            };
            Dictionary<string, ModelScorecardExclusion> byRule = new Dictionary<string, ModelScorecardExclusion>(StringComparer.Ordinal);
            foreach (string[] rule in rules)
            {
                ModelScorecardExclusion exclusion = new ModelScorecardExclusion { Rule = rule[0], Description = rule[1] };
                card.Exclusions.Add(exclusion);
                byRule[rule[0]] = exclusion;
            }
            return byRule;
        }

        private static string? ExclusionOf(StageOutcome outcome, string persona)
        {
            if (!PersonaCatalog.Matches(outcome.Persona, persona)) return RuleOtherStage;
            if (outcome.IsEvaluation) return RuleEvaluation;
            if (outcome.SuspectedCause == FailureCauseEnum.NotTheModel) return RuleNotTheModel;
            bool classifiedByHand = outcome.CauseSource == FailureCauseSourceEnum.Manual && outcome.SuspectedCause != FailureCauseEnum.None;
            if (!classifiedByHand)
            {
                if (outcome.FailureKind == MissionFailureKindEnum.Infra) return RuleInfra;
                if (outcome.FailureKind == MissionFailureKindEnum.OperatorAction || outcome.Disposition == StageDispositionEnum.Cancelled) return RuleOperatorAction;
                if (outcome.Disposition == StageDispositionEnum.Interrupted
                    || outcome.FailureKind == MissionFailureKindEnum.Crash
                    || outcome.RuntimeFailureKind == RuntimeFailureKindEnum.UsageLimit
                    || outcome.RuntimeFailureKind == RuntimeFailureKindEnum.AuthFailure
                    || outcome.RuntimeFailureKind == RuntimeFailureKindEnum.Crash
                    || outcome.RuntimeFailureKind == RuntimeFailureKindEnum.ModelUnavailable
                    || outcome.RuntimeFailureKind == RuntimeFailureKindEnum.Transient)
                    return RuleRuntimeEnvironment;
            }
            if (outcome.Disposition == StageDispositionEnum.Pending) return RuleNotSettled;
            return null;
        }

        private static bool MatchesProfile(StageOutcome outcome, string profileKey)
        {
            ModelProfileObservation? observation = ModelProfileService.ObservationFrom(outcome);
            if (observation == null) return false;
            return String.Equals(ModelProfileRules.IdentityKey(outcome.TenantId, ModelProfileRules.IdentityOf(observation)), profileKey, StringComparison.Ordinal);
        }

        private static string? AssignRevision(StageOutcome outcome, List<ModelProfileRevision> revisions)
        {
            // The newest revision the attempt pools into that existed when it was recorded (a revision is created just after
            // the outcome that first showed it), else the oldest it pools into.
            ModelProfileObservation? observation = ModelProfileService.ObservationFrom(outcome);
            if (observation == null) return null;
            DateTime recorded = outcome.CreatedUtc > outcome.EndedUtc ? outcome.CreatedUtc : outcome.EndedUtc;
            DateTime cutoff = recorded.ToUniversalTime() + _RevisionSlack;
            ModelProfileRevision? best = null;
            foreach (ModelProfileRevision revision in revisions)
            {
                if (ModelProfileRules.StartsNewRevision(revision, observation)) continue;
                if (best == null || revision.FirstSeenUtc.ToUniversalTime() <= cutoff) best = revision;
            }
            return best?.Id;
        }

        private static Dictionary<string, string> PoolRevisions(List<ModelProfileRevision> revisions)
        {
            // Connected components of the (symmetric) compatibility marks; each component is keyed by its oldest revision.
            Dictionary<string, string> parent = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ModelProfileRevision revision in revisions) parent[revision.Id] = revision.Id;
            foreach (ModelProfileRevision revision in revisions)
            {
                foreach (string other in revision.CompatibleWith)
                {
                    if (!parent.ContainsKey(other)) continue;
                    string a = Root(parent, revision.Id);
                    string b = Root(parent, other);
                    if (!String.Equals(a, b, StringComparison.Ordinal)) parent[b] = a;
                }
            }
            Dictionary<string, string> keyOfRoot = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> groupOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ModelProfileRevision revision in revisions)
            {
                string root = Root(parent, revision.Id);
                if (!keyOfRoot.ContainsKey(root)) keyOfRoot[root] = revision.Id;
                groupOf[revision.Id] = keyOfRoot[root];
            }
            return groupOf;
        }

        private static string Root(Dictionary<string, string> parent, string id)
        {
            string current = id;
            while (!String.Equals(parent[current], current, StringComparison.Ordinal)) current = parent[current];
            return current;
        }

        private static StageOutcome? ReviewedAttempt(List<StageOutcome> missionOutcomes, StageOutcome judge)
        {
            DateTime reviewStart = (judge.StartedUtc ?? judge.EndedUtc).ToUniversalTime();
            List<StageOutcome> completed = missionOutcomes
                .Where(o => o.Disposition == StageDispositionEnum.Completed)
                .OrderBy(o => o.Attempt).ThenBy(o => o.EndedUtc)
                .ToList();
            StageOutcome? before = completed.LastOrDefault(o => o.EndedUtc.ToUniversalTime() <= reviewStart);
            return before ?? completed.LastOrDefault();
        }

        private static JudgeVerdictEnum ParseVerdict(string? text)
        {
            string normalized = (text ?? String.Empty).Replace("_", String.Empty, StringComparison.Ordinal).Trim();
            if (Enum.TryParse<JudgeVerdictEnum>(normalized, true, out JudgeVerdictEnum verdict)) return verdict;
            return JudgeVerdictEnum.None;
        }

        private static bool IsShortfall(StageOutcome outcome)
        {
            return outcome.Disposition == StageDispositionEnum.Failed
                || outcome.Disposition == StageDispositionEnum.Escalated
                || outcome.Disposition == StageDispositionEnum.Relaunched
                || outcome.Disposition == StageDispositionEnum.OutputRepair
                || outcome.FailureKind.HasValue;
        }

        private static bool InGroup(Dictionary<string, string> groupOfOutcome, StageOutcome outcome, string groupKey)
        {
            return groupOfOutcome.TryGetValue(outcome.Id, out string? key) && String.Equals(key, groupKey, StringComparison.Ordinal);
        }

        private static long? DurationOf(StageOutcome outcome)
        {
            if (outcome.DurationMs.HasValue && outcome.DurationMs.Value >= 0) return outcome.DurationMs.Value;
            if (!outcome.StartedUtc.HasValue) return null;
            double ms = (outcome.EndedUtc.ToUniversalTime() - outcome.StartedUtc.Value.ToUniversalTime()).TotalMilliseconds;
            return ms >= 0 ? (long)ms : null;
        }

        private static ModelScorecardMetric Rate(string key, string description, int count, int of)
        {
            return new ModelScorecardMetric
            {
                Key = key,
                Description = description,
                Count = count,
                Of = of,
                Rate = of == 0 ? null : Math.Round((double)count / of, 4),
                Lower = WilsonScoreInterval.Lower(count, of),
                Upper = WilsonScoreInterval.Upper(count, of)
            };
        }

        private static ModelScorecardMetric Cost(string key, string description, string unit, long total, int counted, int of, string noData)
        {
            ModelScorecardMetric metric = new ModelScorecardMetric { Key = key, Description = description, Unit = unit };
            if (of == 0)
            {
                metric.Available = false;
                metric.UnavailableReason = "No accepted results in the window.";
                return metric;
            }
            if (counted == 0)
            {
                metric.Available = false;
                metric.UnavailableReason = noData;
                metric.Count = 0;
                metric.Of = of;
                return metric;
            }
            metric.Count = counted;
            metric.Of = of;
            metric.Total = total;
            metric.Value = Math.Round((double)total / counted, 1);
            return metric;
        }

        private static ModelScorecardMetric Unavailable(string key, string description, string reason)
        {
            return new ModelScorecardMetric { Key = key, Description = description, Available = false, UnavailableReason = reason };
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
