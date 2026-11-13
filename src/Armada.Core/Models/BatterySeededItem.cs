namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// A seeded defect or violation a reviewer should find.
    /// </summary>
    public class BatterySeededItem
    {
        #region Public-Members

        /// <summary>
        /// Identifier, unique in the task.
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// What is seeded.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Case-insensitive phrases; a finding that contains any of them matches this item.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> Keywords
        {
            get => _Keywords;
            set => _Keywords = value ?? new List<string>();
        }

        #endregion

        #region Private-Members

        private List<string> _Keywords = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BatterySeededItem()
        {
        }

        #endregion
    }
}
