namespace Armada.Server
{
    using System;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// Inputs for one manual mission status change. REST, MCP, and WebSocket each fill this from their own caller.
    /// </summary>
    public sealed class MissionStatusTransitionRequest
    {
        #region Public-Members

        /// <summary>
        /// Database that holds the mission.
        /// </summary>
        public DatabaseDriver? Database { get; set; }

        /// <summary>
        /// Admiral whose diff and landing callbacks the manual landing path uses.
        /// </summary>
        public IAdmiralService? Admiral { get; set; }

        /// <summary>
        /// Mission service that performs an existing dependent-stage handoff. Null skips that handoff.
        /// </summary>
        public IMissionService? MissionService { get; set; }

        /// <summary>
        /// Caller scope for the mission and dock reads. Null reads without a tenant scope.
        /// </summary>
        public AuthContext? Caller { get; set; }

        /// <summary>
        /// Mission id.
        /// </summary>
        public string? MissionId { get; set; }

        /// <summary>
        /// Requested status name.
        /// </summary>
        public string? StatusText { get; set; }

        /// <summary>
        /// When true, a mission in Review is not moved by this operation. MCP sets this so review leaves through
        /// approve_mission_review or deny_mission_review. REST and WebSocket leave it false.
        /// </summary>
        public bool RefuseReviewExitExceptCancel { get; set; }

        /// <summary>
        /// Git service for the no-change containment check of a Complete without an active dock. Null disables that check.
        /// </summary>
        public IGitService? Git { get; set; }

        /// <summary>
        /// Settings used to locate the vessel repository for the no-change containment check. Null disables that lookup.
        /// </summary>
        public ArmadaSettings? Settings { get; set; }

        /// <summary>
        /// Event, broadcast, and log callbacks. Null uses a direct event write and skips broadcasts.
        /// </summary>
        public MissionStatusTransitionCallbacks? Callbacks { get; set; }

        #endregion
    }
}
