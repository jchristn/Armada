namespace Armada.Runtimes.Tools
{
    /// <summary>
    /// Classifies whether a tool mutates the workspace, which determines whether it must acquire the
    /// single-writer workspace lease before running. Used to allow read-only tools to run in parallel
    /// across concurrent jobs while serializing mutating tools.
    /// </summary>
    public enum ToolMutationKind
    {
        /// <summary>
        /// The tool only reads state (files, metadata, search, network fetches) and never modifies the
        /// workspace. Read-only tools do not require the write lease and may run concurrently.
        /// </summary>
        ReadOnly = 0,

        /// <summary>
        /// The tool may modify the workspace (write/edit/delete files, create directories, or run
        /// arbitrary processes). Mutating tools must hold the workspace write lease while executing.
        /// This is the safe default for tools of unknown classification (for example external MCP tools).
        /// </summary>
        Mutating = 1
    }
}
