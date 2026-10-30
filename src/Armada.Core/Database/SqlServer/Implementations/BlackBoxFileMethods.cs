namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// SQL Server implementation of black box file row persistence. The SQL is shared (<see cref="BlackBoxFileMethodsBase"/>); this class supplies the
    /// SQL Server dialect.
    /// </summary>
    public class BlackBoxFileMethods : BlackBoxFileMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQL Server driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxFileMethods(SqlServerDatabaseDriver driver) : base(new SqlServerBlackBoxSqlDialect(driver))
        {
        }
    }
}
