namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// The model as served: a runtime, provider, model and served context shared by every captain and Harbor that runs it
    /// (docs/MODEL_HETEROGENEITY.md, The model profile). The identity is stable; what changes over time (runtime version,
    /// model digest, sampling) is recorded as immutable <see cref="ModelProfileRevision"/>s. Routing reads only the tier an
    /// administrator set in <see cref="Tiering"/> (RS-8.5); a suggested tier never changes routing on its own.
    /// </summary>
    public class ModelProfile
    {
        #region Public-Members

        /// <summary>
        /// Most revisions a profile read returns in <see cref="Revisions"/> (500, the newest). Older revisions stay stored.
        /// </summary>
        public const int MaxRevisions = 500;

        /// <summary>
        /// Unique identifier (mdl_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Tenant that owns the profile, or null for a tenantless installation.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Stable identity: runtime, provider, model and served context. Never changes after creation.
        /// </summary>
        public ModelProfileIdentity Identity
        {
            get => _Identity;
            set => _Identity = value ?? new ModelProfileIdentity();
        }

        /// <summary>
        /// What the model can use as served by this runtime and provider (not what the model card says). Editable.
        /// </summary>
        public ModelProfileCapabilities Capabilities
        {
            get => _Capabilities;
            set => _Capabilities = value ?? new ModelProfileCapabilities();
        }

        /// <summary>
        /// Explicit rendering overrides set by an administrator; every value is null unless set. Editable.
        /// </summary>
        public ModelProfileRendering Rendering
        {
            get => _Rendering;
            set => _Rendering = value ?? new ModelProfileRendering();
        }

        /// <summary>
        /// Recovery policy overrides set by an administrator (RS-5.5); every value is null unless set, and null means the
        /// global setting applies. Editable.
        /// </summary>
        public ModelProfileRecovery Recovery
        {
            get => _Recovery;
            set => _Recovery = value ?? new ModelProfileRecovery();
        }

        /// <summary>
        /// The tier an administrator set and the tier the outcome data suggests (RS-8.5). Only <see cref="ModelProfileTiering.Tier"/>
        /// is editable.
        /// </summary>
        public ModelProfileTiering Tiering
        {
            get => _Tiering;
            set => _Tiering = value ?? new ModelProfileTiering();
        }

        /// <summary>
        /// The recovery policy in effect for captains linked to this profile: the global settings with
        /// <see cref="Recovery"/> applied. Filled only by single-profile reads; list pages leave it null. Read-only.
        /// </summary>
        public RecoveryPolicy? EffectiveRecovery { get; set; } = null;

        /// <summary>
        /// Timeout overrides set by an administrator (RS-8.8); every value is null unless set, and null means the
        /// measured-speed default or the global setting applies. Editable.
        /// </summary>
        public ModelProfileTimeouts Timeouts
        {
            get => _Timeouts;
            set => _Timeouts = value ?? new ModelProfileTimeouts();
        }

        /// <summary>
        /// The newest speed measurement from a probe run (RS-8.8). Server-owned: a profile update cannot set it.
        /// </summary>
        public ModelProfileSpeed Speed
        {
            get => _Speed;
            set => _Speed = value ?? new ModelProfileSpeed();
        }

        /// <summary>
        /// The timeouts in effect for captains linked to this profile: the global settings with <see cref="Timeouts"/>
        /// and <see cref="Speed"/> applied. Filled only by single-profile reads; list pages leave it null. Read-only.
        /// </summary>
        public TimeoutPolicy? EffectiveTimeouts { get; set; } = null;

        /// <summary>
        /// Free-text administrator notes, or null. Editable.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// The newest <see cref="MaxRevisions"/> revisions, oldest first. Filled only by single-profile reads; list pages leave it empty.
        /// </summary>
        [CollectionBound(MaxRevisions, Reason = "A profile read returns the newest revisions; Measured and the scorecard read every revision")]
        public List<ModelProfileRevision> Revisions
        {
            get => _Revisions;
            set => _Revisions = value ?? new List<ModelProfileRevision>();
        }

        /// <summary>
        /// Counts from the latest complete Full Evaluate report on the profile's revisions, or null when there is none.
        /// Filled only by single-profile reads (from <see cref="Revisions"/>); list pages leave it null. An incomplete
        /// report never fills it.
        /// </summary>
        public ModelProfileMeasured? Measured { get; set; } = null;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.ModelProfileIdPrefix, 24);
        private ModelProfileIdentity _Identity = new ModelProfileIdentity();
        private ModelProfileCapabilities _Capabilities = new ModelProfileCapabilities();
        private ModelProfileRendering _Rendering = new ModelProfileRendering();
        private ModelProfileRecovery _Recovery = new ModelProfileRecovery();
        private ModelProfileTimeouts _Timeouts = new ModelProfileTimeouts();
        private ModelProfileSpeed _Speed = new ModelProfileSpeed();
        private ModelProfileTiering _Tiering = new ModelProfileTiering();
        private List<ModelProfileRevision> _Revisions = new List<ModelProfileRevision>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfile()
        {
        }

        #endregion
    }
}
