namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of run_blackbox_gc.
    /// </summary>
    public class BlackBoxGcArgs
    {
        /// <summary>
        /// List the candidates without deleting (default false).
        /// </summary>
        public bool? DryRun { get; set; } = null;
    }
}
