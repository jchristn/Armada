namespace Armada.Core.Database
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Provider-neutral SQL fragments for one page of a scoped enumeration: the WHERE clause shared by the COUNT and the
    /// page query, the ORDER BY clause with the id tie-break, and the parameters to bind.
    /// </summary>
    internal class ScopedEnumerationSqlQuery
    {
        #region Internal-Members

        /// <summary>
        /// WHERE clause with a leading space, or empty when nothing is filtered.
        /// </summary>
        internal string Where { get; set; } = String.Empty;

        /// <summary>
        /// ORDER BY clause with a leading space (always ends with the id tie-break).
        /// </summary>
        internal string OrderBy { get; set; } = String.Empty;

        /// <summary>
        /// Parameters referenced by <see cref="Where"/>.
        /// </summary>
        internal List<QueryParameter> Parameters { get; } = new List<QueryParameter>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        internal ScopedEnumerationSqlQuery()
        {
        }

        #endregion
    }
}
