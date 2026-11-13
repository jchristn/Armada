namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// SQL Server implementation of black box note persistence. The SQL is shared (<see cref="BlackBoxNoteMethodsBase"/>); this class supplies the
    /// SQL Server dialect.
    /// </summary>
    public class BlackBoxNoteMethods : BlackBoxNoteMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQL Server driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxNoteMethods(SqlServerDatabaseDriver driver) : base(new SqlServerBlackBoxSqlDialect(driver))
        {
        }
    }
}
