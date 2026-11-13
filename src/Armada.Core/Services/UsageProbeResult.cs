namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// One sanitized account usage capture result, produced on the credential host and carried back to
    /// the Admiral over the Harbor link. The host owns the final account key (the HMAC of the
    /// provider-issued account identity with a host-local protected key and the tenant namespace), the
    /// allowlisted status and source, and the redacted, byte-capped diagnostics, regardless of what a
    /// Custom executable claims. Numbers are only what the source reported: a blank or missing field is
    /// null, never zero, and a source that cannot report returns <see cref="UsageSnapshotStatusEnum.Unsupported"/>
    /// with a stable error code. Deserializing an empty, explicitly null or partially populated payload
    /// never throws: it becomes a Failed result with the given error code rather than invented numbers.
    /// </summary>
    public class UsageProbeResult
    {
        #region Public-Members

        /// <summary>
        /// Capture outcome. The Admiral persists this status; it never upgrades an unsupported, failed or
        /// auth-missing answer into a successful read.
        /// </summary>
        public UsageSnapshotStatusEnum Status { get; set; } = UsageSnapshotStatusEnum.Failed;

        /// <summary>
        /// Where the numbers came from. The host sets it; the Admiral does not trust a source a Custom
        /// executable asserts outside the allowlist.
        /// </summary>
        public UsageCaptureSourceEnum Source { get; set; } = UsageCaptureSourceEnum.Custom;

        /// <summary>
        /// Provider the account belongs to (for example anthropic, openai), when known. Descriptive only.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Opaque host-derived account key (the host-local HMAC), or null when no identity could be
        /// derived. A null key never merges unrelated captains into an unknown group.
        /// </summary>
        public string? AccountKey { get; set; } = null;

        /// <summary>
        /// UTC time the observation was made: the provider's own timestamp when the source reported one,
        /// the probe start otherwise.
        /// </summary>
        public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Observed utilization windows, empty when none were reported. Null numbers are unknown, never
        /// zero.
        /// </summary>
        public List<UsageWindow> Windows { get; set; } = new List<UsageWindow>();

        /// <summary>
        /// Allowlisted, redacted parser diagnostics, already capped by the host. Null or empty means no
        /// excerpt is stored.
        /// </summary>
        public string? RawExcerpt { get; set; } = null;

        /// <summary>
        /// Stable error code for an unsupported, auth-missing or failed capture (for example
        /// RuntimeProbeUnavailable, ProbeTimeout, MalformedOutput, CredentialUnavailable,
        /// HostUpgradeRequired), or null on success.
        /// </summary>
        public string? ErrorCode { get; set; } = null;

        /// <summary>
        /// When the provider asked to wait before the next read (its Retry-After), when reported. The
        /// scheduler honors it when it is longer than its own backoff; it never shortens an interval.
        /// </summary>
        public DateTime? RetryAfterUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageProbeResult()
        {
        }

        /// <summary>
        /// An unsupported answer: the runtime has no usable capture source. Never retried until the
        /// runtime, version or configuration changes.
        /// </summary>
        /// <param name="errorCode">Stable error code (for example RuntimeProbeUnavailable, CursorNoLimitInterface).</param>
        /// <param name="reason">Redacted human-readable reason, or null.</param>
        /// <returns>The result.</returns>
        public static UsageProbeResult Unsupported(string errorCode, string? reason = null)
        {
            return new UsageProbeResult
            {
                Status = UsageSnapshotStatusEnum.Unsupported,
                ErrorCode = errorCode ?? "Unsupported",
                RawExcerpt = String.IsNullOrWhiteSpace(reason) ? null : reason
            };
        }

        /// <summary>
        /// A failed attempt: the source was usable but the read did not produce a valid observation (a
        /// timeout, malformed output, or a host that cannot be reached).
        /// </summary>
        /// <param name="errorCode">Stable error code (for example ProbeTimeout, MalformedOutput, HostUnavailable).</param>
        /// <param name="reason">Redacted human-readable reason, or null.</param>
        /// <returns>The result.</returns>
        public static UsageProbeResult Failed(string errorCode, string? reason = null)
        {
            return new UsageProbeResult
            {
                Status = UsageSnapshotStatusEnum.Failed,
                ErrorCode = errorCode ?? "Failed",
                RawExcerpt = String.IsNullOrWhiteSpace(reason) ? null : reason
            };
        }

        /// <summary>
        /// An auth-missing answer: the host has no usable credential for the runtime. Retried slowly,
        /// hourly or on a configuration change, and never grouped into an unknown account.
        /// </summary>
        /// <param name="errorCode">Stable error code (for example CredentialUnavailable).</param>
        /// <param name="reason">Redacted human-readable reason, or null.</param>
        /// <returns>The result.</returns>
        public static UsageProbeResult AuthMissing(string errorCode, string? reason = null)
        {
            return new UsageProbeResult
            {
                Status = UsageSnapshotStatusEnum.AuthMissing,
                ErrorCode = errorCode ?? "CredentialUnavailable",
                RawExcerpt = String.IsNullOrWhiteSpace(reason) ? null : reason
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize to the wire form carried back over the Harbor link.
        /// </summary>
        /// <returns>JSON text.</returns>
        public string Serialize()
        {
            return JsonSerializer.Serialize(this, _Options);
        }

        /// <summary>
        /// Deserialize the wire form. Null, empty, explicitly null or malformed JSON becomes a Failed
        /// result with the given error code rather than an exception, so a truncated or unreadable reply
        /// is a failed attempt, never invented numbers.
        /// </summary>
        /// <param name="json">JSON text, or null.</param>
        /// <param name="errorCode">Error code used when the payload cannot be read.</param>
        /// <returns>The result.</returns>
        public static UsageProbeResult Deserialize(string? json, string errorCode = "MalformedOutput")
        {
            if (String.IsNullOrWhiteSpace(json)) return Failed(errorCode, "no probe result payload");
            try
            {
                UsageProbeResult? result = JsonSerializer.Deserialize<UsageProbeResult>(json, _Options);
                if (result == null) return Failed(errorCode, "probe result deserialized to null");
                result.Windows = result.Windows ?? new List<UsageWindow>();
                return result;
            }
            catch (JsonException)
            {
                return Failed(errorCode, "probe result payload is not valid JSON");
            }
        }

        #endregion
    }
}