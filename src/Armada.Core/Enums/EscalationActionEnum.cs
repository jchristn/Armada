namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Actions to take when an escalation rule triggers.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EscalationActionEnum
    {
        /// <summary>
        /// Log to the standard log file.
        /// </summary>
        Log,

        /// <summary>
        /// Send an HTTP webhook POST.
        /// </summary>
        Webhook
    }
}
