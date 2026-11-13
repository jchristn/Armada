namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// SQLite implementation of black box object persistence. The SQL is shared (<see cref="BlackBoxObjectMethodsBase"/>); this class supplies the
    /// SQLite dialect.
    /// </summary>
    public class BlackBoxObjectMethods : BlackBoxObjectMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQLite driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxObjectMethods(SqliteDatabaseDriver driver) : base(new SqliteBlackBoxSqlDialect(driver))
        {
        }
    }
}
