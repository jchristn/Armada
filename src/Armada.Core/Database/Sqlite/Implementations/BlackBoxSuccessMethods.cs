namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// Sqlite black box success flight recorder rows (blackbox_successes).
    /// </summary>
    public class BlackBoxSuccessMethods : BlackBoxSuccessMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Driver.</param>
        public BlackBoxSuccessMethods(SqliteDatabaseDriver driver) : base(new SqliteBlackBoxSqlDialect(driver))
        {
        }
    }
}
