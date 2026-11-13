namespace Armada.Server
{
    /// <summary>Reads only the <c>UsageSelection</c> group of a settings update, as sent.</summary>
    public sealed class UsageSelectionPatchRequest
    {
        /// <summary>Sent members of the group, or null when the group was not sent.</summary>
        public UsageSelectionPatch? UsageSelection { get; set; }
    }
}
