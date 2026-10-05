namespace Armada.Proxy.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Tunnel state of a remote Armada instance as seen by the proxy. The portal API reports the lowercase name
    /// ("connected", "stale", "offline").
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RemoteInstanceStateEnum
    {
        /// <summary>
        /// No tunnel session.
        /// </summary>
        [EnumMember(Value = "Offline")]
        Offline,

        /// <summary>
        /// Tunnel session with recent activity.
        /// </summary>
        [EnumMember(Value = "Connected")]
        Connected,

        /// <summary>
        /// Tunnel session without activity for longer than the stale threshold.
        /// </summary>
        [EnumMember(Value = "Stale")]
        Stale
    }
}
