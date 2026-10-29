namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Reference to one stored black box object, enough for any IBlackBoxStore to find its bytes.
    /// </summary>
    public class BlackBoxObjectRef
    {
        /// <summary>
        /// Target holding the bytes.
        /// </summary>
        public BlackBoxStorageTargetEnum Target { get; set; } = BlackBoxStorageTargetEnum.Database;

        /// <summary>
        /// Entry (bbx_ prefix).
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Object row (bbo_ prefix).
        /// </summary>
        public string ObjectId { get; set; } = "";

        /// <summary>
        /// Object name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Full key for external targets (BlackBoxKeyLayout); null for Database.
        /// </summary>
        public string? StorageKey { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxObjectRef()
        {
        }
    }
}
