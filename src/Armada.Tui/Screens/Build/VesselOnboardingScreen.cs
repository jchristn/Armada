namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using TUIKit;

    /// <summary>
    /// Vessel onboarding (W4.4, <c>/vessels/:id/onboarding</c>), the dashboard's VesselOnboarding page: completed
    /// steps, blocking issues, warnings, and environments; the next recommended step (<c>n</c> follows its action);
    /// the current readiness; the setup checklist grouped into Repository Basics, Workflow Profile, and Delivery
    /// Readiness; and the open issues. The Steps panel lists every unfinished step with an action (<c>Enter</c>
    /// follows it). Open Workspace, Run Check, and Back To Vessel. Not thread-safe.
    /// </summary>
    public class VesselOnboardingScreen : OpsDetailScreen
    {
        #region Public-Members

        /// <summary>
        /// Checklist groups: title and the item codes in each.
        /// </summary>
        public static readonly IReadOnlyList<KeyValuePair<string, string[]>> Groups = new List<KeyValuePair<string, string[]>>
        {
            new KeyValuePair<string, string[]>("Repository Basics", new string[] { "working_directory", "repository_context", "default_branch", "toolchains" }),
            new KeyValuePair<string, string[]>("Workflow Profile", new string[] { "workflow_profile", "workflow_profile_valid", "required_inputs" }),
            new KeyValuePair<string, string[]>("Delivery Readiness", new string[] { "deployment_environments", "branch_policy", "deploy_workflow" }),
        };

        /// <summary>
        /// Vessel id from the route.
        /// </summary>
        public string VesselId { get; }

        /// <summary>
        /// The vessel once loaded.
        /// </summary>
        public Vessel? Vessel { get; private set; } = null;

        /// <summary>
        /// Readiness once loaded.
        /// </summary>
        public VesselReadinessResult? Readiness { get; private set; } = null;

        /// <summary>
        /// Overview panel.
        /// </summary>
        public OpsDocumentView Overview { get; } = new OpsDocumentView();

        /// <summary>
        /// Unfinished steps with actions.
        /// </summary>
        public OpsLinkList Steps { get; } = new OpsLinkList();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public VesselOnboardingScreen(RouteMatch route, TuiContext context)
            : base(route, context, "VesselOnboardingScreen", "Vessel Onboarding")
        {
            VesselId = route.Param("id") ?? "";
            Action("workspace", "Open Workspace", () => Context.Navigate("/workspace/" + Uri.EscapeDataString(VesselId)), "w", () => Vessel != null, true);
            Action("run-check", "Run Check", RunCheck, "k", () => Vessel != null, true);
            Action("back", "Back To Vessel", () => Context.Navigate("/vessels/" + Uri.EscapeDataString(VesselId)), "b", null, true);
            Action("next", "Next Recommended Step", FollowNext, "n", () => NextItem() != null && !String.IsNullOrEmpty(NextItem()!.ActionRoute));
            Overview.Builder = BuildOverview;
            AddPanel("overview", "Onboarding", Overview);
            AddPanel("steps", "Steps", Steps);
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Load()
        {
            bool initial = !Loaded;
            Call(async (c, t) =>
            {
                Task<Vessel?> vessel = c.GetVesselAsync(VesselId, t);
                Task<VesselReadinessResult?> readiness = c.GetVesselReadinessAsync(VesselId, null, t);
                await Task.WhenAll(vessel, readiness).ConfigureAwait(false);
                return new KeyValuePair<Vessel?, VesselReadinessResult?>(vessel.Result, readiness.Result);
            }, r =>
            {
                if (r.Key == null)
                {
                    LoadError = Tr("Vessel not found.");
                    return;
                }

                Vessel = r.Key;
                Readiness = r.Value;
                Loaded = true;
                LoadError = null;
                Heading = Tr("Vessel Onboarding");
                SubtitleText = Tr("Vessels") + " > " + Vessel.Name + " > " + Tr("Onboarding");
                Overview.Invalidate();
                BuildSteps();
            }, null, ex =>
            {
                if (initial) LoadError = String.IsNullOrEmpty(ex.Message) ? Tr("Failed to load vessel onboarding.") : ex.Message;
            });
        }

        /// <summary>
        /// The first unfinished checklist item, or null.
        /// </summary>
        /// <returns>Item or null.</returns>
        public VesselSetupChecklistItem? NextItem()
        {
            return Readiness?.SetupChecklist?.FirstOrDefault(i => !i.IsSatisfied);
        }

        #endregion

        #region Private-Methods

        private void FollowNext()
        {
            VesselSetupChecklistItem? next = NextItem();
            if (next != null && !String.IsNullOrEmpty(next.ActionRoute)) Context.Navigate(next.ActionRoute!);
        }

        private void RunCheck()
        {
            if (Vessel == null) return;
            Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
            q["tab"] = "checks";
            q["prefill"] = "1";
            q["vesselId"] = Vessel.Id;
            q["branchName"] = Vessel.DefaultBranch ?? "";
            Context.Navigate("/delivery" + RouteMatch.BuildQuery(q));
        }

        private void BuildSteps()
        {
            List<OpsLinkItem> items = new List<OpsLinkItem>();
            foreach (VesselSetupChecklistItem item in Readiness?.SetupChecklist ?? new List<VesselSetupChecklistItem>())
            {
                if (item.IsSatisfied || String.IsNullOrEmpty(item.ActionLabel) || String.IsNullOrEmpty(item.ActionRoute)) continue;
                string route = item.ActionRoute!;
                items.Add(new OpsLinkItem(item.Title + ": " + item.ActionLabel, () => Context.Navigate(route), null, route));
            }

            if (items.Count == 0) items.Add(new OpsLinkItem(Tr("Every setup step with an action is complete."), null));
            Steps.Items = items;
        }

        private OpsDocument BuildOverview(OpsDocument doc)
        {
            Vessel? v = Vessel;
            if (v == null) return doc;
            VesselReadinessResult? r = Readiness;
            doc.Note("Use this checklist to take the vessel from registration through workflow-ready onboarding.");
            doc.Blank();
            doc.Text(Tr("Completed") + " " + (r?.SetupChecklistSatisfiedCount ?? 0) + "/" + (r?.SetupChecklistTotalCount ?? 0)
                + "  |  " + Tr("Blocking Issues") + " " + (r?.ErrorCount ?? 0)
                + "  |  " + Tr("Warnings") + " " + (r?.WarningCount ?? 0)
                + "  |  " + Tr("Environments") + " " + (r?.DeploymentEnvironments?.Count ?? 0), doc.Theme.Accent);
            VesselSetupChecklistItem? next = NextItem();
            if (next != null)
            {
                doc.Section("Next Recommended Step");
                doc.Text(next.Title, doc.Theme.Text.WithAttribute(CellAttributes.Bold, true));
                doc.Text(next.Message, doc.Theme.Muted);
                if (!String.IsNullOrEmpty(next.ActionLabel) && !String.IsNullOrEmpty(next.ActionRoute)) doc.Text("n  " + next.ActionLabel + "  (" + next.ActionRoute + ")", doc.Theme.Info);
            }

            doc.Blank();
            OpsReadiness.Build(doc, "Current Readiness", r, false, "Readiness data is not available for this vessel yet.", true);
            foreach (KeyValuePair<string, string[]> group in Groups)
            {
                List<VesselSetupChecklistItem> items = (r?.SetupChecklist ?? new List<VesselSetupChecklistItem>()).Where(i => group.Value.Contains(i.Code)).ToList();
                if (items.Count == 0) continue;
                doc.Section(group.Key);
                foreach (VesselSetupChecklistItem item in items)
                {
                    doc.Add(StyledText.From((item.IsSatisfied ? "[x] " : "[ ] ") + item.Title + "  ", doc.Theme.Text)
                        .Append(StyledText.From(item.IsSatisfied ? Tr("Done") : item.Severity.ToString(), item.IsSatisfied ? doc.Theme.Success : doc.Theme.Warning)));
                    doc.Text("    " + item.Message, doc.Theme.Muted);
                    if (!item.IsSatisfied && !String.IsNullOrEmpty(item.ActionLabel) && !String.IsNullOrEmpty(item.ActionRoute))
                        doc.Text("    " + item.ActionLabel + ": " + item.ActionRoute, doc.Theme.Info);
                }
            }

            if (r != null && r.Issues != null && r.Issues.Count > 0)
            {
                doc.Section("Open Issues");
                foreach (VesselReadinessIssue issue in r.Issues)
                {
                    CellStyle sev = issue.Severity == ReadinessSeverityEnum.Error ? doc.Theme.Error : issue.Severity == ReadinessSeverityEnum.Warning ? doc.Theme.Warning : doc.Theme.Info;
                    doc.Add(StyledText.From("[" + issue.Severity + "] ", sev).Append(StyledText.From(issue.Title, doc.Theme.Text)));
                    doc.Text("    " + issue.Message, doc.Theme.Muted);
                    if (!String.IsNullOrEmpty(issue.RelatedValue)) doc.Text("    " + issue.RelatedValue + OpsReadiness.ProviderSuffix(issue.InputProvider), doc.Theme.Code);
                }
            }

            doc.Blank();
            doc.Note("] " + Tr("Steps") + ": Enter " + Tr("Open"));
            return doc;
        }

        #endregion
    }
}
