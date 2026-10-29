namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// SQL Server implementation of black box link row persistence. The SQL is shared (<see cref="BlackBoxLinkMethodsBase"/>); this class supplies the
    /// SQL Server dialect.
    /// </summary>
    public class BlackBoxLinkMethods : BlackBoxLinkMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQL Server driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxLinkMethods(SqlServerDatabaseDriver driver) : base(new SqlServerBlackBoxSqlDialect(driver))
        {
        }
    }
}
