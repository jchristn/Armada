namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// The parity manifest document.
    /// </summary>
    public sealed class ParityManifest
    {
        /// <summary>Description.</summary>
        public string Description { get; set; } = "";

        /// <summary>Allowed statuses.</summary>
        public List<string> Statuses { get; set; } = new List<string>();

        /// <summary>Entries.</summary>
        public List<ParityEntry> Entries { get; set; } = new List<ParityEntry>();
    }
}
