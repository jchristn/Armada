namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// SQLite implementation of black box note persistence. The SQL is shared (<see cref="BlackBoxNoteMethodsBase"/>); this class supplies the
    /// SQLite dialect.
    /// </summary>
    public class BlackBoxNoteMethods : BlackBoxNoteMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQLite driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxNoteMethods(SqliteDatabaseDriver driver) : base(new SqliteBlackBoxSqlDialect(driver))
        {
        }
    }
}
