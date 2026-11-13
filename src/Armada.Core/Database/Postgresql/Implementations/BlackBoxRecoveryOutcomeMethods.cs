namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// Postgresql black box recovery outcome rows (blackbox_recovery_outcomes).
    /// </summary>
    public class BlackBoxRecoveryOutcomeMethods : BlackBoxRecoveryOutcomeMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Driver.</param>
        public BlackBoxRecoveryOutcomeMethods(PostgresqlDatabaseDriver driver) : base(new PostgresqlBlackBoxSqlDialect(driver))
        {
        }
    }
}
