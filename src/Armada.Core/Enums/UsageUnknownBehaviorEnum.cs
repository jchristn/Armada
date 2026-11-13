namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>Admission behavior when usage evidence is unknown.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UsageUnknownBehaviorEnum
    {
        /// <summary>Allow qualified candidates after known candidates in the same routing bucket.</summary>
        AllowDeprioritized = 0,
        /// <summary>Wait for authoritative applicable evidence.</summary>
        Wait = 1,
    }
}
