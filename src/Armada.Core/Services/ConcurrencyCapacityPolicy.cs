namespace Armada.Core.Services
{
    using System.Net;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>Capacity accounting shared by admission and overrun observation.</summary>
    internal static class ConcurrencyCapacityPolicy
    {
        /// <summary>Scope of an endpoint key whose host is not a loopback address: every caller reaches one server.</summary>
        internal const string SharedScope = "any";

        /// <summary>Scope of a loopback endpoint called from the Admiral's host (or from a host not yet known).</summary>
        internal const string AdmiralScope = "admiral";

        internal static Dictionary<AgentRuntimeEnum, int> RuntimeLimits(IEnumerable<AgentSettings>? agents)
        {
            Dictionary<AgentRuntimeEnum, int> limits = new Dictionary<AgentRuntimeEnum, int>();
            foreach (AgentSettings agent in agents ?? Enumerable.Empty<AgentSettings>())
                if (agent.MaxConcurrent > 0 && (!limits.TryGetValue(agent.Runtime, out int existing) || agent.MaxConcurrent < existing))
                    limits[agent.Runtime] = agent.MaxConcurrent;
            return limits;
        }

        /// <summary>
        /// The endpoint's concurrency key as called from the Admiral's host (see <see cref="EndpointKey(ModelEndpoint, string?)"/>).
        /// </summary>
        internal static string EndpointKey(ModelEndpoint endpoint) => EndpointKey(endpoint, null);

        /// <summary>
        /// The concurrency key of an endpoint called from a host (RS-8.9): scope, normalized URL and model. Endpoints with
        /// one key share one MaxConcurrentMissions budget. The URL is normalized (scheme and host lower case, default port
        /// and trailing slashes dropped, a trailing /v1 dropped, every loopback name read as localhost), so
        /// http://127.0.0.1:11434/ and http://localhost:11434/v1 are one server. A loopback URL names a different server
        /// on every host, so its scope is the Harbor (or the Admiral when the Harbor is null); any other URL has the shared
        /// scope. Two models on one server are two keys. An endpoint without a URL is keyed by its id.
        /// </summary>
        internal static string EndpointKey(ModelEndpoint endpoint, string? harborId)
        {
            string raw = (endpoint.BaseUrl ?? String.Empty).Trim();
            if (raw.Length == 0) return "endpoint:" + endpoint.Id;
            string url = NormalizeUrl(raw, out bool loopback);
            string scope = !loopback ? SharedScope : String.IsNullOrWhiteSpace(harborId) ? AdmiralScope : "harbor:" + harborId!.Trim();
            string model = (endpoint.Model ?? String.Empty).Trim().ToLowerInvariant();
            return scope + "|" + url + "|" + model;
        }

        /// <summary>
        /// The endpoint's key without its scope (normalized URL and model): endpoints with one server key are aliases of
        /// one server and model, whatever host calls them.
        /// </summary>
        internal static string ServerKey(ModelEndpoint endpoint)
        {
            string key = EndpointKey(endpoint, null);
            int bar = key.IndexOf('|');
            return bar < 0 ? key : key.Substring(bar + 1);
        }

        /// <summary>
        /// Normalize an endpoint base URL for concurrency accounting; a value that is not an absolute URL is lower cased
        /// with trailing slashes dropped.
        /// </summary>
        internal static string NormalizeUrl(string baseUrl, out bool loopback)
        {
            loopback = false;
            string trimmed = (baseUrl ?? String.Empty).Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri) || String.IsNullOrEmpty(uri.Host))
                return trimmed.TrimEnd('/').ToLowerInvariant();

            string host = uri.Host.ToLowerInvariant();
            if (IsLoopback(host))
            {
                loopback = true;
                host = "localhost";
            }
            else if (uri.HostNameType == UriHostNameType.IPv6)
            {
                host = "[" + host.Trim('[', ']') + "]";
            }

            string scheme = uri.Scheme.ToLowerInvariant();
            string port = uri.IsDefaultPort ? String.Empty : ":" + uri.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string path = uri.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            if (path.EndsWith("/v1", StringComparison.Ordinal)) path = path.Substring(0, path.Length - 3).TrimEnd('/');
            return scheme + "://" + host + port + path;
        }

        // Preserve the staged migration's Working-with-no-count fallback used by admission.
        internal static int Occupancy(Captain captain) => captain.ActiveMissionCount > 0 ? captain.ActiveMissionCount
            : captain.State == CaptainStateEnum.Working ? 1 : 0;

        /// <summary>
        /// The host of each mission slot a captain occupies: the Harbors of its leased or provisioning docks, in order,
        /// then the first of those (or the Admiral, null) for slots without a dock.
        /// </summary>
        internal static List<string?> SlotHosts(Captain captain, Dictionary<string, List<string?>>? hostsByCaptain)
        {
            int occupancy = Occupancy(captain);
            List<string?> hosts = new List<string?>();
            List<string?>? known = null;
            if (hostsByCaptain != null) hostsByCaptain.TryGetValue(captain.Id, out known);
            for (int i = 0; i < occupancy; i++)
                hosts.Add(known == null || known.Count == 0 ? null : i < known.Count ? known[i] : known[0]);
            return hosts;
        }

        /// <summary>
        /// Whether any busy captain drives a loopback endpoint with a concurrency cap, so admission needs dock hosts to
        /// count it per Harbor.
        /// </summary>
        internal static bool NeedsDockHosts(IEnumerable<Captain> captains, Func<Captain, ModelEndpoint?> endpointOf)
        {
            foreach (Captain captain in captains)
            {
                if (Occupancy(captain) == 0) continue;
                ModelEndpoint? endpoint = endpointOf(captain);
                if (endpoint?.InferenceOptions?.MaxConcurrentMissions == null || String.IsNullOrWhiteSpace(endpoint.BaseUrl)) continue;
                NormalizeUrl(endpoint.BaseUrl!, out bool loopback);
                if (loopback) return true;
            }

            return false;
        }

        /// <summary>
        /// Harbor ids of the leased or provisioning docks of each captain (null for a dock on the Admiral's host).
        /// </summary>
        internal static Dictionary<string, List<string?>> HostsByCaptain(IEnumerable<Dock> docks)
        {
            Dictionary<string, List<string?>> hosts = new Dictionary<string, List<string?>>(StringComparer.Ordinal);
            foreach (Dock dock in docks)
            {
                if (dock == null || !dock.Active || String.IsNullOrEmpty(dock.CaptainId)) continue;
                if (dock.State != DockStateEnum.Leased && dock.State != DockStateEnum.Provisioning) continue;
                if (!hosts.TryGetValue(dock.CaptainId!, out List<string?>? list))
                {
                    list = new List<string?>();
                    hosts[dock.CaptainId!] = list;
                }

                list.Add(String.IsNullOrWhiteSpace(dock.HarborId) ? null : dock.HarborId);
            }

            return hosts;
        }

        private static bool IsLoopback(string host)
        {
            string bare = host.Trim('[', ']');
            if (String.Equals(bare, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (bare.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (IPAddress.TryParse(bare, out IPAddress? address)) return IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
            return false;
        }
    }
}
