namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Semantic-version drift between a dependency's current and latest version.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DependencyDriftEnum
    {
        /// <summary>
        /// The dependency is current.
        /// </summary>
        None,

        /// <summary>
        /// A newer patch version is available.
        /// </summary>
        Patch,

        /// <summary>
        /// A newer minor version is available.
        /// </summary>
        Minor,

        /// <summary>
        /// A newer major version is available.
        /// </summary>
        Major
    }
}
