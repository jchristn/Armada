namespace Test.Shared.Suites.Services
{
    using Armada.Core.Enums;

    /// <summary>
    /// Black box storage contract: Cifs target (in-process OpenCIFS server).
    /// </summary>
    public sealed class CifsBlackBoxStoreContractSuite : BlackBoxStoreContractSuiteBase
    {
        /// <inheritdoc />
        protected override string SuiteId => "Services.BlackBoxStoreContract.Cifs";

        /// <inheritdoc />
        protected override string DisplayName => "Black box storage contract: Cifs target (in-process OpenCIFS server)";

        /// <inheritdoc />
        protected override BlackBoxStorageTargetEnum Target => BlackBoxStorageTargetEnum.Cifs;
    }
}
