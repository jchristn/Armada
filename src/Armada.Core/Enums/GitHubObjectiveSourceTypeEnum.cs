namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Supported GitHub source record types that can be imported into objectives.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GitHubObjectiveSourceTypeEnum
    {
        /// <summary>
        /// Import from a GitHub issue.
        /// </summary>
        Issue = 0,

        /// <summary>
        /// Import from a GitHub pull request.
        /// </summary>
        PullRequest = 1
    }
}
