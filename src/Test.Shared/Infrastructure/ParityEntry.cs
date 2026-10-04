namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// One entry of src/Armada.Tui/parity.json.
    /// </summary>
    public sealed class ParityEntry
    {
        /// <summary>Kind: route, tab, api, event, setting.</summary>
        public string Kind { get; set; } = "";

        /// <summary>Dashboard key.</summary>
        public string Key { get; set; } = "";

        /// <summary>Status: implemented, planned, not-applicable, extension.</summary>
        public string Status { get; set; } = "";

        /// <summary>TUI implementation reference.</summary>
        public string Tui { get; set; } = "";

        /// <summary>Workstream.</summary>
        public string Workstream { get; set; } = "";

        /// <summary>Notes.</summary>
        public string Notes { get; set; } = "";
    }
}
