namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// Mysql black box recovery outcome rows (blackbox_recovery_outcomes).
    /// </summary>
    public class BlackBoxRecoveryOutcomeMethods : BlackBoxRecoveryOutcomeMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">Connection string.</param>
        public BlackBoxRecoveryOutcomeMethods(string connectionString) : base(new MysqlBlackBoxSqlDialect(connectionString))
        {
        }
    }
}
