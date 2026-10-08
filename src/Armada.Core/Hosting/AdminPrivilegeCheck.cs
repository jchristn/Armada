namespace Armada.Core.Hosting
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// Decides from the Admiral's own answers whether the credential a client holds is a global administrator, so a
    /// client can disable or explain administrator-only operations (restarting the Admiral) before the server refuses
    /// them. The server enforces the requirement regardless; this only spares the user a request that would be refused.
    /// </summary>
    public static class AdminPrivilegeCheck
    {
        #region Public-Methods

        /// <summary>
        /// From <c>GET /api/v1/whoami</c>.
        /// </summary>
        /// <param name="whoami">The Admiral's answer, or null when it returned nothing.</param>
        /// <returns>The status.</returns>
        public static AdminPrivilegeStatus FromWhoAmI(WhoAmIResult? whoami)
        {
            UserMaster? user = whoami?.User;
            if (user == null)
            {
                return new AdminPrivilegeStatus
                {
                    State = AdminPrivilegeStateEnum.Unknown,
                    Explanation = "The Admiral did not say who Harbor is signed in as, so administrator privileges could not be confirmed."
                };
            }

            string who = String.IsNullOrWhiteSpace(user.Email) ? user.Id : user.Email;
            if (user.IsAdmin)
            {
                return new AdminPrivilegeStatus
                {
                    State = AdminPrivilegeStateEnum.Admin,
                    Principal = who,
                    Explanation = "Harbor is signed in as " + who + ", an administrator."
                };
            }

            return new AdminPrivilegeStatus
            {
                State = AdminPrivilegeStateEnum.NotAdmin,
                Principal = who,
                Explanation = "Harbor is signed in as " + who + (user.IsTenantAdmin ? ", a tenant administrator" : "")
                    + ", which is not an Armada administrator. Set an administrator's access key in Settings > General to restart the Admiral."
            };
        }

        /// <summary>
        /// From a failed <c>GET /api/v1/whoami</c>.
        /// </summary>
        /// <param name="statusCode">HTTP status, or 0 when the Admiral could not be reached.</param>
        /// <param name="message">Error message.</param>
        /// <returns>The status.</returns>
        public static AdminPrivilegeStatus FromError(int statusCode, string? message)
        {
            if (statusCode == 401 || statusCode == 403)
            {
                return new AdminPrivilegeStatus
                {
                    State = AdminPrivilegeStateEnum.Unauthenticated,
                    Explanation = "The Admiral did not accept Harbor's credential. Set an administrator's access key in Settings > General to restart the Admiral."
                };
            }

            if (statusCode == 0)
            {
                return new AdminPrivilegeStatus
                {
                    State = AdminPrivilegeStateEnum.Unreachable,
                    Explanation = "The Admiral could not be reached, so administrator privileges could not be confirmed" + Suffix(message)
                };
            }

            return new AdminPrivilegeStatus
            {
                State = AdminPrivilegeStateEnum.Unknown,
                Explanation = "The Admiral answered " + statusCode + " when asked who Harbor is signed in as" + Suffix(message)
            };
        }

        #endregion

        #region Private-Methods

        private static string Suffix(string? message)
        {
            string trimmed = (message ?? String.Empty).Trim().TrimEnd('.');
            return trimmed.Length > 0 ? ": " + trimmed + "." : ".";
        }

        #endregion
    }
}
