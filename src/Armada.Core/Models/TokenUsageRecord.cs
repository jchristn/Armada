namespace Armada.Core.Models
{
    using System;
    using Armada.Core;

    /// <summary>
    /// A single persisted token-usage record: the tokens a model consumed for one unit of work
    /// (a mission run, an Ask Armada turn, or a planning turn). Records are written at the point the
    /// work completes and aggregated by the token-usage summary APIs and dashboard charts. Token counts
    /// are normalized across providers: <see cref="InputTokens"/> covers what some providers call prompt
    /// tokens, <see cref="OutputTokens"/> covers completion tokens, and <see cref="CachedTokens"/> covers
    /// cache-read tokens. When a runtime does not report real usage the counts are estimated and
    /// <see cref="Estimated"/> is set so the UI can flag them.
    /// </summary>
    public class TokenUsageRecord
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (tku_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value;
        }

        /// <summary>
        /// Owning tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User the work was performed on behalf of, when known.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Model identifier that consumed the tokens (for example "claude-sonnet-4" or "gpt-5").
        /// Empty or "unknown" when the runtime did not report a model.
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Agent runtime that produced the usage (for example "claudecode", "codex", "mux").
        /// </summary>
        public string? Runtime { get; set; } = null;

        /// <summary>
        /// Work surface that generated the usage: "mission", "chat", or "planning".
        /// </summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>
        /// Identifier of the originating unit of work (mission id, chat/planning session id), when known.
        /// </summary>
        public string? SourceId { get; set; } = null;

        /// <summary>
        /// Vessel the work ran against, when known.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Captain that performed the work, when known.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Harbor the work ran on, or null when it ran on the Admiral host (or the record predates Harbor attribution).
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Provider the tokens were spent against (for example anthropic, openai), when known. Null for
        /// legacy records; missing attribution is never guessed or backfilled.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Opaque host-HMAC account key the work is attributed to, when captured at start. Null for legacy
        /// records, which surface as an Unattributed account series (correlation only).
        /// </summary>
        public string? AccountKey { get; set; } = null;

        /// <summary>
        /// Stable credential host id the account was used on, when known. Descriptive attribution that also
        /// covers the Admiral host, unlike <see cref="HarborId"/> which is null on the Admiral.
        /// </summary>
        public string? HostId { get; set; } = null;

        /// <summary>
        /// Usage account membership (usm_ identifier) that covers this record's completion time, when
        /// attribution was captured at start and carried to completion. Null for legacy records.
        /// </summary>
        public string? MembershipId { get; set; } = null;

        /// <summary>
        /// Cost reported by the provider for this unit of work, in <see cref="CostCurrency"/> units.
        /// Null means unknown, never zero: missing cost stays unknown. Negative values normalize to null.
        /// </summary>
        public double? ReportedCost
        {
            get => _ReportedCost;
            set => _ReportedCost = value.HasValue && value.Value < 0 ? null : value;
        }

        /// <summary>
        /// Currency code of <see cref="ReportedCost"/> (for example USD), uppercased when supplied.
        /// Null when no cost was reported. Currencies are never summed across codes.
        /// </summary>
        public string? CostCurrency
        {
            get => _CostCurrency;
            set => _CostCurrency = String.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
        }

        /// <summary>
        /// Input (prompt) tokens consumed. Clamped to be non-negative.
        /// </summary>
        public long InputTokens
        {
            get => _InputTokens;
            set => _InputTokens = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Output (completion) tokens produced. Clamped to be non-negative.
        /// </summary>
        public long OutputTokens
        {
            get => _OutputTokens;
            set => _OutputTokens = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Cache-read tokens served from a prompt cache. Clamped to be non-negative.
        /// </summary>
        public long CachedTokens
        {
            get => _CachedTokens;
            set => _CachedTokens = value < 0 ? 0 : value;
        }

        /// <summary>
        /// Total tokens for the record. Clamped to be non-negative.
        /// </summary>
        public long TotalTokens
        {
            get => _TotalTokens;
            set => _TotalTokens = value < 0 ? 0 : value;
        }

        /// <summary>
        /// True when the counts are estimated (the runtime did not report real usage) rather than measured.
        /// </summary>
        public bool Estimated { get; set; } = false;

        /// <summary>
        /// Creation timestamp (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.TokenUsageIdPrefix, 24);
        private long _InputTokens = 0;
        private long _OutputTokens = 0;
        private long _CachedTokens = 0;
        private long _TotalTokens = 0;
        private double? _ReportedCost = null;
        private string? _CostCurrency = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TokenUsageRecord()
        {
        }

        #endregion
    }
}
