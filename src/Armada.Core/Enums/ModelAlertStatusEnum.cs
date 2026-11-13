namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Whether a model alert condition currently holds.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModelAlertStatusEnum
    {
        /// <summary>
        /// The condition held on the latest evaluation.
        /// </summary>
        [EnumMember(Value = "Open")]
        Open,

        /// <summary>
        /// The condition no longer held on a later evaluation.
        /// </summary>
        [EnumMember(Value = "Resolved")]
        Resolved
    }
}
