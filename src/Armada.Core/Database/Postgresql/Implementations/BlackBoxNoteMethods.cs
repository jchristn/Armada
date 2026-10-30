namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// PostgreSQL implementation of black box note persistence. The SQL is shared (<see cref="BlackBoxNoteMethodsBase"/>); this class supplies the
    /// PostgreSQL dialect.
    /// </summary>
    public class BlackBoxNoteMethods : BlackBoxNoteMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">PostgreSQL driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxNoteMethods(PostgresqlDatabaseDriver driver) : base(new PostgresqlBlackBoxSqlDialect(driver))
        {
        }
    }
}
