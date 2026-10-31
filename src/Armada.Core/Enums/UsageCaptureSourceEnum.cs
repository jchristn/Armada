namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where one usage snapshot's numbers came from. The source is recorded with every snapshot so the
    /// dashboard can label confidence (Official, Undocumented, Estimated) and so quarantine policy can
    /// accept only authoritative fresh sources. Auto capture uses only verified read-only sources that
    /// spend no inference quota; PTY scraping requires a separate explicit opt-in and Custom runs an
    /// administrator-chosen local executable.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UsageCaptureSourceEnum
    {
        /// <summary>
        /// A documented or known vendor endpoint read with the host's existing credentials.
        /// </summary>
        [EnumMember(Value = "OAuthEndpoint")]
        OAuthEndpoint,

        /// <summary>
        /// Bounded recent local session records emitted by the runtime itself.
        /// </summary>
        [EnumMember(Value = "SessionLog")]
        SessionLog,

        /// <summary>
        /// Opt-in interactive screen capture; never an automatic fallback.
        /// </summary>
        [EnumMember(Value = "Pty")]
        Pty,

        /// <summary>
        /// Rate-limit response headers harvested from existing inference calls; passive, never scheduled.
        /// </summary>
        [EnumMember(Value = "Headers")]
        Headers,

        /// <summary>
        /// Armada-derived estimate from explicitly configured tier budgets; never authoritative.
        /// </summary>
        [EnumMember(Value = "Derived")]
        Derived,

        /// <summary>
        /// An administrator-configured local executable contract.
        /// </summary>
        [EnumMember(Value = "Custom")]
        Custom
    }
}