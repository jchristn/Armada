namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Selects quota eviction candidates: unpinned entries that are not being deleted, oldest first.
    /// </summary>
    public class BlackBoxEvictionQuery
    {
        /// <summary>
        /// Only entries of this vessel, or null for every vessel.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Only resolved entries (resolution other than Open).
        /// </summary>
        public bool ResolvedOnly { get; set; } = false;

        /// <summary>
        /// Skip every current entry (the per-vessel quota evicts non-current entries only).
        /// </summary>
        public bool ExcludeCurrent { get; set; } = false;

        /// <summary>
        /// Skip the current entry of a mission or voyage that still exists (blackBox.retention.keepLatestPerMission).
        /// </summary>
        public bool KeepLatestPerMission { get; set; } = true;

        /// <summary>
        /// Page number (1-based).
        /// </summary>
        public int PageNumber
        {
            get => _PageNumber;
            set => _PageNumber = Math.Max(1, value);
        }

        /// <summary>
        /// Rows per page (1 to 1000).
        /// </summary>
        public int PageSize
        {
            get => _PageSize;
            set => _PageSize = Math.Clamp(value, 1, 1000);
        }

        private int _PageNumber = 1;
        private int _PageSize = 100;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxEvictionQuery()
        {
        }
    }
}
