namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Schedules account usage capture: rebuilds the capture groups from the enabled captains (their
    /// resolved credential host, runtime and Custom command fingerprint) on startup, on every tick (so a
    /// configuration change shows up immediately) and whenever the settings generation is invalidated,
    /// and launches the attempts that are due. New groups start with a randomized due time (the effective
    /// interval with jitter, floored by the server-wide minimum), the effective interval is the shortest
    /// one its members requested, and the persisted lease keeps a restart from replaying an attempt whose
    /// result may still arrive. Ticks never borrow a captain's Working/Idle state and never touch a
    /// mission launch slot: capture work runs as its own dedicated UsageProbe job.
    /// </summary>
    public class UsageCaptureScheduler
    {
        #region Public-Members

        /// <summary>
        /// The scheduler's groups as of the last rebuild, newest configuration first. A copy, for
        /// inspection and tests; mutating it does not affect the schedule.
        /// </summary>
        public List<UsageCaptureGroup> Groups()
        {
            lock (_GroupsLock)
            {
                return new List<UsageCaptureGroup>(_Groups.Values);
            }
        }

        /// <summary>
        /// Enabled captains the last rebuild could not resolve a credential host for (reported, never
        /// scheduled, and never captured on the Admiral's credentials as a fallback).
        /// </summary>
        public List<string> UnresolvedCaptains()
        {
            lock (_GroupsLock)
            {
                return new List<string>(_Unresolved);
            }
        }

        #endregion

        #region Private-Members

        private string _Header = "[UsageCaptureScheduler] ";
        private readonly UsageCaptureService _Capture;
        private readonly DatabaseDriver _Database;
        private readonly UsageAccountResolver _Accounts;
        private readonly ArmadaSettings _Settings;
        private readonly HarborConnectionManager? _Harbors;
        private readonly LoggingModule _Logging;
        private readonly object _GroupsLock = new object();
        private Dictionary<string, UsageCaptureGroup> _Groups = new Dictionary<string, UsageCaptureGroup>(StringComparer.Ordinal);
        private List<string> _Unresolved = new List<string>();
        private List<KeyValuePair<string, string>> _ActiveCaptains = new List<KeyValuePair<string, string>>();
        private bool _Rebuilt = false;
        private bool _CaptureEnabledLastTick = true;

        /// <summary>
        /// The scheduling revision of one group's effective configuration: the server-wide scheduling bounds
        /// (the minimum interval floor, the opt-in PTY switch, the host execution deadline, the overall and
        /// per-host concurrency bounds and the failure backoff cap) plus the group's own requested and
        /// effective intervals. Retention and excerpt bounds are deliberately excluded: they change what a
        /// committed observation keeps, never which captures run, when, or what a queued attempt waits for.
        /// </summary>
        /// <param name="capture">Capture settings.</param>
        /// <param name="group">Group.</param>
        /// <returns>The revision.</returns>
        private static string SchedulingRevision(UsageCaptureSettings capture, UsageCaptureGroup group)
        {
            return "min=" + capture.MinimumIntervalSeconds
                + "|pty=" + capture.AllowPtyFallback
                + "|timeout=" + capture.ProbeTimeoutSeconds
                + "|overall=" + capture.MaximumConcurrentProbes
                + "|perHost=" + capture.MaximumConcurrentProbesPerHost
                + "|backoff=" + capture.MaximumBackoffSeconds
                + "|requested=" + group.RequestedIntervalSeconds
                + "|effective=" + group.EffectiveIntervalSeconds;
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="capture">The capture service that owns per-group state and runs the attempts.</param>
        /// <param name="database">Database, to enumerate the enabled captains.</param>
        /// <param name="accounts">Account resolver (host resolution and grouping).</param>
        /// <param name="settings">Server settings.</param>
        /// <param name="harbors">Harbor connections, or null when Harbor delegation is not available.</param>
        /// <param name="logging">Logging module.</param>
        public UsageCaptureScheduler(
            UsageCaptureService capture,
            DatabaseDriver database,
            UsageAccountResolver accounts,
            ArmadaSettings settings,
            HarborConnectionManager? harbors,
            LoggingModule logging)
        {
            _Capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Harbors = harbors;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            // The switch's transition is tracked from the setting the scheduler is built with, so the first
            // tick of a server that starts (or stays) disabled does not invalidate work that never existed;
            // only a genuine enabled-to-disabled transition cancels what is still in flight.
            _CaptureEnabledLastTick = settings.UsageCapture.Enabled;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Rebuild the capture groups from the enabled captains, closing the account memberships of
        /// captains that stopped capturing, and give groups seen for the first time (this run, or after
        /// an invalidation cleared their state) their randomized initial due time. Existing groups keep
        /// their schedule, including a lease a previous process still holds. A rebuild that observes a
        /// configuration change (a captain stopped capturing, a group disappeared, a group's configuration
        /// or scheduling revision changed, or it lost a member) invalidates the capture service first, so
        /// the probes the change orphaned are cancelled and their results are never applied.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        public async Task RebuildAsync(CancellationToken token = default)
        {
            List<Captain> captains = await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false);

            Dictionary<string, UsageCaptureGroup> groups = new Dictionary<string, UsageCaptureGroup>(StringComparer.Ordinal);
            List<string> unresolved = new List<string>();
            List<KeyValuePair<string, string>> active = new List<KeyValuePair<string, string>>();
            int minimum = _Settings.UsageCapture.MinimumIntervalSeconds;

            foreach (Captain captain in captains)
            {
                if (captain == null || !captain.UsageCaptureActive) continue;
                active.Add(new KeyValuePair<string, string>(captain.TenantId ?? String.Empty, captain.Id));

                UsageProbeHost? host = await _Accounts.TryResolveHostAsync(_Database, _Harbors, captain, _Settings, token).ConfigureAwait(false);
                if (host == null)
                {
                    unresolved.Add(captain.Id);
                    _Logging.Debug(_Header + "captain " + captain.Id + " has no resolved credential host; it is reported, never captured on the Admiral's credentials");
                    continue;
                }

                string? fingerprint = captain.UsageCaptureMode == UsageCaptureModeEnum.Custom
                    ? UsageAccountResolver.CommandFingerprint(captain.UsageCaptureCommand)
                    : null;
                string key = UsageAccountResolver.GroupKey(captain.TenantId ?? String.Empty, host.HostId, captain.Runtime, fingerprint);

                if (!groups.TryGetValue(key, out UsageCaptureGroup? group))
                {
                    group = new UsageCaptureGroup
                    {
                        GroupKey = key,
                        TenantId = captain.TenantId ?? String.Empty,
                        HostId = host.HostId,
                        HarborId = host.HarborId,
                        Runtime = captain.Runtime,
                        Mode = captain.UsageCaptureMode,
                        CommandJson = captain.UsageCaptureCommand,
                        CommandFingerprint = fingerprint,
                        ConfigurationRevision = host.ConfigurationRevision,
                        RequestedIntervalSeconds = captain.UsageCaptureIntervalSeconds,
                        EffectiveIntervalSeconds = Math.Max(captain.UsageCaptureIntervalSeconds, minimum),
                        AllowPty = _Settings.UsageCapture.AllowPtyFallback
                    };
                    groups[key] = group;
                }
                else
                {
                    // The group runs at the shortest interval any member requested, floored by the global
                    // minimum; a member that asks for more never slows the group down.
                    if (captain.UsageCaptureIntervalSeconds < group.RequestedIntervalSeconds)
                        group.RequestedIntervalSeconds = captain.UsageCaptureIntervalSeconds;
                    int effective = Math.Max(group.RequestedIntervalSeconds, minimum);
                    if (effective != group.EffectiveIntervalSeconds)
                    {
                        group.EffectiveIntervalSeconds = effective;
                        _Logging.Debug(_Header + "group " + key + " effective interval is now " + effective + " seconds");
                    }
                }

                if (!group.MemberCaptainIds.Contains(captain.Id)) group.MemberCaptainIds.Add(captain.Id);
            }

            // The scheduling revision is recomputed after every member contributed its interval: it captures
            // the server-wide scheduling bounds and the group's own intervals, so a settings or interval
            // change is a configuration change for the work that was scheduled under the old bounds.
            foreach (UsageCaptureGroup group in groups.Values) group.SchedulingRevision = SchedulingRevision(_Settings.UsageCapture, group);

            // Close the memberships of captains that stopped capturing (disabled mode, or a moved
            // captain): a closed membership is attribution history, not a live group.
            bool configurationChanged = false;
            foreach (KeyValuePair<string, string> previously in _ActiveCaptains)
            {
                bool stillActive = false;
                foreach (KeyValuePair<string, string> current in active)
                {
                    if (String.Equals(current.Key, previously.Key, StringComparison.Ordinal) && String.Equals(current.Value, previously.Value, StringComparison.Ordinal))
                    {
                        stillActive = true;
                        break;
                    }
                }

                if (!stillActive)
                {
                    configurationChanged = true;
                    await _Capture.CloseMembershipsAsync(previously.Key, previously.Value, token).ConfigureAwait(false);
                }
            }

            // A group whose configuration revision changed, that lost a member or that disappeared
            // entirely is a configuration change too: its in-flight work belongs to the old configuration.
            foreach (UsageCaptureGroup previous in Groups())
            {
                if (!groups.TryGetValue(previous.GroupKey, out UsageCaptureGroup? rebuilt))
                {
                    configurationChanged = true;
                    continue;
                }

                if (!String.Equals(rebuilt.ConfigurationRevision, previous.ConfigurationRevision, StringComparison.Ordinal))
                {
                    configurationChanged = true;
                    continue;
                }

                // Scheduling and global capture settings are configuration too: a group whose effective
                // interval, floor, deadline, PTY switch, concurrency bound or backoff cap changed cancels
                // the work queued under the old bounds and is rescheduled from scratch with the new ones.
                if (!String.Equals(rebuilt.SchedulingRevision, previous.SchedulingRevision, StringComparison.Ordinal))
                {
                    configurationChanged = true;
                    continue;
                }

                foreach (string member in previous.MemberCaptainIds)
                {
                    if (rebuilt.MemberCaptainIds.Contains(member)) continue;
                    configurationChanged = true;
                    break;
                }
            }

            if (configurationChanged)
            {
                // Cancels the orphaned probes, bumps the settings generation and clears the schedule, so a
                // late result is discarded by the commit path as well as stopped at the host seam.
                _Capture.Invalidate();
            }

            lock (_GroupsLock)
            {
                _Groups = groups;
                _Unresolved = unresolved;
                _ActiveCaptains = active;
                _Rebuilt = true;
            }

            // Groups first seen now get their randomized initial due time; groups the invalidation
            // cleared are randomized again. Existing schedules are untouched.
            foreach (UsageCaptureGroup group in groups.Values) _Capture.ScheduleInitial(group);

            if (groups.Count > 0 || unresolved.Count > 0)
                _Logging.Info(_Header + "rebuilt " + groups.Count + " capture group(s); " + unresolved.Count + " captain(s) without a resolved host");
        }

        /// <summary>
        /// One scheduling tick: rebuild the groups (so configuration changes apply immediately) and start
        /// an attempt for every group whose next eligible time has passed. Already-running groups are
        /// joined rather than duplicated, and a group whose lease is held waits for it. A tick that
        /// observes capture newly disabled cancels the work it still has in flight, so a late result is
        /// never applied. The returned receipts never wait for the attempts themselves.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The receipts of the attempts this tick started or joined.</returns>
        public async Task<List<UsageCaptureAttempt>> TickAsync(CancellationToken token = default)
        {
            List<UsageCaptureAttempt> receipts = new List<UsageCaptureAttempt>();
            if (!_Settings.UsageCapture.Enabled)
            {
                // The switch that just turned off invalidates the capture it still has in flight: the
                // running probes stop and their results are discarded instead of being applied to an
                // account the operator asked not to read.
                if (_CaptureEnabledLastTick)
                {
                    _CaptureEnabledLastTick = false;
                    _Capture.Invalidate();
                    _Logging.Info(_Header + "capture is disabled; in-flight captures stop and their results are discarded");
                }

                return receipts;
            }

            _CaptureEnabledLastTick = true;

            await RebuildAsync(token).ConfigureAwait(false);

            DateTime now = _Capture.NowUtc();

            foreach (UsageCaptureGroup group in Groups())
            {
                if (token.IsCancellationRequested) break;

                DateTime? eligible = _Capture.NextEligibleUtc(group.GroupKey);
                if (!eligible.HasValue) continue; // an Unsupported source waits for a configuration change
                if (eligible.Value > now) continue;

                try
                {
                    UsageCaptureAttempt receipt = await _Capture.TryStartAsync(group, null, token).ConfigureAwait(false);
                    receipts.Add(receipt);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    _Logging.Warn(_Header + "tick could not start the capture of group " + group.GroupKey + ": " + e.Message);
                }
            }

            return receipts;
        }

        #endregion
    }
}