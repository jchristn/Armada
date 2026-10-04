namespace Armada.Core.Services.Ask
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Work an executed action created or affected, resolved by <see cref="AskWorkLinker"/>.
    /// </summary>
    public class AskWorkLink
    {
        #region Public-Members

        /// <summary>
        /// Entity type.
        /// </summary>
        public AskTrackedEntityTypeEnum EntityType { get; set; } = AskTrackedEntityTypeEnum.Voyage;

        /// <summary>
        /// Entity identifier.
        /// </summary>
        public string EntityId
        {
            get => _EntityId;
            set => _EntityId = value ?? String.Empty;
        }

        /// <summary>
        /// True when the action only affects existing work (cancel_*): refresh the item if the thread tracks it, but do
        /// not start tracking it.
        /// </summary>
        public bool RefreshOnly { get; set; } = false;

        #endregion

        #region Private-Members

        private string _EntityId = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskWorkLink()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="entityType">Entity type.</param>
        /// <param name="entityId">Entity identifier.</param>
        /// <param name="refreshOnly">Whether only an existing tracked item is refreshed.</param>
        public AskWorkLink(AskTrackedEntityTypeEnum entityType, string entityId, bool refreshOnly)
        {
            EntityType = entityType;
            EntityId = entityId;
            RefreshOnly = refreshOnly;
        }

        #endregion
    }
}
