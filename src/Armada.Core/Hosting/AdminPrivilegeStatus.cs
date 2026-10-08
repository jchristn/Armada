namespace Armada.Core.Hosting
{
    using System;

    /// <summary>
    /// The outcome of <see cref="AdminPrivilegeCheck"/>: whether an administrator-only operation may be offered, and a
    /// sentence explaining why not.
    /// </summary>
    public class AdminPrivilegeStatus
    {
        #region Public-Members

        /// <summary>
        /// What the Admiral reported.
        /// </summary>
        public AdminPrivilegeStateEnum State { get; set; } = AdminPrivilegeStateEnum.Unknown;

        /// <summary>
        /// True only for <see cref="AdminPrivilegeStateEnum.Admin"/>.
        /// </summary>
        public bool IsAdmin
        {
            get { return State == AdminPrivilegeStateEnum.Admin; }
        }

        /// <summary>
        /// Who the credential belongs to (an email address), or null.
        /// </summary>
        public string? Principal { get; set; } = null;

        /// <summary>
        /// One sentence for the user: who Harbor is signed in as and, when not an administrator, what to change.
        /// </summary>
        public string Explanation
        {
            get { return _Explanation; }
            set { _Explanation = value ?? String.Empty; }
        }

        #endregion

        #region Private-Members

        private string _Explanation = String.Empty;

        #endregion
    }
}
