namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Resolves the black box entries of a mission that completed after they were captured: a non-terminal entry (an
    /// unconfirmed stop the mission recovered from) or a terminal one the mission later got past (a LandingFailed
    /// reconciled to Complete). Each Open entry captured before the completion is set to Resolved with a System note
    /// naming the completion time and commit, and its expiry is recomputed. Entries an operator already resolved are left
    /// alone; a pinned entry is resolved and stays pinned. Driven by mission.completed and mission.landing_reconciled
    /// events (newest first, bounded), and callable for one mission. Thread-safe.
    /// </summary>
    public class BlackBoxCompletionResolver
    {
        #region Public-Members

        /// <summary>
        /// Event types that announce a mission's work is landed.
        /// </summary>
        public static readonly IReadOnlyList<string> CompletionEventTypes = new List<string> { "mission.completed", "mission.landing_reconciled" };

        #endregion

        #region Private-Members

        private readonly string _Header = "[BlackBoxCompletionResolver] ";
        private readonly DatabaseDriver _Database;
        private readonly IBlackBoxService _Service;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentDictionary<string, DateTime> _Seen = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);
        private static readonly AuthContext _System = new AuthContext { IsAuthenticated = true, IsAdmin = true };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="service">Black box service (resolution, notes, expiry).</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxCompletionResolver(DatabaseDriver database, IBlackBoxService service, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the open entries of the missions announced complete by recent completion events (the newest 200 of each
        /// type) that were not handled yet.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries resolved.</returns>
        public async Task<int> RunOnceAsync(CancellationToken token = default)
        {
            int resolved = 0;
            foreach (string eventType in CompletionEventTypes)
            {
                List<ArmadaEvent> events = await _Database.Events.EnumerateByTypeAsync(eventType, BlackBoxInsightLimits.MaxSuccessScan, token).ConfigureAwait(false);
                foreach (ArmadaEvent e in events.Where(x => !String.IsNullOrEmpty(x.MissionId)).OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id, StringComparer.Ordinal))
                {
                    if (_Seen.ContainsKey(e.Id)) continue;
                    try
                    {
                        resolved += await ResolveMissionAsync(e.MissionId!, LandingCommit(e), token).ConfigureAwait(false);
                        _Seen[e.Id] = e.CreatedUtc;
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                    {
                        _Logging.Warn(_Header + "could not resolve the entries of mission " + e.MissionId + ": " + ex.Message);
                    }
                }
            }

            if (_Seen.Count > 10000)
            {
                DateTime cutoff = DateTime.UtcNow.AddDays(-30);
                foreach (KeyValuePair<string, DateTime> old in _Seen.Where(kv => kv.Value < cutoff).ToList()) _Seen.TryRemove(old.Key, out _);
            }

            return resolved;
        }

        /// <summary>
        /// Resolve the Open entries of one mission captured before it completed. Nothing happens unless the mission is
        /// Complete.
        /// </summary>
        /// <param name="missionId">Mission identifier.</param>
        /// <param name="landingCommit">Commit the work landed as, when known (for example the reconciliation evidence).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entries resolved.</returns>
        /// <exception cref="ArgumentNullException">Thrown when missionId is null or empty.</exception>
        public async Task<int> ResolveMissionAsync(string missionId, string? landingCommit = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(missionId)) throw new ArgumentNullException(nameof(missionId));
            Mission? mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (mission == null || mission.Status != MissionStatusEnum.Complete) return 0;
            DateTime completedUtc = mission.CompletedUtc ?? mission.LastUpdateUtc;

            EnumerationResult<BlackBoxEntry> page = await _Database.BlackBoxEntries.EnumerateByMissionAsync(mission.Id, null,
                new EnumerationQuery { PageSize = 100, Order = EnumerationOrderEnum.CreatedDescending }, token).ConfigureAwait(false);
            int resolved = 0;
            foreach (BlackBoxEntry entry in page.Objects)
            {
                if (entry.Resolution != BlackBoxResolutionEnum.Open) continue;
                if (entry.CreatedUtc > completedUtc) continue;
                string note = Note(mission, completedUtc, landingCommit);
                await _Service.ResolveAsync(_System, entry.Id, new BlackBoxResolveRequest { Resolution = BlackBoxResolutionEnum.Resolved, Note = note }, token).ConfigureAwait(false);
                await _Service.AddNoteAsync(_System, entry.Id, new BlackBoxNoteAddRequest { Body = note, AuthorKind = BlackBoxNoteAuthorKindEnum.System }, BlackBoxNoteAuthorKindEnum.System, token).ConfigureAwait(false);
                _Logging.Info(_Header + "resolved black box entry " + entry.Id + ": " + note);
                resolved++;
            }

            return resolved;
        }

        /// <summary>
        /// The resolution note for a mission that completed after its failure.
        /// </summary>
        /// <param name="mission">Completed mission.</param>
        /// <param name="completedUtc">When it completed (UTC).</param>
        /// <param name="landingCommit">Commit the work landed as, or null.</param>
        /// <returns>The note.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public static string Note(Mission mission, DateTime completedUtc, string? landingCommit)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            string note = "Mission completed after this failure at " + completedUtc.ToString("u") + "; commit " + (String.IsNullOrWhiteSpace(mission.CommitHash) ? "unknown" : mission.CommitHash);
            if (!String.IsNullOrWhiteSpace(landingCommit) && !String.Equals(landingCommit, mission.CommitHash, StringComparison.OrdinalIgnoreCase))
                note += "; landed as " + landingCommit;
            return note + ".";
        }

        #endregion

        #region Private-Methods

        private string? LandingCommit(ArmadaEvent e)
        {
            if (!String.Equals(e.EventType, "mission.landing_reconciled", StringComparison.Ordinal) || String.IsNullOrWhiteSpace(e.Payload)) return null;
            try
            {
                return JsonSerializer.Deserialize<LandingReconciliationEvidence>(e.Payload!)?.EvidenceCommit;
            }
            catch (JsonException ex)
            {
                _Logging.Debug(_Header + "could not read the reconciliation evidence of event " + e.Id + ": " + ex.Message);
                return null;
            }
        }

        #endregion
    }
}
