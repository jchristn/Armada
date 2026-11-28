namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;

    /// <summary>
    /// Sqlite recovery state persistence. The SQL is shared (<see cref="RecoveryStateMethodsBase"/>); this class supplies the dialect.
    /// </summary>
    public class RecoveryStateMethods : RecoveryStateMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Sqlite driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public RecoveryStateMethods(SqliteDatabaseDriver driver) : base(new SqliteBlackBoxSqlDialect(driver))
        {
        }
    }
}
