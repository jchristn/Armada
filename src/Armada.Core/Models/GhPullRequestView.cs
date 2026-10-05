namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Fields read from gh pr view --json url,state.
    /// </summary>
    public class GhPullRequestView
    {
        #region Public-Members

        /// <summary>
        /// Pull request URL.
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; } = null;

        /// <summary>
        /// Pull request state (OPEN, CLOSED, MERGED).
        /// </summary>
        [JsonPropertyName("state")]
        public string? State { get; set; } = null;

        #endregion
    }
}
