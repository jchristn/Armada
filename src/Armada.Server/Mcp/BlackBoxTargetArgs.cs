namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments that name a black box entry directly (entryId) or through its mission (missionId) or voyage (voyageId): the base of every black box tool that acts on one entry. entryId wins, then missionId, then voyageId.
    /// </summary>
    public class BlackBoxTargetArgs
    {
        /// <summary>
        /// Black box entry (bbx_ prefix), or null.
        /// </summary>
        public string? EntryId { get; set; } = null;

        /// <summary>
        /// Mission (msn_ prefix) whose current entry is meant, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Voyage (vyg_ prefix) whose current entry is meant, or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;
    }
}
