namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One task entry in a plan_tasks call.
    /// </summary>
    public class PlanTaskArguments : IToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The task id; an absent id is reported by the plan validator.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; } = null;

        /// <summary>
        /// The task title; an absent title is reported by the plan validator.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; } = null;

        /// <summary>
        /// Ids of tasks that must complete first, or null for none.
        /// </summary>
        [JsonPropertyName("dependsOn")]
        public List<string?>? DependsOn { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validates that required parameters are present.
        /// </summary>
        /// <returns>Null when valid; otherwise the problem message.</returns>
        public string? Validate()
        {
            return null;
        }

        #endregion
    }
}
