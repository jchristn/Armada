namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Answers "where does this vessel's checkout live, and how do I work there" for every operation that needs the
    /// checkout outside a mission (check runs, Workspace, readiness, health, branch views). The Admiral host is used when
    /// the vessel's working directory exists on it, as before. Otherwise a connected Harbor that serves checkout operations
    /// is asked, as for a mission dock, whether it can serve the vessel, and the first one with a checkout (a folder named
    /// for the vessel in its settings, or one discovered under its root folders) is used: the Harbor that owns the
    /// vessel's most recent docks first, then the vessel's preferred Harbor, then the others by name. Harbors are scoped
    /// like launch routing: the vessel's tenant or a shared Harbor, and with requireHarborForLaunch only the requesting
    /// user's Harbors. A Harbor's answer is remembered for a short time so Workspace browsing does not ask on every click.
    /// Thread-safe.
    /// </summary>
    public class VesselHostResolver
    {
        #region Public-Members

        /// <summary>
        /// The Harbor connections, or null when Harbor delegation is not available.
        /// </summary>
        public HarborConnectionManager? Harbors { get; }

        /// <summary>
        /// How long a Harbor's answer is remembered. Default 30 seconds; 0 disables the cache.
        /// </summary>
        public TimeSpan CacheDuration
        {
            get => _CacheDuration;
            set => _CacheDuration = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[VesselHostResolver] ";
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly Dictionary<string, CachedCheckout> _Cache = new Dictionary<string, CachedCheckout>(StringComparer.Ordinal);
        private readonly object _CacheLock = new object();
        private TimeSpan _CacheDuration = TimeSpan.FromSeconds(30);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Admiral settings (requireHarborForLaunch is read on every call).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="harbors">Harbor connections, or null.</param>
        public VesselHostResolver(DatabaseDriver database, ArmadaSettings settings, LoggingModule logging, HarborConnectionManager? harbors)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            Harbors = harbors;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the vessel's working directory exists on the Admiral host.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <returns>True when it does.</returns>
        public static bool HasAdmiralWorkingDirectory(Vessel? vessel)
        {
            return vessel != null && !String.IsNullOrWhiteSpace(vessel.WorkingDirectory) && Directory.Exists(vessel.WorkingDirectory);
        }

        /// <summary>
        /// The Admiral host with the vessel's working directory (which must exist).
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="logging">Logging module.</param>
        /// <returns>The host.</returns>
        /// <exception cref="DirectoryNotFoundException">Thrown when the working directory does not exist.</exception>
        public static VesselHost Local(Vessel vessel, LoggingModule logging)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            string root = WorkspaceFileEngine.RequireRoot(vessel.WorkingDirectory);
            return new VesselHost(
                vessel,
                null,
                null,
                root,
                HarborRepositorySourceEnum.None,
                System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                new LocalHostCommandExecutor(),
                new GitService(logging),
                new LocalCheckoutFiles(root));
        }

        /// <summary>
        /// The Admiral host when the vessel's working directory exists on it; otherwise the error a resolver without Harbor
        /// connections reports. For callers that run without Harbor delegation (for example the local stdio MCP server).
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="logging">Logging module.</param>
        /// <returns>The host.</returns>
        /// <exception cref="VesselCheckoutUnavailableException">Thrown when the working directory does not exist.</exception>
        public static VesselHost LocalOrThrow(Vessel vessel, LoggingModule logging)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (HasAdmiralWorkingDirectory(vessel)) return Local(vessel, logging);
            VesselHostResolution resolution = NoHarborConnected(vessel, AdmiralNote(vessel), null);
            throw new VesselCheckoutUnavailableException(resolution.ErrorCode!.Value, vessel.Id, vessel.Name, resolution.Message ?? String.Empty, resolution.HarborReasons);
        }

        /// <summary>
        /// Find the host for a vessel's checkout. Never throws for a vessel without one: the resolution says why.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="userId">The user the work is for (with requireHarborForLaunch only that user's Harbors count), or
        /// null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The resolution.</returns>
        public async Task<VesselHostResolution> TryResolveAsync(Vessel vessel, string? userId, CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (HasAdmiralWorkingDirectory(vessel)) return VesselHostResolution.Found(Local(vessel, _Logging));

            string admiralNote = AdmiralNote(vessel);

            HarborConnectionManager? harbors = Harbors;
            if (harbors == null || !harbors.HasConnectedHarbor())
                return NoHarborConnected(vessel, admiralNote, null);

            List<Harbor> candidates = await harbors.ListCandidatesAsync(vessel.TenantId, _Settings.RequireHarborForLaunch, userId, token).ConfigureAwait(false);
            List<string> reasons = new List<string>();
            List<Harbor> usable = new List<Harbor>();
            foreach (Harbor harbor in candidates)
            {
                if (!harbor.Enabled || !harbors.IsConnected(harbor.Id)) continue;
                if (!harbors.HostsCheckouts(harbor.Id))
                {
                    reasons.Add("Harbor " + harbors.Describe(harbor.Id) + ": it predates checkout operations; update it");
                    continue;
                }

                usable.Add(harbor);
            }

            if (usable.Count == 0)
                return NoHarborConnected(vessel, admiralNote, reasons);

            List<Harbor> ordered = await OrderAsync(vessel, usable, token).ConfigureAwait(false);
            HarborDockClient client = new HarborDockClient(harbors);
            foreach (Harbor harbor in ordered)
            {
                token.ThrowIfCancellationRequested();
                string described = harbors.Describe(harbor.Id);
                string? checkout = ReadCache(harbor.Id, vessel);
                HarborRepositorySourceEnum source = HarborRepositorySourceEnum.Mapped;
                if (checkout == null)
                {
                    HarborDockResult? resolved;
                    try
                    {
                        resolved = await client.ResolveAsync(harbor.Id, vessel, token).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException ex)
                    {
                        reasons.Add("Harbor " + described + ": " + ex.Message);
                        continue;
                    }

                    if (resolved == null)
                    {
                        reasons.Add("Harbor " + described + ": it did not answer within " + (client.ResolveTimeoutMs / 1000) + " seconds");
                        continue;
                    }

                    if (!resolved.Success || String.IsNullOrWhiteSpace(resolved.CheckoutPath))
                    {
                        string why = !resolved.Success
                            ? (resolved.Message ?? "it did not say why")
                            : "it has no checkout of vessel " + vessel.Name + ", only its own clone for mission docks";
                        reasons.Add("Harbor " + described + ": " + why);
                        continue;
                    }

                    checkout = resolved.CheckoutPath!;
                    source = resolved.Source;
                    WriteCache(harbor.Id, vessel, checkout, source);
                }
                else
                {
                    source = ReadCachedSource(harbor.Id, vessel);
                }

                _Logging.Debug(_Header + "vessel " + vessel.Name + " is served from checkout " + checkout + " on Harbor " + described);
                return VesselHostResolution.Found(ForHarbor(vessel, harbors, harbor, checkout, source));
            }

            string message = "No connected Harbor has a checkout of " + vessel.Name
                + "; in Harbor > Settings > Repositories set its folder or add a root folder that contains it ("
                + admiralNote + "; " + String.Join("; ", reasons) + ").";
            return VesselHostResolution.Unavailable(VesselCheckoutErrorCodeEnum.NoHarborCheckout, message, reasons);
        }

        /// <summary>
        /// Find the host for a vessel's checkout.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="userId">The user the work is for, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The host.</returns>
        /// <exception cref="VesselCheckoutUnavailableException">Thrown when the vessel has no checkout Armada can use; the
        /// message says what to set.</exception>
        public async Task<VesselHost> ResolveAsync(Vessel vessel, string? userId, CancellationToken token = default)
        {
            VesselHostResolution resolution = await TryResolveAsync(vessel, userId, token).ConfigureAwait(false);
            if (resolution.Host != null) return resolution.Host;
            throw new VesselCheckoutUnavailableException(resolution.ErrorCode!.Value, vessel.Id, vessel.Name, resolution.Message ?? String.Empty, resolution.HarborReasons);
        }

        /// <summary>
        /// Forget the remembered Harbor answers for a vessel (for example after a Harbor refused a path in it).
        /// </summary>
        /// <param name="vesselId">Vessel identifier.</param>
        public void Forget(string vesselId)
        {
            if (String.IsNullOrWhiteSpace(vesselId)) return;
            lock (_CacheLock)
            {
                List<string> keys = _Cache.Keys.Where(k => k.EndsWith("|" + vesselId, StringComparison.Ordinal)).ToList();
                foreach (string key in keys) _Cache.Remove(key);
            }
        }

        #endregion

        #region Private-Methods

        private static string AdmiralNote(Vessel vessel)
        {
            return String.IsNullOrWhiteSpace(vessel.WorkingDirectory)
                ? "it has no working directory on the Admiral host"
                : "its working directory " + vessel.WorkingDirectory + " does not exist on the Admiral host";
        }

        private static VesselHostResolution NoHarborConnected(Vessel vessel, string admiralNote, List<string>? reasons)
        {
            string message = "No connected Harbor has a checkout of " + vessel.Name + ", and " + admiralNote
                + "; start Harbor on the machine that has the checkout and, in Harbor > Settings > Repositories, set its folder or add a root folder that contains it"
                + (reasons != null && reasons.Count > 0 ? " (" + String.Join("; ", reasons) + ")" : "")
                + ".";
            return VesselHostResolution.Unavailable(VesselCheckoutErrorCodeEnum.NoHarborConnected, message, reasons);
        }

        private VesselHost ForHarbor(Vessel vessel, HarborConnectionManager harbors, Harbor harbor, string checkout, HarborRepositorySourceEnum source)
        {
            RemoteHostCommandExecutor commands = new RemoteHostCommandExecutor(harbors, harbor.Id);
            return new VesselHost(
                vessel,
                harbor.Id,
                harbors.Describe(harbor.Id),
                checkout,
                source,
                harbor.OsPlatform,
                commands,
                new GitService(_Logging, commands),
                new HarborCheckoutFiles(harbors, harbor.Id, checkout, vessel));
        }

        private async Task<List<Harbor>> OrderAsync(Vessel vessel, List<Harbor> usable, CancellationToken token)
        {
            // The Harbor that owns the vessel's most recent docks first: that is where the user's work on it happens.
            List<string> preference = new List<string>();
            List<Dock> docks = await _Database.Docks.EnumerateByVesselAsync(vessel.Id, token).ConfigureAwait(false);
            foreach (Dock dock in docks.OrderByDescending(d => d.LastUpdateUtc))
            {
                if (!String.IsNullOrWhiteSpace(dock.HarborId) && !preference.Contains(dock.HarborId!)) preference.Add(dock.HarborId!);
            }

            if (!String.IsNullOrWhiteSpace(vessel.PreferredHarborId) && !preference.Contains(vessel.PreferredHarborId!))
                preference.Add(vessel.PreferredHarborId!);

            List<Harbor> ordered = new List<Harbor>();
            foreach (string harborId in preference)
            {
                Harbor? match = usable.FirstOrDefault(h => String.Equals(h.Id, harborId, StringComparison.Ordinal));
                if (match != null && !ordered.Contains(match)) ordered.Add(match);
            }

            foreach (Harbor harbor in usable.OrderBy(h => h.Name, StringComparer.Ordinal).ThenBy(h => h.Id, StringComparer.Ordinal))
            {
                if (!ordered.Contains(harbor)) ordered.Add(harbor);
            }

            return ordered;
        }

        private static string CacheKey(string harborId, Vessel vessel)
        {
            return harborId + "|" + vessel.Id;
        }

        private string? ReadCache(string harborId, Vessel vessel)
        {
            if (_CacheDuration <= TimeSpan.Zero) return null;
            lock (_CacheLock)
            {
                if (_Cache.TryGetValue(CacheKey(harborId, vessel), out CachedCheckout? entry)
                    && entry.ExpiresUtc > DateTime.UtcNow
                    && String.Equals(entry.RepoUrl, vessel.RepoUrl ?? String.Empty, StringComparison.Ordinal)
                    && String.Equals(entry.VesselName, vessel.Name, StringComparison.Ordinal))
                    return entry.CheckoutPath;
            }

            return null;
        }

        private HarborRepositorySourceEnum ReadCachedSource(string harborId, Vessel vessel)
        {
            lock (_CacheLock)
            {
                if (_Cache.TryGetValue(CacheKey(harborId, vessel), out CachedCheckout? entry)) return entry.Source;
            }

            return HarborRepositorySourceEnum.Mapped;
        }

        private void WriteCache(string harborId, Vessel vessel, string checkout, HarborRepositorySourceEnum source)
        {
            if (_CacheDuration <= TimeSpan.Zero) return;
            lock (_CacheLock)
            {
                _Cache[CacheKey(harborId, vessel)] = new CachedCheckout(checkout, source, vessel.Name, vessel.RepoUrl ?? String.Empty, DateTime.UtcNow.Add(_CacheDuration));
            }
        }

        #endregion

        #region Private-Types

        private sealed class CachedCheckout
        {
            public string CheckoutPath { get; }

            public HarborRepositorySourceEnum Source { get; }

            public string VesselName { get; }

            public string RepoUrl { get; }

            public DateTime ExpiresUtc { get; }

            public CachedCheckout(string checkoutPath, HarborRepositorySourceEnum source, string vesselName, string repoUrl, DateTime expiresUtc)
            {
                CheckoutPath = checkoutPath;
                Source = source;
                VesselName = vesselName;
                RepoUrl = repoUrl;
                ExpiresUtc = expiresUtc;
            }
        }

        #endregion
    }
}
