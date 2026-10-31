namespace Armada.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One utilization window inside a usage snapshot: a named quota window (for example five_hour or
    /// seven_day) with the account's observed utilization and reset time. All numbers are nullable:
    /// a null value means the source did not report it, never zero. Windows that apply to a single model
    /// or scope carry <see cref="Model"/> or <see cref="Scope"/> so a model-specific window can never be
    /// mistaken for an account-wide hold. Estimated windows carry the configuration revision and a
    /// coverage explanation they were derived from. Deserialization of an empty object, explicit nulls
    /// or an empty window list must not crash.
    /// </summary>
    public class UsageWindow
    {
        #region Public-Members

        /// <summary>
        /// Window name as reported by the source (for example five_hour, seven_day, or a per-model limit id).
        /// Empty when the source reported no name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name
        {
            get => _Name;
            set => _Name = value ?? String.Empty;
        }

        /// <summary>
        /// Observed utilization as a percentage of the window's limit, when the source reports a percentage.
        /// Null means unknown, not zero.
        /// </summary>
        [JsonPropertyName("usedPercent")]
        public double? UsedPercent { get; set; } = null;

        /// <summary>
        /// Observed usage in the window's unit, when reported. Null means unknown.
        /// </summary>
        [JsonPropertyName("used")]
        public long? Used { get; set; } = null;

        /// <summary>
        /// Window limit in the window's unit, when reported. Null means unknown.
        /// </summary>
        [JsonPropertyName("limit")]
        public long? Limit { get; set; } = null;

        /// <summary>
        /// Unit of <see cref="Used"/> and <see cref="Limit"/> (for example percent, tokens, requests).
        /// Null means the source did not state a unit.
        /// </summary>
        [JsonPropertyName("unit")]
        public string? Unit { get; set; } = null;

        /// <summary>
        /// Duration of the window in minutes, when the source reports it. Null means unknown; the capture
        /// policy never assumes a window is five hours or weekly.
        /// </summary>
        [JsonPropertyName("windowMinutes")]
        public int? WindowMinutes { get; set; } = null;

        /// <summary>
        /// UTC time the window resets, when the source reports it. Null means unknown.
        /// </summary>
        [JsonPropertyName("resetsUtc")]
        public DateTime? ResetsUtc { get; set; } = null;

        /// <summary>
        /// Model identifier when this window applies to a single model only, so it cannot be treated as an
        /// account-wide hold. Null means the window is account-wide (or the source did not scope it).
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; } = null;

        /// <summary>
        /// Scope identifier when this window applies to a narrower scope than the account (for example an
        /// organization or project limit id). Null means unscoped.
        /// </summary>
        [JsonPropertyName("scope")]
        public string? Scope { get; set; } = null;

        /// <summary>
        /// Configuration revision an Estimated window was derived from. Null for observed windows.
        /// </summary>
        [JsonPropertyName("configurationRevision")]
        public string? ConfigurationRevision { get; set; } = null;

        /// <summary>
        /// Coverage explanation for an Estimated window (which budget and counters were used, what is
        /// missing). Null for observed windows.
        /// </summary>
        [JsonPropertyName("coverageNote")]
        public string? CoverageNote { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Name = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public UsageWindow()
        {
        }

        #endregion
    }
}