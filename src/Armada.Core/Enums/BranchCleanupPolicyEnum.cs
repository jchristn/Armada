namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Policy for cleaning up mission branches after successful landing.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BranchCleanupPolicyEnum
    {
        /// <summary>
        /// Delete the mission branch from the local bare repository only.
        /// Remote branches are left for the git host to manage (e.g. auto-delete on PR merge).
        /// This is the default behavior.
        /// </summary>
        [EnumMember(Value = "LocalOnly")]
        LocalOnly,

        /// <summary>
        /// Delete the mission branch from both the local bare repository and the remote origin.
        /// Use this when the git host does not auto-delete merged branches.
        /// </summary>
        [EnumMember(Value = "LocalAndRemote")]
        LocalAndRemote,

        /// <summary>
        /// Do not delete mission branches after landing. Branches are retained for inspection.
        /// Useful for auditing or debugging — stale branches must be cleaned up manually.
        /// </summary>
        [EnumMember(Value = "None")]
        None
    }
}
