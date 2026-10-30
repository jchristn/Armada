namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A note on a black box entry by an operator, an agent, Ask Armada or Armada (blackbox_notes).
    /// </summary>
    public class BlackBoxNote
    {
        /// <summary>
        /// Unique identifier (bbn_ prefix).
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or empty.</exception>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Entry.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Tenant.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Author user, or null.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Who wrote it.
        /// </summary>
        public BlackBoxNoteAuthorKindEnum AuthorKind { get; set; } = BlackBoxNoteAuthorKindEnum.Operator;

        /// <summary>
        /// Markdown body (at most 8000 characters).
        /// </summary>
        public string Body { get; set; } = "";

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.BlackBoxNoteIdPrefix, 24);

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxNote()
        {
        }
    }
}
