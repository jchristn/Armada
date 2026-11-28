namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Globalization;
    using Armada.Core.Models;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;

    /// <summary>
    /// Model profile detail (<c>/model-profiles/:id</c>), the dashboard's model profile page: served context and its
    /// source, configured versus probed tools and MCP, tier and the outcome-data suggestion, recovery policy and
    /// timeouts (override, value in effect, and source), rendering and measured speed. Admins edit the tier, recovery
    /// policy and timeouts. Not thread-safe.
    /// </summary>
    public class ModelProfileScreen : OpsDetailScreen
    {
        #region Public-Members

        /// <summary>
        /// Profile ID from the route.
        /// </summary>
        public string ProfileId { get; }

        /// <summary>
        /// The loaded profile, or null.
        /// </summary>
        public ModelProfile? Profile { get; private set; } = null;

        /// <summary>
        /// Overview panel.
        /// </summary>
        public OpsDocumentView Overview { get; } = new OpsDocumentView();

        /// <summary>
        /// Recovery and timeouts panel.
        /// </summary>
        public OpsDocumentView Policy { get; } = new OpsDocumentView();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        public ModelProfileScreen(RouteMatch route, TuiContext context)
            : base(route, context, "ModelProfileScreen", "Model Profile")
        {
            ProfileId = route.Param("id") ?? "";
            Action("edit", "Edit", () => ModelProfileForms.Edit(Context, Profile!, Load), "e", () => Profile != null && IsAdmin, true);
            Action("json", "View JSON", () => ShowJson(Tr("Model Profile") + ": " + ProfileId, Profile), "j", () => Profile != null, true);
            Action("copy-id", "Copy ID", () => Copy(ProfileId, "Model Profile ID"), "y");
            Action("back", "Back to Model Profiles", () => Context.Navigate("/captains?tab=profiles"), "b");

            Overview.Builder = BuildOverview;
            Policy.Builder = BuildPolicy;
            AddPanel("overview", "Model Profile", Overview);
            AddPanel("policy", "Recovery and Timeouts", Policy);
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Load()
        {
            bool initial = !Loaded;
            Call((c, t) => c.ModelProfiles.GetAsync(ProfileId, t), p =>
            {
                if (p == null)
                {
                    if (initial) LoadError = Tr("Failed to load model profile.");
                    return;
                }

                Profile = p;
                Loaded = true;
                LoadError = null;
                Heading = p.Identity.Model ?? p.Id;
                Status = p.Tiering.Tier?.ToString() ?? "";
                SubtitleText = Tr("Model Profiles") + " / " + p.Id;
                Overview.Invalidate();
                Policy.Invalidate();
            }, null, ex =>
            {
                if (initial) LoadError = Tr("Failed to load model profile.");
            });
        }

        #endregion

        #region Private-Methods

        private OpsDocument BuildOverview(OpsDocument doc)
        {
            ModelProfile? p = Profile;
            if (p == null) return doc;
            ModelProfileIdentity id = p.Identity;
            doc.Field("ID", p.Id);
            doc.Field("Model", id.Model ?? "-");
            doc.Field("Runtime", id.Runtime);
            doc.Field("Provider", id.Provider ?? "-");
            doc.Field("Served context", id.ServedContextTokens.HasValue
                ? id.ServedContextTokens.Value.ToString("N0", CultureInfo.InvariantCulture) + " " + Tr("tokens") + " (" + Tr(id.ServedContextSource?.ToString() ?? "unknown source") + ")"
                : Tr("Unknown"));
            doc.Field("Tools", Capability(p.Capabilities.ToolsAvailable, p.Capabilities.ProbedToolsAvailable));
            doc.Field("MCP", Capability(p.Capabilities.McpAvailable, p.Capabilities.ProbedMcpAvailable));
            if (p.Capabilities.ProbedUtc.HasValue) doc.Field("Probed", Context.Loc.FormatDateTime(p.Capabilities.ProbedUtc.Value));

            doc.Section("Tier");
            doc.Field("Tier", p.Tiering.Tier?.ToString() ?? Tr("None"));
            if (p.Tiering.SuggestedTier.HasValue)
            {
                doc.Field("Suggested tier", p.Tiering.SuggestedTier.Value.ToString());
                if (!String.IsNullOrEmpty(p.Tiering.SuggestedReason)) doc.Field("Why", p.Tiering.SuggestedReason);
                if (p.Tiering.SuggestedSamples.HasValue) doc.Field("Samples", p.Tiering.SuggestedSamples.Value.ToString(CultureInfo.InvariantCulture));
                doc.Note("A suggestion never changes routing until an administrator applies it (Needs You, or Edit here).");
            }

            if (!String.IsNullOrEmpty(p.Notes))
            {
                doc.Section("Notes");
                doc.Text(p.Notes);
            }

            doc.Time("Last Updated", p.LastUpdateUtc, Context.Clock.UtcNow);
            return doc;
        }

        private OpsDocument BuildPolicy(OpsDocument doc)
        {
            ModelProfile? p = Profile;
            if (p == null) return doc;
            ModelProfileRecovery r = p.Recovery;
            RecoveryPolicy? e = p.EffectiveRecovery;
            doc.Section("Recovery", Tr("override / in effect"));
            doc.Field("Max resumes", Pair(r.MaxResumes, e?.MaxResumes));
            doc.Field("Unclassified resumes", Pair(r.UnclassifiedResumes, null));
            doc.Field("Transient grace minutes", Pair(r.TransientGraceMinutes, e?.TransientGraceMinutes));
            doc.Field("Judge rework rounds", Pair(r.JudgeReworkRounds, e?.JudgeReworkRounds));
            doc.Field("Output repair attempts", Pair(r.OutputRepairAttempts, e?.OutputRepairAttempts));
            doc.Field("Escalate after resumes", Pair(r.EscalateAfterResumes, e?.EscalateAfterResumes));

            ModelProfileTimeouts t = p.Timeouts;
            TimeoutPolicy? te = p.EffectiveTimeouts;
            doc.Section("Timeouts in minutes", Tr("override / in effect, source"));
            doc.Field("Stall threshold", Pair(t.StallThresholdMinutes, te?.StallThresholdMinutes) + Source(te?.StallThresholdSource.ToString()));
            doc.Field("Tool wait hard limit", Pair(t.ToolWaitHardLimitMinutes, te?.ToolWaitHardLimitMinutes) + Source(te?.ToolWaitHardLimitSource.ToString()));
            doc.Field("Max runtime", Pair(t.MaxRuntimeMinutes, te?.MaxRuntimeMinutes) + Source(te?.MaxRuntimeSource.ToString()));
            doc.Field("First output", Pair(t.FirstOutputMinutes, te?.FirstOutputMinutes) + Source(te?.FirstOutputSource.ToString()));
            if (te != null && te.SpeedScale != 1) doc.Note(Tr("Scaled by measured speed") + " x" + te.SpeedScale.ToString("0.##", CultureInfo.InvariantCulture));
            return doc;
        }

        private string Capability(bool? configured, bool? probed)
        {
            if (configured.HasValue) return Tr(configured.Value ? "Yes" : "No") + " (" + Tr("set by admin") + ")";
            if (probed.HasValue) return Tr(probed.Value ? "Yes" : "No") + " (" + Tr("probed") + ")";
            return Tr("Unknown");
        }

        private static string Pair(int? overrideValue, int? effective)
        {
            string o = overrideValue.HasValue ? overrideValue.Value.ToString(CultureInfo.InvariantCulture) : "-";
            string v = effective.HasValue ? effective.Value.ToString(CultureInfo.InvariantCulture) : "-";
            return o + " / " + v;
        }

        private static string Source(string? source)
        {
            return String.IsNullOrEmpty(source) ? "" : ", " + source;
        }

        #endregion
    }
}
