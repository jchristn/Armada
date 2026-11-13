namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox.Enrichment;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// The success flight recorder: keeps a minimal record (vessel, mission, timings, base and landed commits, changed
    /// files) of each successful landing for blackBox.successRetentionDays (0 turns it off), so a failure can be compared
    /// with nearby successes. Driven by mission.completed events (every landing path records one), read in bounded pages
    /// by the insight worker; <see cref="RecordAsync"/> records one mission directly. Thread-safe.
    /// </summary>
    public class BlackBoxSuccessRecorder
    {
        #region Private-Members

        private readonly string _Header = "[BlackBoxSuccessRecorder] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly VesselGitInspector _Inspector;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentDictionary<string, DateTime> _Seen = new ConcurrentDictionary<string, DateTime>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Settings.</param>
        /// <param name="inspector">Git inspector (changed files when the mission has no diff snapshot).</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxSuccessRecorder(DatabaseDriver database, ArmadaSettings settings, VesselGitInspector inspector, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record the successful landings announced by recent mission.completed events (the newest 200) that are not
        /// recorded yet.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Records written.</returns>
        public async Task<int> RunOnceAsync(CancellationToken token = default)
        {
            int days = _Settings.BlackBox.SuccessRetentionDays;
            if (days <= 0) return 0;
            DateTime since = DateTime.UtcNow.AddDays(-days);
            List<ArmadaEvent> events = await _Database.Events.EnumerateByTypeAsync("mission.completed", BlackBoxInsightLimits.MaxSuccessScan, token).ConfigureAwait(false);
            int written = 0;
            foreach (ArmadaEvent e in events.Where(x => !String.IsNullOrEmpty(x.MissionId) && x.CreatedUtc >= since).OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                if (_Seen.ContainsKey(e.Id)) continue;
                Mission? mission = await _Database.Missions.ReadAsync(e.MissionId!, token).ConfigureAwait(false);
                if (mission != null && await RecordAsync(mission, token).ConfigureAwait(false) != null) written++;
                _Seen[e.Id] = e.CreatedUtc;
            }
            if (_Seen.Count > 10000)
            {
                foreach (KeyValuePair<string, DateTime> old in _Seen.Where(kv => kv.Value < since).ToList()) _Seen.TryRemove(old.Key, out _);
            }
            return written;
        }

        /// <summary>
        /// Record one landed mission, unless the recorder is off, the mission is not Complete, or its landing is already
        /// recorded.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The record, or null when nothing was written.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mission is null.</exception>
        public async Task<BlackBoxSuccess?> RecordAsync(Mission mission, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            int days = _Settings.BlackBox.SuccessRetentionDays;
            if (days <= 0 || mission.Status != MissionStatusEnum.Complete) return null;
            DateTime landed = mission.CompletedUtc ?? mission.LastUpdateUtc;
            BlackBoxSuccess? existing = await _Database.BlackBoxSuccesses.ReadLatestByMissionAsync(mission.Id, token).ConfigureAwait(false);
            if (existing != null && Math.Abs((existing.LandedUtc - landed).TotalSeconds) < 2) return null;

            Vessel? vessel = String.IsNullOrEmpty(mission.VesselId) ? null : await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
            List<string> files = await ChangedFilesAsync(mission, vessel, token).ConfigureAwait(false);
            BlackBoxSuccess s = new BlackBoxSuccess
            {
                TenantId = mission.TenantId,
                UserId = mission.UserId,
                VesselId = mission.VesselId,
                MissionId = mission.Id,
                VoyageId = mission.VoyageId,
                CaptainId = mission.CaptainId,
                MissionTitle = BlackBoxLimits.Clip(mission.Title, BlackBoxLimits.MaxTitleChars),
                BaseCommit = mission.StartCommit,
                TargetCommit = mission.CommitHash,
                TargetBranch = vessel?.DefaultBranch,
                FileCount = files.Count,
                FilesChanged = files.Take(BlackBoxInsightLimits.MaxSuccessFiles).ToList(),
                MissionCreatedUtc = mission.CreatedUtc,
                StartedUtc = mission.StartedUtc,
                LandedUtc = landed,
                QueueMs = mission.StartedUtc.HasValue ? Ms(mission.StartedUtc.Value - mission.CreatedUtc) : null,
                RunMs = mission.StartedUtc.HasValue ? Ms(landed - mission.StartedUtc.Value) : null,
                TotalMs = Ms(landed - mission.CreatedUtc),
                CreatedUtc = DateTime.UtcNow,
                ExpiresUtc = landed.AddDays(days)
            };
            await _Database.BlackBoxSuccesses.CreateAsync(s, token).ConfigureAwait(false);
            return s;
        }

        /// <summary>
        /// Delete expired success records (every record when the recorder is off).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Records deleted.</returns>
        public Task<int> CleanupAsync(CancellationToken token = default)
        {
            DateTime cutoff = _Settings.BlackBox.SuccessRetentionDays <= 0 ? DateTime.UtcNow.AddYears(100) : DateTime.UtcNow;
            return _Database.BlackBoxSuccesses.DeleteExpiredAsync(cutoff, token);
        }

        #endregion

        #region Private-Methods

        private async Task<List<string>> ChangedFilesAsync(Mission mission, Vessel? vessel, CancellationToken token)
        {
            if (!String.IsNullOrEmpty(mission.DiffSnapshot)) return BlackBoxForensics.DiffPaths(mission.DiffSnapshot);
            if (vessel == null || String.IsNullOrEmpty(mission.StartCommit) || String.IsNullOrEmpty(mission.CommitHash)) return new List<string>();
            VesselRepositoryState repo = _Inspector.ResolveRepository(vessel, null);
            if (repo.Host == null || String.IsNullOrWhiteSpace(repo.Path)) return new List<string>();
            try
            {
                GitCommitRange? range = await repo.Host.Git.GetCommitRangeAsync(repo.Path, mission.StartCommit!, mission.CommitHash!, 100, token).ConfigureAwait(false);
                return range == null ? new List<string>() : range.Commits.SelectMany(c => c.ChangedPaths).Distinct(StringComparer.Ordinal).ToList();
            }
            catch (Exception e) when (!(e is OperationCanceledException && token.IsCancellationRequested))
            {
                _Logging.Debug(_Header + "could not read the changed files of mission " + mission.Id + ": " + e.Message);
                return new List<string>();
            }
        }

        private static long Ms(TimeSpan span)
        {
            return span.Ticks < 0 ? 0 : (long)span.TotalMilliseconds;
        }

        #endregion
    }
}
