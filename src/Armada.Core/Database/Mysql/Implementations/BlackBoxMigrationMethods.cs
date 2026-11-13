namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// MySQL implementation of black box storage migration job persistence. The SQL is shared
    /// (<see cref="BlackBoxMigrationMethodsBase"/>); this class supplies the MySQL dialect.
    /// </summary>
    public class BlackBoxMigrationMethods : BlackBoxMigrationMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxMigrationMethods(string connectionString) : base(new MysqlBlackBoxSqlDialect(connectionString))
        {
        }
    }
}
