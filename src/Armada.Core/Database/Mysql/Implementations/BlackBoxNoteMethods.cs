namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// MySQL implementation of black box note persistence. The SQL is shared (<see cref="BlackBoxNoteMethodsBase"/>); this class supplies the
    /// MySQL dialect.
    /// </summary>
    public class BlackBoxNoteMethods : BlackBoxNoteMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxNoteMethods(string connectionString) : base(new MysqlBlackBoxSqlDialect(connectionString))
        {
        }
    }
}
