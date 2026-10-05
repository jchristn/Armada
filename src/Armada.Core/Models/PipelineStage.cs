namespace Armada.Core.Models
{
    using System;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

    /// <summary>
    /// A single stage within a pipeline, associating a persona with an execution order.
    /// </summary>
    public class PipelineStage
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
        /// Pipeline identifier this stage belongs to.
        /// </summary>
        public string? PipelineId { get; set; } = null;

        /// <summary>
        /// Execution order within the pipeline (1-based).
        /// </summary>
        public int Order
        {
            get => _Order;
            set => _Order = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Name of the persona for this stage (references Persona.Name).
        /// </summary>
        public string PersonaName
        {
            get => _PersonaName;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(PersonaName));
                _PersonaName = value;
            }
        }

        /// <summary>
        /// Reserved. Marks a stage as optional (the original design let the Admiral skip such a stage), but no skip
        /// policy was ever defined: the value is stored, returned, and shown in the dashboard and TUI, and dispatch
        /// currently runs every stage regardless. Kept for API compatibility.
        /// </summary>
        public bool IsOptional { get; set; } = false;

        /// <summary>
        /// Description of what this stage does (e.g. "Plan the voyage", "Review the diff").
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Whether this stage requires an explicit review approval before the pipeline may continue.
        /// </summary>
        public bool RequiresReview { get; set; } = false;

        /// <summary>
        /// Action to take when the review gate is denied.
        /// </summary>
        public ReviewDenyActionEnum ReviewDenyAction { get; set; } = ReviewDenyActionEnum.RetryStage;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable("pps_", 24);
        private int _Order = 1;
        private string _PersonaName = "Worker";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PipelineStage()
        {
        }

        /// <summary>
        /// Instantiate with order and persona.
        /// </summary>
        /// <param name="order">Execution order.</param>
        /// <param name="personaName">Persona name for this stage.</param>
        public PipelineStage(int order, string personaName)
        {
            Order = order;
            PersonaName = personaName;
        }

        #endregion
    }
}
