namespace Armada.Proxy.Services
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Text;
    using Armada.Core;
    using Armada.Proxy.Enums;

    /// <summary>
    /// Reads the Armada.Proxy session token from a request and removes proxy credentials from anything relayed to the
    /// Admiral. The proxy session and the Admiral session are separate credentials:
    /// <list type="bullet">
    /// <item><c>X-Armada-Proxy-Session</c> is the dedicated proxy header and is accepted on every proxy route.</item>
    /// <item><c>Authorization: Bearer</c> is a proxy credential only on <c>/proxy-api/*</c>; on relayed routes it is
    /// the Admiral's and passes through untouched.</item>
    /// <item>On <c>/ws</c>, a Sec-WebSocket-Protocol entry <c>armada-proxy-session.&lt;base64url(token)&gt;</c> carries
    /// the proxy token; <c>armada-token.*</c> entries and the query string are the Admiral's.</item>
    /// <item>The <c>armada_proxy_session</c> cookie (browsers) is accepted everywhere, last.</item>
    /// </list>
    /// The first location present wins; a present but invalid credential does not fall back to a later one.
    /// </summary>
    public static class ProxySessionCredentials
    {
        #region Private-Members

        private const string BearerScheme = "Bearer ";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the proxy session token presented on a request for the given route scope.
        /// </summary>
        /// <param name="headers">Request headers.</param>
        /// <param name="scope">Kind of route being served.</param>
        /// <returns>The candidate token, or null when none was presented.</returns>
        public static string? ResolveToken(NameValueCollection? headers, ProxyCredentialScopeEnum scope)
        {
            if (headers == null) return null;

            string? headerToken = headers.Get(Constants.ProxySessionTokenHeader);
            if (!String.IsNullOrWhiteSpace(headerToken))
            {
                return headerToken.Trim();
            }

            if (scope == ProxyCredentialScopeEnum.ProxyApi)
            {
                string? bearer = GetBearerToken(headers.Get("Authorization"));
                if (bearer != null) return bearer;
            }

            if (scope == ProxyCredentialScopeEnum.WebSocket)
            {
                string? protocolToken = GetProtocolToken(headers.Get("Sec-WebSocket-Protocol"));
                if (protocolToken != null) return protocolToken;
            }

            return GetCookieToken(headers.Get("Cookie"));
        }

        /// <summary>
        /// Remove proxy credentials (<c>X-Armada-Proxy-Session</c> and the <c>Cookie</c> header, which on the proxy
        /// origin carries the proxy session cookie) from headers about to be relayed to the Admiral. Admiral
        /// credentials (<c>Authorization</c>, <c>X-Token</c>, <c>X-Api-Key</c>) are kept.
        /// </summary>
        /// <param name="headers">Headers to relay; modified in place.</param>
        public static void StripFromRelayHeaders(Dictionary<string, string>? headers)
        {
            if (headers == null) return;

            List<string> remove = new List<string>();
            foreach (string key in headers.Keys)
            {
                if (String.Equals(key, Constants.ProxySessionTokenHeader, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(key, "Cookie", StringComparison.OrdinalIgnoreCase))
                {
                    remove.Add(key);
                }
            }

            foreach (string key in remove)
            {
                headers.Remove(key);
            }
        }

        /// <summary>
        /// Remove <c>armada-proxy-session.*</c> entries from a Sec-WebSocket-Protocol value before it is relayed to the
        /// Admiral; every other entry (including <c>armada-token.*</c>) is kept in order.
        /// </summary>
        /// <param name="headerValue">Raw Sec-WebSocket-Protocol header value, or null.</param>
        /// <returns>The remaining entries joined with ", ", or null when none remain.</returns>
        public static string? StripFromSubprotocols(string? headerValue)
        {
            if (String.IsNullOrWhiteSpace(headerValue)) return null;

            List<string> kept = new List<string>();
            foreach (string rawEntry in headerValue.Split(','))
            {
                string entry = rawEntry.Trim();
                if (entry.Length == 0) continue;
                if (entry.StartsWith(Constants.ProxySessionProtocolPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                kept.Add(entry);
            }

            return kept.Count == 0 ? null : String.Join(", ", kept);
        }

        /// <summary>
        /// Encode a proxy session token as a Sec-WebSocket-Protocol entry (<c>armada-proxy-session.&lt;base64url&gt;</c>).
        /// </summary>
        /// <param name="token">Proxy session token.</param>
        /// <returns>The protocol entry.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="token"/> is null or blank.</exception>
        public static string EncodeProtocolEntry(string token)
        {
            if (String.IsNullOrWhiteSpace(token)) throw new ArgumentNullException(nameof(token));
            string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(token.Trim()));
            return Constants.ProxySessionProtocolPrefix + base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        #endregion

        #region Private-Methods

        private static string? GetBearerToken(string? authorization)
        {
            if (String.IsNullOrWhiteSpace(authorization)) return null;
            string value = authorization.Trim();
            if (!value.StartsWith(BearerScheme, StringComparison.OrdinalIgnoreCase)) return null;
            string token = value.Substring(BearerScheme.Length).Trim();
            return token.Length == 0 ? null : token;
        }

        private static string? GetProtocolToken(string? headerValue)
        {
            if (String.IsNullOrWhiteSpace(headerValue)) return null;

            foreach (string rawEntry in headerValue.Split(','))
            {
                string entry = rawEntry.Trim();
                if (!entry.StartsWith(Constants.ProxySessionProtocolPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                string? decoded = DecodeBase64Url(entry.Substring(Constants.ProxySessionProtocolPrefix.Length));
                if (!String.IsNullOrWhiteSpace(decoded)) return decoded.Trim();
            }

            return null;
        }

        private static string? GetCookieToken(string? cookieHeader)
        {
            if (String.IsNullOrWhiteSpace(cookieHeader)) return null;

            foreach (string part in cookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!part.StartsWith(Constants.ProxySessionCookieName + "=", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string candidate = part.Substring(Constants.ProxySessionCookieName.Length + 1).Trim();
                return String.IsNullOrWhiteSpace(candidate) ? null : candidate;
            }

            return null;
        }

        private static string? DecodeBase64Url(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string base64 = value.Replace('-', '+').Replace('_', '/');
            int padding = base64.Length % 4;
            if (padding == 2) base64 += "==";
            else if (padding == 3) base64 += "=";
            else if (padding == 1) return null;

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        #endregion
    }
}
