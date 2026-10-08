namespace Armada.Core.Harbor
{
    /// <summary>
    /// A Harbor setting that names the checkout on the Harbor's machine that serves one vessel.
    /// </summary>
    public class HarborRepositoryMapping
    {
        #region Public-Members

        /// <summary>
        /// The vessel, by name or identifier (vsl_ prefix), compared without regard to case.
        /// </summary>
        public string Vessel { get; set; } = string.Empty;

        /// <summary>
        /// Absolute path of the git checkout (the folder that contains .git) on this machine.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborRepositoryMapping()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="vessel">Vessel name or identifier.</param>
        /// <param name="path">Checkout path.</param>
        public HarborRepositoryMapping(string vessel, string path)
        {
            Vessel = vessel ?? string.Empty;
            Path = path ?? string.Empty;
        }

        #endregion
    }
}
