namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// SqlServer black box recovery outcome rows (blackbox_recovery_outcomes).
    /// </summary>
    public class BlackBoxRecoveryOutcomeMethods : BlackBoxRecoveryOutcomeMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Driver.</param>
        public BlackBoxRecoveryOutcomeMethods(SqlServerDatabaseDriver driver) : base(new SqlServerBlackBoxSqlDialect(driver))
        {
        }
    }
}
