namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// SQL Server implementation of black box storage migration job persistence. The SQL is shared
    /// (<see cref="BlackBoxMigrationMethodsBase"/>); this class supplies the SQL Server dialect.
    /// </summary>
    public class BlackBoxMigrationMethods : BlackBoxMigrationMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQL Server driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxMigrationMethods(SqlServerDatabaseDriver driver) : base(new SqlServerBlackBoxSqlDialect(driver))
        {
        }
    }
}
