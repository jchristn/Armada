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
    /// The one store of stage handoffs (RS-6.9): one artifact per stage attempt, keyed by its attempt id
    /// (<see cref="HandoffArtifact.AttemptIdFor"/>). The artifact is built once, when the stage completes and before any
    /// stage that reads it can launch (in the pipeline handoff transaction, the Architect fan-out transaction, or just
    /// before a rework chain is written). The stage outcome recorder links its outcome to that same row; for an attempt
    /// that handed nothing on it stores the attempt's artifact itself. A launch only reads.
    /// </summary>
    public class HandoffArtifactService
    {
        #region Public-Members

        /// <summary>
        /// Most earlier stages a launch reads behind the prior stage (RS-6.10); the handoff budget limits it further.
        /// </summary>
        public const int MaxChainStages = 8;

        #endregion

        #region Private-Members

        private string _Header = "[HandoffArtifactService] ";
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
        public HandoffArtifactService(DatabaseDriver database, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The artifact a completing stage hands on, for the caller to write in the same transaction that releases the
        /// stages reading it; null when this attempt's artifact is already stored (a recorder or an earlier handoff ran
        /// first). Carries the attempt's latest stage check result and its attempt number.
        /// </summary>
        /// <param name="stage">The completed stage as it will be handed on.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact to store, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stage is null.</exception>
        public async Task<HandoffArtifact?> PrepareForHandoffAsync(Mission stage, CancellationToken token = default)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));
            string attemptId = HandoffArtifact.AttemptIdFor(stage.Id, stage.StartedUtc);
            HandoffArtifact? stored = await _Database.HandoffArtifacts.ReadByAttemptAsync(stage.Id, attemptId, token).ConfigureAwait(false);
            if (stored != null) return null;

            // The check, attempt number and captain name enrich the artifact; failing to read them never blocks a handoff.
            StageCheckResult? check = null;
            int attempt = 1;
            Captain? captain = null;
            try
            {
                check = await ReadAttemptCheckAsync(stage, token).ConfigureAwait(false);
                attempt = await _Database.StageOutcomes.CountByMissionAsync(stage.Id, token).ConfigureAwait(false) + 1;
                if (!String.IsNullOrEmpty(stage.CaptainId)) captain = await _Database.Captains.ReadAsync(stage.CaptainId!, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not read the check, attempt or captain for the handoff of mission " + stage.Id + ": " + ex.Message);
            }

            HandoffArtifact built = HandoffArtifactBuilder.Build(stage, check, captain, null, attempt);
            built.AttemptId = attemptId;
            return built;
        }

        /// <summary>
        /// Store the completing stage's artifact on its own, for a handoff whose dependents are written by a call that
        /// takes no artifact (a rework chain). Call it before that write so the readers never launch without it.
        /// Idempotent per attempt.
        /// </summary>
        /// <param name="stage">The completed stage.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stage is null.</exception>
        public async Task StoreForHandoffAsync(Mission stage, CancellationToken token = default)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));
            HandoffArtifact? built = await PrepareForHandoffAsync(stage, token).ConfigureAwait(false);
            if (built != null) await _Database.HandoffArtifacts.CreateAsync(built, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Record the artifact of a stage attempt whose exit was handled. When the attempt already handed on its artifact
        /// (same attempt id, no outcome linked yet) the outcome, captain and check are added to that row; otherwise the
        /// attempt's artifact is stored now. Never throws for a storage problem.
        /// </summary>
        /// <param name="mission">The stage as it is after the exit was handled.</param>
        /// <param name="check">The attempt's stage check result, or null.</param>
        /// <param name="captain">The captain that ran the attempt, or null.</param>
        /// <param name="outcome">The stored stage outcome of the attempt, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored artifact, or null when nothing could be stored.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public async Task<HandoffArtifact?> RecordAttemptAsync(Mission mission, StageCheckResult? check, Captain? captain, StageOutcome? outcome, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            try
            {
                // The outcome carries the launch time of the attempt it records; the mission may already be on a new one.
                DateTime? startedUtc = outcome != null ? outcome.StartedUtc : mission.StartedUtc;
                string attemptId = HandoffArtifact.AttemptIdFor(mission.Id, startedUtc);
                HandoffArtifact? handedOn = await _Database.HandoffArtifacts.ReadByAttemptAsync(mission.Id, attemptId, token).ConfigureAwait(false);
                if (handedOn != null && handedOn.StageOutcomeId == null)
                {
                    handedOn.StageOutcomeId = outcome?.Id;
                    handedOn.Attempt = outcome?.Attempt ?? handedOn.Attempt;
                    if (captain != null)
                    {
                        handedOn.CaptainId = captain.Id;
                        handedOn.CaptainName = captain.Name;
                    }

                    if (check != null && handedOn.CheckResults.Count == 0) handedOn.CheckResults.Add(check);
                    return await _Database.HandoffArtifacts.UpdateAsync(handedOn, token).ConfigureAwait(false);
                }

                HandoffArtifact built = HandoffArtifactBuilder.Build(mission, check, captain, outcome?.Id, outcome?.Attempt ?? 1);
                built.AttemptId = attemptId;
                return await _Database.HandoffArtifacts.CreateAsync(built, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not record the handoff artifact for mission " + mission.Id + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The stored artifact of a stage's latest attempt, for a launch that reads it. Never stores anything. A stage
        /// that completed before attempt ids were recorded (an upgrade with a pipeline in flight) has none; its artifact is
        /// then built from the stage, unstored, and logged as a legacy handoff. A read failure takes the same path.
        /// </summary>
        /// <param name="stage">The prior stage.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact.</returns>
        /// <exception cref="ArgumentNullException">Thrown when stage is null.</exception>
        public async Task<HandoffArtifact> ReadForLaunchAsync(Mission stage, CancellationToken token = default)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));
            try
            {
                HandoffArtifact? stored = await _Database.HandoffArtifacts.ReadByAttemptAsync(stage.Id, HandoffArtifact.AttemptIdFor(stage.Id, stage.StartedUtc), token).ConfigureAwait(false);
                if (stored != null) return stored;
                _Logging.Info(_Header + "legacy handoff: mission " + stage.Id + " has no stored artifact for its latest attempt; building one from the stage, unstored");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Warn(_Header + "could not read the handoff artifact for mission " + stage.Id + "; building one from the stage, unstored: " + ex.Message);
            }

            return HandoffArtifactBuilder.Build(stage);
        }

        /// <summary>
        /// The artifacts of the stages behind a prior stage (RS-6.10), newest first: the prior stage's own dependency,
        /// then its dependency, up to <see cref="MaxChainStages"/> stages, within the voyage. The prior stage itself is not
        /// included. Stops at a missing stage or a cycle.
        /// </summary>
        /// <param name="previousStage">The prior stage.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The earlier artifacts, newest first.</returns>
        /// <exception cref="ArgumentNullException">Thrown when previousStage is null.</exception>
        public async Task<List<HandoffArtifact>> ReadEarlierChainAsync(Mission previousStage, CancellationToken token = default)
        {
            if (previousStage == null) throw new ArgumentNullException(nameof(previousStage));
            List<HandoffArtifact> chain = new List<HandoffArtifact>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal) { previousStage.Id };
            Mission cursor = previousStage;
            while (chain.Count < MaxChainStages && !String.IsNullOrEmpty(cursor.DependsOnMissionId) && seen.Add(cursor.DependsOnMissionId!))
            {
                Mission? earlier;
                try
                {
                    earlier = !String.IsNullOrEmpty(cursor.TenantId)
                        ? await _Database.Missions.ReadAsync(cursor.TenantId!, cursor.DependsOnMissionId!, token).ConfigureAwait(false)
                        : await _Database.Missions.ReadAsync(cursor.DependsOnMissionId!, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    _Logging.Warn(_Header + "could not read earlier stage " + cursor.DependsOnMissionId + ": " + ex.Message);
                    break;
                }

                if (earlier == null || !String.Equals(earlier.VoyageId, previousStage.VoyageId, StringComparison.Ordinal)) break;
                chain.Add(await ReadForLaunchAsync(earlier, token).ConfigureAwait(false));
                cursor = earlier;
            }

            return chain;
        }

        #endregion

        #region Private-Methods

        private async Task<StageCheckResult?> ReadAttemptCheckAsync(Mission stage, CancellationToken token)
        {
            EnumerationResult<ArmadaEvent> checks = await _Database.Events.EnumerateAsync(new EnumerationQuery
            {
                MissionId = stage.Id,
                EventType = StageCheckEvents.EventType,
                PageSize = 1,
                Order = EnumerationOrderEnum.CreatedDescending
            }, token).ConfigureAwait(false);
            ArmadaEvent? latest = checks.Objects.FirstOrDefault();
            if (latest == null) return null;
            // A check from an earlier attempt says nothing about this one.
            if (stage.StartedUtc.HasValue && latest.CreatedUtc < stage.StartedUtc.Value) return null;
            return StageCheckEvents.Read(latest);
        }

        #endregion
    }
}
