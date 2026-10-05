namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Raised when a dispatch names a pipeline (by id or name) that does not exist. Dispatch no longer falls back to
    /// the vessel or fleet default pipeline in that case; the defaults apply only when no pipeline was named. Derives
    /// from <see cref="KeyNotFoundException"/>, so REST maps it to 404 and MCP to a NotFound tool error.
    /// </summary>
    public class PipelineNotFoundException : KeyNotFoundException
    {
        #region Public-Members

        /// <summary>
        /// The pipeline id or name the caller supplied.
        /// </summary>
        public string PipelineReference { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="pipelineReference">The pipeline id or name the caller supplied.</param>
        public PipelineNotFoundException(string pipelineReference)
            : base("Pipeline not found: " + (pipelineReference ?? String.Empty))
        {
            PipelineReference = pipelineReference ?? String.Empty;
        }

        #endregion
    }
}
