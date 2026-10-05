namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// The dashboard's ReadinessPanel as document lines: tone pill (Ready, Needs Attention, Blocked, Unknown),
    /// resolved workflow profile, onboarding progress, availability summary, branch and drift, toolchains, probes,
    /// environments, delivery coverage, setup checklist, and issues. The compact form is what Dispatch and Planning
    /// show inline; the full form opens in a viewer. Use on the UI loop.
    /// </summary>
    public static class OpsReadiness
    {
        #region Public-Methods

        /// <summary>
        /// The pill label (the dashboard's getReadinessLabel).
        /// </summary>
        /// <param name="readiness">Readiness, or null.</param>
        /// <returns>English label.</returns>
        public static string Label(VesselReadinessResult? readiness)
        {
            if (readiness == null) return "Unknown";
            if (readiness.ErrorCount > 0) return "Blocked";
            if (readiness.WarningCount > 0) return "Needs Attention";
            return "Ready";
        }

        /// <summary>
        /// The pill style.
        /// </summary>
        /// <param name="readiness">Readiness, or null.</param>
        /// <param name="theme">Palette.</param>
        /// <returns>Style.</returns>
        public static CellStyle Style(VesselReadinessResult? readiness, ArmadaTheme theme)
        {
            if (readiness == null) return theme.Warning;
            if (readiness.ErrorCount > 0) return theme.Error;
            if (readiness.WarningCount > 0) return theme.Warning;
            return theme.Success;
        }

        /// <summary>
        /// Add the panel to a document.
        /// </summary>
        /// <param name="doc">Document.</param>
        /// <param name="title">English title (for example Vessel Readiness).</param>
        /// <param name="readiness">Readiness, or null.</param>
        /// <param name="loading">True while loading.</param>
        /// <param name="emptyMessage">English text when there is no data.</param>
        /// <param name="compact">Compact form (no checklist, branch, or toolchain cards).</param>
        /// <returns>The document.</returns>
        public static OpsDocument Build(OpsDocument doc, string title, VesselReadinessResult? readiness, bool loading, string emptyMessage, bool compact)
        {
            ArmadaTheme theme = doc.Theme;
            StyledText head = StyledText.From(doc.Loc.T(title) + "  ", theme.Accent.WithAttribute(CellAttributes.Bold, true))
                .Append(StyledText.From("[" + doc.Loc.T(Label(readiness)) + "]", Style(readiness, theme)));
            doc.Add(head);
            if (readiness != null && !String.IsNullOrEmpty(readiness.WorkflowProfileName))
            {
                doc.Text(doc.Loc.T("Resolved profile") + ": " + readiness.WorkflowProfileName + (readiness.WorkflowProfileScope.HasValue ? " (" + readiness.WorkflowProfileScope.Value + ")" : ""), theme.Muted);
            }

            if (!compact && readiness != null && readiness.SetupChecklistTotalCount > 0)
            {
                doc.Text(doc.Loc.T("Onboarding") + ": " + readiness.SetupChecklistSatisfiedCount + "/" + readiness.SetupChecklistTotalCount + " " + doc.Loc.T("steps complete"), theme.Muted);
            }

            if (loading)
            {
                doc.Note("Checking readiness...");
                return doc;
            }

            if (readiness == null)
            {
                doc.Note(emptyMessage);
                return doc;
            }

            List<string> summary = new List<string>
            {
                doc.Loc.T(readiness.HasWorkingDirectory ? "Working directory available" : "Working directory unavailable"),
                doc.Loc.T(readiness.HasRepositoryContext ? "Repository context available" : "Repository context unavailable"),
            };
            if (readiness.AvailableCheckTypes.Count > 0) summary.Add(readiness.AvailableCheckTypes.Count + " " + doc.Loc.T("check type(s) available"));
            doc.Text(String.Join("  |  ", summary), theme.Text);

            if (!compact)
            {
                if (readiness.AvailableCheckTypes.Count > 0) doc.Field("Check types", String.Join(", ", readiness.AvailableCheckTypes));
                if (!String.IsNullOrEmpty(readiness.CurrentBranch)) doc.Field("Branch", readiness.CurrentBranch + (readiness.IsDetachedHead == true ? " (detached HEAD)" : ""));
                if (readiness.CommitsAhead != null || readiness.CommitsBehind != null) doc.Field("Remote drift", (readiness.CommitsAhead ?? 0) + " ahead / " + (readiness.CommitsBehind ?? 0) + " behind");
                if (readiness.HasUncommittedChanges != null) doc.Field("Working tree", doc.Loc.T(readiness.HasUncommittedChanges.Value ? "Uncommitted changes present" : "Clean working tree"));
                if (readiness.DetectedToolchains.Count > 0) doc.Field("Detected toolchains", String.Join(", ", readiness.DetectedToolchains));
                foreach (VesselToolchainProbe probe in readiness.ToolchainProbes)
                {
                    doc.Field("Toolchain probe", probe.Name + "  " + (probe.Version ?? (probe.Available ? "available" : "missing")) + (probe.Expected ? "  (expected)" : ""), probe.Available ? (CellStyle?)null : theme.Warning);
                }

                if (readiness.DeploymentEnvironments.Count > 0) doc.Field("Environments", String.Join(", ", readiness.DeploymentEnvironments));
                if (readiness.DeploymentMetadata != null)
                {
                    VesselDeploymentMetadata m = readiness.DeploymentMetadata;
                    List<string> cov = new List<string> { m.EnvironmentCount + " env(s)" };
                    if (m.HasDeployCommand) cov.Add("Deploy");
                    if (m.HasRollbackCommand) cov.Add("Rollback");
                    if (m.HasSmokeTestCommand) cov.Add("Smoke");
                    if (m.HasHealthCheckCommand) cov.Add("Health");
                    if (m.HasDeploymentVerificationCommand) cov.Add("Deploy Verify");
                    if (m.HasRollbackVerificationCommand) cov.Add("Rollback Verify");
                    doc.Field("Delivery coverage", String.Join(", ", cov));
                }

                if (readiness.SetupChecklist.Count > 0)
                {
                    doc.Section("Setup checklist", "  " + readiness.SetupChecklistSatisfiedCount + "/" + readiness.SetupChecklistTotalCount + " " + doc.Loc.T("complete"));
                    foreach (VesselSetupChecklistItem item in readiness.SetupChecklist)
                    {
                        doc.Add(StyledText.From((item.IsSatisfied ? "[x] " : "[ ] ") + item.Title + "  ", theme.Text)
                            .Append(StyledText.From(item.IsSatisfied ? doc.Loc.T("Done") : item.Severity.ToString(), item.IsSatisfied ? theme.Success : theme.Warning)));
                        doc.Text("    " + item.Message, theme.Muted);
                        if (!item.IsSatisfied && !String.IsNullOrEmpty(item.ActionLabel) && !String.IsNullOrEmpty(item.ActionRoute))
                            doc.Text("    " + item.ActionLabel + ": " + item.ActionRoute, theme.Info);
                    }
                }
            }

            if (readiness.Issues.Count > 0)
            {
                foreach (VesselReadinessIssue issue in readiness.Issues)
                {
                    CellStyle sev = issue.Severity.ToString() == "Error" ? theme.Error : issue.Severity.ToString() == "Warning" ? theme.Warning : theme.Info;
                    doc.Add(StyledText.From("[" + issue.Severity + "] ", sev).Append(StyledText.From(issue.Title, theme.Text.WithAttribute(CellAttributes.Bold, true))));
                    doc.Text("    " + issue.Message, theme.Muted);
                    if (!String.IsNullOrEmpty(issue.RelatedValue)) doc.Text("    " + issue.RelatedValue + ProviderSuffix(issue.RelatedValue!), theme.Code);
                }
            }
            else
            {
                doc.Note("This vessel looks ready for the currently selected workflow surface.", theme.Success);
            }

            return doc;
        }

        /// <summary>
        /// Show the full readiness panel in a viewer.
        /// </summary>
        /// <param name="screen">Screen.</param>
        /// <param name="readiness">Readiness, or null.</param>
        /// <param name="loading">Loading flag.</param>
        /// <returns>The viewer.</returns>
        public static ViewerModal ShowDetails(OpsScreen screen, VesselReadinessResult? readiness, bool loading)
        {
            OpsDocumentView view = new OpsDocumentView();
            view.Builder = doc => Build(doc, "Vessel Readiness", readiness, loading, "Select a vessel to inspect readiness.", false);
            ViewerModal modal = new ViewerModal(screen.Tr("Vessel Readiness"), view, screen.Context.Loc, screen.Context.Theme.Current);
            modal.CopyRequested += (s, e) => screen.Context.Clipboard.Copy(view.PlainText, "Readiness");
            screen.Context.Modals.Show(modal);
            return modal;
        }

        #endregion

        #region Private-Methods

        private static string ProviderSuffix(string related)
        {
            if (related.StartsWith("env:", StringComparison.Ordinal)) return " (Environment variable)";
            if (related.StartsWith("file:", StringComparison.Ordinal)) return " (File path)";
            if (related.StartsWith("dir:", StringComparison.Ordinal)) return " (Directory path)";
            return "";
        }

        #endregion
    }
}
