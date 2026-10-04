namespace Armada.Core.Database
{
    /// <summary>
    /// A named SQL parameter value produced by a provider-neutral query builder. Providers bind DateTime values with
    /// their own timestamp conventions and every other value as-is (null binds as DBNull).
    /// </summary>
    internal class QueryParameter
    {
        #region Internal-Members

        /// <summary>
        /// Parameter name including the @ prefix.
        /// </summary>
        internal string Name { get; }

        /// <summary>
        /// Parameter value, or null.
        /// </summary>
        internal object? Value { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Parameter name including the @ prefix.</param>
        /// <param name="value">Parameter value, or null.</param>
        internal QueryParameter(string name, object? value)
        {
            Name = name;
            Value = value;
        }

        #endregion
    }
}
