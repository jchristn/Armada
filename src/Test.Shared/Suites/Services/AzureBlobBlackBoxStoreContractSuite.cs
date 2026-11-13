namespace Test.Shared.Suites.Services
{
    using Armada.Core.Enums;

    /// <summary>
    /// Black box storage contract: AzureBlob target (Azurite in Docker).
    /// </summary>
    public sealed class AzureBlobBlackBoxStoreContractSuite : BlackBoxStoreContractSuiteBase
    {
        /// <inheritdoc />
        protected override string SuiteId => "Services.BlackBoxStoreContract.AzureBlob";

        /// <inheritdoc />
        protected override string DisplayName => "Black box storage contract: AzureBlob target (Azurite in Docker)";

        /// <inheritdoc />
        protected override BlackBoxStorageTargetEnum Target => BlackBoxStorageTargetEnum.AzureBlob;
    }
}
