namespace Armada.Core.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Accepts submit tool calls and applies the latest submission to a completed stage (RELIABILITY_SIMPLICITY_PLAN.md
    /// RS-6.2). A submission is checked synchronously; a valid one is stored on the mission attempt. At completion the latest
    /// submission of the attempt is appended to the stage output as a block, and every decision about the stage reads
    /// <see cref="StageSubmissionText.Effective"/>, so the submission wins over any marker the captain also wrote.
    /// </summary>
    public class StageSubmissionService
    {
        #region Private-Members

        private readonly string _Header = "[StageSubmissionService] ";
        private readonly DatabaseDriver _Database;
        private readonly LoggingModule _Logging;
        private readonly int _MaxHierarchyDepth;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="maxHierarchyDepth">Deepest allowed plan parent chain (Concurrency.MaxHierarchyDepth).</param>
        /// <exception cref="ArgumentNullException">Thrown when database or logging is null.</exception>
        public StageSubmissionService(DatabaseDriver database, LoggingModule logging, int maxHierarchyDepth = Constants.DefaultMaxHierarchyDepth)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _MaxHierarchyDepth = maxHierarchyDepth < 1 ? Constants.DefaultMaxHierarchyDepth : maxHierarchyDepth;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Check and store a submission for a running mission. The caller has already established that the caller is the
        /// mission's own captain.
        /// </summary>
        /// <param name="mission">The caller's mission.</param>
        /// <param name="kind">What the tool submits.</param>
        /// <param name="argumentsJson">Tool arguments as JSON.</param>
        /// <param name="source">How the submission arrived.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Accepted with the stored id, or the errors.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public async Task<StageSubmitResult> SubmitAsync(Mission mission, StageSubmissionKindEnum kind, string? argumentsJson, StageSubmissionSourceEnum source, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            StageSubmitResult result = new StageSubmitResult { Kind = kind };
            if (mission.Status != MissionStatusEnum.InProgress && mission.Status != MissionStatusEnum.Assigned)
            {
                result.Errors.Add("Mission " + mission.Id + " is " + mission.Status + "; only a running mission can submit.");
                result.Message = "Not stored.";
                return result;
            }

            StageSubmissionCheck check = StageSubmissionValidator.Check(kind, mission.Persona, argumentsJson, _MaxHierarchyDepth);
            if (!check.Accepted)
            {
                result.Errors = check.Errors;
                result.Message = "Not stored. Fix every error and call " + StageSubmissionTools.ToolFor(kind) + " again with the complete submission.";
                return result;
            }

            StageSubmission submission = new StageSubmission
            {
                TenantId = mission.TenantId,
                MissionId = mission.Id,
                AttemptId = HandoffArtifact.AttemptIdFor(mission.Id, mission.StartedUtc),
                Persona = String.IsNullOrWhiteSpace(mission.Persona) ? PersonaCatalog.Worker : PersonaCatalog.NormalizeName(mission.Persona),
                Kind = kind,
                Source = source,
                Submission = check.Submission!
            };
            await _Database.StageSubmissions.CreateAsync(submission, token).ConfigureAwait(false);
            _Logging.Info(_Header + "mission " + mission.Id + " submitted a " + kind.ToString().ToLowerInvariant() + " (" + submission.Id + ", " + source + ")");

            result.Accepted = true;
            result.SubmissionId = submission.Id;
            result.Message = "Stored. This submission is your result; finish with a brief plain-text reply. Submitting again replaces it.";
            return result;
        }

        /// <summary>
        /// Apply the latest submission of the mission's current attempt to its output, at completion. A stored submission
        /// that is not yet in the output is appended as a block; a native submission found in the output (an API endpoint
        /// captain without MCP) is stored. A submission that disagrees with a marker the captain wrote wins, and the
        /// disagreement is logged. The caller persists the mission when this returns true.
        /// </summary>
        /// <param name="mission">The completing mission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the output changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public async Task<bool> ApplyToOutputAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (StageSubmissionTools.KindFor(mission.Persona) == null) return false;
            string attemptId = HandoffArtifact.AttemptIdFor(mission.Id, mission.StartedUtc);
            StageSubmission? stored = await _Database.StageSubmissions.ReadLatestForAttemptAsync(mission.Id, attemptId, token).ConfigureAwait(false);

            if (stored == null)
            {
                StageSubmissionEnvelope? native = StageSubmissionText.Read(mission.AgentOutput, mission.Persona, out string? nativeId);
                if (native == null) return false;
                StageSubmission record = new StageSubmission
                {
                    TenantId = mission.TenantId,
                    MissionId = mission.Id,
                    AttemptId = attemptId,
                    Persona = PersonaCatalog.NormalizeName(String.IsNullOrWhiteSpace(mission.Persona) ? PersonaCatalog.Worker : mission.Persona),
                    Kind = native.Kind,
                    Source = StageSubmissionSourceEnum.Native,
                    Submission = native
                };
                await _Database.StageSubmissions.CreateAsync(record, token).ConfigureAwait(false);
                _Logging.Info(_Header + "mission " + mission.Id + " submitted a " + native.Kind.ToString().ToLowerInvariant() + " through its runtime's native tool (" + record.Id + ", block " + (nativeId ?? "unnamed") + ")");
                LogConflict(mission, native);
                return false;
            }

            if (StageSubmissionText.ContainsBlock(mission.AgentOutput, stored.Id)) return false;
            LogConflict(mission, stored.Submission);
            mission.AgentOutput = (mission.AgentOutput ?? String.Empty).TrimEnd() + StageSubmissionText.Block(stored);
            return true;
        }

        #endregion

        #region Private-Methods

        private void LogConflict(Mission mission, StageSubmissionEnvelope submission)
        {
            string? own = StageSubmissionText.WithoutBlocks(mission.AgentOutput);
            if (String.IsNullOrWhiteSpace(own)) return;
            string? conflict = null;
            switch (submission.Kind)
            {
                case StageSubmissionKindEnum.Verdict:
                    JudgeVerdictEnum written = JudgeVerdictParser.Parse(own);
                    if (written != JudgeVerdictEnum.None && StageOutputContract.VerdictToken(written) != submission.Verdict?.Verdict)
                        conflict = "its output says " + StageOutputContract.VerdictToken(written) + " but it submitted " + submission.Verdict?.Verdict;
                    break;
                case StageSubmissionKindEnum.Result:
                    bool submittedBlocked = String.Equals(submission.Result?.Status, StageResultSubmission.StatusBlocked, StringComparison.Ordinal);
                    if (VerifierContract.IsBlocked(own) != submittedBlocked && (VerifierContract.IsBlocked(own) || CompletionLine.HasCompletionLine(own)))
                        conflict = "its output says " + (VerifierContract.IsBlocked(own) ? "BLOCKED" : "COMPLETE") + " but it submitted " + submission.Result?.Status;
                    break;
                case StageSubmissionKindEnum.Plan:
                    if (ArchitectPlanParser.Parse(own, _MaxHierarchyDepth).Status != ArchitectPlanParseStatusEnum.NoPlanFound)
                        conflict = "its output also holds a plan block";
                    break;
            }
            if (conflict != null)
                _Logging.Warn(_Header + "mission " + mission.Id + ": " + conflict + "; the submission is used");
        }

        #endregion
    }
}
