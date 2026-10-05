namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Structured check-run types supported by Armada.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CheckRunTypeEnum
    {
        /// <summary>
        /// Static-analysis or linting check.
        /// </summary>
        Lint = 0,

        /// <summary>
        /// Project build or compile check.
        /// </summary>
        Build = 1,

        /// <summary>
        /// Unit-test execution.
        /// </summary>
        UnitTest = 2,

        /// <summary>
        /// Integration-test execution.
        /// </summary>
        IntegrationTest = 3,

        /// <summary>
        /// End-to-end test execution.
        /// </summary>
        E2ETest = 4,

        /// <summary>
        /// Migration readiness or schema-validation execution.
        /// </summary>
        Migration = 5,

        /// <summary>
        /// Security-scan execution.
        /// </summary>
        SecurityScan = 6,

        /// <summary>
        /// Performance-check execution.
        /// </summary>
        Performance = 7,

        /// <summary>
        /// Package creation or assembly.
        /// </summary>
        Package = 8,

        /// <summary>
        /// Deployment-verification execution.
        /// </summary>
        DeploymentVerification = 9,

        /// <summary>
        /// Rollback-verification execution.
        /// </summary>
        RollbackVerification = 10,

        /// <summary>
        /// Artifact publication.
        /// </summary>
        PublishArtifact = 11,

        /// <summary>
        /// Release versioning step.
        /// </summary>
        ReleaseVersioning = 12,

        /// <summary>
        /// Changelog generation step.
        /// </summary>
        Changelog = 13,

        /// <summary>
        /// Deployment execution.
        /// </summary>
        Deploy = 14,

        /// <summary>
        /// Rollback execution.
        /// </summary>
        Rollback = 15,

        /// <summary>
        /// Smoke-test verification.
        /// </summary>
        SmokeTest = 16,

        /// <summary>
        /// Health-check verification.
        /// </summary>
        HealthCheck = 17,

        /// <summary>
        /// Caller-specified custom command.
        /// </summary>
        Custom = 18
    }
}
