namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// The VS Code extension's parity manifest (src/Armada.VSCode/parity.json).
    /// </summary>
    public sealed class VsCodeParityManifest
    {
        /// <summary>Description.</summary>
        public string Description { get; set; } = "";

        /// <summary>Allowed statuses.</summary>
        public List<string> Statuses { get; set; } = new List<string>();

        /// <summary>Entries.</summary>
        public List<VsCodeParityEntry> Entries { get; set; } = new List<VsCodeParityEntry>();
    }
}
