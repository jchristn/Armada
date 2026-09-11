namespace Armada.Core.Harbor
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Base type for every message exchanged over the Harbor link. Messages are JSON-polymorphic: the wire
    /// carries a "type" discriminator that selects the concrete message class on deserialization, so both
    /// sides work against typed objects rather than a JSON DOM. Correlation and trace context ride on the
    /// base so any command/response pair and any delegated work can be tied into one distributed trace.
    /// </summary>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(HarborHandshake), "handshake")]
    [JsonDerivedType(typeof(HarborHandshakeAck), "handshakeAck")]
    [JsonDerivedType(typeof(HarborLaunchRequest), "launch")]
    [JsonDerivedType(typeof(HarborStdinRequest), "stdin")]
    [JsonDerivedType(typeof(HarborKillRequest), "kill")]
    [JsonDerivedType(typeof(HarborGitRequest), "git")]
    [JsonDerivedType(typeof(HarborStarted), "started")]
    [JsonDerivedType(typeof(HarborOutput), "output")]
    [JsonDerivedType(typeof(HarborExited), "exited")]
    [JsonDerivedType(typeof(HarborGitResult), "gitResult")]
    [JsonDerivedType(typeof(HarborHeartbeat), "heartbeat")]
    [JsonDerivedType(typeof(HarborError), "error")]
    [JsonDerivedType(typeof(HarborDeferredLaunchRequest), "deferredLaunch")]
    [JsonDerivedType(typeof(HarborDeferredLaunchAck), "deferredLaunchAck")]
    public abstract class HarborMessage
    {
        #region Public-Members

        /// <summary>
        /// Correlation identifier tying a response to its request, or null for unsolicited events.
        /// </summary>
        public string? CorrelationId { get; set; } = null;

        /// <summary>
        /// W3C trace context (traceparent) so delegated work joins the Admiral's trace. Null when tracing
        /// is not propagated.
        /// </summary>
        public string? TraceParent { get; set; } = null;

        #endregion
    }
}
