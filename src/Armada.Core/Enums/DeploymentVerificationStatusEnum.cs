namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Post-deploy verification state for a deployment.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeploymentVerificationStatusEnum
    {
        /// <summary>
        /// Verification has not been attempted yet.
        /// </summary>
        NotRun = 0,

        /// <summary>
        /// Verification is currently running.
        /// </summary>
        Running = 1,

        /// <summary>
        /// All executed verification steps passed.
        /// </summary>
        Passed = 2,

        /// <summary>
        /// At least one executed verification step failed.
        /// </summary>
        Failed = 3,

        /// <summary>
        /// Some verification evidence exists, but not every configured step ran.
        /// </summary>
        Partial = 4,

        /// <summary>
        /// No verification steps were configured for this deployment.
        /// </summary>
        Skipped = 5
    }
}
