namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A suggested action of a model alert, applied only when the operator asks.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModelAlertActionEnum
    {
        /// <summary>
        /// Move the persona captain priority entries that select only this profile's captains to the bottom of the list.
        /// </summary>
        [EnumMember(Value = "MoveDown")]
        MoveDown,

        /// <summary>
        /// Remove the persona captain priority entries that select only this profile's captains, and the default captain when it is on this profile.
        /// </summary>
        [EnumMember(Value = "RemoveFromPersona")]
        RemoveFromPersona,

        /// <summary>
        /// Switch this profile to the compact prompt template for this persona.
        /// </summary>
        [EnumMember(Value = "CompactTemplate")]
        CompactTemplate,

        /// <summary>
        /// Start a Quick Evaluate of a captain on this profile, within the evaluation budgets.
        /// </summary>
        [EnumMember(Value = "Reevaluate")]
        Reevaluate,

        /// <summary>
        /// Hide the alert for a number of days (default 7).
        /// </summary>
        [EnumMember(Value = "Ignore")]
        Ignore
    }
}
