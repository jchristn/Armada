namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using Armada.Core.Database.BlackBox;

    /// <summary>
    /// MySQL implementation of black box link row persistence. The SQL is shared (<see cref="BlackBoxLinkMethodsBase"/>); this class supplies the
    /// MySQL dialect.
    /// </summary>
    public class BlackBoxLinkMethods : BlackBoxLinkMethodsBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when the argument is null.</exception>
        public BlackBoxLinkMethods(string connectionString) : base(new MysqlBlackBoxSqlDialect(connectionString))
        {
        }
    }
}
