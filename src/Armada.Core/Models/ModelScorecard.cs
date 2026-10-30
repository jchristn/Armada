namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A read-only scorecard of one (model profile, persona), optionally one vessel, over a rolling window, computed from
    /// stage outcomes, token usage and timing (docs/MODEL_HETEROGENEITY.md, The scorecard). Revisions are reported as
    /// separate groups unless marked compatible. Nothing here changes routing or captain settings.
    /// </summary>
    public class ModelScorecard
    {
        #region Public-Members

        /// <summary>
        /// Model profile ID (mdl_ prefix).
        /// </summary>
        public string ProfileId { get; set; } = "";

        /// <summary>
        /// Tenant of the profile, or null.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Identity of the profile (runtime, provider, model, served context).
        /// </summary>
        public ModelProfileIdentity Identity
        {
            get => _Identity;
            set => _Identity = value ?? new ModelProfileIdentity();
        }

        /// <summary>
        /// Persona the scorecard is for.
        /// </summary>
        public string Persona { get; set; } = "";

        /// <summary>
        /// Vessel filter, or null for every vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Window length in days that was applied.
        /// </summary>
        public int WindowDays { get; set; } = ModelScorecardRequest.DefaultWindowDays;

        /// <summary>
        /// Window length in missions that was applied.
        /// </summary>
        public int WindowMissions { get; set; } = ModelScorecardRequest.DefaultWindowMissions;

        /// <summary>
        /// Which limit bounded the window: Days (fewer missions than the mission limit in the day window) or Missions.
        /// </summary>
        public string WindowLimitedBy { get; set; } = "Days";

        /// <summary>
        /// Start of the day window.
        /// </summary>
        public DateTime WindowStartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// End of the window (when the scorecard was computed).
        /// </summary>
        public DateTime WindowEndUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Start of the oldest mission counted (its earliest counted attempt), or null when no mission is counted.
        /// </summary>
        public DateTime? OldestCountedUtc { get; set; } = null;

        /// <summary>
        /// Missions counted after exclusions and the mission limit.
        /// </summary>
        public int Missions { get; set; } = 0;

        /// <summary>
        /// Attempts (stage outcomes) counted after exclusions and the mission limit.
        /// </summary>
        public int Attempts { get; set; } = 0;

        /// <summary>
        /// True when more stage outcomes matched the day window than the scorecard reads, so the oldest were not read.
        /// </summary>
        public bool Truncated { get; set; } = false;

        /// <summary>
        /// Attribution rules applied and how many items each removed.
        /// </summary>
        public List<ModelScorecardExclusion> Exclusions
        {
            get => _Exclusions;
            set => _Exclusions = value ?? new List<ModelScorecardExclusion>();
        }

        /// <summary>
        /// One group per revision, or per set of revisions marked compatible, oldest first; outcomes that match no
        /// recorded revision come last.
        /// </summary>
        public List<ModelScorecardGroup> Groups
        {
            get => _Groups;
            set => _Groups = value ?? new List<ModelScorecardGroup>();
        }

        #endregion

        #region Private-Members

        private ModelProfileIdentity _Identity = new ModelProfileIdentity();
        private List<ModelScorecardExclusion> _Exclusions = new List<ModelScorecardExclusion>();
        private List<ModelScorecardGroup> _Groups = new List<ModelScorecardGroup>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelScorecard()
        {
        }

        #endregion
    }
}
