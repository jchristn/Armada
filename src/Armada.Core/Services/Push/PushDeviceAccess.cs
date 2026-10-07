namespace Armada.Core.Services.Push
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// Who may see and manage push devices: the owner; a tenant admin of the device's tenant; a global admin. A captain
    /// session (a mission- or thread-scoped token) never may.
    /// </summary>
    public static class PushDeviceAccess
    {
        #region Public-Methods

        /// <summary>
        /// Whether the caller may register a device for itself.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <returns>True for an authenticated user that is not a captain session.</returns>
        public static bool CanRegister(AuthContext caller)
        {
            if (caller == null || !caller.IsAuthenticated) return false;
            if (CliPermissionAccess.IsCaptainSession(caller)) return false;
            return !String.IsNullOrEmpty(caller.UserId) && !String.IsNullOrEmpty(caller.TenantId);
        }

        /// <summary>
        /// Whether the caller may see, update, delete, or test a device.
        /// </summary>
        /// <param name="caller">Caller.</param>
        /// <param name="device">Device.</param>
        /// <returns>True when allowed.</returns>
        public static bool CanManage(AuthContext caller, PushDevice device)
        {
            if (device == null || !CanRegister(caller)) return false;
            if (caller.IsAdmin) return true;
            if (!String.Equals(caller.TenantId, device.TenantId, StringComparison.Ordinal)) return false;
            if (caller.IsTenantAdmin) return true;
            return String.Equals(caller.UserId, device.UserId, StringComparison.Ordinal);
        }

        #endregion
    }
}
