namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A file recorded on a black box entry (blackbox_files), the cross-entry query surface for hot spots.
    /// </summary>
    public class BlackBoxFile
    {
        /// <summary>
        /// Unique identifier (bbf_ prefix).
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
        /// Vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Repository-relative path, forward slashes.
        /// </summary>
        public string Path { get; set; } = "";

        /// <summary>
        /// Parent directory of Path (empty for the root).
        /// </summary>
        public string Directory { get; set; } = "";

        /// <summary>
        /// Why the file is recorded.
        /// </summary>
        public BlackBoxFileRoleEnum Role { get; set; } = BlackBoxFileRoleEnum.Conflicted;

        /// <summary>
        /// The mission whose landed commit also touched it, when known.
        /// </summary>
        public string? ConflictingMissionId { get; set; } = null;

        /// <summary>
        /// Created (UTC).
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.BlackBoxFileIdPrefix, 24);

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxFile()
        {
        }
    }
}
