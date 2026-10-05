namespace Armada.Tui.Screens.Operations
{
    using System;

    /// <summary>
    /// One row of the backlog item's Links panel: a linked record (fleet, vessel, planning session, refinement session,
    /// voyage, mission, check, release, deployment, or incident) and the route that opens it.
    /// </summary>
    public class BacklogLink
    {
        #region Public-Members

        /// <summary>
        /// Row key (kind and id).
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// English kind label (for example Linked Vessels).
        /// </summary>
        public string Kind { get; }

        /// <summary>
        /// Record id.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Display name (or the id).
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Route that opens the record.
        /// </summary>
        public string Route { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="kind">English kind label.</param>
        /// <param name="id">Id.</param>
        /// <param name="name">Name.</param>
        /// <param name="route">Route.</param>
        public BacklogLink(string kind, string id, string name, string route)
        {
            Kind = kind ?? "";
            Id = id ?? "";
            Name = String.IsNullOrEmpty(name) ? Id : name;
            Route = route ?? "/";
            Key = Kind + "|" + Id;
        }

        #endregion
    }
}
