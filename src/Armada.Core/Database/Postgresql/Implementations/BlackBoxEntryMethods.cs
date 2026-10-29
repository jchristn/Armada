namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// PostgreSQL implementation of black box entry persistence. The SQL is shared (<see cref="BlackBoxEntryMethodsBase"/>); this class supplies the
    /// PostgreSQL dialect.
    /// </summary>
    public class BlackBoxEntryMethods : BlackBoxEntryMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">PostgreSQL driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxEntryMethods(PostgresqlDatabaseDriver driver) : base(new PostgresqlBlackBoxSqlDialect(driver))
        {
        }
    }
}
