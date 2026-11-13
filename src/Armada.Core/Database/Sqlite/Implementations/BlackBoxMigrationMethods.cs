namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// SQLite implementation of black box storage migration job persistence. The SQL is shared
    /// (<see cref="BlackBoxMigrationMethodsBase"/>); this class supplies the SQLite dialect.
    /// </summary>
    public class BlackBoxMigrationMethods : BlackBoxMigrationMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQLite driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxMigrationMethods(SqliteDatabaseDriver driver) : base(new SqliteBlackBoxSqlDialect(driver))
        {
        }
    }
}
