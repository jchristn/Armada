namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One difference between two black box entries.
    /// </summary>
    public class BlackBoxDiffField
    {
        #region Public-Members

        /// <summary>
        /// Record section (Subject, Site, Classification, Configuration, Workspace, Conflict, Scope).
        /// </summary>
        public string Section { get; set; } = "";

        /// <summary>
        /// Field or setting name.
        /// </summary>
        public string Field { get; set; } = "";

        /// <summary>
        /// Value on the first entry, or null (clipped to 500 characters).
        /// </summary>
        public string? Value { get; set; } = null;

        /// <summary>
        /// Value on the other entry, or null (clipped to 500 characters).
        /// </summary>
        public string? OtherValue { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxDiffField()
        {
        }

        #endregion
    }
}
