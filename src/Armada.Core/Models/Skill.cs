namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// A reusable, named capability snippet (a "skill") that can be attached to projects via a project
    /// profile and injected into mission prompts. Skills form a tenant-scoped directory of editable,
    /// categorized instructions -- lighter-weight than playbooks and intended for cross-cutting habits
    /// (e.g. "write ADRs", "TDD", "conventional commits").
    /// </summary>
    public class Skill
    {
        /// <summary>
        /// Unique identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user identifier.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Ownership scope: tenant-wide skills are visible to everyone in the tenant but editable only by
        /// tenant/global admins; user-specific skills are owned by <see cref="UserId"/>. Defaults tenant-wide.
        /// </summary>
        public Armada.Core.Enums.ScopeEnum Scope { get; set; } = Armada.Core.Enums.ScopeEnum.TenantWide;

        /// <summary>
        /// Human-readable skill name (also the reference key used by project profiles).
        /// </summary>
        public string Name
        {
            get => _Name;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value.Trim();
            }
        }

        /// <summary>
        /// Optional short description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Optional category for grouping in the directory (e.g. "engineering", "testing").
        /// </summary>
        public string? Category { get; set; } = null;

        /// <summary>
        /// The skill instructions injected into mission prompts (markdown or plain text).
        /// </summary>
        public string Content { get; set; } = String.Empty;

        /// <summary>
        /// Whether this is a built-in seeded skill.
        /// </summary>
        public bool IsBuiltIn { get; set; } = false;

        /// <summary>
        /// Whether the skill is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Creation timestamp.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.SkillIdPrefix, 24);
        private string _Name = "Untitled Skill";
    }
}
