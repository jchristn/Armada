namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>Configurable preference among captains allowed by the existing routing and safety gates.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CaptainSelectionStrategyEnum
    {
        /// <summary>Keep existing routing order. Usage gates still apply when enforcement is enabled.</summary>
        FirstAvailable = 0,
        /// <summary>Prefer comfortable remaining allowance, then less occupied capacity within comparable groups.</summary>
        MostRemainingAllowance = 1,
        /// <summary>Consolidate on more utilized accounts that still satisfy reserves; unknown evidence ranks last.</summary>
        MostUsedAllowance = 2
    }
}
