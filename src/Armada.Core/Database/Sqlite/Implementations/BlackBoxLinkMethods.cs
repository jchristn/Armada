namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// SQLite implementation of black box link row persistence. The SQL is shared (<see cref="BlackBoxLinkMethodsBase"/>); this class supplies the
    /// SQLite dialect.
    /// </summary>
    public class BlackBoxLinkMethods : BlackBoxLinkMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQLite driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxLinkMethods(SqliteDatabaseDriver driver) : base(new SqliteBlackBoxSqlDialect(driver))
        {
        }
    }
}
