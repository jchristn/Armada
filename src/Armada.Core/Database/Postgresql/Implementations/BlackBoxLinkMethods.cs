namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// PostgreSQL implementation of black box link row persistence. The SQL is shared (<see cref="BlackBoxLinkMethodsBase"/>); this class supplies the
    /// PostgreSQL dialect.
    /// </summary>
    public class BlackBoxLinkMethods : BlackBoxLinkMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">PostgreSQL driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxLinkMethods(PostgresqlDatabaseDriver driver) : base(new PostgresqlBlackBoxSqlDialect(driver))
        {
        }
    }
}
