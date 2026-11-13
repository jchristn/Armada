namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// Sqlite black box recovery outcome rows (blackbox_recovery_outcomes).
    /// </summary>
    public class BlackBoxRecoveryOutcomeMethods : BlackBoxRecoveryOutcomeMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Driver.</param>
        public BlackBoxRecoveryOutcomeMethods(SqliteDatabaseDriver driver) : base(new SqliteBlackBoxSqlDialect(driver))
        {
        }
    }
}
