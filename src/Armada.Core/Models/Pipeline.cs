namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// An ordered sequence of persona stages that a dispatch goes through.
    /// Pipelines define the workflow for processing missions (e.g. Architect then Worker then Judge).
    /// </summary>
    public class Pipeline
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Pipeline name (e.g. "WorkerOnly", "FullPipeline", "Reviewed").
        /// </summary>
        public string Name
        {
            get => _Name;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Human-readable description of the pipeline workflow.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Ordered list of pipeline stages.
        /// </summary>
        public List<PipelineStage> Stages
        {
            get => _Stages;
            set => _Stages = value ?? new List<PipelineStage>();
        }

        /// <summary>
        /// Whether this is a built-in system pipeline. Built-in pipelines cannot be deleted.
        /// </summary>
        public bool IsBuiltIn { get; set; } = false;

        /// <summary>
        /// Whether the pipeline is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable("ppl_", 24);
        private string _Name = "WorkerOnly";
        private List<PipelineStage> _Stages = new List<PipelineStage>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public Pipeline()
        {
        }

        /// <summary>
        /// Instantiate with name.
        /// </summary>
        /// <param name="name">Pipeline name.</param>
        public Pipeline(string name)
        {
            Name = name;
        }

        #endregion
    }
}
