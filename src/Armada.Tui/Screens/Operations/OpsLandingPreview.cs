namespace Armada.Tui.Screens.Operations
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using TUIKit;

    /// <summary>
    /// The dashboard's Landing Preview card (Mission and Merge Entry screens) rendered into an <see cref="OpsDocument"/>:
    /// branches, Ready To Land or Needs Review, branch category, landing mode, cleanup, expected action, check policy,
    /// protection and policy flags, latest check, and the predicted blockers.
    /// </summary>
    public static class OpsLandingPreview
    {
        #region Public-Methods

        /// <summary>
        /// Render the card.
        /// </summary>
        /// <param name="doc">Document.</param>
        /// <param name="preview">Preview, or null.</param>
        /// <param name="loading">True while calculating.</param>
        /// <param name="meta">Branch line shown under the heading.</param>
        /// <param name="mission">True for the Mission screen (check policy, latest check, mission wording).</param>
        public static void Render(OpsDocument doc, LandingPreviewResult? preview, bool loading, string meta, bool mission)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            bool ready = preview != null && preview.IsReadyToLand;
            doc.Section("Landing Preview");
            doc.Add(StyledText.From(meta ?? "", doc.Theme.Muted).Append(StyledText.From("   [" + doc.Loc.T(ready ? "Ready To Land" : "Needs Review") + "]", ready ? doc.Theme.Success : doc.Theme.Warning)));
            if (loading)
            {
                doc.Note("Calculating landing preview...");
                return;
            }

            if (preview == null)
            {
                doc.Note(mission ? "Landing preview is not available for this mission yet." : "Landing preview is not available for this merge entry yet.");
                return;
            }

            doc.Field("Branch category", preview.BranchCategory);
            doc.Field("Landing mode", preview.LandingMode?.ToString() ?? doc.Loc.T("Inherited"));
            doc.Field("Cleanup", preview.BranchCleanupPolicy?.ToString() ?? doc.Loc.T("Inherited"));
            if (!String.IsNullOrEmpty(preview.ExpectedLandingAction)) doc.Field("Action", preview.ExpectedLandingAction);
            if (mission) doc.Text(doc.Loc.T(preview.RequirePassingChecksToLand ? "Passing checks required" : "Passing checks optional"));
            doc.Text(doc.Loc.T(preview.TargetBranchProtected ? "Protected target branch" : "Target branch not protected"));
            if (!String.IsNullOrEmpty(preview.ProtectedBranchMatch)) doc.Field("Policy", preview.ProtectedBranchMatch);
            if (preview.RequirePullRequestForProtectedBranches) doc.Text(doc.Loc.T("PR required for protected branches"));
            if (preview.RequireMergeQueueForReleaseBranches) doc.Text(doc.Loc.T("Merge queue required for release branches"));
            if (mission && !String.IsNullOrEmpty(preview.LatestCheckSummary)) doc.Field("Latest check", preview.LatestCheckSummary);
            if (preview.Issues != null && preview.Issues.Count > 0)
            {
                foreach (LandingPreviewIssue issue in preview.Issues)
                {
                    CellStyle style = issue.Severity == ReadinessSeverityEnum.Error ? doc.Theme.Error : issue.Severity == ReadinessSeverityEnum.Warning ? doc.Theme.Warning : doc.Theme.Info;
                    doc.Add(StyledText.From("[" + issue.Severity + "] ", style).Append(StyledText.From(issue.Title ?? "", doc.Theme.Text)));
                    doc.Text("    " + (issue.Message ?? ""), doc.Theme.Muted);
                }
            }
            else
            {
                doc.Text(doc.Loc.T(mission ? "No landing blockers are currently predicted for this mission." : "No landing blockers are currently predicted for this merge entry."), doc.Theme.Success);
            }
        }

        #endregion
    }
}
