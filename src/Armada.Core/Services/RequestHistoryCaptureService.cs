namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Builds sanitized request-history records from raw request and response primitives.
    /// </summary>
    public class RequestHistoryCaptureService
    {
        #region Private-Members

        private static readonly HashSet<string> _SensitiveHeaderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization",
            "Cookie",
            "Set-Cookie",
            "X-Api-Key",
            "X-Token",
            "X-Access-Key",
            "X-Secret-Key",
            "Sec-WebSocket-Protocol",
            "Proxy-Authorization",
            Constants.ProxySessionTokenHeader
        };

        private static readonly HashSet<string> _SensitiveBodyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "password",
            "passwordSha256",
            "bearerToken",
            "gitHubToken",
            "gitHubTokenOverride",
            "token",
            "apiKey",
            "authorization",
            "cookie",
            "set-cookie",
            "secret",
            "session",
            "sessionToken"
        };

        private static readonly Regex _LooseJsonPair = new Regex("\"(?<k>[A-Za-z0-9_\\-]+)\"\\s*:\\s*\"(?:[^\"\\\\]|\\\\.)*\"", RegexOptions.Compiled);
        private static readonly Regex _LooseAssignment = new Regex("(?<k>[A-Za-z0-9_\\-]+)=(?<v>[^&\\s\"]*)", RegexOptions.Compiled);

        private readonly ArmadaSettings _Settings;
        private readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Armada settings.</param>
        public RequestHistoryCaptureService(ArmadaSettings settings)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether request-history capture is enabled for the supplied path.
        /// </summary>
        /// <param name="path">Raw request path.</param>
        /// <returns>True if the request should be captured.</returns>
        public bool ShouldCapture(string? path)
        {
            if (!_Settings.RequestHistoryEnabled) return false;
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)) return false;

            foreach (string excluded in _Settings.RequestHistoryExcludeRoutes)
            {
                if (string.IsNullOrWhiteSpace(excluded)) continue;
                if (path.Equals(excluded, StringComparison.OrdinalIgnoreCase)) return false;
                if (path.StartsWith(excluded.EndsWith("/") ? excluded : excluded + "/", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Build a sanitized request-history record.
        /// </summary>
        /// <param name="auth">Authenticated context if available.</param>
        /// <param name="input">Raw request and response primitives.</param>
        /// <returns>Sanitized request-history record.</returns>
        public RequestHistoryRecord BuildRecord(AuthContext? auth, RequestHistoryCaptureInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            bool requestTruncated = false;
            bool responseTruncated = false;

            string? requestBody = SanitizeBody(input.RequestBodyText, input.RequestContentType, out requestTruncated);
            string? responseBody = SanitizeBody(input.ResponseBodyText, input.ResponseContentType, out responseTruncated);

            Dictionary<string, string?>? requestHeaders = _Settings.RequestHistoryCaptureRequestHeaders
                ? SanitizeHeaders(input.RequestHeaders)
                : null;
            Dictionary<string, string?>? responseHeaders = _Settings.RequestHistoryCaptureResponseHeaders
                ? SanitizeHeaders(input.ResponseHeaders)
                : null;

            RequestHistoryEntry entry = new RequestHistoryEntry
            {
                TenantId = auth?.TenantId,
                UserId = auth?.UserId,
                CredentialId = auth?.CredentialId,
                PrincipalDisplay = auth?.PrincipalDisplay ?? auth?.UserId,
                AuthMethod = auth?.AuthMethod,
                Method = input.Method,
                Route = input.Route,
                RouteTemplate = input.RouteTemplate,
                QueryString = RedactQueryString(input.QueryString),
                StatusCode = input.StatusCode,
                DurationMs = Math.Round(input.DurationMs, 2),
                RequestSizeBytes = input.RequestSizeBytes > 0 ? input.RequestSizeBytes : GetUtf8ByteCount(input.RequestBodyText),
                ResponseSizeBytes = input.ResponseSizeBytes > 0 ? input.ResponseSizeBytes : GetUtf8ByteCount(input.ResponseBodyText),
                RequestContentType = input.RequestContentType,
                ResponseContentType = input.ResponseContentType,
                IsSuccess = input.StatusCode >= 200 && input.StatusCode < 400,
                ClientIp = input.ClientIp,
                CorrelationId = input.CorrelationId,
                CreatedUtc = DateTime.UtcNow
            };

            RequestHistoryDetail detail = new RequestHistoryDetail
            {
                RequestHistoryId = entry.Id,
                PathParamsJson = null,
                QueryParamsJson = SerializeOrNull(SanitizeParameters(ParseQueryString(input.QueryString))),
                RequestHeadersJson = SerializeOrNull(requestHeaders),
                ResponseHeadersJson = SerializeOrNull(responseHeaders),
                RequestBodyText = requestBody,
                ResponseBodyText = responseBody,
                RequestBodyTruncated = requestTruncated,
                ResponseBodyTruncated = responseTruncated
            };

            return new RequestHistoryRecord
            {
                Entry = entry,
                Detail = detail
            };
        }

        #endregion

        #region Private-Methods

        private Dictionary<string, string?> SanitizeHeaders(Dictionary<string, string?> headers)
        {
            Dictionary<string, string?> results = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string?> header in headers)
            {
                if (_SensitiveHeaderNames.Contains(header.Key) || IsSensitiveKey(header.Key))
                {
                    results[header.Key] = "[REDACTED]";
                }
                else
                {
                    results[header.Key] = header.Value;
                }
            }
            return results;
        }

        private Dictionary<string, string?> SanitizeParameters(Dictionary<string, string?> values)
        {
            Dictionary<string, string?> results = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string?> value in values)
            {
                results[value.Key] = IsSensitiveKey(value.Key) ? "[REDACTED]" : value.Value;
            }
            return results;
        }

        private string? SanitizeBody(string? body, string? contentType, out bool truncated)
        {
            truncated = false;

            if (string.IsNullOrEmpty(body)) return body;
            if (_Settings.RequestHistoryMaxBodyBytes <= 0)
            {
                truncated = true;
                return null;
            }

            string sanitized = body;

            if (IsJsonContentType(contentType))
            {
                sanitized = RedactJsonBody(body);
            }
            else if (IsFormContentType(contentType))
            {
                sanitized = RedactFormBody(body);
            }
            else if (!IsTextContentType(contentType))
            {
                return "[binary content omitted]";
            }
            else
            {
                sanitized = RedactLooseText(body);
            }

            return TruncateToMaxBytes(sanitized, _Settings.RequestHistoryMaxBodyBytes, out truncated);
        }

        private static bool IsJsonContentType(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType)) return false;
            return contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("+json", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFormContentType(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType)) return false;
            return contentType.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsTextContentType(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType)) return true;
            return contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("application/problem+json", StringComparison.OrdinalIgnoreCase)
                || IsJsonContentType(contentType)
                || IsFormContentType(contentType);
        }

        private string RedactJsonBody(string body)
        {
            try
            {
                JsonNode? node = JsonNode.Parse(body);
                if (node == null) return body;
                RedactJsonNode(node);
                return node.ToJsonString(_JsonOptions);
            }
            catch
            {
                return RedactLooseText(body);
            }
        }

        private void RedactJsonNode(JsonNode node)
        {
            if (node is JsonObject obj)
            {
                foreach (KeyValuePair<string, JsonNode?> kvp in obj.ToList())
                {
                    if (IsSensitiveKey(kvp.Key))
                    {
                        obj[kvp.Key] = "[REDACTED]";
                    }
                    else if (kvp.Value is JsonValue stringValue && stringValue.TryGetValue<string>(out string? text) && text != null)
                    {
                        // Secret-shaped values (ghp_..., sk-..., AKIA...) are scrubbed even under keys not known to hold secrets.
                        string scrubbed = SecretRedactor.Redact(text);
                        if (!String.Equals(scrubbed, text, StringComparison.Ordinal)) obj[kvp.Key] = scrubbed;
                    }
                    else if (kvp.Value != null)
                    {
                        RedactJsonNode(kvp.Value);
                    }
                }
            }
            else if (node is JsonArray array)
            {
                foreach (JsonNode? child in array)
                {
                    if (child != null) RedactJsonNode(child);
                }
            }
        }

        private string RedactFormBody(string body)
        {
            Dictionary<string, string?> values = ParseQueryString(body);
            foreach (string key in values.Keys.ToList())
            {
                if (IsSensitiveKey(key))
                {
                    values[key] = "[REDACTED]";
                }
            }

            return SerializeOrNull(values) ?? string.Empty;
        }

        private string RedactLooseText(string body)
        {
            // Replace the values (not just mark them) of "key": "value" pairs and key=value pairs whose key names a
            // secret, then scrub secret-shaped values by the shared definition so a token that appears without a known
            // key name is still redacted the same way the runtime-log formatter redacts it.
            string sanitized = _LooseJsonPair.Replace(body, m => IsSensitiveKey(m.Groups["k"].Value)
                ? "\"" + m.Groups["k"].Value + "\":\"[REDACTED]\""
                : m.Value);
            sanitized = _LooseAssignment.Replace(sanitized, m => IsSensitiveKey(m.Groups["k"].Value)
                ? m.Groups["k"].Value + "=[REDACTED]"
                : m.Value);
            sanitized = SecretRedactor.Redact(sanitized);
            return sanitized;
        }

        /// <summary>
        /// Whether a JSON property, form field, header, or query parameter name holds a secret: the explicit list, or a
        /// name (ignoring case, '-' and '_') that contains "password" or "secret" or ends with token, apikey,
        /// accesskey, secretkey, privatekey, or encryptionkey. Token counts ("tokens", "maxTokens") are not matched.
        /// </summary>
        /// <param name="key">Name.</param>
        /// <returns>True when the value must be redacted.</returns>
        public static bool IsSensitiveKey(string? key)
        {
            if (String.IsNullOrEmpty(key)) return false;
            if (_SensitiveBodyKeys.Contains(key)) return true;
            string normalized = key.Replace("-", String.Empty).Replace("_", String.Empty).ToLowerInvariant();
            if (normalized.Contains("password") || normalized.Contains("secret")) return true;
            return normalized.EndsWith("token", StringComparison.Ordinal)
                || normalized.EndsWith("apikey", StringComparison.Ordinal)
                || normalized.EndsWith("accesskey", StringComparison.Ordinal)
                || normalized.EndsWith("secretkey", StringComparison.Ordinal)
                || normalized.EndsWith("privatekey", StringComparison.Ordinal)
                || normalized.EndsWith("encryptionkey", StringComparison.Ordinal);
        }

        private static string? RedactQueryString(string? queryString)
        {
            if (String.IsNullOrEmpty(queryString)) return queryString;
            bool leading = queryString.StartsWith("?", StringComparison.Ordinal);
            string working = leading ? queryString.Substring(1) : queryString;
            string[] pairs = working.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                int eq = pairs[i].IndexOf('=');
                string name = eq >= 0 ? pairs[i].Substring(0, eq) : pairs[i];
                if (eq >= 0 && IsSensitiveKey(Uri.UnescapeDataString(name))) pairs[i] = name + "=[REDACTED]";
            }

            string rebuilt = String.Join("&", pairs);
            return SecretRedactor.Redact(leading ? "?" + rebuilt : rebuilt);
        }

        private static Dictionary<string, string?> ParseQueryString(string? queryString)
        {
            Dictionary<string, string?> result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(queryString)) return result;

            string working = queryString.StartsWith("?") ? queryString.Substring(1) : queryString;
            string[] pairs = working.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (string pair in pairs)
            {
                int eq = pair.IndexOf('=');
                if (eq >= 0)
                {
                    string key = Uri.UnescapeDataString(pair.Substring(0, eq));
                    string value = Uri.UnescapeDataString(pair.Substring(eq + 1));
                    result[key] = value;
                }
                else
                {
                    result[Uri.UnescapeDataString(pair)] = null;
                }
            }

            return result;
        }

        private string? SerializeOrNull<T>(T? value)
        {
            if (value == null) return null;

            if (value is Dictionary<string, string?> stringDict && stringDict.Count == 0)
                return null;

            return JsonSerializer.Serialize(value, _JsonOptions);
        }

        private static string? TruncateToMaxBytes(string? value, int maxBytes, out bool truncated)
        {
            truncated = false;
            if (string.IsNullOrEmpty(value)) return value;
            if (maxBytes <= 0)
            {
                truncated = true;
                return null;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length <= maxBytes) return value;

            truncated = true;
            byte[] truncatedBytes = bytes.Take(maxBytes).ToArray();
            return Encoding.UTF8.GetString(truncatedBytes) + Environment.NewLine + "...[truncated]";
        }

        private static long GetUtf8ByteCount(string? value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            return Encoding.UTF8.GetByteCount(value);
        }

        #endregion
    }
}
