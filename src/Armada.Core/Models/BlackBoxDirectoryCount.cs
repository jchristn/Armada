namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A directory and how many conflicted files it holds.
    /// </summary>
    public class BlackBoxDirectoryCount
    {
        /// <summary>
        /// Directory (repository-relative, forward slashes; empty for the root).
        /// </summary>
        public string Directory { get; set; } = "";

        /// <summary>
        /// Depth (1 to 3).
        /// </summary>
        public int Depth { get; set; } = 0;

        /// <summary>
        /// Conflicted files under it.
        /// </summary>
        public int FileCount { get; set; } = 0;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxDirectoryCount()
        {
        }
    }
}
