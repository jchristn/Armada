namespace Armada.Core.Services.Health.Json
{
    /// <summary>
    /// A problem reported by dotnet list package (for example a failed restore).
    /// </summary>
    public class DotnetListProblem
    {
        #region Public-Members

        /// <summary>
        /// Project the problem applies to, or null.
        /// </summary>
        public string? Project { get; set; } = null;

        /// <summary>
        /// Problem level, for example error or warning.
        /// </summary>
        public string? Level { get; set; } = null;

        /// <summary>
        /// Problem text.
        /// </summary>
        public string? Text { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DotnetListProblem()
        {
        }

        #endregion
    }
}
