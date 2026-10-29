namespace Test.Shared.Suites.Database
{
    using Armada.Core.Enums;
    using Test.Shared.Suites.Services;

    /// <summary>
    /// Black box storage contract: Database target (every provider; migrates to Disk).
    /// </summary>
    public sealed class DatabaseBlackBoxStoreContractSuite : BlackBoxStoreContractSuiteBase
    {
        /// <inheritdoc />
        protected override string SuiteId => "Database.BlackBoxStoreContract";

        /// <inheritdoc />
        protected override string DisplayName => "Black box storage contract: Database target (every provider; migrates to Disk)";

        /// <inheritdoc />
        protected override BlackBoxStorageTargetEnum Target => BlackBoxStorageTargetEnum.Database;

        /// <inheritdoc />
        protected override BlackBoxStorageTargetEnum MigrationTarget => BlackBoxStorageTargetEnum.Disk;
    }
}
