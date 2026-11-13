namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// MySQL implementation of black box entry persistence. The SQL is shared (<see cref="BlackBoxEntryMethodsBase"/>); this class supplies the
    /// MySQL dialect.
    /// </summary>
    public class BlackBoxEntryMethods : BlackBoxEntryMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxEntryMethods(string connectionString) : base(new MysqlBlackBoxSqlDialect(connectionString))
        {
        }
    }
}
