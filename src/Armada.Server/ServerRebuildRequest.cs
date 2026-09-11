namespace Armada.Server
{
    /// <summary>
    /// Request body for POST /api/v1/server/rebuild. Every field is optional; sensible defaults resolve the
    /// Armada source vessel and build its default-branch HEAD. See docs/SERVER_REBUILD.md.
    /// </summary>
    public class ServerRebuildRequest
    {
        #region Public-Members

        /// <summary>
        /// Explicit source directory to build from. When null or empty, the source is resolved from
        /// <c>ArmadaSettings.SelfVesselId</c> to the vessel's working directory (falling back to its local
        /// clone). Provide this to override the configured self vessel.
        /// </summary>
        public string? SourcePath { get; set; } = null;

        /// <summary>
        /// Git ref (branch, tag, or commit sha) to build. Null or empty builds the source's current HEAD.
        /// </summary>
        public string? Ref { get; set; } = null;

        /// <summary>
        /// When true, skip rebuilding the React dashboard and publish only the server. Defaults to false.
        /// </summary>
        public bool SkipDashboard { get; set; } = false;

        /// <summary>
        /// Seconds a supervising Harbor waits for the new slot to report healthy before rolling back to the
        /// previous slot. Clamped to [10, 600]; defaults to 120. Only used when a Harbor performs the
        /// health-gated cutover (Phase 2); ignored by the built-in baton restart.
        /// </summary>
        public int RollbackTimeoutSeconds
        {
            get => _RollbackTimeoutSeconds;
            set => _RollbackTimeoutSeconds = value < 10 ? 10 : (value > 600 ? 600 : value);
        }

        #endregion

        #region Private-Members

        private int _RollbackTimeoutSeconds = 120;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ServerRebuildRequest()
        {
        }

        #endregion
    }
}
