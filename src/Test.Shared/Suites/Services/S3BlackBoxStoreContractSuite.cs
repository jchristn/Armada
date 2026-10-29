namespace Test.Shared.Suites.Services
{
    using Armada.Core.Enums;

    /// <summary>
    /// Black box storage contract: S3 target (MinIO in Docker).
    /// </summary>
    public sealed class S3BlackBoxStoreContractSuite : BlackBoxStoreContractSuiteBase
    {
        /// <inheritdoc />
        protected override string SuiteId => "Services.BlackBoxStoreContract.S3";

        /// <inheritdoc />
        protected override string DisplayName => "Black box storage contract: S3 target (MinIO in Docker)";

        /// <inheritdoc />
        protected override BlackBoxStorageTargetEnum Target => BlackBoxStorageTargetEnum.S3;
    }
}
