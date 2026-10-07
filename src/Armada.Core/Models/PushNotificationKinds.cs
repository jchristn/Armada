namespace Armada.Core.Models
{
    /// <summary>
    /// Wire values of <see cref="PushMessageData.Kind"/>: the inbox kinds (<see cref="InboxItemKinds"/>) a push can
    /// announce, plus voyage_finished and test.
    /// </summary>
    public static class PushNotificationKinds
    {
        #region Public-Members

        /// <summary>An Ask Armada action proposal is waiting for approval.</summary>
        public const string AskProposal = InboxItemKinds.AskProposal;

        /// <summary>A CLI captain's permission prompt is waiting for an approver.</summary>
        public const string CliPermission = InboxItemKinds.CliPermission;

        /// <summary>A mission is awaiting review.</summary>
        public const string Review = InboxItemKinds.Review;

        /// <summary>A deployment is awaiting approval.</summary>
        public const string DeploymentApproval = InboxItemKinds.DeploymentApproval;

        /// <summary>A mission failed.</summary>
        public const string Failed = InboxItemKinds.Failed;

        /// <summary>A mission's landing failed.</summary>
        public const string LandingFailed = InboxItemKinds.LandingFailed;

        /// <summary>A captain is stalled.</summary>
        public const string StalledCaptain = InboxItemKinds.StalledCaptain;

        /// <summary>A voyage finished (completed or failed).</summary>
        public const string VoyageFinished = "voyage_finished";

        /// <summary>A test push sent from POST /api/v1/push/devices/{id}/test.</summary>
        public const string Test = "test";

        /// <summary>The iOS notification category the app registers with Approve and Deny actions.</summary>
        public const string ApproveDenyCategoryId = "armada_approve_deny";

        #endregion
    }
}
