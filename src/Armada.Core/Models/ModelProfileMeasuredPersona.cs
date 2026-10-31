namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Battery counts for one persona in the profile's measured block, one count per pack.
    /// </summary>
    public class ModelProfileMeasuredPersona
    {
        #region Public-Members

        /// <summary>
        /// Persona name (for example Worker).
        /// </summary>
        public string Persona { get; set; } = "";

        /// <summary>
        /// Counts per battery pack, ordered by pack identifier.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "One per battery pack run")]
        public List<ModelProfileMeasuredCount> Packs
        {
            get => _Packs;
            set => _Packs = value ?? new List<ModelProfileMeasuredCount>();
        }

        #endregion

        #region Private-Members

        private List<ModelProfileMeasuredCount> _Packs = new List<ModelProfileMeasuredCount>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileMeasuredPersona()
        {
        }

        #endregion
    }
}
