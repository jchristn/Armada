namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Who is asking for a CLI permission: the session the captain runs in (an Ask turn or a mission), resolved by the
    /// Admiral from the captain's scoped MCP token, never from the captain's own arguments.
    /// </summary>
    public class CliPermissionPromptContext
    {
        #region Public-Members

        /// <summary>
        /// Tenant of the thread or mission.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owner of the thread or mission.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Captain.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Captain runtime.
        /// </summary>
        public AgentRuntimeEnum Runtime { get; set; } = AgentRuntimeEnum.ClaudeCode;

        /// <summary>
        /// Mission, or null for an Ask turn.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Voyage of the mission, or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Vessel of the mission, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Ask thread, or null for a mission.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Working directory relative path rules resolve against (the mission dock), or null.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionPromptContext()
        {
        }

        #endregion
    }
}
