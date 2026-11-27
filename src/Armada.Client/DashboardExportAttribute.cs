namespace Armada.Client
{
    using System;

    /// <summary>
    /// Names the web dashboard <c>api/client.ts</c> export a client method mirrors. The TUI parity manifest and the
    /// parity tests map each export to its sub-client method through this attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class DashboardExportAttribute : Attribute
    {
        #region Public-Members

        /// <summary>
        /// The <c>api/client.ts</c> export name, in camelCase (for example <c>listFleets</c>).
        /// </summary>
        public string Name { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Export name.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null or whitespace.</exception>
        public DashboardExportAttribute(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name));
            Name = name;
        }

        #endregion
    }
}
