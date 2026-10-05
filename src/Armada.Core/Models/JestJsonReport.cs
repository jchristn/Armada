namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Aggregate counts from a Jest or Vitest JSON report (jest --json, vitest --reporter=json).
    /// </summary>
    public class JestJsonReport
    {
        #region Public-Members

        /// <summary>
        /// Total tests.
        /// </summary>
        [JsonPropertyName("numTotalTests")]
        public int? NumTotalTests { get; set; } = null;

        /// <summary>
        /// Passed tests.
        /// </summary>
        [JsonPropertyName("numPassedTests")]
        public int? NumPassedTests { get; set; } = null;

        /// <summary>
        /// Failed tests.
        /// </summary>
        [JsonPropertyName("numFailedTests")]
        public int? NumFailedTests { get; set; } = null;

        /// <summary>
        /// Pending (skipped) tests.
        /// </summary>
        [JsonPropertyName("numPendingTests")]
        public int? NumPendingTests { get; set; } = null;

        /// <summary>
        /// Todo tests.
        /// </summary>
        [JsonPropertyName("numTodoTests")]
        public int? NumTodoTests { get; set; } = null;

        #endregion
    }
}
