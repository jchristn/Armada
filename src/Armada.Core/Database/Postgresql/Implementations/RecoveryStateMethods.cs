namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;

    /// <summary>
    /// Postgresql recovery state persistence. The SQL is shared (<see cref="RecoveryStateMethodsBase"/>); this class supplies the dialect.
    /// </summary>
    public class RecoveryStateMethods : RecoveryStateMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Postgresql driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public RecoveryStateMethods(PostgresqlDatabaseDriver driver) : base(new PostgresqlBlackBoxSqlDialect(driver))
        {
        }
    }
}
