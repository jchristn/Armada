namespace Armada.Server.WebSocket
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Credentials presented on a /ws upgrade request. Browsers cannot set the Authorization header on a WebSocket, so
    /// besides the REST headers (Authorization: Bearer, X-Token, X-Api-Key) a token may be passed as the <c>token</c>
    /// query parameter or inside the Sec-WebSocket-Protocol header as <c>armada-token.&lt;base64url(token)&gt;</c>
    /// (a raw protocol entry that is not <c>armada</c> is also tried as a token).
    /// </summary>
    public class WebSocketCredentials
    {
        #region Public-Members

        /// <summary>
        /// Prefix of a Sec-WebSocket-Protocol entry that carries a base64url-encoded token.
        /// </summary>
        public const string ProtocolTokenPrefix = "armada-token.";

        /// <summary>
        /// Authorization header value, if any.
        /// </summary>
        public string? AuthorizationHeader { get; set; } = null;

        /// <summary>
        /// X-Token header value, if any.
        /// </summary>
        public string? TokenHeader { get; set; } = null;

        /// <summary>
        /// X-Api-Key header value, if any.
        /// </summary>
        public string? ApiKeyHeader { get; set; } = null;

        /// <summary>
        /// Token query parameter value, if any.
        /// </summary>
        public string? QueryToken { get; set; } = null;

        /// <summary>
        /// Candidate tokens parsed from the Sec-WebSocket-Protocol header, in order.
        /// </summary>
        public List<string> ProtocolTokens
        {
            get => _ProtocolTokens;
            set => _ProtocolTokens = value ?? new List<string>();
        }

        /// <summary>
        /// Whether any credential was presented.
        /// </summary>
        public bool HasAny =>
            !String.IsNullOrWhiteSpace(AuthorizationHeader)
            || !String.IsNullOrWhiteSpace(TokenHeader)
            || !String.IsNullOrWhiteSpace(ApiKeyHeader)
            || !String.IsNullOrWhiteSpace(QueryToken)
            || _ProtocolTokens.Count > 0;

        #endregion

        #region Private-Members

        private List<string> _ProtocolTokens = new List<string>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse candidate tokens from a Sec-WebSocket-Protocol header value. Entries prefixed with
        /// <see cref="ProtocolTokenPrefix"/> are base64url-decoded; the plain <c>armada</c> marker is skipped; any other
        /// entry is returned as-is.
        /// </summary>
        /// <param name="headerValue">Header value (comma separated), or null.</param>
        /// <returns>Candidate tokens; empty when none.</returns>
        public static List<string> ParseProtocolTokens(string? headerValue)
        {
            List<string> tokens = new List<string>();
            if (String.IsNullOrWhiteSpace(headerValue)) return tokens;

            foreach (string rawEntry in headerValue.Split(','))
            {
                string entry = rawEntry.Trim();
                if (entry.Length == 0) continue;
                if (String.Equals(entry, "armada", StringComparison.OrdinalIgnoreCase)) continue;

                if (entry.StartsWith(ProtocolTokenPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string? decoded = DecodeBase64Url(entry.Substring(ProtocolTokenPrefix.Length));
                    if (!String.IsNullOrEmpty(decoded)) tokens.Add(decoded);
                    continue;
                }

                tokens.Add(entry);
            }

            return tokens;
        }

        /// <summary>
        /// Every raw token candidate (query token first, then protocol tokens), without duplicates.
        /// </summary>
        /// <returns>Candidate tokens.</returns>
        public List<string> TokenCandidates()
        {
            List<string> candidates = new List<string>();
            if (!String.IsNullOrWhiteSpace(QueryToken))
            {
                // Some transports hand the raw (still percent-encoded) query value through; try the decoded form first.
                string normalized = NormalizeQueryToken(QueryToken);
                if (normalized.Contains('%'))
                {
                    string decoded = NormalizeQueryToken(Uri.UnescapeDataString(normalized));
                    if (!candidates.Contains(decoded)) candidates.Add(decoded);
                }

                if (!candidates.Contains(normalized)) candidates.Add(normalized);
            }

            foreach (string token in _ProtocolTokens)
            {
                if (!String.IsNullOrWhiteSpace(token) && !candidates.Contains(token)) candidates.Add(token);
            }

            return candidates;
        }

        #endregion

        #region Private-Methods

        private static string NormalizeQueryToken(string value)
        {
            // A base64 session token sent without percent-encoding has its '+' decoded to a space by form decoding.
            return value.Trim().Replace(' ', '+');
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
