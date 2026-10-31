namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The model as served: a runtime, provider, model and served context shared by every captain and Harbor that runs it
    /// (docs/MODEL_HETEROGENEITY.md, The model profile). The identity is stable; what changes over time (runtime version,
    /// model digest, sampling) is recorded as immutable <see cref="ModelProfileRevision"/>s. A profile is advisory: it does
    /// not set a captain's tier or change routing or dispatch.
    /// </summary>
    public class ModelProfile
    {
        #region Public-Members

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
        /// Free-text administrator notes, or null. Editable.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Revisions, oldest first. Filled only by single-profile reads; list pages leave it empty.
        /// </summary>
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
