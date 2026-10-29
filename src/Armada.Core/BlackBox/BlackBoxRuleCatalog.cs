namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The versioned black box rule catalog the <see cref="BlackBoxAdvisor"/> runs (same style as
    /// FailureCauseRuleCatalog). Every starter rule comes from a failure observed in this repository; every phase of the
    /// step taxonomy has at least one rule, and <c>BBX-UNKNOWN-001</c> fires when nothing else does. Bump
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
        public const int CurrentRulesVersion = 1;

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
                }));

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
                }));

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
                }));

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
                }));

            rules.Add(new BlackBoxRule("BBX-LAND-005", "Verification failed", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.Verification)
                .In(BlackBoxPhaseEnum.Landing)
                .Because("Post-merge verification `{failingCommand|the verification command}` failed with exit {commandExitCode|code unknown}.", BlackBoxConfidenceEnum.High, "mechanism.failingCommand", "mechanism.commandOutputTail")
                .Votes(BlackBoxTriageEnum.MissionBrief, 2)
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
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission so the captain fixes the failure", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Medium, ExpectedOutcome = "The captain sees the failure and fixes it." }));

            rules.Add(new BlackBoxRule("BBX-LAND-006", "Push or pull request failed", r =>
                    BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.Push || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.PullRequest || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.UnpublishedAncestry)
                .In(BlackBoxPhaseEnum.Landing)
                .YieldsTo("BBX-INFRA-001")
                .Because("Publishing {branchName|the mission branch} to the remote failed at step {step}: {gitStderr|reason}.", BlackBoxConfidenceEnum.Medium, "mechanism.gitStderrTail", "classification.rawReason")
                .Votes(BlackBoxTriageEnum.External, 2)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing once the remote accepts pushes", Preconditions = { "The remote is reachable and the credentials Armada uses can push." }, Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The branch is published and the mission lands." }));

            rules.Add(new BlackBoxRule("BBX-LAND-007", "Landing failed", r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Landing)
                .In(BlackBoxPhaseEnum.Landing)
                .YieldsTo("BBX-LAND-001", "BBX-LAND-002", "BBX-LAND-003", "BBX-LAND-004", "BBX-LAND-005", "BBX-LAND-006", "BBX-INFRA-001")
                .Because("The landing failed at step {step}: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Low, "classification.rawReason", "mechanism")
                .Votes(BlackBoxTriageEnum.Unknown, 0)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The landing is attempted again on the current target." }));
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
                }));

            rules.Add(new BlackBoxRule("BBX-MQ-002", "Merge-queue tests failed", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.QueueTests)
                .In(BlackBoxPhaseEnum.MergeQueue)
                .Because("The merge queue's test command `{failingCommand|the queue test command}` failed with exit {commandExitCode|code unknown} on the integrated branch.", BlackBoxConfidenceEnum.Medium, "mechanism.commandOutputTail", BlackBoxObjectNames.MergeEntry)
                .Votes(BlackBoxTriageEnum.MissionBrief, 1)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission so the captain fixes the failing tests", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Medium, ExpectedOutcome = "The fixed branch passes the queue's tests." }));

            rules.Add(new BlackBoxRule("BBX-MQ-003", "Merge-queue failure", r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.MergeQueue)
                .In(BlackBoxPhaseEnum.MergeQueue)
                .YieldsTo("BBX-MQ-001", "BBX-MQ-002", "BBX-LAND-003", "BBX-LAND-004")
                .Because("The merge queue failed at step {step}: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Low, "classification.rawReason")
                .Votes(BlackBoxTriageEnum.Unknown, 0)
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission is landed again." }));
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
                }));

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
                }));

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
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "A fresh attempt follows the contract." }));

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
                .YieldsTo("BBX-LAND-005", "BBX-MQ-002")
                .Because("The check `{failingCommand|build or test command}` failed with exit {commandExitCode|code unknown}.", BlackBoxConfidenceEnum.Medium, "evidence.checkResults", "mechanism.commandOutputTail")
                .Votes(BlackBoxTriageEnum.MissionBrief, 1)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission so the captain fixes the failing check", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The check passes." }));

            rules.Add(new BlackBoxRule("BBX-UNFIN-001", "Unfinished work", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.Unfinished)
                .In(BlackBoxPhaseEnum.Completion)
                .Because("The captain stopped before finishing the mission's work.", BlackBoxConfidenceEnum.Medium, BlackBoxObjectNames.AgentOutput)
                .Votes(BlackBoxTriageEnum.MissionBrief, 1)
                .Recover(new BlackBoxRulePath { Key = "split-restart", Title = "Split or narrow the brief, then restart", Steps = { BlackBoxRuleStep.Manual("Split {missionId} into smaller missions or narrow its brief."), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "Smaller missions finish." }));
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
                }));

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
                .YieldsTo("BBX-INFRA-001")
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
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "A new dock is provisioned." }));
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
                .Recover(new BlackBoxRulePath { Key = "retry-landing", Title = "Retry the landing from the branch", Steps = { RetryLandingStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The approved branch lands." }));

            rules.Add(new BlackBoxRule("BBX-REV-004", "Review timed out", r => BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.ReviewTimeout)
                .In(BlackBoxPhaseEnum.Review)
                .Because("Nobody decided the review within ReviewTimeoutMinutes.", BlackBoxConfidenceEnum.High, "site.step")
                .Votes(BlackBoxTriageEnum.External, 2)
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission and review it in time", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission returns to review." }));

            rules.Add(new BlackBoxRule("BBX-DEP-002", "Dependency failed", r => BlackBoxRuleFacts.Kind(r) == MissionFailureKindEnum.DependencyFailed || BlackBoxRuleFacts.Step(r) == BlackBoxStepEnum.DependencyCancelled)
                .In(BlackBoxPhaseEnum.Handoff)
                .Because("The mission did not run because a mission it depends on failed or was cancelled.", BlackBoxConfidenceEnum.High, "classification.kind", "related.cascadeRootEntryId")
                .Votes(BlackBoxTriageEnum.External, 1)
                .Recover(new BlackBoxRulePath { Key = "fix-root-restart", Title = "Fix the root mission, then restart this one", Steps = { BlackBoxRuleStep.Manual("Open the black box of the failed prerequisite and recover it first."), RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs once its prerequisite lands." }));

            rules.Add(new BlackBoxRule("BBX-HAND-001", "Handoff failed", r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Handoff)
                .In(BlackBoxPhaseEnum.Handoff)
                .YieldsTo("BBX-DEP-002")
                .Because("The handoff between pipeline stages failed at step {step}: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "classification.rawReason")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 1)
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Pipelines",
                    Hypothesis = "Make the stage handoff tolerate this condition ({step}) or report it before the next stage starts.",
                    SourcePointers = { "src/Armada.Core/Services/MissionService.Handoff.cs" },
                    Severity = BlackBoxSeverityEnum.Low
                })
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The stage runs again with a fresh handoff." }));

            rules.Add(new BlackBoxRule("BBX-RECOV-001", "Armada recovery failed", r => BlackBoxRuleFacts.Phase(r) == BlackBoxPhaseEnum.Recovery)
                .In(BlackBoxPhaseEnum.Recovery)
                .Because("Armada's own recovery could not finish at step {step}: {reason|no reason recorded}.", BlackBoxConfidenceEnum.Medium, "classification.rawReason", "attempts")
                .Votes(BlackBoxTriageEnum.ArmadaDefect, 2)
                .Improve(new BlackBoxRuleImprovement
                {
                    Area = "Recovery",
                    Hypothesis = "Recovery step {step} ended the mission instead of retrying or handing it to the operator with its work intact.",
                    SourcePointers = { "src/Armada.Core/Services/AdmiralService.cs:HandleTerminalProcessExitFailureAsync" },
                    Severity = BlackBoxSeverityEnum.Medium
                })
                .Recover(new BlackBoxRulePath { Key = "restart", Title = "Restart the mission", Steps = { RestartStep() }, Risk = BlackBoxSeverityEnum.Low, ExpectedOutcome = "The mission runs again." }));
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
