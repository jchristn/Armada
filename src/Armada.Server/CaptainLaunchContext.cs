namespace Armada.Server
{
    using System;
    using Armada.Core.Harbor;
    using Armada.Core.Models;

    /// <summary>
    /// What a captain launch needs routed: the captain, whose launch it is (tenant and user, for Harbor ownership), the
    /// vessel it works on (preferred Harbor and required capabilities), and the Harbor its dock is pinned to, if any.
    /// Used by <see cref="CaptainLaunchRouter"/> for missions and for every interactive launch (chat, Ask turns,
    /// planning, refinement, vessel context).
    /// </summary>
    public class CaptainLaunchContext
    {
        #region Public-Members

        /// <summary>
        /// Captain to launch.
        /// </summary>
        public Captain Captain
        {
            get => _Captain;
            set => _Captain = value ?? throw new ArgumentNullException(nameof(Captain));
        }

        /// <summary>
        /// Tenant the launch belongs to (Harbors are chosen within it), or null.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// User the launch belongs to. With requireHarborForLaunch only that user's Harbors are eligible.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Vessel the launch works on, or null (its preferred Harbor and required capabilities apply).
        /// </summary>
        public Vessel? Vessel { get; set; } = null;

        /// <summary>
        /// Harbor the launch's dock is pinned to, or null. A pinned launch goes to that Harbor or nowhere else.
        /// </summary>
        public string? PinnedHarborId { get; set; } = null;

        /// <summary>
        /// Whether a launch routed to a Harbor may run in a Harbor-owned scratch directory when its working directory is
        /// empty or does not exist on the Harbor host. Default true (interactive launches); a launch that needs its
        /// repository checkout (vessel context) sets false so a missing directory fails instead.
        /// </summary>
        public bool AllowScratchWorkingDirectory { get; set; } = true;

        /// <summary>
        /// What the launch is for. Sent to a Harbor that runs it, for its job list and logs.
        /// </summary>
        public HarborJobKindEnum Kind { get; set; } = HarborJobKindEnum.Other;

        /// <summary>
        /// The mission the launch runs, for a mission. Sent to a Harbor that runs it.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Short name of the launch for logs and messages (for example "chat", "planning").
        /// </summary>
        public string Purpose
        {
            get => _Purpose;
            set => _Purpose = String.IsNullOrWhiteSpace(value) ? "launch" : value;
        }

        #endregion

        #region Private-Members

        private Captain _Captain = new Captain();
        private string _Purpose = "launch";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CaptainLaunchContext()
        {
        }

        /// <summary>
        /// Instantiate for a captain.
        /// </summary>
        /// <param name="captain">Captain to launch.</param>
        /// <param name="purpose">Short name of the launch for logs and messages.</param>
        public CaptainLaunchContext(Captain captain, string purpose)
        {
            Captain = captain;
            Purpose = purpose;
        }

        #endregion
    }
}
