namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using D = Armada.Core.BlackBox.BlackBoxResilienceDefaults;

    /// <summary>
    /// The versioned black box rule catalog the <see cref="BlackBoxAdvisor"/> runs (same style as
    /// FailureCauseRuleCatalog). Every starter rule comes from a failure observed in this repository; every phase of the
    /// step taxonomy has at least one rule, and <c>BBX-UNKNOWN-001</c> fires when nothing else does. Every rule about
    /// Armada's handoff and landing (<see cref="BlackBoxRule.InResilienceScope"/>) carries resilience content
    /// (<see cref="BlackBoxRule.Heals"/>): whether Armada could have healed the failure itself, the detection gap, code
    /// pointers, proposed Armada changes and the regression test. Bump
    /// <see cref="CurrentRulesVersion"/> whenever a rule's match, text, vote or recovery changes: entries analyzed with an
    /// older version are re-analyzed in the background (<see cref="BlackBoxReanalyzer"/>). The catalog is read-only once
    /// built and thread-safe.
    /// </summary>
    public class BlackBoxRuleCatalog
    {
        #region Public-Members

        /// <summary>
        /// Version of the built-in rules. Bump on every rule change.
        /// </summary>
        public const int CurrentRulesVersion = 2;

        /// <summary>
        /// The built-in catalog.
        /// </summary>
        public static BlackBoxRuleCatalog Default { get; } = new BlackBoxRuleCatalog(CurrentRulesVersion, BuildRules());

        /// <summary>
        /// Version of this catalog's rules.
        /// </summary>
        public int RulesVersion { get; }

        /// <summary>
        /// Rules in evaluation order (the fallback rule last).
        /// </summary>
        public IReadOnlyList<BlackBoxRule> Rules { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Build a catalog (tests and extensions; Armada uses <see cref="Default"/>).
        /// </summary>
        /// <param name="rulesVersion">Rules version (at least 1).</param>
        /// <param name="rules">Rules in evaluation order.</param>
        /// <exception cref="ArgumentNullException">Thrown when rules is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when rulesVersion is below 1.</exception>
        /// <exception cref="ArgumentException">Thrown when two rules share an id.</exception>
        public BlackBoxRuleCatalog(int rulesVersion, IEnumerable<BlackBoxRule> rules)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (rulesVersion < 1) throw new ArgumentOutOfRangeException(nameof(rulesVersion), "The rules version must be at least 1.");
            List<BlackBoxRule> list = rules.ToList();
            string? duplicate = list.GroupBy(r => r.Id, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).FirstOrDefault();
            if (duplicate != null) throw new ArgumentException("Duplicate rule id " + duplicate + ".", nameof(rules));
            RulesVersion = rulesVersion;
            Rules = list;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A rule by id, or null.
        /// </summary>
        /// <param name="id">Rule id.</param>
        /// <returns>The rule or null.</returns>
        public BlackBoxRule? Find(string id)
        {
            return Rules.FirstOrDefault(r => String.Equals(r.Id, id, StringComparison.Ordinal));
        }

        /// <summary>
        /// Build the built-in rules, in evaluation order.
        /// </summary>
        /// <returns>New rule instances.</returns>
        public static List<BlackBoxRule> BuildRules()
        {
            List<BlackBoxRule> rules = new List<BlackBoxRule>();
            AddLanding(rules);
            AddMergeQueue(rules);
            AddCompletion(rules);
            AddExecution(rules);
            AddDispatchAndProvision(rules);
            AddReviewHandoffRecovery(rules);
            AddVoyageAndExternal(rules);

            rules.Add(new BlackBoxRule("BBX-UNKNOWN-001", "No rule matched", r => true)
                { IsFallback = true }
                .Because("No black box rule matched this failure ({phase|Unknown} / {step|Unknown}); decide the cause from the reason, mechanism and evidence.", BlackBoxConfidenceEnum.Low, "classification.rawReason", "mechanism")
                .Votes(BlackBoxTriageEnum.Unknown, 0)
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Black box rules",
                    Hypothesis = "Add a rule for this failure mode ({phase|Unknown} / {step|Unknown} / {kind|no kind}) so the next occurrence is explained.",
                    SourcePointers = { "src/Armada.Core/BlackBox/BlackBoxRuleCatalog.cs:BuildRules" },
                    Severity = BlackBoxSeverityEnum.Low
                }));
            return rules;
        }

        #endregion

        #region Private-Methods

        private static void AddLanding(List<BlackBoxRule> rules)
        {
            rules.Add(new BlackBoxRule("BBX-LAND-001", "Dirty target checkout", r =>
                    BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.DirtyCheckout && (BlackBoxRuleFacts.DirtyCount(r) > 0 || r.Workspace?.CheckoutDirty == true))
                .In(BlackBoxPhaseEnum.Landing)
                .Because("The checkout at {checkoutPath|the vessel's working directory} has uncommitted tracked changes in {dirtyCount|several} files, so the landing refused to overwrite them.", BlackBoxConfidenceEnum.High, "workspace.dirtyPaths", "workspace.checkoutPath", BlackBoxObjectNames.GitStatus)
                .Votes(BlackBoxTriageEnum.UserConfig, 3)
                .Recommend(new BlackBoxRuleRecommendation
                {
                    Key = "landing-mode",
                    Setting = "LandingMode",
                    Scope = BlackBoxConfigSourceEnum.Vessel,
                    SuggestedValue = "PullRequest",
                    Rationale = "The vessel's working directory is also a human working copy; landing through a pull request (or the merge queue) never writes into it.",
                    HowToApply = "update_vessel {\"vesselId\": \"{vesselId}\", \"landingMode\": \"PullRequest\"}, or point the vessel's WorkingDirectory at a dedicated clone.",
                    ApplyTool = "update_vessel",
                    ApplyArguments = { ["vesselId"] = "{vesselId}", ["landingMode"] = "PullRequest" }
                })
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Landing",
                    Hypothesis = "Land into a dedicated integration worktree instead of the operator's checkout, so local edits never block a landing.",
                    Evidence = "{dirtyCount|Several} dirty paths in {checkoutPath|the checkout} blocked the landing of {missionId}.",
                    SourcePointers = { "src/Armada.Server/MissionLandingHandler.cs:HandleMissionCompleteAsync", "src/Armada.Core/Services/GitService.cs:EnsureTrackedFilesCleanAsync" },
                    Severity = BlackBoxSeverityEnum.Medium
                })
                .Recover(new BlackBoxRulePath
                {
                    Key = "stash-retry",
                    Title = "Commit or stash the checkout's changes, then retry the landing",
                    Preconditions = { "Someone has reviewed the {dirtyCount|dirty} changed files in {checkoutPath|the checkout} and knows they can be stashed." },
                    Steps =
                    {
                        BlackBoxRuleStep.Manual("Commit or stash the uncommitted changes in {checkoutPath|the vessel's working directory}.", "git -C \"{checkoutPath|<checkout>}\" stash push --include-untracked"),
                        RetryLandingStep()
                    },
                    Risk = BlackBoxSeverityEnum.Low,
                    ExpectedOutcome = "The landing merges {branchName|the mission branch} into {targetBranch|the target branch} and the mission completes."
                })
                .Recover(new BlackBoxRulePath
                {
                    Key = "pull-request",
                    Title = "Switch the vessel to pull-request landing, then retry the landing",
                    Preconditions = { "The vessel's repository accepts pull requests from Armada." },
                    Steps =
                    {
                        BlackBoxRuleStep.Run("Set the vessel's landing mode to PullRequest.", "update_vessel", "vesselId", "{vesselId}", "landingMode", "PullRequest"),
                        RetryLandingStep()
                    },
                    Risk = BlackBoxSeverityEnum.Medium,
                    ExpectedOutcome = "Armada opens a pull request for {branchName|the mission branch} instead of writing into the checkout."
                })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "landed through an Armada-owned integration worktree (or deferred the landing and retried once {checkoutPath|the checkout} was clean) instead of refusing because of the operator's uncommitted changes",
                    BlackBoxDetectionGapEnum.NoPreflightCheck,
                    "Armada checked the checkout for uncommitted changes only at landing time, after the captain had finished; nothing warned at dispatch that the vessel's working directory is a human working copy with {dirtyCount|some} dirty files.",
                    "A landing never depends on the cleanliness of a human working copy: it integrates in an Armada-owned worktree and only fast-forwards the checkout when it is clean.",
                    "Services.LandingPipeline",
                    "Land {missionId|a mission} while {checkoutPath|the vessel's working directory} has {dirtyCount|several} uncommitted tracked changes and assert it lands through the integration worktree without touching them.",
                    new[] { D.LandingHandler, "src/Armada.Core/Services/GitService.cs:EnsureTrackedFilesCleanAsync", D.Deferrals },
                    BlackBoxRuleChange.Of("Land through an integration worktree when the checkout is dirty", "When the vessel's working directory has uncommitted tracked changes, MissionLandingHandler should merge {branchName|the mission branch} into {targetBranch|the target} in an Armada-owned integration worktree, push from there and leave the operator's checkout untouched (fast-forwarding it later only when it is clean), instead of failing the landing with DirtyCheckout.", "Services.LandingPipeline: a checkout with unrelated uncommitted edits still lands the mission through the integration worktree, and the dirty files are unchanged.", BlackBoxSeverityEnum.High, D.LandingHandler, "src/Armada.Core/Services/GitService.cs:EnsureTrackedFilesCleanAsync"),
                    BlackBoxRuleChange.Of("Auto-defer dirty-checkout landings and retry them", "Instead of LandingFailed, record a landing deferral (mission.landing_deferred) naming the {dirtyCount|dirty} files that block it, and let LandingIntegrityService retry the landing with backoff once the checkout is clean.", "Services.LandingPipeline: a DirtyCheckout landing is deferred, not failed, and lands on the next integrity sweep after the checkout is cleaned.", BlackBoxSeverityEnum.Medium, D.Deferrals, D.LandingHandler))));

            rules.Add(new BlackBoxRule("BBX-LAND-002", "Dock deleted before landing", r =>
                    BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Landing
                    && (r.Workspace?.WorktreeExists == false || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.DockMissing || BlackBoxRuleFacts.MissingWorkingDirectory(r)))
                .In(BlackBoxPhaseEnum.Landing)
                .Because("The dock directory {worktreePath|of this mission} was removed before landing, so git could not start in it.", BlackBoxConfidenceEnum.High, "workspace.worktreeExists", "mechanism.exceptionMessage", "classification.rawReason")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 3)
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Docks",
                    Hypothesis = "Check DockLeaseState.LeaseStatuses on every reclaim path so a dock is never reclaimed while its mission still has to land, and land from the branch rather than the worktree.",
                    Evidence = "Landing of {missionId} ran git in {worktreePath|a dock directory} that no longer existed.",
                    SourcePointers = { "src/Armada.Core/Services/DockService.cs:ReclaimAsync", "src/Armada.Server/MissionLandingHandler.cs:EnsureLandingDockAsync" },
                    Severity = BlackBoxSeverityEnum.High
                })
                .Recover(new BlackBoxRulePath
                {
                    Key = "retry-landing",
                    Title = "Retry the landing (recreates the worktree from the branch)",
                    Preconditions = { "The mission branch {branchName|of this mission} still exists in the vessel's repository." },
                    Steps = { RetryLandingStep() },
                    Risk = BlackBoxSeverityEnum.Low,
                    ExpectedOutcome = "The landing recreates the dock from {branchName|the mission branch} and lands it."
                })
                .Recover(new BlackBoxRulePath
                {
                    Key = "restore-branch",
                    Title = "Restore the branch from the mission commit, then retry the landing",
                    Optional = true,
                    Preconditions = { "The branch {branchName} is gone but the mission commit {missionCommit} is still in the repository." },
                    Steps =
                    {
                        BlackBoxRuleStep.Manual("Recreate the branch {branchName} at {missionCommit} in the vessel's repository.", "git branch {branchName} {missionCommit}"),
                        RetryLandingStep()
                    },
                    Risk = BlackBoxSeverityEnum.Medium,
                    ExpectedOutcome = "The work at {missionCommit} lands on {targetBranch|the target branch}."
                })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "recreated the worktree from {branchName|the mission branch} (or from {missionCommit|the mission commit}) and retried the landing instead of running git in a directory that no longer existed",
                    BlackBoxDetectionGapEnum.Race,
                    "The dock was reclaimed or its directory removed while the mission still had to land: the reclaim path did not honor the dock's lease, and the landing did not verify the worktree before running git in it.",
                    "A dock whose mission is WorkProduced, in review or landing stays leased and on disk until the landing finishes, and a landing verifies its worktree exists before running git in it.",
                    "Services.LandingPipeline",
                    "Remove the dock directory of {missionId|a WorkProduced mission} before landing and assert the landing recreates it from {branchName|the branch} and lands.",
                    new[] { D.Reclaim, D.LandingDock, D.RestoreWorktree, D.RepairDocks },
                    BlackBoxRuleChange.Of("Recreate a missing dock worktree from the branch before landing", "Before landing, EnsureLandingDockAsync should check that {worktreePath|the dock worktree} exists and is a registered worktree; when it is missing, recreate it from {branchName|the mission branch} (DockService.RestoreWorktreeAsync) and continue, rather than letting git fail with a missing working directory.", "Services.LandingPipeline: delete the dock directory of a WorkProduced mission, land it, and assert the worktree is recreated from the branch and the mission lands.", BlackBoxSeverityEnum.High, D.LandingDock, D.RestoreWorktree),
                    BlackBoxRuleChange.Of("Never reclaim a dock whose mission still has to land", "DockService.ReclaimAsync and every reclaim path should check the dock lease and refuse (dock.reclaim_refused) while the mission is WorkProduced, awaiting review or landing, so the worktree survives until the landing is done.", "Services.DockLease: reclaiming the dock of a WorkProduced mission is refused and the worktree stays on disk.", BlackBoxSeverityEnum.High, D.Reclaim))));

            rules.Add(new BlackBoxRule("BBX-LAND-004", "Conflict only in generated files", r => BlackBoxRuleFacts.OnlyGeneratedConflicts(r))
                .In(BlackBoxPhaseEnum.Landing)
                .Because("Only generated or append-only files conflicted: {conflictedFiles}.", BlackBoxConfidenceEnum.High, "conflict.conflictedFiles")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 3)
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Landing",
                    Hypothesis = "Extend ChangelogLandingConflict to regenerate the i18n catalog, the API surface snapshot and the Postman collection on conflict instead of failing the landing.",
                    Evidence = "Conflicted files: {conflictedFiles}.",
                    SourcePointers = { "src/Armada.Core/Services/ChangelogLandingConflict.cs:TryResolveAsync", "src/Armada.Core/Services/LandingTargetSynchronization.cs:SynchronizeAsync" },
                    Severity = BlackBoxSeverityEnum.Medium
                })
                .Recover(new BlackBoxRulePath
                {
                    Key = "regenerate-retry",
                    Title = "Regenerate the generated files on the branch, then retry the landing",
                    Steps =
                    {
                        BlackBoxRuleStep.Manual("On {branchName|the mission branch}, merge {targetBranch|the target branch}, regenerate {conflictedFiles} and commit."),
                        RetryLandingStep()
                    },
                    Risk = BlackBoxSeverityEnum.Low,
                    ExpectedOutcome = "The landing succeeds with the regenerated files."
                })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "regenerated {conflictedFiles|the generated files} on the merged tree (union the CHANGELOG, merge the i18n catalog, regenerate the API surface, Postman collection and schemas) and retried the landing",
                    BlackBoxDetectionGapEnum.MissingInvariant,
                    "ChangelogLandingConflict only resolves CHANGELOG.md, so conflicts in other generated or append-only files ({conflictedFiles|the generated files}) still fail the landing.",
                    "A conflict confined to generated or append-only files never fails a landing: Armada unions or regenerates them.",
                    "Services.LandingConflict",
                    "Make two missions conflict only in {conflictedFiles|generated files} and assert the landing regenerates them and lands.",
                    new[] { "src/Armada.Core/Services/ChangelogLandingConflict.cs:TryResolveAsync", D.TargetSync },
                    BlackBoxRuleChange.Of("Auto-resolve conflicts in generated and append-only files", "Extend ChangelogLandingConflict into a generated-file resolver: union append-only files (CHANGELOG), merge JSON catalogs (i18n) key by key, and regenerate snapshots (API surface, Postman collection, JSON Schemas, lock files) on the merged tree, then continue the landing instead of failing on {conflictedFiles|the generated files}.", "Services.LandingConflict: a conflict only in CHANGELOG.md, an i18n catalog and the API surface snapshot is resolved automatically and the mission lands with both sides' entries.", BlackBoxSeverityEnum.High, "src/Armada.Core/Services/ChangelogLandingConflict.cs:TryResolveAsync"))));

            rules.Add(new BlackBoxRule("BBX-LAND-003", "Conflict with a landed mission", r => BlackBoxRuleFacts.LandedConflict(r) != null && BlackBoxRuleFacts.ConflictCount(r) > 0)
                .In(BlackBoxPhaseEnum.Landing)
                .YieldsTo("BBX-LAND-004")
                .Because("{conflictMission} ({conflictMissionTitle|another mission}) landed {conflictCommit|a commit} on {targetBranch|the target} at {conflictLandedUtc|an earlier time}, changing {conflictOverlapCount|some} of the same files.", BlackBoxConfidenceEnum.High, "conflict.conflictingMissions", "conflict.conflictedFiles")
                .Votes(BlackBoxTriageEnum.UserConfig, 2)
                .Recommend(new BlackBoxRuleRecommendation
                {
                    Key = "serialize-vessel",
                    Setting = "AllowConcurrentMissions",
                    Scope = BlackBoxConfigSourceEnum.Vessel,
                    SuggestedValue = "False",
                    Rationale = "Missions on this vessel touch the same files; running them one at a time avoids the conflict.",
                    HowToApply = "update_vessel {\"vesselId\": \"{vesselId}\", \"allowConcurrentMissions\": false}; or chain the missions with DependsOnMissionId, or set WaitForVoyageWorkers on the voyage.",
                    ApplyTool = "update_vessel",
                    ApplyArguments = { ["vesselId"] = "{vesselId}", ["allowConcurrentMissions"] = "false" }
                })
                .Recommend(new BlackBoxRuleRecommendation
                {
                    Key = "depends-on",
                    Setting = "DependsOnMissionId",
                    Scope = BlackBoxConfigSourceEnum.Mission,
                    SuggestedValue = "{conflictMission}",
                    Rationale = "Dispatch overlapping work after {conflictMission} so it starts from its result.",
                    HowToApply = "Set dependsOnMissionId when dispatching the next missions that touch these files."
                })
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Dispatch",
                    Hypothesis = "Detect file overlap between active missions at dispatch and serialize overlapping missions.",
                    Evidence = "{conflictMission} and {missionId} changed {conflictOverlapCount|some} of the same files.",
                    SourcePointers = { "src/Armada.Core/Services/MissionService.cs:TryAssignAsync" },
                    Severity = BlackBoxSeverityEnum.Medium
                })
                .Recover(new BlackBoxRulePath
                {
                    Key = "restart-on-target",
                    Title = "Restart the mission on the current target",
                    Steps = { RestartStep() },
                    Risk = BlackBoxSeverityEnum.Medium,
                    ExpectedOutcome = "The mission runs again from {targetBranch|the target} including {conflictMission}'s change."
                })
                .Recover(new BlackBoxRulePath
                {
                    Key = "resolve-retry",
                    Title = "Resolve the conflict on the branch, then retry the landing",
                    Steps =
                    {
                        BlackBoxRuleStep.Manual("Merge {targetBranch|the target branch} into {branchName|the mission branch}, resolve {conflictedFiles} and commit."),
                        RetryLandingStep()
                    },
                    Risk = BlackBoxSeverityEnum.Low,
                    ExpectedOutcome = "The resolved branch lands."
                })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "rebased {branchName|the mission branch} onto {targetBranch|the moved target} and retried, or serialized the overlapping missions at dispatch so this one started from {conflictMission|the other mission}'s result",
                    BlackBoxDetectionGapEnum.Race,
                    "Armada ran {conflictMission|the other mission} and {missionId|this mission} concurrently on overlapping files without chaining them and found the overlap only at landing, after both captains had finished.",
                    "Two active missions that touch the same files are never integrated blindly: they are chained at dispatch or the later one is rebased onto the earlier one's landing before it integrates.",
                    "Services.LandingConflict",
                    "Land {conflictMission|a mission}, then {missionId|an overlapping mission}, and assert the second is rebased and lands, or was chained at dispatch, instead of failing with a conflict.",
                    new[] { D.TargetSync, "src/Armada.Core/Services/MissionService.cs:TryAssignAsync", "src/Armada.Core/BlackBox/MissionOverlapAnalyzer.cs:PreviewAsync" },
                    BlackBoxRuleChange.Of("Rebase onto the moved target and retry before failing a conflicted landing", "When a landing conflicts with a commit that landed since the mission started ({conflictCommit|a newer commit} from {conflictMission|another mission}), LandingTargetSynchronization should rebase {branchName|the branch} onto {targetBranch|the target}, or hand the conflict to the captain as a fix-up stage, and retry once; fail only when the conflict survives with the conflicting mission attributed.", "Services.LandingConflict: two missions edit the same file; the second landing is rebased and lands when the hunks do not overlap, and fails with the conflicting mission attributed when they do.", BlackBoxSeverityEnum.High, D.TargetSync),
                    BlackBoxRuleChange.Of("Chain overlapping missions automatically where conflicts recur", "Use the dispatch overlap preview and the black box hotspots to chain missions that touch the same files ({conflictOverlapCount|some} overlapping files here) with DependsOnMissionId, and turn serializeOverlappingMissions on for a vessel where this conflict fingerprint recurs.", "Services.BlackBoxPrevention: a second mission whose brief names files an active mission is changing is dispatched with DependsOnMissionId set to it.", BlackBoxSeverityEnum.Medium, "src/Armada.Core/BlackBox/MissionOverlapAnalyzer.cs:PreviewAsync", "src/Armada.Core/Services/MissionService.cs:TryAssignAsync"))));

            rules.Add(new BlackBoxRule("BBX-LAND-005", "Verification failed", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.Verification && BlackBoxResilienceScope.ConcreteCheckFailure(r))
                .In(BlackBoxPhaseEnum.Landing)
                .YieldsTo("BBX-CHECK-002", "BBX-CHECK-003")
                .Because("Post-merge verification `{failingCommand|the verification command}` failed with exit {commandExitCode|code unknown}.", BlackBoxConfidenceEnum.High, "mechanism.failingCommand", "mechanism.commandOutputTail")
                .Votes(BlackBoxTriageEnum.MissionBrief, 3)
                .Recommend(new BlackBoxRuleRecommendation
                {
                    Key = "verification-commands",
                    Setting = "StageCheckTimeoutMinutes",
                    Scope = BlackBoxConfigSourceEnum.Global,
                    Rationale = "Check the workflow profile's build and test commands and the stage check timeout ({config:StageCheckTimeoutMinutes|default} minutes) if the command was slow rather than failing.",
                    HowToApply = "Edit the workflow profile commands, or stageCheckTimeoutMinutes in PUT /api/v1/settings."
                })
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Landing verification",
                    Hypothesis = "Run the verification command on the target tip too, to separate a pre-existing failure from one this mission introduced.",
                    SourcePointers = { "src/Armada.Core/Services/LandingVerification.cs:VerifyAsync" },
                    Severity = BlackBoxSeverityEnum.Low
                })
                .Recover(new BlackBoxRulePath
                {
                    Key = "fix-forward",
                    Title = "Fix forward on the branch, then retry the landing",
                    Steps =
                    {
                        BlackBoxRuleStep.Manual("Fix the failure `{failingCommand|the verification command}` reports on {branchName|the mission branch} and commit."),
                        RetryLandingStep()
                    },
                    Risk = BlackBoxSeverityEnum.Low,
                    ExpectedOutcome = "Verification passes and the mission lands."
                })
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission so the captain fixes the failure", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Medium, ExpectedOutcome = "The captain sees the failure and fixes it." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "handed the failing verification output back to the captain as a fix-up attempt (after checking the target tip to rule out a pre-existing failure) instead of ending the mission at landing",
                    BlackBoxDetectionGapEnum.CheckTooLate,
                    "The verification `{failingCommand|command}` ran only after the merge; the same command did not gate the mission's completion on its own branch, so the failure surfaced after the captain had finished.",
                    "A mission reaches landing only after its verification command passes on its branch, and a post-merge failure is compared with the target tip before it is blamed on the mission.",
                    "Services.LandingPipeline",
                    "Make `{failingCommand|the verification command}` fail on the merged tree of {missionId|a mission} and assert Armada checks the target tip and sends the failure back to the captain instead of failing the mission.",
                    new[] { D.Verification, "src/Armada.Core/Services/MissionService.Completion.cs:TryFailForDefinitionOfDoneAsync" },
                    BlackBoxRuleChange.Of("Check the target tip before blaming the mission for a verification failure", "When post-merge verification `{failingCommand|the command}` fails, LandingVerification should run it on the target tip too; a failure there is pre-existing and must not fail this mission, and a failure only on the merged tree is sent back to the captain as a fix-up attempt with the output.", "Services.LandingPipeline: a verification failure that also fails on the target tip is reported as pre-existing and the mission still lands.", BlackBoxSeverityEnum.Medium, D.Verification),
                    BlackBoxRuleChange.Of("Gate completion on the landing verification command", "Run the landing verification commands as part of the mission's definition of done on its own branch, so a failing build or test is fixed by the captain before Armada spends a landing attempt on it.", "Services.LandingPipeline: a mission whose branch fails the verification command does not reach landing and is returned to its captain.", BlackBoxSeverityEnum.Low, "src/Armada.Core/Services/MissionService.Completion.cs:TryFailForDefinitionOfDoneAsync"))));

            rules.Add(new BlackBoxRule("BBX-LAND-006", "Push or pull request failed", r =>
                    BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.Push || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.PullRequest || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.UnpublishedAncestry)
                .In(BlackBoxPhaseEnum.Landing)
                .YieldsTo("BBX-INFRA-001", "BBX-LAND-008", "BBX-LAND-009", "BBX-LAND-010", "BBX-LAND-015")
                .Because("Publishing {branchName|the mission branch} to the remote failed at step {step}: {gitStderr|reason}.", BlackBoxConfidenceEnum.Medium, "mechanism.gitStderrTail", "classification.rawReason")
                .Votes(BlackBoxTriageEnum.External, 2)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing once the remote accepts pushes", Preconditions = { "The remote is reachable and the credentials Armada uses can push." }, Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The branch is published and the mission lands." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "retried publishing {branchName|the branch} with backoff (fetching and rebasing first when the remote had moved) and kept the mission WorkProduced with its branch instead of failing it",
                    BlackBoxDetectionGapEnum.MissingRetry,
                    "Publishing failed at step {step} ({gitStderr|no stderr recorded}) and Armada neither classified the remote's answer nor retried before failing the landing.",
                    "A transient publish failure (network, remote busy, moved target) is retried with backoff; only a classified permanent refusal fails the landing, and the branch is kept.",
                    "Services.LandingPipeline",
                    "Make the first push of {branchName|the branch} fail transiently and assert Armada retries with backoff and lands.",
                    new[] { "src/Armada.Core/Services/GitService.cs:PushBranchAsync", D.LandingHandler, D.RetryLanding },
                    BlackBoxRuleChange.Of("Classify and retry publish failures with backoff", "Classify push and pull request failures (network, remote busy, non-fast-forward, credentials, protected branch) in the landing handler; retry the retryable ones with exponential backoff, rebasing onto the target when it moved, and fail only on a permanent refusal, keeping the mission WorkProduced with its branch.", "Services.LandingPipeline: a push that fails once with a network error is retried and lands; a credentials failure fails once with a clear reason.", BlackBoxSeverityEnum.Medium, "src/Armada.Core/Services/GitService.cs:PushBranchAsync", D.LandingHandler))));

            rules.Add(new BlackBoxRule("BBX-LAND-007", "Landing failed", r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Landing)
                .In(BlackBoxPhaseEnum.Landing)
                .YieldsTo("BBX-LAND-001", "BBX-LAND-002", "BBX-LAND-003", "BBX-LAND-004", "BBX-LAND-005", "BBX-LAND-006", "BBX-INFRA-001", "BBX-LAND-008", "BBX-LAND-009", "BBX-LAND-010", "BBX-LAND-011", "BBX-LAND-012", "BBX-LAND-013", "BBX-LAND-014", "BBX-LAND-015", "BBX-CHECK-002", "BBX-CHECK-003", "BBX-PROV-002")
                .Because("The landing failed at step {step}: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Low, "classification.rawReason", "mechanism")
                .Votes(BlackBoxTriageEnum.Unknown, 0)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The landing is attempted again on the current target." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Unknown,
                    "retried the landing with backoff after re-checking the dock, the branch and the target, and kept the work intact for a person when the retry failed too",
                    BlackBoxDetectionGapEnum.NoPreflightCheck,
                    "No specific rule or preflight check covers landing step {step} ({reason|no reason recorded}); the landing ended on its first failure.",
                    "Before each landing step the dock, branch and target are verified, and a failure there leaves the mission WorkProduced with its branch rather than terminal.",
                    "Services.LandingPipeline",
                    "Reproduce the {step} landing failure of {missionId|the mission} and assert Armada retries once with backoff and keeps the branch for recovery.",
                    new[] { D.LandingHandler, D.RetryLanding, D.Integrity },
                    BlackBoxRuleChange.Of("Classify landing step {step} failures and retry the retryable ones", "The landing failed at step {step} with no specific handling ({reason|no reason recorded}). Classify this failure in MissionLandingHandler, retry it once with backoff when it is retryable, keep the mission WorkProduced with its branch when it is not, and add a black box rule with resilience content for it.", "Services.LandingPipeline: a mission that fails at {step} once is retried and lands; a second failure leaves it recoverable with its branch.", BlackBoxSeverityEnum.Medium, D.LandingHandler, D.RetryLanding))));

            rules.Add(new BlackBoxRule("BBX-LAND-008", "Push rejected because the target moved", r => (BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.Push || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.QueuePush || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.FastForward) && BlackBoxRuleFacts.PushRejectedMovedTarget(r))
                .In(BlackBoxPhaseEnum.Landing)
                .Because("The remote rejected the push of {branchName|the mission branch} because {targetBranch|the target} moved while Armada integrated it ({gitStderr|non-fast-forward}).", BlackBoxConfidenceEnum.High, "mechanism.gitStderrTail", "workspace.targetTipAtFailure", "handoff.steps")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 3)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The landing is attempted again on the current target." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "fetched {targetBranch|the target}, re-integrated {branchName|the branch} on its new tip and pushed again instead of failing on a non-fast-forward",
                    BlackBoxDetectionGapEnum.MissingRetry,
                    "Armada pushed against a target tip it had read before integrating; when another landing moved {targetBranch|the target} in between, the push was rejected and Armada did not fetch and retry.",
                    "A landing pushes only on top of the target tip it integrated against, and a non-fast-forward rejection triggers a fetch, re-integration and retry.",
                    "Services.LandingPipeline",
                    "Land two missions on {targetBranch|the same target} back to back and assert the second, rejected as non-fast-forward, re-integrates on the new tip and lands.",
                    new[] { "src/Armada.Core/Services/GitService.cs:PushBranchAsync", D.TargetSync, D.LandingHandler },
                    BlackBoxRuleChange.Of("Retry a non-fast-forward push after re-integrating on the new target tip", "When the push of {branchName|the branch} is rejected as non-fast-forward or fetch first, fetch {targetBranch|the target}, re-run the target integration on its new tip (LandingTargetSynchronization), verify again and push, with a bounded number of attempts, instead of failing the landing.", "Services.LandingPipeline: a push rejected because another landing moved the target is re-integrated and lands without operator action.", BlackBoxSeverityEnum.High, D.TargetSync, "src/Armada.Core/Services/GitService.cs:PushBranchAsync"))));

            rules.Add(new BlackBoxRule("BBX-LAND-010", "Remote refused Armada's credentials", r => (BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.Push || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.PullRequest || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.QueuePush || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.UnpublishedAncestry) && BlackBoxRuleFacts.RemoteAuthRefused(r))
                .In(BlackBoxPhaseEnum.Landing)
                .Because("The remote refused the credentials Armada used to publish {branchName|the mission branch}: {gitStderr|authentication failed}.", BlackBoxConfidenceEnum.High, "mechanism.gitStderrTail", "classification.rawReason")
                .Votes(BlackBoxTriageEnum.UserConfig, 3)
                .Recover(new BlackBoxRulePath { Key = "fix-credentials-retry", Title = "Fix the remote credentials, then retry the landing", Steps = { BlackBoxRuleStep.Manual("Sign git and gh in again on {harborName|the Admiral host} (git credential, SSH key or gh auth login) so it can push to {vesselName|the vessel}'s remote."), RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The branch is published and the mission lands." })
                .Heals(Heal(BlackBoxSelfHealableEnum.No,
                    "checked the remote credentials before dispatch (git ls-remote and gh auth status on the owning host) and refused to start work on a vessel that cannot publish, instead of discovering it after the captain finished",
                    BlackBoxDetectionGapEnum.NoPreflightCheck,
                    "Armada first used the publish credentials at landing; no preflight at dispatch verified that the host can push to the vessel's remote.",
                    "A mission is dispatched only to a host whose credentials can publish to the vessel's remote, and a credential failure keeps the mission WorkProduced with its branch.",
                    "Services.LandingPipeline",
                    "Dispatch to a vessel whose remote rejects the host's credentials and assert dispatch warns before the captain starts, and the landing keeps the branch.",
                    new[] { D.LandingHandler, "src/Armada.Core/Services/GitService.cs:PushBranchAsync", "src/Armada.Core/Services/AdmiralService.cs:ValidateDispatchAsync" },
                    BlackBoxRuleChange.Of("Preflight publish credentials at dispatch", "Before dispatching a mission whose landing publishes, verify on the owning host that git can push to the remote and gh is authenticated (cached per vessel and host), warn or refuse dispatch when it cannot, and keep a landing that fails on credentials WorkProduced with its branch so it can be retried after the fix.", "Services.LandingPipeline: a credential refusal at landing leaves the mission WorkProduced with its branch, and dispatch to a host without push credentials is warned.", BlackBoxSeverityEnum.Medium, "src/Armada.Core/Services/AdmiralService.cs:ValidateDispatchAsync", D.LandingHandler))));

            rules.Add(new BlackBoxRule("BBX-LAND-009", "Pull request creation failed", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.PullRequest)
                .In(BlackBoxPhaseEnum.Landing)
                .YieldsTo("BBX-LAND-010", "BBX-INFRA-001")
                .Because("Armada published {branchName|the mission branch} but could not open its pull request: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "classification.rawReason", "mechanism.gitStderrTail")
                .Votes(BlackBoxTriageEnum.External, 2)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The landing is attempted again on the current target." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "retried the pull request creation with backoff, reused an already open pull request for {branchName|the branch}, and kept the mission WorkProduced instead of failing it",
                    BlackBoxDetectionGapEnum.MissingRetry,
                    "The pull request call failed once and Armada neither retried it nor looked for an existing pull request for the same branch.",
                    "Opening a pull request is idempotent (an open pull request for the branch is reused) and a transient API failure is retried.",
                    "Services.LandingPipeline",
                    "Make the first pull request creation for {branchName|a branch} fail transiently and assert Armada retries and the mission reaches PullRequestOpen.",
                    new[] { "src/Armada.Core/Services/GitService.cs:CreatePullRequestAsync", D.LandingHandler },
                    BlackBoxRuleChange.Of("Make pull request creation idempotent and retried", "GitService.CreatePullRequestAsync should look up an open pull request for {branchName|the branch} first and reuse it, retry transient API failures with backoff, and the landing handler should keep the mission WorkProduced with its branch pushed when the API still refuses.", "Services.LandingPipeline: an existing open pull request is reused; a transient API failure is retried and the mission reaches PullRequestOpen.", BlackBoxSeverityEnum.Medium, "src/Armada.Core/Services/GitService.cs:CreatePullRequestAsync"))));

            rules.Add(new BlackBoxRule("BBX-LAND-011", "Target moved during integration", r => (BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.TargetIntegration || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.FastForward || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.LocalMerge) && BlackBoxRuleFacts.ConflictCount(r) == 0 && BlackBoxRuleFacts.TargetMoved(r))
                .In(BlackBoxPhaseEnum.Landing)
                .YieldsTo("BBX-LAND-002", "BBX-LAND-008")
                .Because("{targetBranch|The target branch} moved from {targetTipAtStart|its tip at start} to {targetTipAtFailure|a newer tip} while Armada integrated {branchName|the mission branch}, and the integration did not recover from it.", BlackBoxConfidenceEnum.Medium, "workspace.targetTipAtStart", "workspace.targetTipAtFailure", "handoff.targetMoved")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 3)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The landing is attempted again on the current target." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "re-read {targetBranch|the target}, re-ran the integration on its new tip and retried until the tip was stable, instead of failing because the target moved underneath it",
                    BlackBoxDetectionGapEnum.Race,
                    "The landing integrated against a target tip that another landing moved before Armada fast-forwarded or merged; the target synchronization did not loop until the tip was stable.",
                    "A landing completes against the target tip it last read: when the tip moves, the integration is re-run on the new tip before the landing gives up.",
                    "Services.LandingPipeline",
                    "Advance {targetBranch|the target} between integration and fast-forward of {missionId|a mission} and assert the landing re-integrates on the new tip and lands.",
                    new[] { D.TargetSync, D.LandingHandler, "src/Armada.Core/Services/GitService.cs:FastForwardVerifiedCommitAsync" },
                    BlackBoxRuleChange.Of("Re-integrate on the moved target tip instead of failing", "LandingTargetSynchronization.SynchronizeAsync should detect that {targetBranch|the target} moved since the integration began (mission.landing_target_advanced), re-run the integration and verification on the new tip and retry the fast-forward a bounded number of times, failing only when it never stabilizes.", "Services.LandingPipeline: a target that advances once during landing is re-integrated and the mission lands.", BlackBoxSeverityEnum.High, D.TargetSync))));

            rules.Add(new BlackBoxRule("BBX-LAND-012", "Mission branch missing", r => (BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Landing || BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.MergeQueue || BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Review) && BlackBoxRuleFacts.BranchMissing(r))
                .In(BlackBoxPhaseEnum.Landing)
                .Because("The mission branch {branchName|of this mission} was missing when Armada tried to land it, although the work was produced.", BlackBoxConfidenceEnum.High, "workspace.branchExists", "mechanism.gitStderrTail")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 3)
                .Recover(new BlackBoxRulePath { Key = "restore-branch", Title = "Restore the branch from the mission commit, then retry the landing", Optional = true, Preconditions = { "The mission commit {missionCommit} is still in the repository." }, Steps = { BlackBoxRuleStep.Manual("Recreate the branch {branchName} at {missionCommit}.", "git branch {branchName} {missionCommit}"), RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Medium, ExpectedOutcome = "The work at {missionCommit} lands." })
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The landing is attempted again on the current target." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "restored {branchName|the branch} from {missionCommit|the mission commit} (or the dock's HEAD) and retried, since the work was still in the repository",
                    BlackBoxDetectionGapEnum.StateNotPersisted,
                    "The branch was deleted (cleanup, a reclaim or a force-push) before landing, and Armada had not persisted the mission commit as a ref it could land from.",
                    "Until a mission lands, its work is reachable from a ref Armada owns (the branch or a refs/armada/missions ref at the mission commit).",
                    "Services.LandingPipeline",
                    "Delete the branch of {missionId|a WorkProduced mission} and assert the landing restores it from the mission commit and lands.",
                    new[] { "src/Armada.Server/MissionLandingHandler.cs:CleanupMissionBranchAsync", D.LandingHandler, "src/Armada.Core/Services/GitService.cs:EnsureLocalBranchAsync" },
                    BlackBoxRuleChange.Of("Keep a ref to the mission commit until it lands and restore the branch from it", "Record the mission commit under an Armada-owned ref (refs/armada/missions/{missionId|id}) when the work is produced, never delete it before landing, and when {branchName|the branch} is missing at landing recreate it from that ref and continue.", "Services.LandingPipeline: a WorkProduced mission whose branch was deleted lands from the Armada ref.", BlackBoxSeverityEnum.High, D.LandingHandler, "src/Armada.Core/Services/GitService.cs:EnsureLocalBranchAsync"))));

            rules.Add(new BlackBoxRule("BBX-LAND-013", "Landing retries exhausted", r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Landing && BlackBoxRuleFacts.LandingRetriesExhausted(r))
                .In(BlackBoxPhaseEnum.Landing)
                .Because("Armada retried the landing until its retry budget ran out ({reason|no reason recorded}).", BlackBoxConfidenceEnum.Medium, "handoff.autoRecoveries", "classification.rawReason")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The landing is attempted again on the current target." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Unknown,
                    "classified why each landing attempt failed, kept retrying with exponential backoff while the cause was retryable and escalated with the dock and branch preserved when it was not, instead of spending a fixed retry budget on the same failure",
                    BlackBoxDetectionGapEnum.MissingInvariant,
                    "Armada retried the landing without classifying each attempt's failure, so the retries repeated a cause retrying could not fix and the budget ran out.",
                    "Every landing retry is preceded by a classification of the previous failure, and a non-retryable cause stops the retries early with the work preserved.",
                    "Services.LandingReconciler",
                    "Fail a landing three times with the same non-retryable cause and assert Armada stops after the first classification and preserves the dock and branch.",
                    new[] { D.RetryLanding, D.Integrity, D.LandingHandler },
                    BlackBoxRuleChange.Of("Classify each landing retry and back off instead of exhausting a fixed budget", "Before each automatic landing retry, classify the previous failure (dirty checkout, conflict, moved target, missing dock, credentials, transient); retry the retryable ones with exponential backoff, stop at once on a permanent cause, and record the classification on mission.landing_retry so the black box sees why the retries ran out.", "Services.LandingReconciler: a permanent failure stops the retries after one attempt; a transient one is retried with growing delays.", BlackBoxSeverityEnum.Medium, D.RetryLanding, D.Integrity))));

            rules.Add(new BlackBoxRule("BBX-LAND-014", "Landing deferral expired", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.DeferralExpired)
                .In(BlackBoxPhaseEnum.Landing)
                .Because("The landing was deferred (the checkout at {checkoutPath|the vessel's working directory} stayed dirty) and the deferral expired before it could land.", BlackBoxConfidenceEnum.High, "handoff.autoRecoveries", "workspace.dirtyPaths")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "clean-retry", Title = "Clean the checkout, then retry the landing", Steps = { BlackBoxRuleStep.Manual("Commit or stash the changes in {checkoutPath|the checkout}.", "git -C \"{checkoutPath|<checkout>}\" stash push --include-untracked"), RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The landing merges the branch and the mission completes." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "landed through an Armada-owned integration worktree when the checkout stayed dirty for the whole deferral window, or re-armed the deferral and told the operator which files blocked it",
                    BlackBoxDetectionGapEnum.MissingInvariant,
                    "The deferral waited for the operator's checkout to become clean and then expired; Armada had no landing path that does not need the checkout.",
                    "A deferred landing never expires into a failure while an Armada-owned integration path is available.",
                    "Services.LandingPipeline",
                    "Keep {checkoutPath|the checkout} dirty past the deferral window and assert the landing lands through the integration worktree instead of expiring.",
                    new[] { D.Deferrals, D.LandingHandler, "src/Armada.Server/MissionLandingHandler.cs:RecordLandingDeferralAsync" },
                    BlackBoxRuleChange.Of("Fall back to an integration worktree when a landing deferral expires", "When LandingIntegrityService finds an expired deferral, land through an Armada-owned integration worktree (or re-arm the deferral with a notification naming the dirty files) instead of failing the mission with DeferralExpired.", "Services.LandingPipeline: an expired deferral lands through the integration worktree and the operator's dirty files are unchanged.", BlackBoxSeverityEnum.High, D.Deferrals))));

            rules.Add(new BlackBoxRule("BBX-LAND-015", "Unpublished ancestry", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.UnpublishedAncestry && !BlackBoxRuleFacts.RemoteAuthRefused(r))
                .In(BlackBoxPhaseEnum.Landing)
                .Because("{branchName|The mission branch} builds on commits that were never published (an earlier stage or dependency branch), so the landing refused to push it.", BlackBoxConfidenceEnum.Medium, "classification.rawReason", "workspace.commitsAheadOfTarget")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The landing is attempted again on the current target." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "published the ancestor stage branch first (or rebased {branchName|the branch} onto the published target) and retried the landing",
                    BlackBoxDetectionGapEnum.CheckTooLate,
                    "Armada learned at landing that the branch's ancestry was unpublished; the stage handoff that created it did not publish or harden the ancestor branch first.",
                    "A mission branch handed to landing has all of its ancestry on the remote or on the target.",
                    "Services.HandoffResilience",
                    "Hand off a stage whose ancestor branch was never pushed and assert Armada publishes the ancestor before landing.",
                    new[] { D.HardenStage, D.LandingHandler },
                    BlackBoxRuleChange.Of("Publish the ancestor branch before landing a stage branch", "During the stage handoff (HardenStageBranchAsync) publish the ancestor stage branch, and at landing, when ancestry is unpublished, publish it or rebase {branchName|the branch} onto the published target before pushing.", "Services.HandoffResilience: a stage branch built on an unpublished ancestor lands after Armada publishes the ancestor.", BlackBoxSeverityEnum.Medium, D.HardenStage))));
        }

        private static void AddMergeQueue(List<BlackBoxRule> rules)
        {
            rules.Add(new BlackBoxRule("BBX-MQ-001", "Merge-queue failure labeled as a conflict", r =>
                    BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.MergeQueue && BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.LandingConflict && BlackBoxRuleFacts.ConflictCount(r) == 0)
                .In(BlackBoxPhaseEnum.MergeQueue)
                .Because("The merge queue reported a failure that was not a merge conflict (no conflicted files were recorded) at step {step}.", BlackBoxConfidenceEnum.Medium, "classification.kind", "conflict")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Merge queue",
                    Hypothesis = "Classify merge-queue failures by step (resolve repository, tests, push) instead of reporting every failure as LandingConflict.",
                    SourcePointers = { "src/Armada.Core/Services/MergeQueueService.cs:ProcessEntryAsync", "src/Armada.Core/Services/MergeQueueService.cs:MergeBranchAsync" },
                    Severity = BlackBoxSeverityEnum.Medium
                })
                .Recover(new BlackBoxRulePath
                {
                    Key = "read-retry",
                    Title = "Read the merge entry's output, fix the real cause, then retry the landing",
                    Steps = { BlackBoxRuleStep.Manual("Read the merge entry's test output (evidence/merge-entry.txt) to find the real failure."), RetryLandingStep() },
                    Risk = BlackBoxSeverityEnum.Low,
                    ExpectedOutcome = "The entry is re-queued once the real cause is fixed."
                })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "classified the merge-queue failure by its real step and retried the entry when the cause was retryable, instead of reporting it as a conflict",
                    BlackBoxDetectionGapEnum.MissingInvariant,
                    "The merge queue reports every failure as LandingConflict, so Armada could not tell a retryable test, repository or push problem from a real conflict and did not retry.",
                    "A merge-queue failure carries the step that failed (resolve repository, merge, tests, push) and a LandingConflict always names its conflicted files.",
                    "Services.MergeQueueProcessing",
                    "Fail the merge-queue push of an entry and assert the failure is classified as a push failure (not LandingConflict) and the entry is retried.",
                    new[] { D.MergeQueue, D.MergeQueueMerge },
                    BlackBoxRuleChange.Of("Classify merge-queue failures by step", "MergeQueueService should record the failing step (QueueResolveRepo, QueueMergeConflict, QueueTests, QueuePush) and kind on the mission and the merge entry instead of LandingConflict for everything, and retry the retryable steps once.", "Services.MergeQueueProcessing: a queue test failure is reported as QueueTests/TestFail and a push failure as QueuePush, never as LandingConflict without conflicted files.", BlackBoxSeverityEnum.Medium, D.MergeQueue))));

            rules.Add(new BlackBoxRule("BBX-MQ-002", "Merge-queue tests failed", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.QueueTests && BlackBoxResilienceScope.ConcreteCheckFailure(r))
                .In(BlackBoxPhaseEnum.MergeQueue)
                .YieldsTo("BBX-CHECK-002", "BBX-CHECK-003")
                .Because("The merge queue's test command `{failingCommand|the queue test command}` failed with exit {commandExitCode|code unknown} on the integrated branch.", BlackBoxConfidenceEnum.High, "mechanism.commandOutputTail", BlackBoxObjectNames.MergeEntry)
                .Votes(BlackBoxTriageEnum.MissionBrief, 3)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission so the captain fixes the failing tests", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Medium, ExpectedOutcome = "The fixed branch passes the queue's tests." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "returned the failing merge-queue test output to the captain as a fix-up attempt (after checking that the tests pass on the target tip) instead of failing the entry",
                    BlackBoxDetectionGapEnum.CheckTooLate,
                    "The queue's test command `{failingCommand|the queue test command}` first ran on the integrated branch inside the merge queue, after the mission had completed.",
                    "A mission enters the merge queue only after the queue's test command passes on its branch, and a queue failure is checked against the target tip.",
                    "Services.MergeQueueProcessing",
                    "Make the queue's tests fail on {missionId|an entry} and assert the failure goes back to the captain with the output instead of ending the mission.",
                    new[] { D.MergeQueue, D.Completion },
                    BlackBoxRuleChange.Of("Run the merge-queue tests before enqueueing", "Run the vessel's MergeQueueTestCommand on the mission branch at completion, and when the queue's run still fails, check the target tip and send a mission-introduced failure back to the captain as a fix-up attempt.", "Services.MergeQueueProcessing: a branch that fails the queue tests is returned to its captain before it is enqueued.", BlackBoxSeverityEnum.Medium, D.MergeQueue, D.Completion))));

            rules.Add(new BlackBoxRule("BBX-MQ-003", "Merge-queue failure", r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.MergeQueue)
                .In(BlackBoxPhaseEnum.MergeQueue)
                .YieldsTo("BBX-MQ-001", "BBX-MQ-002", "BBX-LAND-003", "BBX-LAND-004", "BBX-MQ-004", "BBX-MQ-005", "BBX-MQ-006", "BBX-LAND-008", "BBX-LAND-010", "BBX-LAND-012", "BBX-CHECK-002", "BBX-CHECK-003")
                .Because("The merge queue failed at step {step}: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Low, "classification.rawReason")
                .Votes(BlackBoxTriageEnum.Unknown, 0)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission is landed again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Unknown,
                    "retried the merge-queue entry with backoff after re-checking the repository, the branch and the target, and kept the branch for recovery",
                    BlackBoxDetectionGapEnum.NoPreflightCheck,
                    "No specific rule covers merge-queue step {step} ({reason|no reason recorded}); the entry failed on its first attempt.",
                    "A merge-queue failure leaves the mission recoverable with its branch, and retryable steps are retried before the entry fails.",
                    "Services.MergeQueueProcessing",
                    "Reproduce the {step} merge-queue failure and assert the entry is retried once and the branch kept.",
                    new[] { D.MergeQueue, D.MergeQueueMerge },
                    BlackBoxRuleChange.Of("Retry merge-queue step {step} before failing the entry", "The merge queue failed at {step} with no specific handling ({reason|no reason recorded}). Classify it in MergeQueueService.ProcessEntryAsync, retry it once with backoff when it is retryable, and add a black box rule with resilience content for it.", "Services.MergeQueueProcessing: an entry that fails at {step} once is retried and lands.", BlackBoxSeverityEnum.Medium, D.MergeQueue))));

            rules.Add(new BlackBoxRule("BBX-MQ-004", "Merge entry stale or lease expired", r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.MergeQueue && BlackBoxRuleFacts.StaleOrExpired(r))
                .In(BlackBoxPhaseEnum.MergeQueue)
                .Because("The merge entry went stale or its processing lease expired at step {step}: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "classification.rawReason", "handoff.steps")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 3)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The landing is attempted again on the current target." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "refreshed the merge entry's lease while it was being processed and reclaimed the stale entry for another attempt, instead of failing it",
                    BlackBoxDetectionGapEnum.StateNotPersisted,
                    "The merge entry's processing lease was not renewed (or not persisted) while it ran, so a slow step or a restart left it stale.",
                    "A merge entry being processed holds a renewed lease; an expired lease returns the entry to the queue rather than failing the mission.",
                    "Services.MergeQueueProcessing",
                    "Let the lease of a processing merge entry expire and assert the entry is re-queued and lands.",
                    new[] { D.MergeQueue, "src/Armada.Core/Services/MergeQueueService.cs:FailUnprocessedAsync", "src/Armada.Core/Services/MergeQueueService.cs:ReconcileMissionStatusAsync" },
                    BlackBoxRuleChange.Of("Renew merge-entry leases and re-queue stale entries", "MergeQueueService should renew the processing lease of an entry while its tests run and, when it finds a stale entry, return it to the queue (with the attempt counted) instead of failing the mission.", "Services.MergeQueueProcessing: a stale entry is re-queued and lands; only an entry over its attempt limit fails.", BlackBoxSeverityEnum.High, D.MergeQueue))));

            rules.Add(new BlackBoxRule("BBX-MQ-005", "Merge queue could not resolve the repository", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.QueueResolveRepo)
                .In(BlackBoxPhaseEnum.MergeQueue)
                .Because("The merge queue could not resolve the repository of {vesselName|the vessel}: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "classification.rawReason")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The landing is attempted again on the current target." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "re-resolved the repository from the vessel (recreating the bare clone when it was missing, or waiting for its Harbor) and retried the entry",
                    BlackBoxDetectionGapEnum.NoPreflightCheck,
                    "The merge queue resolved the repository only when it processed the entry, with no fallback when the clone or host was missing.",
                    "A merge entry is enqueued only for a vessel whose repository resolves, and a resolution failure is repaired and retried.",
                    "Services.MergeQueueProcessing",
                    "Remove the bare clone of a vessel with a queued entry and assert the queue recreates it and lands the entry.",
                    new[] { "src/Armada.Core/Services/MergeQueueService.cs:GetRepoPathAsync", "src/Armada.Core/Services/MergeQueueService.cs:ResolveHostAsync", D.MergeQueue },
                    BlackBoxRuleChange.Of("Repair repository resolution in the merge queue", "MergeQueueService.GetRepoPathAsync should recreate a missing bare clone from the vessel's repository URL (or wait for the owning Harbor) and retry the entry instead of failing it.", "Services.MergeQueueProcessing: an entry whose bare clone is missing is processed after the queue recreates the clone.", BlackBoxSeverityEnum.Medium, "src/Armada.Core/Services/MergeQueueService.cs:GetRepoPathAsync"))));

            rules.Add(new BlackBoxRule("BBX-MQ-006", "Merge queue push failed", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.QueuePush)
                .In(BlackBoxPhaseEnum.MergeQueue)
                .YieldsTo("BBX-LAND-008", "BBX-LAND-010", "BBX-INFRA-001")
                .Because("The merge queue could not push the integrated branch to {targetBranch|the target}: {gitStderr|reason}.", BlackBoxConfidenceEnum.Medium, "mechanism.gitStderrTail", BlackBoxObjectNames.MergeEntry)
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The landing is attempted again on the current target." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "re-fetched {targetBranch|the target}, rebuilt the integration on its new tip and pushed again, with backoff for transient remote errors",
                    BlackBoxDetectionGapEnum.MissingRetry,
                    "The queue pushed once and failed the entry; it did not retry or rebuild the integration when the remote refused.",
                    "A merge-queue push failure is retried after re-integration; only a classified permanent refusal fails the entry.",
                    "Services.MergeQueueProcessing",
                    "Make the queue's first push fail and assert the entry is re-integrated, pushed again and lands.",
                    new[] { D.MergeQueue, "src/Armada.Core/Services/MergeQueueService.cs:LandEntryAsync" },
                    BlackBoxRuleChange.Of("Retry merge-queue pushes after re-integration", "When MergeQueueService.LandEntryAsync fails to push, fetch the target, rebuild the integration on its new tip, re-run the queue tests when the tip moved and push again, with backoff and a bounded number of attempts.", "Services.MergeQueueProcessing: a queue push that fails once lands on the retry.", BlackBoxSeverityEnum.Medium, "src/Armada.Core/Services/MergeQueueService.cs:LandEntryAsync"))));
        }

        private static void AddCompletion(List<BlackBoxRule> rules)
        {
            rules.Add(new BlackBoxRule("BBX-SCOPE-001", "False scope violation", r =>
                    (BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.ScopeViolation || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.ScopeViolation) && BlackBoxRuleFacts.FalseScopeViolation(r))
                .In(BlackBoxPhaseEnum.Completion)
                .Because("The scope check flagged {outOfScopeCount} entries ({outOfScopeFiles}) that were blank or in the mission's scoped file list, so the violation is false.", BlackBoxConfidenceEnum.High, "scope.outOfScopeFiles", "scope.scopedFiles", "classification.rawReason")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 4)
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Scope check",
                    Hypothesis = "FindOutOfScopeFiles reports blank entries or files that are in scope; drop blank paths and normalize both lists before comparing.",
                    Evidence = "Out of scope: {outOfScopeFiles} ({blankCount} blank).",
                    SourcePointers = { "src/Armada.Core/Services/MissionService.cs:FindOutOfScopeFiles", "src/Armada.Core/Services/MissionService.cs:TryFailMissionForScopeViolationAsync" },
                    Severity = BlackBoxSeverityEnum.High
                })
                .Recover(new BlackBoxRulePath
                {
                    Key = "review-restart",
                    Title = "Review the diff, then restart the mission",
                    Steps = { BlackBoxRuleStep.Manual("Review the mission's diff (get_mission_diff {\"missionId\": \"{missionId}\"}) and confirm every changed file is in scope."), RestartStep() },
                    Risk = BlackBoxSeverityEnum.Medium,
                    ExpectedOutcome = "The mission completes; if the scope check flags it again, land the reviewed diff by hand."
                })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "normalized both file lists (dropping blank entries and leading ./) before comparing, and completed the mission instead of failing it for a violation that did not exist",
                    BlackBoxDetectionGapEnum.MissingInvariant,
                    "FindOutOfScopeFiles compared un-normalized lists and kept blank entries, so {outOfScopeCount|some} entries that were blank or scoped ({outOfScopeFiles|see the scope section}) were reported as out of scope; no invariant check caught the empty or scoped entries.",
                    "Every out-of-scope entry is a non-blank, normalized repository path that is not in the mission's scoped file list.",
                    "Services.MissionScopeValidation",
                    "Complete a mission whose scoped files are written with ./ prefixes and blank lines and assert the scope check passes.",
                    new[] { "src/Armada.Core/Services/MissionService.cs:FindOutOfScopeFiles", "src/Armada.Core/Services/MissionService.cs:ParseMissionScopedFiles", "src/Armada.Core/Services/MissionService.cs:TryFailMissionForScopeViolationAsync" },
                    BlackBoxRuleChange.Of("Fix the scope parser: drop blank entries and normalize paths", "FindOutOfScopeFiles and ParseMissionScopedFiles should trim, drop blank entries and normalize separators and leading ./ on both the scoped list and the changed files before comparing, and TryFailMissionForScopeViolationAsync should never fail a mission whose out-of-scope list is empty after normalization.", "Services.MissionScopeValidation: scoped files with ./ prefixes, Windows separators and blank lines never produce an out-of-scope finding for those files.", BlackBoxSeverityEnum.High, "src/Armada.Core/Services/MissionService.cs:FindOutOfScopeFiles", "src/Armada.Core/Services/MissionService.cs:ParseMissionScopedFiles")), true));

            rules.Add(new BlackBoxRule("BBX-SCOPE-003", "Scope violation", r => BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.ScopeViolation || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.ScopeViolation)
                .In(BlackBoxPhaseEnum.Completion)
                .YieldsTo("BBX-SCOPE-001")
                .Because("The mission changed files outside its scoped file list: {outOfScopeFiles|see the reason}.", BlackBoxConfidenceEnum.Medium, "scope.outOfScopeFiles", "classification.rawReason")
                .Votes(BlackBoxTriageEnum.MissionBrief, 2)
                .Recover(new BlackBoxRulePath
                {
                    Key = "rescope-restart",
                    Title = "Widen the scoped files or narrow the brief, then restart",
                    Steps = { BlackBoxRuleStep.Manual("Either add {outOfScopeFiles|the extra files} to the mission's scope or tell the captain to leave them alone."), RestartStep() },
                    Risk = BlackBoxSeverityEnum.Low,
                    ExpectedOutcome = "The mission completes inside its scope."
                }));

            rules.Add(new BlackBoxRule("BBX-SCOPE-002", "Scope check skipped", r =>
                    BlackBoxRuleFacts.IsMission(r) && r.Workspace != null && String.IsNullOrEmpty(r.Workspace.StartCommit)
                    && (BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.ScopeViolation || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.ScopeViolation || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.StartCommitRecord
                        || BlackBoxRuleFacts.AllText(r).Contains("start commit", StringComparison.OrdinalIgnoreCase)))
                .In(BlackBoxPhaseEnum.Completion)
                .Because("The start commit was not recorded, so the scope check and the target movement since start could not be computed.", BlackBoxConfidenceEnum.Medium, "workspace.startCommit")
                .Votes(BlackBoxTriageEnum.Unknown, 0)
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Provisioning",
                    Hypothesis = "Record the start commit at every provision (GitAnchorsJson.StartCommit and Mission.StartCommit).",
                    SourcePointers = { "src/Armada.Core/Services/DockService.cs:ProvisionAsync" },
                    Severity = BlackBoxSeverityEnum.Medium
                })
                .Heals(Heal(BlackBoxSelfHealableEnum.No,
                    "recorded the start commit at provisioning so the scope check and the target movement could be computed, and skipped (rather than failed) the scope check when it was missing",
                    BlackBoxDetectionGapEnum.StateNotPersisted,
                    "The start commit was not persisted when the dock was provisioned, so the scope check ran without a base and its out-of-scope list cannot be trusted.",
                    "Every provisioned mission has its start commit persisted (Mission.StartCommit and the dock's start commit file) before the captain starts.",
                    "Services.DockService",
                    "Provision a dock and assert the start commit is persisted before the captain starts; complete a mission without one and assert the scope check is skipped, not failed.",
                    new[] { D.Provision, "src/Armada.Core/Services/DockService.cs:PersistDockStartCommitAsync", "src/Armada.Core/Services/MissionService.cs:TryFailMissionForScopeViolationAsync" },
                    BlackBoxRuleChange.Of("Persist the start commit at every provision and skip the scope check without it", "DockService.ProvisionAsync should persist the start commit on every path (local and Harbor) before the captain starts, and TryFailMissionForScopeViolationAsync should skip, with a note, instead of failing when no start commit is recorded.", "Services.DockService: every provision path records the start commit; a mission without one is not failed by the scope check.", BlackBoxSeverityEnum.Medium, D.Provision, "src/Armada.Core/Services/DockService.cs:PersistDockStartCommitAsync")), true));

            rules.Add(new BlackBoxRule("BBX-NOOP-001", "No-op completion", r => BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.NoOp || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.NoOp)
                .In(BlackBoxPhaseEnum.Completion)
                .Because("The captain finished without producing changes.", BlackBoxConfidenceEnum.High, "classification.kind", BlackBoxObjectNames.AgentOutput)
                .Votes(BlackBoxTriageEnum.MissionBrief, 2)
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Mission briefs",
                    Hypothesis = "Flag briefs that ask only for verification or investigation at dispatch, so they are dispatched as read-only missions instead of failing as no-ops.",
                    SourcePointers = { "src/Armada.Core/Services/MissionService.Completion.cs:TryAssignAsync" },
                    Severity = BlackBoxSeverityEnum.Low
                })
                .Recover(new BlackBoxRulePath
                {
                    Key = "clarify-restart",
                    Title = "Clarify the brief, then restart the mission",
                    Steps = { BlackBoxRuleStep.Manual("Rewrite the brief of {missionId} so it names the change to make (or mark it as a review)."), RestartStep() },
                    Risk = BlackBoxSeverityEnum.Low,
                    ExpectedOutcome = "The captain produces the requested change."
                }));

            rules.Add(new BlackBoxRule("BBX-DEP-001", "Dependent work dispatched in parallel", r =>
                    (BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.Blocked || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.CaptainBlocked || BlackBoxRuleFacts.AllText(r).Contains("BLOCKED", StringComparison.Ordinal))
                    && BlackBoxRuleFacts.ParallelDependency(r))
                .In(BlackBoxPhaseEnum.Completion)
                .Because("Missions that depend on each other were dispatched in parallel: the captain reported BLOCKED because {prerequisite|a prerequisite mission} had not landed.", BlackBoxConfidenceEnum.High, "classification.rawReason", BlackBoxObjectNames.AgentOutput)
                .Votes(BlackBoxTriageEnum.MissionBrief, 4)
                .Recommend(new BlackBoxRuleRecommendation
                {
                    Key = "depends-on",
                    Setting = "DependsOnMissionId",
                    Scope = BlackBoxConfigSourceEnum.Mission,
                    SuggestedValue = "the prerequisite mission's id",
                    Rationale = "Chain dependent missions with DependsOnMissionId so each starts after its prerequisite lands.",
                    HowToApply = "When dispatching the voyage, set dependsOnMissionId on every mission that builds on another (dispatch {\"missions\": [{\"title\": ..., \"dependsOnMissionId\": ...}]})."
                })
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Dispatch",
                    Hypothesis = "Detect \"depends on mission N\" language in mission briefs at dispatch and warn, or chain the missions automatically.",
                    Evidence = "{missionId} reported BLOCKED on {prerequisite|a prerequisite}.",
                    SourcePointers = { "src/Armada.Core/Services/AdmiralService.cs:DispatchVoyageAsync", "src/Armada.Core/Services/MissionService.cs:TryAssignAsync" },
                    Severity = BlackBoxSeverityEnum.Medium
                })
                .Recover(new BlackBoxRulePath
                {
                    Key = "redispatch-after",
                    Title = "Restart the mission after its prerequisites land",
                    Preconditions = { "{prerequisite|The prerequisite missions} has landed on {targetBranch|the target branch}." },
                    Steps = { RestartStep() },
                    Risk = BlackBoxSeverityEnum.Low,
                    ExpectedOutcome = "The mission starts from a target that has its prerequisites and completes."
                }));

            rules.Add(new BlackBoxRule("BBX-BLOCK-001", "Captain reported blocked", r => BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.Blocked || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.CaptainBlocked)
                .In(BlackBoxPhaseEnum.Completion)
                .YieldsTo("BBX-DEP-001")
                .Because("The captain reported BLOCKED and left no work: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "classification.rawReason", BlackBoxObjectNames.AgentOutput)
                .Votes(BlackBoxTriageEnum.MissionBrief, 2)
                .Recover(new BlackBoxRulePath { Key = "unblock-restart", Title = "Remove the blocker named in the captain's report, then restart", Steps = { BlackBoxRuleStep.Manual("Read the captain's BLOCKED report and fix the prerequisite or the brief it names."), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The captain completes the mission." }));

            rules.Add(new BlackBoxRule("BBX-OUT-001", "Invalid output", r => BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.InvalidOutput || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.InvalidOutput)
                .In(BlackBoxPhaseEnum.Completion)
                .Because("The captain's report broke the output contract (markers, sections or plan block) and could not be repaired.", BlackBoxConfidenceEnum.Medium, BlackBoxObjectNames.AgentOutput)
                .Votes(BlackBoxTriageEnum.UserConfig, 1)
                .Recommend(new BlackBoxRuleRecommendation
                {
                    Key = "captain-model",
                    Setting = "CaptainModel",
                    Scope = BlackBoxConfigSourceEnum.Captain,
                    Rationale = "{captainModel|This model} did not follow Armada's output contract; route this persona to a model that does (persona CaptainPriorities).",
                    HowToApply = "update_persona with CaptainPriorities that list a stronger model first."
                })
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "A fresh attempt follows the contract." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "asked the captain to repair its report (re-requesting the missing markers, sections or plan block) and parsed the repaired output, instead of failing the mission on the first contract violation",
                    BlackBoxDetectionGapEnum.MissingRetry,
                    "The output contract was checked only after the captain exited, and the output repair either did not run or gave up ({kind|InvalidOutput}); Armada had the work but discarded it over the report's format.",
                    "A captain report that breaks the output contract is repaired (markers re-requested) before the mission fails, and work on the branch is never discarded because of the report's format.",
                    "Services.ContractRepair",
                    "Finish {missionId|a mission} with a report missing its markers and assert Armada requests an output repair and completes the mission with the repaired report.",
                    new[] { D.OutputRepair, D.Completion, "src/Armada.Core/Services/MissionService.cs:FinishOutputRepairScheduledAsync" },
                    BlackBoxRuleChange.Of("Re-request missing markers and repair the output contract before failing", "When the captain's report breaks the output contract, TryScheduleOutputRepairAsync should always schedule an output repair (re-requesting the exact missing markers or sections) up to the configured attempts, and parse markers tolerantly (case, whitespace, fenced blocks) before deciding the report is invalid.", "Services.ContractRepair: a report with lower-case or fenced markers parses, and a report missing a marker triggers a repair that completes the mission.", BlackBoxSeverityEnum.High, D.OutputRepair, D.Completion)), true));

            rules.Add(new BlackBoxRule("BBX-BOUND-001", "Boundary violation", r => BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.Boundary || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.BoundaryViolation)
                .In(BlackBoxPhaseEnum.Completion)
                .Because("The mission changed a protected area of the repository: {reason|see the boundary findings}.", BlackBoxConfidenceEnum.Medium, "scope.boundaryFindings", "classification.rawReason")
                .Votes(BlackBoxTriageEnum.MissionBrief, 2)
                .Recommend(new BlackBoxRuleRecommendation
                {
                    Key = "protected-paths",
                    Setting = "ProtectedPathPatterns",
                    Scope = BlackBoxConfigSourceEnum.Vessel,
                    Rationale = "If the change was intended, the vessel's protected path patterns ({config:ProtectedPathPatterns|none recorded}) are too broad.",
                    HowToApply = "Edit the vessel's protectedPathPatterns (PUT /api/v1/vessels/{vesselId})."
                })
                .Recover(new BlackBoxRulePath { Key = "rescope-restart", Title = "Narrow the brief to the allowed area, then restart", Steps = { BlackBoxRuleStep.Manual("Edit the brief of {missionId} so it stays out of the protected paths."), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission completes inside its boundary." }));

            rules.Add(new BlackBoxRule("BBX-CHECK-001", "Definition of done or stage check failed", r =>
                    BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.DefinitionOfDone || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.StageCheck
                    || BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.Compile || BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.TestFail)
                .In(BlackBoxPhaseEnum.Completion)
                .YieldsTo("BBX-LAND-005", "BBX-MQ-002", "BBX-CHECK-002", "BBX-CHECK-003")
                .Because("The check `{failingCommand|build or test command}` failed with exit {commandExitCode|code unknown}.", BlackBoxConfidenceEnum.Medium, "evidence.checkResults", "mechanism.commandOutputTail")
                .Votes(BlackBoxTriageEnum.MissionBrief, 1)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission so the captain fixes the failing check", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The check passes." }));

            rules.Add(new BlackBoxRule("BBX-UNFIN-001", "Unfinished work", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.Unfinished)
                .In(BlackBoxPhaseEnum.Completion)
                .Because("The captain stopped before finishing the mission's work.", BlackBoxConfidenceEnum.Medium, BlackBoxObjectNames.AgentOutput)
                .Votes(BlackBoxTriageEnum.MissionBrief, 1)
                .Recover(new BlackBoxRulePath { Key = "split-restart", Title = "Split or narrow the brief, then restart", Steps = { BlackBoxRuleStep.Manual("Split {missionId} into smaller missions or narrow its brief."), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "Smaller missions finish." }));

            rules.Add(new BlackBoxRule("BBX-CHECK-002", "Check harness error", r => BlackBoxResilienceScope.CheckSteps.Contains(BlackBoxRuleFacts.Step(r)) && BlackBoxResilienceScope.HarnessError(r))
                .In(BlackBoxPhaseEnum.Completion)
                .Because("The {step} check could not run (`{failingCommand|the check command}` exit {commandExitCode|unknown}): the harness failed, not the code.", BlackBoxConfidenceEnum.High, "mechanism.commandOutputTail", "evidence.checkResults")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "re-ran the check after repairing its environment (restoring the working directory, resolving the command, extending a timed-out run), reported it as CouldNotRun instead of a failure, and never failed the mission for it",
                    BlackBoxDetectionGapEnum.MissingInvariant,
                    "Armada treated the harness failure of `{failingCommand|the check command}` like a failing test; nothing distinguished a check that could not run from a check that ran and failed.",
                    "A check that could not run (timeout, command not found, missing project or directory, killed) is retried or reported as CouldNotRun and never counts as the mission's failure.",
                    "Services.StageCheckResumeSafety",
                    "Run {step} with a command that is not found and with one that times out and assert both are reported as CouldNotRun and retried, and the mission is not failed.",
                    new[] { D.Completion, "src/Armada.Core/Services/AdmiralService.cs:RecordStageCheckAsync", D.Verification },
                    BlackBoxRuleChange.Of("Separate check harness errors from real failures and retry them", "Classify a check result as CouldNotRun when the command was not found, timed out, was killed or its working directory or project was missing; retry it once after repairing the environment and never fail the mission (or a landing) for a CouldNotRun check.", "Services.StageCheckResumeSafety: exit 127 and a timeout are CouldNotRun and retried; exit 1 with test output is Failed.", BlackBoxSeverityEnum.High, D.Completion, D.Verification)), true));

            rules.Add(new BlackBoxRule("BBX-CHECK-003", "Check flaked", r => BlackBoxResilienceScope.CheckSteps.Contains(BlackBoxRuleFacts.Step(r)) && BlackBoxResilienceScope.CheckFlaked(r) && !BlackBoxResilienceScope.HarnessError(r))
                .In(BlackBoxPhaseEnum.Completion)
                .Because("The {step} check both passed and failed on this mission ({failingCommand|the check command}): the check flaked rather than failed deterministically.", BlackBoxConfidenceEnum.Medium, "evidence.checkResults")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "re-ran the failing check once before failing the mission, and quarantined the flaky check with its history",
                    BlackBoxDetectionGapEnum.MissingRetry,
                    "Armada failed the mission on one failing run of a check that also passed on the same work; it did not re-run a check with a mixed history.",
                    "A check failure is confirmed by a second run before it fails a mission when the same check has passed on the same work.",
                    "Services.StageCheckResumeSafety",
                    "Make a check fail once and pass on the re-run for {missionId|a mission} and assert the mission completes with the flake recorded.",
                    new[] { D.Completion, "src/Armada.Core/Services/AdmiralService.cs:RecordStageCheckAsync", D.Verification },
                    BlackBoxRuleChange.Of("Confirm failing checks with one re-run", "When a stage check, definition of done, landing verification or merge-queue test fails and the same check passed earlier on the work, re-run it once; fail only when it fails again, and record the flake so the check can be quarantined.", "Services.StageCheckResumeSafety: a check that fails then passes on the re-run does not fail the mission and is recorded as flaky.", BlackBoxSeverityEnum.Medium, D.Completion, D.Verification)), true));
        }

        private static void AddExecution(List<BlackBoxRule> rules)
        {
            rules.Add(new BlackBoxRule("BBX-EXEC-001", "Usage limit", r => BlackBoxRuleFacts.RuntimeKind(r) == RuntimeFailureKindEnum.UsageLimit || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.UsageLimit)
                .In(BlackBoxPhaseEnum.Execution)
                .Because("The {captainRuntime|captain's} account hit its usage limit.", BlackBoxConfidenceEnum.High, "mechanism.runtimeFailureKind", BlackBoxObjectNames.AgentOutput)
                .Votes(BlackBoxTriageEnum.Transient, 3)
                .Recommend(new BlackBoxRuleRecommendation
                {
                    Key = "fallback-captain",
                    Setting = "CaptainPriorities",
                    Scope = BlackBoxConfigSourceEnum.Persona,
                    Rationale = "Add a fallback captain on another account or runtime through the persona's CaptainPriorities so work continues when one account is exhausted.",
                    HowToApply = "update_persona {\"name\": \"{persona|Worker}\", \"captainPriorities\": [...]} with a second runtime or model."
                })
                .Recover(new BlackBoxRulePath { Key = "wait-restart", Title = "Wait for the quarantine to expire, then restart", Preconditions = { "The account's usage window has reset." }, Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again once the limit resets." })
                .Recover(new BlackBoxRulePath { Key = "other-captain", Title = "Restart on another captain", Steps = { BlackBoxRuleStep.Manual("Make another captain eligible for {persona|this persona} (or release {captainName|this captain})."), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "Another captain takes the mission." }));

            rules.Add(new BlackBoxRule("BBX-EXEC-004", "Authentication failure", r => BlackBoxRuleFacts.RuntimeKind(r) == RuntimeFailureKindEnum.AuthFailure || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.AuthFailure)
                .In(BlackBoxPhaseEnum.Execution)
                .Because("The {captainRuntime|captain's} runtime could not authenticate with its provider.", BlackBoxConfidenceEnum.High, "mechanism.runtimeFailureKind", BlackBoxObjectNames.AgentOutput)
                .Votes(BlackBoxTriageEnum.UserConfig, 3)
                .Recommend(new BlackBoxRuleRecommendation
                {
                    Key = "credentials",
                    Setting = "CaptainRuntime",
                    Scope = BlackBoxConfigSourceEnum.Captain,
                    Rationale = "The captain's credentials (CLI login, API key or model endpoint key) are missing or expired.",
                    HowToApply = "Log the runtime in again on the host, or update the model endpoint's key."
                })
                .Recover(new BlackBoxRulePath { Key = "fix-restart", Title = "Fix the credentials, then restart", Steps = { BlackBoxRuleStep.Manual("Sign the {captainRuntime|runtime} in again on {harborName|the Admiral host} or update its API key."), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The captain authenticates and runs the mission." }));

            rules.Add(new BlackBoxRule("BBX-EXEC-005", "Model unavailable", r => BlackBoxRuleFacts.RuntimeKind(r) == RuntimeFailureKindEnum.ModelUnavailable || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.ModelUnavailable)
                .In(BlackBoxPhaseEnum.Execution)
                .Because("The model {captainModel|configured for the captain} was unavailable.", BlackBoxConfidenceEnum.Medium, "mechanism.runtimeFailureKind")
                .Votes(BlackBoxTriageEnum.Transient, 2)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart once the model is available", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs on the model." }));

            rules.Add(new BlackBoxRule("BBX-EXEC-002", "Stall", r => BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.StallRecoveryExhausted || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.Stall)
                .In(BlackBoxPhaseEnum.Execution)
                .Because("No activity for {stallThresholdMinutes|the stall threshold of} minutes; recovery was exhausted.", BlackBoxConfidenceEnum.High, "classification.kind", BlackBoxObjectNames.AgentOutput)
                .Votes(BlackBoxTriageEnum.Transient, 2)
                .Recommend(new BlackBoxRuleRecommendation
                {
                    Key = "stall-threshold",
                    Setting = "StallThresholdMinutes",
                    Scope = BlackBoxConfigSourceEnum.Global,
                    Rationale = "If the captain was running a long build or test, raise StallThresholdMinutes (now {stallThresholdMinutes|the default}) and check ToolWaitHardLimitMinutes.",
                    HowToApply = "stallThresholdMinutes in PUT /api/v1/settings."
                })
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Health checks",
                    Hypothesis = "Treat a long-running tool call as activity so a captain waiting on a build is not reported as stalled.",
                    SourcePointers = { "src/Armada.Core/Services/AdmiralService.cs:HealthCheckCaptainAsync" },
                    Severity = BlackBoxSeverityEnum.Medium
                })
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Recover(new BlackBoxRulePath
                {
                    Key = "unstick-restart",
                    Title = "Unstick the wedged dock, then restart the mission",
                    Optional = true,
                    Preconditions = { "Nothing uncommitted in {worktreePath|the dock's worktree} needs to be kept (committed branch history is preserved)." },
                    Steps = { BlackBoxRuleStep.Run("Release the captain holding dock {dockId} and reclaim its worktree.", "unstick_dock", "dockId", "{dockId}"), RestartStep() },
                    Risk = BlackBoxSeverityEnum.High,
                    ExpectedOutcome = "The dock is freed and the mission runs again on a fresh worktree."
                }));

            rules.Add(new BlackBoxRule("BBX-EXEC-003", "Max runtime", r => BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.MaxRuntimeExceeded || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.MaxRuntime)
                .In(BlackBoxPhaseEnum.Execution)
                .Because("The mission ran longer than {maxMissionRuntimeMinutes|its maximum runtime in} minutes.", BlackBoxConfidenceEnum.High, "classification.kind")
                .Votes(BlackBoxTriageEnum.MissionBrief, 2)
                .Recommend(new BlackBoxRuleRecommendation
                {
                    Key = "max-runtime",
                    Setting = "MaxMissionRuntimeMinutes",
                    Scope = BlackBoxConfigSourceEnum.Global,
                    Rationale = "Raise MaxMissionRuntimeMinutes (now {maxMissionRuntimeMinutes|the default}) if missions of this size are expected, or split the mission.",
                    HowToApply = "maxMissionRuntimeMinutes in PUT /api/v1/settings."
                })
                .Recover(new BlackBoxRulePath { Key = "split", Title = "Split into smaller missions", Steps = { BlackBoxRuleStep.Manual("Split {missionId} into smaller missions and dispatch them as a voyage with dependsOnMissionId.") }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "Each smaller mission finishes inside the limit." })
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission as it is", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Medium, ExpectedOutcome = "The mission may finish if the slowness was transient." }));

            rules.Add(new BlackBoxRule("BBX-EXEC-006", "Process crash", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.ProcessCrash || BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.Crash || BlackBoxRuleFacts.RuntimeKind(r) == RuntimeFailureKindEnum.Crash)
                .In(BlackBoxPhaseEnum.Execution)
                .YieldsTo("BBX-EXEC-001", "BBX-EXEC-004", "BBX-EXEC-005")
                .Because("The captain's process exited with code {exitCode|unknown} before finishing.", BlackBoxConfidenceEnum.Medium, "mechanism.exitCode", BlackBoxObjectNames.AgentOutput)
                .Votes(BlackBoxTriageEnum.Transient, 1)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "A fresh process runs the mission." }));

            rules.Add(new BlackBoxRule("BBX-EXEC-007", "Stopped or interrupted", r =>
                    BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.OperatorStop || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.Interrupted || BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.OperatorAction)
                .In(BlackBoxPhaseEnum.Execution)
                .Because("The captain was stopped from outside ({step}).", BlackBoxConfidenceEnum.High, "site.step")
                .Votes(BlackBoxTriageEnum.External, 3)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission if the stop was not intended", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." }));

            rules.Add(new BlackBoxRule("BBX-EXEC-008", "Recovery exhausted", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.RecoveryExhausted)
                .In(BlackBoxPhaseEnum.Execution)
                .YieldsTo("BBX-EXEC-002")
                .Because("Armada's automatic recovery of the captain gave up after its attempts (MaxRecoveryAttempts {config:MaxRecoveryAttempts|default}).", BlackBoxConfidenceEnum.Medium, "attempts")
                .Votes(BlackBoxTriageEnum.Transient, 1)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." }));
        }

        private static void AddDispatchAndProvision(List<BlackBoxRule> rules)
        {
            rules.Add(new BlackBoxRule("BBX-INFRA-001", "Harbor disconnected", r => BlackBoxRuleFacts.HarborDisconnected(r) || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.HarborUnavailable)
                .In(BlackBoxPhaseEnum.Dispatch)
                .Because("Harbor {harborName|of this dock} was disconnected.", BlackBoxConfidenceEnum.High, "workspace.harborLinkState", "subject.harborId")
                .Votes(BlackBoxTriageEnum.External, 3)
                .Recover(new BlackBoxRulePath
                {
                    Key = "reconnect-retry",
                    Title = "Reconnect the Harbor, then retry the landing",
                    When = r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Landing || BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.MergeQueue,
                    Steps = { BlackBoxRuleStep.Manual("Start Harbor {harborName|on the developer machine} and wait until it shows Connected."), RetryLandingStep() },
                    Risk = BlackBoxSeverityEnum.Low,
                    ExpectedOutcome = "The landing runs on the reconnected Harbor."
                })
                .Recover(new BlackBoxRulePath
                {
                    Key = "reconnect-restart",
                    Title = "Reconnect the Harbor, then restart the mission",
                    When = r => BlackBoxRuleFacts.Phase(r) != BlackBoxPhaseEnum.Landing && BlackBoxRuleFacts.Phase(r) != BlackBoxPhaseEnum.MergeQueue,
                    Steps = { BlackBoxRuleStep.Manual("Start Harbor {harborName|on the developer machine} and wait until it shows Connected."), RestartStep() },
                    Risk = BlackBoxSeverityEnum.Low,
                    ExpectedOutcome = "The mission runs on the reconnected Harbor."
                })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "paused the mission until Harbor {harborName|of this dock} reconnected and then resumed the landing (or the run) on it, instead of failing while the link was down",
                    BlackBoxDetectionGapEnum.MissingRetry,
                    "Armada acted while the Harbor link was {harborLinkState|down} and failed the step instead of waiting for the link to come back.",
                    "Work on a Harbor-hosted dock waits (bounded) for the Harbor to reconnect and resumes; a lost link never fails a mission by itself.",
                    "Services.HarborLinkResilience",
                    "Disconnect the Harbor of {missionId|a mission} during landing, reconnect it, and assert the landing resumes and succeeds.",
                    new[] { "src/Armada.Core/Services/MergeQueueService.cs:WaitForHarborAsync", D.LandingHandler, "src/Armada.Core/Services/AdmiralService.cs:WithinTransientOutageGrace" },
                    BlackBoxRuleChange.Of("Wait for the Harbor to reconnect and resume instead of failing", "When the Harbor that owns the dock is disconnected, park the landing (or the run) as waiting on the Harbor, resume it when the link reconnects within a grace window, and only then fail with the Harbor named.", "Services.HarborLinkResilience: a landing that starts while the Harbor is offline waits and lands after it reconnects.", BlackBoxSeverityEnum.Medium, D.LandingHandler))));

            rules.Add(new BlackBoxRule("BBX-DISP-001", "No eligible captain", r =>
                    BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.NoEligibleCaptain || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.AssignmentFailed)
                .In(BlackBoxPhaseEnum.Dispatch)
                .Because("No captain could take the mission ({step}): {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "classification.rawReason")
                .Votes(BlackBoxTriageEnum.UserConfig, 2)
                .Recommend(new BlackBoxRuleRecommendation
                {
                    Key = "captain-priorities",
                    Setting = "CaptainPriorities",
                    Scope = BlackBoxConfigSourceEnum.Persona,
                    Rationale = "Persona {persona|of the mission} has no captain that is idle, allowed and not quarantined; add one or widen its CaptainPriorities.",
                    HowToApply = "create_captain, or update_persona with broader CaptainPriorities."
                })
                .Recover(new BlackBoxRulePath { Key = "add-captain-restart", Title = "Make a captain available, then restart", Steps = { BlackBoxRuleStep.Manual("Add or release a captain that can run {persona|this persona}."), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "A captain takes the mission." }));

            rules.Add(new BlackBoxRule("BBX-DISP-002", "Dispatch deferred or refused", r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Dispatch)
                .In(BlackBoxPhaseEnum.Dispatch)
                .YieldsTo("BBX-DISP-001", "BBX-INFRA-001")
                .Because("Dispatch failed at step {step}: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Low, "classification.rawReason")
                .Votes(BlackBoxTriageEnum.Transient, 1)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission is dispatched again." }));

            rules.Add(new BlackBoxRule("BBX-PROV-001", "Provisioning failed", r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Provision)
                .In(BlackBoxPhaseEnum.Provision)
                .YieldsTo("BBX-INFRA-001", "BBX-PROV-002", "BBX-PROV-003", "BBX-PROV-004")
                .Because("Armada could not provision the mission's dock at step {step}: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "classification.rawReason", "mechanism.gitStderrTail")
                .Votes(BlackBoxTriageEnum.Transient, 1)
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Provisioning",
                    Hypothesis = "Report the git command and stderr that failed during provisioning so the cause is visible without the logs.",
                    SourcePointers = { "src/Armada.Core/Services/DockService.cs:ProvisionAsync" },
                    Severity = BlackBoxSeverityEnum.Low
                })
                .Recover(new BlackBoxRulePath { Key = "repair-restart", Title = "Repair the dock, then restart", Optional = true, Steps = { BlackBoxRuleStep.Run("Repair the dock's worktree registration.", "repair_dock", "dockId", "{dockId}"), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The dock provisions and the mission runs." })
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "A new dock is provisioned." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "retried the provisioning with backoff, pruning stale worktree registrations and recreating the dock from the vessel's repository, and kept the mission Pending instead of failing it",
                    BlackBoxDetectionGapEnum.MissingRetry,
                    "Provisioning failed at step {step} ({reason|no reason recorded}) and Armada did not repair the repository state (stale worktrees, locks, a missing bare clone) and retry before giving up.",
                    "Provisioning is idempotent: a failed attempt leaves no half-created dock or worktree registration, and a retry after a repair succeeds.",
                    "Services.DockService",
                    "Make the first worktree creation fail on {vesselName|a vessel} and assert Armada prunes, retries and provisions the dock.",
                    new[] { D.Provision, D.RepairDock, "src/Armada.Core/Services/GitService.cs:CreateWorktreeAsync" },
                    BlackBoxRuleChange.Of("Make dock provisioning self-repairing and idempotent", "DockService.ProvisionAsync should, on a git failure at {step}, prune stale worktree registrations, remove a half-created directory, recreate the bare clone when it is missing, and retry with backoff before failing; report the failing git command and stderr in the failure reason.", "Services.DockService: a provision whose first worktree add fails because of a stale registration is retried after a prune and succeeds.", BlackBoxSeverityEnum.Medium, D.Provision, D.RepairDock))));

            rules.Add(new BlackBoxRule("BBX-PROV-002", "Stale or unregistered worktree", r => (BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Provision || BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Landing) && BlackBoxRuleFacts.WorktreeRegistrationError(r))
                .In(BlackBoxPhaseEnum.Provision)
                .Because("Git refused the dock's worktree because a stale or unregistered worktree was in the way: {gitStderr|reason}.", BlackBoxConfidenceEnum.High, "mechanism.gitStderrTail", "workspace.worktreePath")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 3)
                .Recover(new BlackBoxRulePath { Key = "repair-restart", Title = "Repair the dock, then restart", Optional = true, Steps = { BlackBoxRuleStep.Run("Repair the dock's worktree registration.", "repair_dock", "dockId", "{dockId}"), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The dock is repaired and the mission runs." })
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "pruned the stale worktree registration (git worktree prune, unlocking a dead lock) and recreated the dock's worktree, instead of failing",
                    BlackBoxDetectionGapEnum.MissingInvariant,
                    "Armada's worktree registrations drifted from the directories on disk (a removed directory still registered, or a lock left by a dead process), and provisioning did not reconcile them first.",
                    "Before a worktree is added or reused, git's worktree registrations match the dock directories on disk (pruned, unlocked).",
                    "Services.DockService",
                    "Leave a stale worktree registration for {worktreePath|a dock path} and assert provisioning prunes it and creates the dock.",
                    new[] { D.Provision, D.RepairDock, "src/Armada.Core/Services/GitService.cs:PruneWorktreesAsync", "src/Armada.Core/Services/GitService.cs:IsWorktreeRegisteredAsync" },
                    BlackBoxRuleChange.Of("Reconcile worktree registrations before provisioning or landing", "Before git worktree add (and before landing from a dock), run a prune, unlock locks whose process is gone and remove registrations without a directory, then retry the operation once.", "Services.DockService: a stale registration at the dock path is pruned and the dock provisions on the first try.", BlackBoxSeverityEnum.High, D.Provision, "src/Armada.Core/Services/GitService.cs:PruneWorktreesAsync"))));

            rules.Add(new BlackBoxRule("BBX-PROV-003", "Branch creation failed", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.BranchCreate)
                .In(BlackBoxPhaseEnum.Provision)
                .YieldsTo("BBX-PROV-002")
                .Because("Armada could not create the mission branch {branchName|for this mission}: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "classification.rawReason", "mechanism.gitStderrTail")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "repair-restart", Title = "Repair the dock, then restart", Optional = true, Steps = { BlackBoxRuleStep.Run("Repair the dock's worktree registration.", "repair_dock", "dockId", "{dockId}"), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The dock is repaired and the mission runs." })
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "reused or reset the leftover branch when it held no unlanded work (or picked a fresh branch name) and continued provisioning",
                    BlackBoxDetectionGapEnum.MissingInvariant,
                    "A branch with the mission's name was left behind by an earlier attempt (or the base ref did not resolve), and provisioning did not check for it before creating the branch.",
                    "Provisioning a mission branch is idempotent: a leftover branch without unlanded work is reused or reset, never a reason to fail.",
                    "Services.DockService",
                    "Leave a branch named {branchName|like the mission branch} from an earlier attempt and assert provisioning reuses it.",
                    new[] { D.Provision, "src/Armada.Core/Services/MissionService.cs:BuildMissionBranchName" },
                    BlackBoxRuleChange.Of("Make mission branch creation idempotent", "When the mission branch already exists, DockService.ProvisionAsync should reuse it when it has no commits beyond the target, reset it when it only has an earlier attempt's work that is preserved elsewhere, or choose a suffixed name, instead of failing provisioning.", "Services.DockService: provisioning over a leftover branch from an earlier attempt succeeds.", BlackBoxSeverityEnum.Medium, D.Provision))));

            rules.Add(new BlackBoxRule("BBX-PROV-004", "Start commit not recorded", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.StartCommitRecord)
                .In(BlackBoxPhaseEnum.Provision)
                .Because("Armada could not record the start commit of the mission's dock: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "workspace.startCommit", "classification.rawReason")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "read the start commit again from the dock's HEAD (or the target tip it was created from) and persisted it before starting the captain",
                    BlackBoxDetectionGapEnum.StateNotPersisted,
                    "The start commit was read but not persisted (or not read at all) before the captain started, so later checks had no base.",
                    "A captain starts only after its dock's start commit is persisted.",
                    "Services.DockService",
                    "Make the first start commit write fail and assert Armada retries it before starting the captain.",
                    new[] { "src/Armada.Core/Services/DockService.cs:PersistDockStartCommitAsync", D.Provision },
                    BlackBoxRuleChange.Of("Persist the start commit before the captain starts", "DockService should retry PersistDockStartCommitAsync and fall back to the dock's HEAD or the target tip it was provisioned from, and block the captain's start until a start commit is recorded.", "Services.DockService: a transient start commit write failure is retried and the captain starts with a recorded start commit.", BlackBoxSeverityEnum.Medium, "src/Armada.Core/Services/DockService.cs:PersistDockStartCommitAsync"))));

            rules.Add(new BlackBoxRule("BBX-DOCK-001", "Dock worktree missing", r => BlackBoxRuleFacts.IsMission(r) && (r.Workspace?.WorktreeExists == false || BlackBoxRuleFacts.MissingWorkingDirectory(r)) && BlackBoxRuleFacts.Phase(r) != BlackBoxPhaseEnum.Landing && BlackBoxRuleFacts.Phase(r) != BlackBoxPhaseEnum.Review && BlackBoxRuleFacts.Phase(r) != BlackBoxPhaseEnum.Provision)
                .In(BlackBoxPhaseEnum.Provision)
                .Because("The dock worktree {worktreePath|of this mission} was missing during {phase} ({step}), although the mission still needed it.", BlackBoxConfidenceEnum.High, "workspace.worktreeExists", "mechanism.exceptionMessage")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 3)
                .Recover(new BlackBoxRulePath { Key = "repair-restart", Title = "Repair the dock, then restart", Optional = true, Steps = { BlackBoxRuleStep.Run("Repair the dock's worktree registration.", "repair_dock", "dockId", "{dockId}"), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The dock is repaired and the mission runs." })
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "recreated the worktree from {branchName|the mission branch} and resumed the mission instead of failing it",
                    BlackBoxDetectionGapEnum.Race,
                    "The dock's directory disappeared while its mission was active: a reclaim, cleanup or Harbor restart removed it without checking the lease.",
                    "An active mission's dock directory exists for as long as the mission holds the lease.",
                    "Services.DockLease",
                    "Remove the dock directory of an active mission and assert Armada recreates it from the branch and the mission continues.",
                    new[] { D.Reclaim, D.RestoreWorktree, "src/Armada.Core/Services/AdmiralService.cs:ReclaimOrphanedDocksAsync" },
                    BlackBoxRuleChange.Of("Recreate a vanished dock of an active mission from its branch", "When an active mission's dock directory is missing, restore it from {branchName|the branch} (DockService.RestoreWorktreeAsync) and resume, and make every reclaim and cleanup path (including ReclaimOrphanedDocksAsync) refuse to remove a leased dock.", "Services.DockLease: deleting an active mission's dock directory is repaired and the mission completes.", BlackBoxSeverityEnum.High, D.RestoreWorktree, D.Reclaim)), true));
        }

        private static void AddReviewHandoffRecovery(List<BlackBoxRule> rules)
        {
            rules.Add(new BlackBoxRule("BBX-REV-001", "Review denied", r => BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.ReviewDenied || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.ReviewDenied)
                .In(BlackBoxPhaseEnum.Review)
                .Because("A reviewer denied the mission's work: {reason|no comment recorded}.", BlackBoxConfidenceEnum.High, "classification.rawReason")
                .Votes(BlackBoxTriageEnum.External, 2)
                .Recover(new BlackBoxRulePath { Key = "address-restart", Title = "Address the review comment, then restart", Steps = { BlackBoxRuleStep.Manual("Fold the reviewer's comment into the brief of {missionId}."), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The reworked mission passes review." }));

            rules.Add(new BlackBoxRule("BBX-REV-002", "Judge rejected", r => BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.JudgeRejected || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.JudgeRejected)
                .In(BlackBoxPhaseEnum.Review)
                .Because("The judge rejected the work: {reason|no verdict text recorded}.", BlackBoxConfidenceEnum.High, "classification.rawReason")
                .Votes(BlackBoxTriageEnum.MissionBrief, 2)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission with the judge's findings", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The captain addresses the findings." }));

            rules.Add(new BlackBoxRule("BBX-REV-003", "Review approved but the dock is gone", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.ReviewApprovedDockMissing)
                .In(BlackBoxPhaseEnum.Review)
                .Because("The review was approved, but the mission's dock had been removed, so the work could not land.", BlackBoxConfidenceEnum.High, "workspace.worktreeExists")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 3)
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Docks",
                    Hypothesis = "Keep the dock of a mission awaiting review leased until the review is decided.",
                    SourcePointers = { "src/Armada.Core/Services/DockService.cs:ReclaimAsync" },
                    Severity = BlackBoxSeverityEnum.High
                })
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing from the branch", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The approved branch lands." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "kept the reviewed mission's dock leased until the review was decided, or recreated the worktree from {branchName|the branch} and landed the approved work from it",
                    BlackBoxDetectionGapEnum.Race,
                    "The dock was reclaimed while the mission waited for review: the reclaim path did not treat AwaitingReview as a lease, and the landing after approval did not recreate the worktree.",
                    "A mission awaiting review keeps its dock (or a recreatable branch) until the review is decided, and an approved mission always lands from its branch.",
                    "Services.DockLease",
                    "Approve {missionId|a mission} whose dock was reclaimed during review and assert it lands from its branch.",
                    new[] { D.Reclaim, D.LandingDock, D.RestoreWorktree },
                    BlackBoxRuleChange.Of("Keep docks leased through review and land approved work from the branch", "Treat AwaitingReview as a dock lease in every reclaim path, and when an approved mission's dock is gone, recreate it from {branchName|the branch} before landing instead of failing with ReviewApprovedDockMissing.", "Services.DockLease: a dock is not reclaimed while its mission awaits review; an approved mission with a missing dock lands from its branch.", BlackBoxSeverityEnum.High, D.Reclaim, D.LandingDock)), true));

            rules.Add(new BlackBoxRule("BBX-REV-004", "Review timed out", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.ReviewTimeout)
                .In(BlackBoxPhaseEnum.Review)
                .Because("Nobody decided the review within ReviewTimeoutMinutes.", BlackBoxConfidenceEnum.High, "site.step")
                .Votes(BlackBoxTriageEnum.External, 2)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission and review it in time", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission returns to review." }));

            rules.Add(new BlackBoxRule("BBX-DEP-002", "Dependency failed", r => BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.DependencyFailed || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.DependencyCancelled)
                .In(BlackBoxPhaseEnum.Handoff)
                .Because("The mission did not run because a mission it depends on failed or was cancelled.", BlackBoxConfidenceEnum.High, "classification.kind", "related.cascadeRootEntryId")
                .Votes(BlackBoxTriageEnum.External, 3)
                .Recover(new BlackBoxRulePath { Key = "fix-root-restart", Title = "Fix the root mission, then restart this one", Steps = { BlackBoxRuleStep.Manual("Open the black box of the failed prerequisite and recover it first."), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs once its prerequisite lands." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "held this mission as Blocked behind its failed dependency and resumed it automatically once the dependency recovered, instead of cancelling it",
                    BlackBoxDetectionGapEnum.MissingInvariant,
                    "A dependency failure cancelled the dependent outright; Armada did not keep it recoverable for when the root mission is fixed.",
                    "A dependent of a failed mission stays Blocked (not Cancelled) and resumes when its dependency lands.",
                    "Services.PipelineDispatch",
                    "Fail the dependency of {missionId|a mission}, then recover it, and assert the dependent resumes on its own.",
                    new[] { "src/Armada.Core/Services/MissionService.Handoff.cs:CancelDependentPipelineStagesAsync", "src/Armada.Core/Services/MissionService.Handoff.cs:SettleStagesBehindFailedDependenciesAsync" },
                    BlackBoxRuleChange.Of("Hold dependents as Blocked and resume them when the dependency recovers", "Instead of cancelling the dependents of a failed mission, mark them Blocked with the root's black box entry, and resume them automatically when the root lands after a recovery.", "Services.PipelineDispatch: a dependent of a failed mission is Blocked, and runs after the dependency is restarted and lands.", BlackBoxSeverityEnum.Medium, "src/Armada.Core/Services/MissionService.Handoff.cs:SettleStagesBehindFailedDependenciesAsync"))));

            rules.Add(new BlackBoxRule("BBX-HAND-001", "Handoff failed", r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Handoff)
                .In(BlackBoxPhaseEnum.Handoff)
                .YieldsTo("BBX-DEP-002", "BBX-HAND-002", "BBX-HAND-003", "BBX-HAND-004")
                .Because("The handoff between pipeline stages failed at step {step}: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "classification.rawReason")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 1)
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Pipelines",
                    Hypothesis = "Make the stage handoff tolerate this condition ({step}) or report it before the next stage starts.",
                    SourcePointers = { "src/Armada.Core/Services/MissionService.Handoff.cs" },
                    Severity = BlackBoxSeverityEnum.Low
                })
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The stage runs again with a fresh handoff." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Unknown,
                    "retried the stage handoff after re-checking the stage branch and the next stage's inputs, and kept the stage's output for recovery",
                    BlackBoxDetectionGapEnum.NoPreflightCheck,
                    "No specific rule or preflight check covers handoff step {step} ({reason|no reason recorded}); the handoff ended the pipeline on its first failure.",
                    "A stage handoff is retryable: the previous stage's output and branch are persisted before the next stage starts.",
                    "Services.HandoffResilience",
                    "Reproduce the {step} handoff failure and assert the handoff is retried and the stage output kept.",
                    new[] { D.StageHandoff, D.DanglingHandoffs },
                    BlackBoxRuleChange.Of("Make handoff step {step} retryable", "The stage handoff failed at {step} with no specific handling ({reason|no reason recorded}). Persist the stage output and branch before handing off, retry the handoff once (RecoverDanglingHandoffsAsync), and add a black box rule with resilience content for it.", "Services.HandoffResilience: a handoff that fails once at {step} is retried and the next stage starts.", BlackBoxSeverityEnum.Medium, D.StageHandoff, D.DanglingHandoffs))));

            rules.Add(new BlackBoxRule("BBX-RECOV-001", "Armada recovery failed", r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Recovery)
                .In(BlackBoxPhaseEnum.Recovery)
                .YieldsTo("BBX-RECOV-002", "BBX-RECOV-003", "BBX-RECOV-004", "BBX-RECOV-005")
                .Because("Armada's own recovery could not finish at step {step}: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "classification.rawReason", "attempts")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Recovery",
                    Hypothesis = "Recovery step {step} ended the mission instead of retrying or handing it to the operator with its work intact.",
                    SourcePointers = { "src/Armada.Core/Services/AdmiralService.cs:HandleTerminalProcessExitFailureAsync" },
                    Severity = BlackBoxSeverityEnum.Medium
                })
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Unknown,
                    "kept the mission's work intact (WorkProduced with its branch) and handed it to the operator, or retried the recovery step, instead of ending the mission",
                    BlackBoxDetectionGapEnum.MissingInvariant,
                    "Armada's own recovery step {step} ended the mission ({reason|no reason recorded}) instead of retrying or preserving the work for a person.",
                    "A failure in Armada's recovery never loses the mission's work: the branch and dock are preserved and the mission is left recoverable.",
                    "Services.AdmiralService",
                    "Make recovery step {step} fail for {missionId|a mission} and assert the mission keeps its branch and is recoverable.",
                    new[] { D.TerminalExit, D.Orphans },
                    BlackBoxRuleChange.Of("Make recovery step {step} preserve the work and retry", "When Armada's recovery fails at {step}, keep the mission recoverable (WorkProduced with its branch and dock) and retry the recovery once with backoff instead of failing the mission.", "Services.AdmiralService: a recovery that fails once at {step} is retried, and on a second failure the mission keeps its branch.", BlackBoxSeverityEnum.Medium, D.TerminalExit))));

            rules.Add(new BlackBoxRule("BBX-HAND-002", "Plan or marker parse failure", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.PlanParse)
                .In(BlackBoxPhaseEnum.Handoff)
                .Because("Armada could not parse the plan or handoff markers in the captain's report: {reason|no reason recorded}.", BlackBoxConfidenceEnum.High, BlackBoxObjectNames.AgentOutput, "classification.rawReason")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 3)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "asked the captain to restate its plan in the expected block (an output repair) and parsed it tolerantly (case, whitespace, fenced or numbered lists), instead of failing the handoff",
                    BlackBoxDetectionGapEnum.MissingRetry,
                    "The plan parser accepted only one exact layout and the handoff failed on the first parse error, with no output repair.",
                    "A plan that a person can read is parsed or repaired before the handoff fails.",
                    "Services.HandoffResilience",
                    "Hand off a plan written as a fenced, numbered list and assert it parses; hand off an unreadable one and assert an output repair is requested.",
                    new[] { D.StageHandoff, D.OutputRepair, "src/Armada.Core/Services/MissionService.cs:StripPipelineHandoff" },
                    BlackBoxRuleChange.Of("Parse plans tolerantly and repair them instead of failing the handoff", "Make the plan and marker parser accept the common variants (case, surrounding whitespace, fenced blocks, numbered or bulleted lists) and, when it still cannot parse the plan, request an output repair that restates it in the expected block before failing the handoff.", "Services.HandoffResilience: a fenced numbered plan parses; an unreadable plan triggers an output repair.", BlackBoxSeverityEnum.High, D.StageHandoff, D.OutputRepair))));

            rules.Add(new BlackBoxRule("BBX-HAND-003", "Stage branch could not be hardened", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.StageBranchHarden)
                .In(BlackBoxPhaseEnum.Handoff)
                .Because("Armada could not harden the stage branch {branchName|of this stage} for the next stage: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "classification.rawReason", "mechanism.gitStderrTail")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "committed or recovered the stage's work from the dock and retried hardening the branch before handing off",
                    BlackBoxDetectionGapEnum.MissingRetry,
                    "Hardening the stage branch failed once and the handoff ended; Armada did not recover the stage's work from the dock and retry.",
                    "A stage's work is committed to its branch before the next stage starts, and a hardening failure is retried with the work intact.",
                    "Services.HandoffResilience",
                    "Make the first branch hardening of a stage fail and assert it is retried and the next stage starts with the work.",
                    new[] { D.HardenStage, "src/Armada.Core/Services/MissionService.cs:CheckStageCommitContinuityAsync" },
                    BlackBoxRuleChange.Of("Retry stage branch hardening with the work preserved", "HardenStageBranchAsync should commit any uncommitted stage work, retry the hardening once with backoff, and keep the stage WorkProduced with its dock when it still fails, instead of failing the pipeline.", "Services.HandoffResilience: a hardening failure is retried and the pipeline continues.", BlackBoxSeverityEnum.Medium, D.HardenStage))));

            rules.Add(new BlackBoxRule("BBX-HAND-004", "Integration barrier conflict", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.IntegrationBarrier)
                .In(BlackBoxPhaseEnum.Handoff)
                .Because("The voyage's integration barrier could not merge the parallel stage branches: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "classification.rawReason", "conflict.conflictedFiles")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "serialized the overlapping parallel stages (or rebased the later branch onto the earlier one) and retried the barrier merge",
                    BlackBoxDetectionGapEnum.Race,
                    "Parallel stages that touched the same files were merged only at the barrier; Armada did not detect the overlap when it fanned them out.",
                    "Parallel stages that reach an integration barrier do not overlap, or are rebased onto each other before the barrier merges them.",
                    "Services.HandoffResilience",
                    "Fan out two stages that edit the same file and assert Armada serializes them or rebases before the barrier.",
                    new[] { D.StageHandoff, "src/Armada.Core/Services/MissionService.Handoff.cs:TryHandoffToNextStageAsync" },
                    BlackBoxRuleChange.Of("Detect overlap when fanning out parallel stages", "Before starting parallel stages, estimate their file overlap; serialize overlapping stages, and at the barrier rebase a conflicting branch onto the merged result and retry before failing the voyage.", "Services.HandoffResilience: two overlapping parallel stages are serialized and the barrier merges cleanly.", BlackBoxSeverityEnum.Medium, D.StageHandoff))));

            rules.Add(new BlackBoxRule("BBX-RECOV-002", "Stop not confirmed by the owning host", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.UnconfirmedStop)
                .In(BlackBoxPhaseEnum.Recovery)
                .Because("Armada asked the host to stop the captain of {missionId|this mission}, but the host never confirmed the stop.", BlackBoxConfidenceEnum.Medium, "workspace.harborLinkState", "handoff.steps")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "re-sent the stop after the Harbor reconnected and reconciled the captain's real state before deciding the mission's outcome",
                    BlackBoxDetectionGapEnum.StateNotPersisted,
                    "The stop request was not persisted for re-delivery, so when the host missed it Armada could only record the stop as unconfirmed.",
                    "A stop request is delivered at least once: it is persisted and re-sent until the owning host confirms it.",
                    "Services.HarborLinkResilience",
                    "Stop a captain while its Harbor is disconnected, reconnect it, and assert the stop is re-sent and confirmed.",
                    new[] { D.UnconfirmedStop, "src/Armada.Core/Services/AdmiralService.cs:RecallCaptainAsync" },
                    BlackBoxRuleChange.Of("Persist and re-send stop requests until the host confirms them", "Persist stop requests in the Harbor outbox and re-send them on reconnect, reconciling the captain's process state before RecordUnconfirmedStopAsync gives up.", "Services.HarborLinkResilience: a stop sent while the Harbor is offline is delivered on reconnect and confirmed.", BlackBoxSeverityEnum.Medium, D.UnconfirmedStop))));

            rules.Add(new BlackBoxRule("BBX-RECOV-003", "Orphaned captain exit", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.OrphanedExit)
                .In(BlackBoxPhaseEnum.Recovery)
                .Because("The captain of {missionId|this mission} exited while Armada was not tracking it (an orphaned exit), so the handoff of its work did not run.", BlackBoxConfidenceEnum.Medium, BlackBoxObjectNames.AgentOutput, "handoff.autoRecoveries")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "adopted the orphaned exit (read the captain's output and the dock's commits) and resumed the normal completion handoff, instead of holding the work",
                    BlackBoxDetectionGapEnum.StateNotPersisted,
                    "The captain's process handle was lost (a restart or a Harbor reconnect) and the exit was not reconciled with the mission's state.",
                    "Every captain exit, tracked or not, runs the completion handoff for its mission exactly once.",
                    "Services.AdmiralService",
                    "Restart the Admiral while a captain is running, let the captain exit, and assert the orphaned exit runs the completion handoff.",
                    new[] { D.Orphans, "src/Armada.Core/Services/AdmiralService.cs:RecoverOrphanedMissionsAsync", D.Completion },
                    BlackBoxRuleChange.Of("Adopt orphaned exits and run the completion handoff", "RecoverOrphanedMissionAsync should read the orphaned captain's output and the dock's commits and run HandleCompletionAsync for the mission, instead of holding it as WorkProduced for a person.", "Services.AdmiralService: an orphaned exit with commits completes and lands like a tracked one.", BlackBoxSeverityEnum.Medium, D.Orphans, D.Completion))));

            rules.Add(new BlackBoxRule("BBX-RECOV-004", "Missing merge handoff", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.MissingMergeHandoff)
                .In(BlackBoxPhaseEnum.Recovery)
                .Because("{missionId|The mission} produced work but no landing or merge-queue handoff was recorded for it, so it never landed.", BlackBoxConfidenceEnum.High, "handoff.steps", "status")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 3)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The landing is attempted again on the current target." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Yes,
                    "re-enqueued the landing (or the merge-queue entry) from the WorkProduced state as soon as the missing handoff was detected",
                    BlackBoxDetectionGapEnum.StateNotPersisted,
                    "The completion handed off to landing in memory; when that step was lost (a restart or an exception) nothing persisted the intent to land.",
                    "A WorkProduced mission always has a persisted landing intent (a landing attempt or a merge entry) until it lands or fails.",
                    "Services.LandingReconciler",
                    "Drop the landing handoff of {missionId|a WorkProduced mission} and assert the integrity sweep re-enqueues and lands it.",
                    new[] { D.MissingHandoffs, D.Integrity, D.LandingHandler },
                    BlackBoxRuleChange.Of("Persist the landing intent and re-enqueue missing handoffs", "Persist a landing intent when a mission reaches WorkProduced, and have FlagMissingMergeHandoffsAsync re-enqueue the landing (retry_landing) for a WorkProduced mission without one, instead of only flagging it.", "Services.LandingReconciler: a WorkProduced mission with no landing handoff is re-enqueued and lands.", BlackBoxSeverityEnum.High, D.MissingHandoffs, D.Integrity))));

            rules.Add(new BlackBoxRule("BBX-RECOV-005", "Redispatch attempts exhausted", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.RedispatchExhausted)
                .In(BlackBoxPhaseEnum.Recovery)
                .Because("Armada redispatched {missionId|the mission} until its redispatch budget ran out: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "subject.redispatchAttempts", "handoff.autoRecoveries")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." })
                .Heals(Heal(BlackBoxSelfHealableEnum.Unknown,
                    "classified why each attempt failed and changed something before redispatching (another captain or model, a fresh dock, the failure fed back to the captain), instead of repeating the same attempt",
                    BlackBoxDetectionGapEnum.MissingInvariant,
                    "Each redispatch repeated the previous attempt with the same captain, dock and brief, so the budget was spent on the same failure.",
                    "A redispatch differs from the attempt it replaces in at least one cause-relevant input.",
                    "Services.AdmiralService",
                    "Fail a mission twice with the same cause and assert the redispatch changes captain or dock, or stops early.",
                    new[] { "src/Armada.Core/Services/AdmiralService.cs:HandleProcessExitCoreAsync", D.TerminalExit },
                    BlackBoxRuleChange.Of("Make redispatches change something cause-relevant", "Before redispatching, classify the previous attempt's failure and change the relevant input (captain or model for a runtime failure, a fresh dock for a workspace failure, the failure text fed back for a work failure); stop early when nothing relevant can change.", "Services.AdmiralService: a runtime failure redispatches to another captain; the same failure twice stops the redispatches.", BlackBoxSeverityEnum.Medium, D.TerminalExit))));
        }

        private static void AddVoyageAndExternal(List<BlackBoxRule> rules)
        {
            rules.Add(new BlackBoxRule("BBX-VOY-001", "Voyage failed", r => r.Subject?.SubjectType == BlackBoxSubjectTypeEnum.Voyage || BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Voyage)
                .In(BlackBoxPhaseEnum.Voyage)
                .Because("The voyage stopped at step {step} ({haltReason|no halt reason recorded}); start with the black box of {cascadeRootMission|its first failed mission}.", BlackBoxConfidenceEnum.Medium, "voyage.cascadeRootMissionId", "voyage.failedMissionEntryIds")
                .Votes(BlackBoxTriageEnum.Unknown, 0)
                .Recover(new BlackBoxRulePath { Key = "recover-root", Title = "Recover the root mission first", Steps = { BlackBoxRuleStep.Manual("Open get_blackbox {\"missionId\": \"{cascadeRootMission|<root mission>}\"} and follow its recovery paths.") }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The voyage continues once its root mission lands." }));

            rules.Add(new BlackBoxRule("BBX-EXT-001", "Changed from outside", r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.External)
                .In(BlackBoxPhaseEnum.External)
                .Because("The mission was ended from outside Armada's own flow ({step}).", BlackBoxConfidenceEnum.High, "site.step", "status")
                .Votes(BlackBoxTriageEnum.External, 3));
        }

        private static BlackBoxRuleResilience Heal(BlackBoxSelfHealableEnum selfHealable, string selfHealing, BlackBoxDetectionGapEnum gapKind, string detectionGap, string invariant, string regressionSuite, string regressionTest, string[] codePointers, params BlackBoxRuleChange[] changes)
        {
            BlackBoxRuleResilience r = new BlackBoxRuleResilience
            {
                SelfHealable = selfHealable,
                SelfHealing = selfHealing,
                GapKind = gapKind,
                DetectionGap = detectionGap,
                Invariant = invariant,
                RegressionSuite = regressionSuite,
                RegressionTest = regressionTest
            };
            r.CodePointers.AddRange(codePointers);
            r.Changes.AddRange(changes);
            return r;
        }

        private static BlackBoxRuleStep RetryLandingStep()
        {
            return BlackBoxRuleStep.Run("Retry the landing of {missionId}.", "retry_landing", "missionId", "{missionId}");
        }

        private static BlackBoxRuleStep RestartStep()
        {
            return BlackBoxRuleStep.Run("Restart {missionId}.", "restart_mission", "missionId", "{missionId}");
        }

        #endregion
    }
}
