namespace Armada.Tui.Screens.Delivery
{
    using Armada.Core.Models;

    /// <summary>
    /// Live state of an open Run Check modal: the latest resolved-profile preview and readiness, with generation
    /// counters so late responses are ignored. Use on the UI loop.
    /// </summary>
    public class RunCheckState
    {
        #region Public-Members

        /// <summary>
        /// Latest preview, or null.
        /// </summary>
        public WorkflowProfileResolutionPreviewResult? Preview { get; set; } = null;

        /// <summary>
        /// Latest readiness, or null.
        /// </summary>
        public VesselReadinessResult? Readiness { get; set; } = null;

        /// <summary>
        /// Preview request generation.
        /// </summary>
        public int PreviewGeneration { get; set; } = 0;

        /// <summary>
        /// Readiness request generation.
        /// </summary>
        public int ReadinessGeneration { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RunCheckState()
        {
        }

        #endregion
    }
}
