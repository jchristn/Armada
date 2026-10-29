namespace Armada.Core.BlackBox
{
    using System;

    /// <summary>
    /// A confirm token issued by a black box dry run (<see cref="BlackBoxConfirmationStore"/>): what it is bound to and
    /// when it expires. Immutable once issued; thread-safe.
    /// </summary>
    public class BlackBoxPendingConfirmation
    {
        #region Public-Members

        /// <summary>Token text.</summary>
        public string Token { get; set; } = "";

        /// <summary>What the token confirms: <see cref="BlackBoxConfirmationStore.RecoveryKind"/> or <see cref="BlackBoxConfirmationStore.RecommendationKind"/>.</summary>
        public string Kind { get; set; } = "";

        /// <summary>Entry id.</summary>
        public string EntryId { get; set; } = "";

        /// <summary>Recovery path id or recommendation id.</summary>
        public string TargetId { get; set; } = "";

        /// <summary>Version of the subject (mission or voyage, and vessel for recommendations) at the dry run.</summary>
        public string SubjectVersion { get; set; } = "";

        /// <summary>Fingerprint of the exact plan shown by the dry run.</summary>
        public string PlanHash { get; set; } = "";

        /// <summary>User the token was issued to, or null.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>Tenant the token was issued to, or null.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>Expiry.</summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxPendingConfirmation()
        {
        }

        #endregion
    }
}
