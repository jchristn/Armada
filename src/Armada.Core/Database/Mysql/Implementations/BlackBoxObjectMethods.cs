namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// MySQL implementation of black box object persistence. The SQL is shared (<see cref="BlackBoxObjectMethodsBase"/>); this class supplies the
    /// MySQL dialect.
    /// </summary>
    public class BlackBoxObjectMethods : BlackBoxObjectMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxObjectMethods(string connectionString) : base(new MysqlBlackBoxSqlDialect(connectionString))
        {
        }
    }
}
