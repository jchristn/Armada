namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Scope at which a workflow profile applies.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum WorkflowProfileScopeEnum
    {
        /// <summary>
        /// Profile is generally available within the tenant.
        /// </summary>
        Global = 0,

        /// <summary>
        /// Profile applies to a specific fleet.
        /// </summary>
        Fleet = 1,

        /// <summary>
        /// Profile applies to a specific vessel.
        /// </summary>
        Vessel = 2
    }
}
