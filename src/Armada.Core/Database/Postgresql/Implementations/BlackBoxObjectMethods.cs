namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// PostgreSQL implementation of black box object persistence. The SQL is shared (<see cref="BlackBoxObjectMethodsBase"/>); this class supplies the
    /// PostgreSQL dialect.
    /// </summary>
    public class BlackBoxObjectMethods : BlackBoxObjectMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">PostgreSQL driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxObjectMethods(PostgresqlDatabaseDriver driver) : base(new PostgresqlBlackBoxSqlDialect(driver))
        {
        }
    }
}
