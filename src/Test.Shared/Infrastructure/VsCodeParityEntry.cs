namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// One entry of src/Armada.VSCode/parity.json.
    /// </summary>
    public sealed class VsCodeParityEntry
    {
        /// <summary>Kind: route, tab, api, event, setting, tui, harbor, cli, vscode.</summary>
        public string Kind { get; set; } = "";

        /// <summary>Key of the surface (a dashboard route, a Harbor menu:Command, a CLI command, ...).</summary>
        public string Key { get; set; } = "";

        /// <summary>Status: implemented, planned, not-applicable, extension.</summary>
        public string Status { get; set; } = "";

        /// <summary>The VS Code surface that implements it: command:id, view:id or setting:key.</summary>
        public string Surface { get; set; } = "";

        /// <summary>Milestone (VSCODE_EXTENSION_PLAN.md section 7).</summary>
        public string Milestone { get; set; } = "";

        /// <summary>Notes.</summary>
        public string Notes { get; set; } = "";
    }
}
