namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// SqlServer black box success flight recorder rows (blackbox_successes).
    /// </summary>
    public class BlackBoxSuccessMethods : BlackBoxSuccessMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Driver.</param>
        public BlackBoxSuccessMethods(SqlServerDatabaseDriver driver) : base(new SqlServerBlackBoxSqlDialect(driver))
        {
        }
    }
}
