namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Which dependency check a vessel health dependency scan performs.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DependencyScanModeEnum
    {
        /// <summary>
        /// Find outdated packages (dotnet list package --outdated, npm outdated).
        /// </summary>
        Outdated,

        /// <summary>
        /// Find vulnerable packages (dotnet list package --vulnerable, npm audit).
        /// </summary>
        Vulnerable
    }
}
