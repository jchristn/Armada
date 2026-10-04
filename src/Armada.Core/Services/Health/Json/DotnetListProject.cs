namespace Armada.Core.Services.Health.Json
{
    using System.Collections.Generic;

    /// <summary>
    /// One project in dotnet list package output.
    /// </summary>
    public class DotnetListProject
    {
        #region Public-Members

        /// <summary>
        /// Absolute project path.
        /// </summary>
        public string? Path { get; set; } = null;

        /// <summary>
        /// Per-target-framework results.
        /// </summary>
        public List<DotnetListFramework>? Frameworks { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DotnetListProject()
        {
        }

        #endregion
    }
}
