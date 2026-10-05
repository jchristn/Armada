namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// High-level classification for a deployment environment.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EnvironmentKindEnum
    {
        /// <summary>
        /// Developer-local or shared development environment.
        /// </summary>
        Development,

        /// <summary>
        /// Test or QA validation environment.
        /// </summary>
        Test,

        /// <summary>
        /// Pre-production staging environment.
        /// </summary>
        Staging,

        /// <summary>
        /// Production environment.
        /// </summary>
        Production,

        /// <summary>
        /// Customer-hosted or dedicated tenant environment.
        /// </summary>
        CustomerHosted,

        /// <summary>
        /// Custom environment classification.
        /// </summary>
        Custom
    }
}
