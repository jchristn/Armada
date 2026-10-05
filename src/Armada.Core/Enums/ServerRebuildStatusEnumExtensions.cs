namespace Armada.Core.Enums
{
    /// <summary>
    /// Helpers for <see cref="ServerRebuildStatusEnum"/>.
    /// </summary>
    public static class ServerRebuildStatusEnumExtensions
    {
        #region Public-Methods

        /// <summary>
        /// True when the rebuild has finished (succeeded, failed, or rolled back).
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>True for a terminal status.</returns>
        public static bool IsTerminal(this ServerRebuildStatusEnum status)
        {
            return status == ServerRebuildStatusEnum.Succeeded
                || status == ServerRebuildStatusEnum.Failed
                || status == ServerRebuildStatusEnum.RolledBack;
        }

        #endregion
    }
}
