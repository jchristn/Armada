namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// SQLite implementation of black box file row persistence. The SQL is shared (<see cref="BlackBoxFileMethodsBase"/>); this class supplies the
    /// SQLite dialect.
    /// </summary>
    public class BlackBoxFileMethods : BlackBoxFileMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQLite driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxFileMethods(SqliteDatabaseDriver driver) : base(new SqliteBlackBoxSqlDialect(driver))
        {
        }
    }
}
