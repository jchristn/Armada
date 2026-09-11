namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// A durable agent memory distilled from work. Memories are the persistent counterpart to a captain's
    /// transient working context: the Recorder persona reviews a voyage's conversation, classifies what is
    /// worth keeping into <see cref="MemoryTypeEnum.Episodic"/>, <see cref="MemoryTypeEnum.Semantic"/>, or
    /// <see cref="MemoryTypeEnum.Procedural"/> memory, reconciles it against what is already stored, and
    /// persists it here alongside where it came from. Memories carry an ownership <see cref="Scope"/> so they
    /// can be tenant-wide or user-specific, exactly like other scoped configuration entities.
    /// </summary>
    public class Memory
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (mem_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user identifier (for user-specific memories).
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Ownership scope: a tenant-wide memory is visible to everyone in the tenant but editable only by
        /// tenant/global admins; a user-specific memory is owned by <see cref="UserId"/>. Defaults to
        /// tenant-wide; regular users create user-specific memories.
        /// </summary>
        public ScopeEnum Scope { get; set; } = ScopeEnum.TenantWide;

        /// <summary>
        /// The memory classification. Working memory is never stored, so only the three durable categories
        /// are valid here.
        /// </summary>
        public MemoryTypeEnum Type { get; set; } = MemoryTypeEnum.Semantic;

        /// <summary>
        /// An agent-chosen grouping within a type (the sub-structure of the store), e.g. "code-style" or
        /// "deployment". Optional but recommended so related memories consolidate under one heading.
        /// </summary>
        public string? Topic { get; set; } = null;

        /// <summary>
        /// A stable, human-meaningful idempotency key (slug) for this memory, e.g. "code-style/no-var".
        /// When set, the Recorder upserts by key so re-recording the same idea updates the existing memory
        /// in place instead of creating a duplicate. Unique within a tenant when present.
        /// </summary>
        public string? Key { get; set; } = null;

        /// <summary>
        /// A one-line recall hook: a terse summary cheap to return in listings and search so a caller can
        /// decide whether to read the full content.
        /// </summary>
        public string? Summary { get; set; } = null;

        /// <summary>
        /// The memory itself: the fact, the episode, or the procedure, written to stand on its own.
        /// </summary>
        public string Content
        {
            get => _Content;
            set => _Content = value ?? String.Empty;
        }

        /// <summary>
        /// Importance ranking in [0.0, 1.0], used to order recall (higher first). Defaults to 0.5. The
        /// Recorder raises it for load-bearing facts and lowers it for incidental ones.
        /// </summary>
        public double Salience
        {
            get => _Salience;
            set => _Salience = value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);
        }

        /// <summary>
        /// Monotonic revision counter, starting at 1 and incremented each time the memory is updated. Lets a
        /// reconciler reason about how much a memory has churned.
        /// </summary>
        public int Version
        {
            get => _Version;
            set => _Version = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Where this memory was extracted from.
        /// </summary>
        public MemorySourceKindEnum SourceKind { get; set; } = MemorySourceKindEnum.Manual;

        /// <summary>
        /// Originating voyage id, when the source was a voyage or one of its missions.
        /// </summary>
        public string? SourceVoyageId { get; set; } = null;

        /// <summary>
        /// Originating mission id, when the source was a specific mission.
        /// </summary>
        public string? SourceMissionId { get; set; } = null;

        /// <summary>
        /// Originating vessel id recorded as provenance (which repository the work touched).
        /// </summary>
        public string? SourceVesselId { get; set; } = null;

        /// <summary>
        /// Free-text note describing where the memory came from, for traceability beyond the typed ids.
        /// </summary>
        public string? SourceDetail { get; set; } = null;

        /// <summary>
        /// Optional vessel association: the repository this memory is about, so it can be recalled per-repo.
        /// May differ from <see cref="SourceVesselId"/> (provenance).
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Free-form tags for retrieval and consolidation. Persisted in a child table.
        /// </summary>
        public List<string> Tags
        {
            get => _Tags;
            set => _Tags = value ?? new List<string>();
        }

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

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.MemoryIdPrefix, 24);
        private string _Content = String.Empty;
        private double _Salience = 0.5;
        private int _Version = 1;
        private List<string> _Tags = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public Memory()
        {
        }

        #endregion
    }
}
