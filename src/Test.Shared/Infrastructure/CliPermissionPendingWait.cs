namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The condition a test waits for before it reads a thread prompt's pending CLI permission request together with its
    /// Ask card. CliPermissionService.PromptAsync stores the request as Pending first and posts the card afterwards (the
    /// card message, then the request's MessageId), so a request can be listed as Pending before its card exists. A wait
    /// that returned on the first Pending row let a test enumerate the thread's messages in that gap and fail with
    /// "Sequence contains no matching element" under load. Wait for a Pending row whose MessageId is set instead: the
    /// MessageId is stored after the card message, so the card is readable once it is set.
    /// </summary>
    public static class CliPermissionPendingWait
    {
        #region Public-Methods

        /// <summary>
        /// The first Pending request whose Ask card has been posted (MessageId set), or null when there is none yet.
        /// </summary>
        /// <param name="rows">Requests listed for the thread.</param>
        /// <returns>The request, or null.</returns>
        public static CliPermissionRequest? FirstWithCard(IEnumerable<CliPermissionRequest>? rows)
        {
            if (rows == null) return null;
            foreach (CliPermissionRequest row in rows)
            {
                if (row != null && row.Status == CliPermissionRequestStatusEnum.Pending && !String.IsNullOrEmpty(row.MessageId)) return row;
            }

            return null;
        }

        #endregion
    }
}
