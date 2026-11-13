namespace Armada.Server
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Decides where mux runs for endpoint discovery and captain validation. Mux captains run on a Harbor, so the mux
    /// that matters is the one installed next to that Harbor, not one on the Admiral host (which, in a container, has
    /// none). The order is: an explicitly requested Harbor; the Harbor that owns the captain's current dock; a Harbor
    /// the user owns; any Harbor the tenant may use (unless launches are restricted to the user's own Harbors); and,
    /// only when no Harbor is eligible and Harbors are not required, the Admiral host itself.
    /// </summary>
    public class MuxHostResolver
    {
        #region Private-Members

        private readonly string _Header = "[MuxHostResolver] ";
        private readonly ArmadaSettings _Settings;
        private readonly DatabaseDriver _Database;
        private readonly Func<HarborConnectionManager?> _Harbors;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Settings (for <see cref="ArmadaSettings.RequireHarborForLaunch"/>).</param>
        /// <param name="database">Database (to find the Harbor that owns a captain's dock).</param>
        /// <param name="harbors">Current Harbor connection manager, or null when Harbor delegation is off.</param>
        /// <param name="logging">Logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public MuxHostResolver(ArmadaSettings settings, DatabaseDriver database, Func<HarborConnectionManager?> harbors, LoggingModule logging)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Harbors = harbors ?? throw new ArgumentNullException(nameof(harbors));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve where to run mux.
        /// </summary>
        /// <param name="tenantId">Tenant of the caller or captain.</param>
        /// <param name="userId">User of the caller or captain (their own Harbor is preferred).</param>
        /// <param name="harborId">Harbor explicitly requested, or null.</param>
        /// <param name="captain">Captain being validated, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The host, or a host with <see cref="MuxHost.Error"/> set when none is available.</returns>
        public async Task<MuxHost> ResolveAsync(string? tenantId, string? userId, string? harborId, Captain? captain, CancellationToken token = default)
        {
            HarborConnectionManager? harbors = _Harbors();
            bool required = _Settings.RequireHarborForLaunch;

            if (!String.IsNullOrWhiteSpace(harborId))
            {
                if (harbors == null || !harbors.IsConnected(harborId!))
                    return Unavailable("Harbor " + harborId + " is not connected. Start Armada Harbor on that machine and try again.");
                HarborRoutingDecision pinned = await harbors.SelectHarborAsync(tenantId, Request(harborId, false, null), token).ConfigureAwait(false);
                if (!pinned.Success || !String.Equals(pinned.HarborId, harborId, StringComparison.Ordinal))
                    return Unavailable("Harbor " + harborId + " cannot run mux for this request: " + (pinned.Reason ?? "it is not available to this tenant") + ".");
                return Remote(harbors, harborId!, null);
            }

            if (harbors != null && captain != null)
            {
                Dock? dock = await CaptainContextResolver.ReadDockAsync(_Database, captain, token).ConfigureAwait(false);
                if (dock != null && !String.IsNullOrWhiteSpace(dock.HarborId) && harbors.IsConnected(dock.HarborId!))
                    return Remote(harbors, dock.HarborId!, dock.WorktreePath);
            }

            if (harbors != null && harbors.HasConnectedHarbor())
            {
                if (!String.IsNullOrWhiteSpace(userId))
                {
                    HarborRoutingDecision own = await harbors.SelectHarborAsync(tenantId, Request(null, true, userId), token).ConfigureAwait(false);
                    if (own.Success && !String.IsNullOrWhiteSpace(own.HarborId)) return Remote(harbors, own.HarborId!, null);
                }

                if (!required)
                {
                    HarborRoutingDecision any = await harbors.SelectHarborAsync(tenantId, Request(null, false, null), token).ConfigureAwait(false);
                    if (any.Success && !String.IsNullOrWhiteSpace(any.HarborId)) return Remote(harbors, any.HarborId!, null);
                }
            }

            if (required)
            {
                return Unavailable("No Harbor of yours with mux is connected, and this Admiral runs captains only on Harbors (requireHarborForLaunch). "
                    + "Start Armada Harbor on the machine where mux is installed and try again.");
            }

            _Logging.Debug(_Header + "no eligible Harbor; running mux on the Admiral host");
            return new MuxHost { Executor = new LocalHostCommandExecutor() };
        }

        #endregion

        #region Private-Methods

        private static HarborRoutingRequest Request(string? existingHarborId, bool restrictToOwner, string? ownerUserId)
        {
            return new HarborRoutingRequest
            {
                ExistingHarborId = existingHarborId,
                RequestedRuntime = "Mux",
                RestrictToOwner = restrictToOwner,
                OwnerUserId = ownerUserId
            };
        }

        private MuxHost Remote(HarborConnectionManager harbors, string harborId, string? workingDirectory)
        {
            _Logging.Debug(_Header + "running mux on Harbor " + harborId);
            return new MuxHost
            {
                Executor = new RemoteHostCommandExecutor(harbors, harborId),
                WorkingDirectory = workingDirectory,
                HarborId = harborId
            };
        }

        private static MuxHost Unavailable(string message)
        {
            return new MuxHost { Error = message };
        }

        #endregion
    }
}
