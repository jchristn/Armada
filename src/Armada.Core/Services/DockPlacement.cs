namespace Armada.Core.Services
{
    /// <summary>
    /// Where a mission's dock is created: on a Harbor, on the Admiral's host, or nowhere yet (the mission waits).
    /// </summary>
    public class DockPlacement
    {
        #region Public-Members

        /// <summary>
        /// The Harbor to create the dock on, or null.
        /// </summary>
        public string? HarborId { get; private set; } = null;

        /// <summary>
        /// When true the mission is not assigned now and stays Pending (no eligible Harbor can serve it and it may not run
        /// on the Admiral's host).
        /// </summary>
        public bool Wait { get; private set; } = false;

        /// <summary>
        /// Why this placement was chosen, for logs.
        /// </summary>
        public string Reason { get; private set; } = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate (the Admiral's host).
        /// </summary>
        public DockPlacement()
        {
        }

        /// <summary>
        /// Create the dock on the Admiral's host.
        /// </summary>
        /// <param name="reason">Why.</param>
        /// <returns>The placement.</returns>
        public static DockPlacement OnAdmiral(string reason)
        {
            return new DockPlacement { Reason = reason ?? string.Empty };
        }

        /// <summary>
        /// Create the dock on a Harbor.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="reason">Why.</param>
        /// <returns>The placement.</returns>
        public static DockPlacement OnHarbor(string harborId, string reason)
        {
            return new DockPlacement { HarborId = harborId, Reason = reason ?? string.Empty };
        }

        /// <summary>
        /// Do not assign the mission now.
        /// </summary>
        /// <param name="reason">Why, and what would let it run.</param>
        /// <returns>The placement.</returns>
        public static DockPlacement WaitFor(string reason)
        {
            return new DockPlacement { Wait = true, Reason = reason ?? string.Empty };
        }

        #endregion
    }
}
