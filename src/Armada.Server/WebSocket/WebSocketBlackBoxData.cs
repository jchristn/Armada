namespace Armada.Server.WebSocket
{
    /// <summary>
    /// The data of the blackbox.get and blackbox.brief WebSocket commands. The entry can also be named by the command's
    /// id (a mission id with msn_, a voyage id with vyg_, otherwise an entry id).
    /// </summary>
    public class WebSocketBlackBoxData
    {
        /// <summary>
        /// Black box entry (bbx_ prefix), or null.
        /// </summary>
        public string? EntryId { get; set; } = null;

        /// <summary>
        /// Mission whose current entry is meant (msn_ prefix), or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Voyage whose current entry is meant (vyg_ prefix), or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// blackbox.brief audience: agent (default), user or maintainer.
        /// </summary>
        public string? Audience { get; set; } = null;

        /// <summary>
        /// blackbox.brief: strip absolute paths, host names and user names.
        /// </summary>
        public bool? RedactHosts { get; set; } = null;

        /// <summary>
        /// blackbox.get: include the record (default true).
        /// </summary>
        public bool? IncludeRecord { get; set; } = null;
    }
}
