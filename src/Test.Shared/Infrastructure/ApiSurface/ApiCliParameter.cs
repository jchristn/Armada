namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One CLI argument or option.
    /// </summary>
    public sealed class ApiCliParameter
    {
        #region Public-Members

        /// <summary>
        /// argument or option.
        /// </summary>
        public string Kind { get; set; } = "";

        /// <summary>
        /// Argument name, or the long option name without dashes.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Short option alias, or null.
        /// </summary>
        public string? Short { get; set; } = null;

        /// <summary>
        /// Spectre value kind (scalar, flag, vector).
        /// </summary>
        public string ValueKind { get; set; } = "";

        /// <summary>
        /// Value type.
        /// </summary>
        public string Type { get; set; } = "";

        /// <summary>
        /// Whether the parameter is required.
        /// </summary>
        public bool Required { get; set; } = false;

        #endregion
    }
}
