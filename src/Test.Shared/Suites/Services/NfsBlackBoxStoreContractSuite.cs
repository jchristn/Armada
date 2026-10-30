namespace Test.Shared.Suites.Services
{
    using Armada.Core.Enums;

    /// <summary>
    /// Black box storage contract: Nfs target (in-process OpenNFS server).
    /// </summary>
    public sealed class NfsBlackBoxStoreContractSuite : BlackBoxStoreContractSuiteBase
    {
        /// <inheritdoc />
        protected override string SuiteId => "Services.BlackBoxStoreContract.Nfs";

        /// <inheritdoc />
        protected override string DisplayName => "Black box storage contract: Nfs target (in-process OpenNFS server)";

        /// <inheritdoc />
        protected override BlackBoxStorageTargetEnum Target => BlackBoxStorageTargetEnum.Nfs;
    }
}
