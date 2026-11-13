namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;
    using System.Runtime.Serialization;
    /// <summary>Branch relationship requested by a mission; execution support is tracked separately.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MissionBranchStrategyEnum
    {
        /// <summary>Create an independent mission branch.</summary>
        [EnumMember(Value = "New")] New,
        /// <summary>Continue the parent mission branch.</summary>
        [EnumMember(Value = "ContinueParent")] ContinueParent,
        /// <summary>Fork a branch from the parent mission.</summary>
        [EnumMember(Value = "ForkFromParent")] ForkFromParent
    }
}
