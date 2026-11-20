namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// One contributed command.
    /// </summary>
    public sealed class VsCodeCommandContribution
    {
        /// <summary>Command id.</summary>
        public string Command { get; set; } = "";

        /// <summary>Title.</summary>
        public string Title { get; set; } = "";
    }
}
