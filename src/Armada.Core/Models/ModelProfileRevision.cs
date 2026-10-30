namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One revision of a model profile: the runtime version, model digest, sampling and tool configuration first seen
    /// together. A revision is immutable once created; a new runtime major version, a new model digest or a change in
    /// sampling starts a new revision (Decision 6 of docs/MODEL_HETEROGENEITY.md) while a runtime minor or patch release
    /// pools into the current one. <see cref="CompatibleWith"/> is administrator data kept beside the revision, not part of
    /// it: marking revisions compatible never edits a revision.
    /// </summary>
    public class ModelProfileRevision
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (mdv_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Profile this revision belongs to (mdl_ prefix).
        /// </summary>
        public string ProfileId { get; set; } = "";

        /// <summary>
        /// Tenant of the profile, or null.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Runtime version first seen with this revision (for example 1.4.1), or null when not known.
        /// </summary>
        public string? RuntimeVersion { get; set; } = null;

        /// <summary>
        /// Model digest (for example sha256:...), or null when the runtime does not report one.
        /// </summary>
        public string? ModelDigest { get; set; } = null;

        /// <summary>
        /// Sampling settings first seen with this revision.
        /// </summary>
        public ModelProfileSampling Sampling
        {
            get => _Sampling;
            set => _Sampling = value ?? new ModelProfileSampling();
        }

        /// <summary>
        /// Tool configuration first seen with this revision.
        /// </summary>
        public ModelProfileToolConfiguration ToolConfiguration
        {
            get => _ToolConfiguration;
            set => _ToolConfiguration = value ?? new ModelProfileToolConfiguration();
        }

        /// <summary>
        /// Revisions an administrator marked compatible with this one (mdv_ prefix), so their results may be pooled.
        /// </summary>
        public List<string> CompatibleWith
        {
            get => _CompatibleWith;
            set => _CompatibleWith = value ?? new List<string>();
        }

        /// <summary>
        /// When the revision was first observed, in UTC.
        /// </summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.ModelProfileRevisionIdPrefix, 24);
        private ModelProfileSampling _Sampling = new ModelProfileSampling();
        private ModelProfileToolConfiguration _ToolConfiguration = new ModelProfileToolConfiguration();
        private List<string> _CompatibleWith = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileRevision()
        {
        }

        #endregion
    }
}
