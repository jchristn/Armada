namespace Armada.Proxy.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core;
    using Armada.Core.Models;

    /// <summary>
    /// Central policy gate for generic dashboard relay requests. The request path is canonicalized once with
    /// <see cref="UrlPathCanonicalizer"/> (percent-decoded, slashes collapsed, trailing slash dropped; ambiguous
    /// encodings and dot segments rejected) and the policy matches route templates against the canonical segments.
    /// On success the request's path is replaced with the canonical path, so the relayed request is exactly the
    /// request the policy evaluated.
    /// </summary>
    public class ProxyRoutePolicyService
    {
        #region Private-Members

        private static readonly string[] _ApiPrefix = new string[] { "api", "v1" };

        /// <summary>
        /// Routes (segments after /api/v1) blocked for every method.
        /// </summary>
        private static readonly List<string[]> _BlockedRoutes = new List<string[]>
        {
            new string[] { "status", "shutdown" },
            new string[] { "server", "stop" },
            new string[] { "status", "factory-reset" },
            new string[] { "server", "reset" },
            new string[] { "restore" }
        };

        /// <summary>
        /// Routes (segments after /api/v1) allowed for POST before the administrative write block applies.
        /// </summary>
        private static readonly List<string[]> _AllowedLoginPostRoutes = new List<string[]>
        {
            new string[] { "authenticate" },
            new string[] { "tenants", "lookup" }
        };

        /// <summary>
        /// First segment after /api/v1 of administrative resources that may only be read through the relay.
        /// </summary>
        private static readonly HashSet<string> _ReadOnlyAdministrativeResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "settings",
            "tenants",
            "users",
            "credentials"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Evaluate whether a relayed dashboard request is allowed. When allowed, <see cref="RemoteTunnelHttpRelayRequest.Path"/>
        /// is rewritten to the canonical path that was evaluated.
        /// </summary>
        /// <param name="request">Relay request.</param>
        /// <param name="statusCode">200 when allowed; 400 for a malformed or non-API path; 403 for a blocked route.</param>
        /// <param name="message">Denial reason, or null when allowed.</param>
        /// <returns>True when the request may be relayed.</returns>
        public bool TryAuthorize(RemoteTunnelHttpRelayRequest? request, out int statusCode, out string? message)
        {
            statusCode = 200;
            message = null;

            if (request == null)
            {
                statusCode = 400;
                message = "Relay request payload is required.";
                return false;
            }

            string method = (request.Method ?? String.Empty).Trim().ToUpperInvariant();
            UrlPathCanonicalizationResult canonical = UrlPathCanonicalizer.Canonicalize(request.Path);
            if (!canonical.Success)
            {
                statusCode = 400;
                message = "Relay request path is not in canonical form (" + canonical.Rejection + ").";
                return false;
            }

            if (!canonical.StartsWithSegments(_ApiPrefix) || canonical.Segments.Count <= _ApiPrefix.Length)
            {
                statusCode = 400;
                message = "Only Armada API routes under /api/v1/* can be relayed.";
                return false;
            }

            List<string> route = canonical.Segments.GetRange(_ApiPrefix.Length, canonical.Segments.Count - _ApiPrefix.Length);

            foreach (string[] blocked in _BlockedRoutes)
            {
                if (RouteEquals(route, blocked))
                {
                    statusCode = 403;
                    message = "This Armada route is blocked by proxy policy for remote access.";
                    return false;
                }
            }

            bool isRead = String.Equals(method, "GET", StringComparison.Ordinal) || String.Equals(method, "HEAD", StringComparison.Ordinal);
            bool isLoginPost = false;
            if (String.Equals(method, "POST", StringComparison.Ordinal))
            {
                foreach (string[] allowed in _AllowedLoginPostRoutes)
                {
                    if (RouteEquals(route, allowed))
                    {
                        isLoginPost = true;
                        break;
                    }
                }
            }

            if (!isRead && !isLoginPost && _ReadOnlyAdministrativeResources.Contains(route[0]))
            {
                statusCode = 403;
                message = "This administrative Armada route is blocked by proxy policy for remote access.";
                return false;
            }

            request.Path = canonical.Path;
            return true;
        }

        #endregion

        #region Private-Methods

        private static bool RouteEquals(List<string> route, string[] template)
        {
            if (route.Count != template.Length) return false;
            for (int i = 0; i < template.Length; i++)
            {
                if (!String.Equals(route[i], template[i], StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }

        #endregion
    }
}
