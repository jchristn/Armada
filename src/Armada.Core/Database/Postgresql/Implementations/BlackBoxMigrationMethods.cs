namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// PostgreSQL implementation of black box storage migration job persistence. The SQL is shared
    /// (<see cref="BlackBoxMigrationMethodsBase"/>); this class supplies the PostgreSQL dialect.
    /// </summary>
    public class BlackBoxMigrationMethods : BlackBoxMigrationMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">PostgreSQL driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxMigrationMethods(PostgresqlDatabaseDriver driver) : base(new PostgresqlBlackBoxSqlDialect(driver))
        {
        }
    }
}
