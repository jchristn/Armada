namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// What kind of friction a captain reported. The category decides where a promoted papercut
    /// lands: platform categories belong to Armada itself, repository categories belong to the vessel.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PapercutCategoryEnum
    {
        /// <summary>
        /// The mission brief contradicts itself, or names a capability the captain does not have.
        /// </summary>
        BriefContradiction,

        /// <summary>
        /// A tool or command failed, hung, or returned an unusable result.
        /// </summary>
        ToolFailure,

        /// <summary>
        /// Documentation is missing, wrong, or out of date.
        /// </summary>
        MissingDoc,

        /// <summary>
        /// A link or cross-reference points at nothing.
        /// </summary>
        BrokenLink,

        /// <summary>
        /// The repository itself made the work harder: dead code, broken script, misleading name.
        /// </summary>
        RepoFriction,

        /// <summary>
        /// A test failed or passed inconsistently for reasons unrelated to the mission.
        /// </summary>
        TestFlake,

        /// <summary>
        /// The dock or environment was incomplete: missing sibling repository, missing toolchain.
        /// </summary>
        EnvSetup,

        /// <summary>
        /// Armada itself misbehaved.
        /// </summary>
        PlatformBug,

        /// <summary>
        /// Friction that fits no other category.
        /// </summary>
        Other
    }
}
