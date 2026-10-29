namespace Test.Shared.Suites.Services
{
    using Armada.Core.Enums;

    /// <summary>
    /// Black box storage contract: GoogleCloud target (fake-gcs-server in Docker).
    /// </summary>
    public sealed class GoogleCloudBlackBoxStoreContractSuite : BlackBoxStoreContractSuiteBase
    {
        /// <inheritdoc />
        protected override string SuiteId => "Services.BlackBoxStoreContract.GoogleCloud";

        /// <inheritdoc />
        protected override string DisplayName => "Black box storage contract: GoogleCloud target (fake-gcs-server in Docker)";

        /// <inheritdoc />
        protected override BlackBoxStorageTargetEnum Target => BlackBoxStorageTargetEnum.GoogleCloud;
    }
}
