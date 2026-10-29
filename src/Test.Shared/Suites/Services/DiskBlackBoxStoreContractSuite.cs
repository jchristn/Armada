namespace Test.Shared.Suites.Services
{
    using Armada.Core.Enums;

    /// <summary>
    /// Black box storage contract: Disk target.
    /// </summary>
    public sealed class DiskBlackBoxStoreContractSuite : BlackBoxStoreContractSuiteBase
    {
        /// <inheritdoc />
        protected override string SuiteId => "Services.BlackBoxStoreContract.Disk";

        /// <inheritdoc />
        protected override string DisplayName => "Black box storage contract: Disk target";

        /// <inheritdoc />
        protected override BlackBoxStorageTargetEnum Target => BlackBoxStorageTargetEnum.Disk;
    }
}
