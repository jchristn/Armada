namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// Postgresql black box success flight recorder rows (blackbox_successes).
    /// </summary>
    public class BlackBoxSuccessMethods : BlackBoxSuccessMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Driver.</param>
        public BlackBoxSuccessMethods(PostgresqlDatabaseDriver driver) : base(new PostgresqlBlackBoxSqlDialect(driver))
        {
        }
    }
}
