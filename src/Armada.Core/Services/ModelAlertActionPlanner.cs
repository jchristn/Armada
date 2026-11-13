namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Works out the suggested actions of a model alert and whether each can be applied now (docs/MODEL_HETEROGENEITY.md,
    /// Alerts). Read only: it never changes a persona, captain, template or routing.
    /// </summary>
    public class ModelAlertActionPlanner
    {
        #region Public-Members

        /// <summary>
        /// Why the compact template action is unavailable: there is no prompt variant per model profile and persona.
        /// </summary>
        public const string CompactUnavailableReason = "Armada has no prompt variant per model profile and persona: the variant comes from a voyage captain override or from the captain's promptVariant runtime option, which applies to every persona the captain serves, and the profile's Rendering.Template is not applied at launch yet. Compare variants with run_battery (promptVariant Compact), then set promptVariant on the captain if it helps.";

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public ModelAlertActionPlanner(DatabaseDriver database)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the plan of an alert.
        /// </summary>
        /// <param name="alert">Alert.</param>
        /// <param name="viewer">Caller whose persona visibility applies, or null to accept the alert tenant's personas.</param>
        /// <param name="defaultIgnoreDays">Days an Ignore hides the alert by default.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="ArgumentNullException">Thrown when alert is null.</exception>
        public async Task<ModelAlertActionPlan> PlanAsync(ModelAlert alert, AuthContext? viewer, int defaultIgnoreDays, CancellationToken token = default)
        {
            if (alert == null) throw new ArgumentNullException(nameof(alert));
            ModelAlertActionPlan plan = new ModelAlertActionPlan();

            List<Captain> tenantCaptains = String.IsNullOrEmpty(alert.TenantId)
                ? await _Database.Captains.EnumerateAsync(token).ConfigureAwait(false)
                : await _Database.Captains.EnumerateAsync(alert.TenantId!, token).ConfigureAwait(false);
            plan.ProfileCaptains = tenantCaptains
                .Where(c => String.Equals(c.ModelProfileId, alert.ProfileId, StringComparison.Ordinal))
                .OrderBy(c => c.State == CaptainStateEnum.Idle ? 0 : 1).ThenBy(c => c.Id, StringComparer.Ordinal)
                .ToList();
            HashSet<string> onProfile = new HashSet<string>(plan.ProfileCaptains.Select(c => c.Id), StringComparer.Ordinal);
            List<Captain> others = tenantCaptains.Where(c => !onProfile.Contains(c.Id)).ToList();

            Persona? persona = String.IsNullOrWhiteSpace(alert.Persona) ? null : await _Database.Personas.ReadByNameAsync(alert.Persona, token).ConfigureAwait(false);
            if (persona != null && !Visible(persona, alert, viewer)) persona = null;
            plan.Persona = persona;
            if (persona != null)
            {
                for (int i = 0; i < persona.CaptainPriorities.Count; i++)
                {
                    PersonaCaptainPriority entry = persona.CaptainPriorities[i];
                    if (String.IsNullOrWhiteSpace(entry.Model)) continue;
                    if (!plan.ProfileCaptains.Any(c => PersonaCaptainPrioritySelector.Matches(entry, c))) continue;
                    if (others.Any(c => PersonaCaptainPrioritySelector.Matches(entry, c))) continue;
                    plan.EntryIndexes.Add(i);
                }
                plan.DefaultCaptainOnProfile = !String.IsNullOrEmpty(persona.DefaultCaptainId) && onProfile.Contains(persona.DefaultCaptainId!);
            }

            string personaName = String.IsNullOrWhiteSpace(alert.Persona) ? "persona" : alert.Persona;
            string model = String.IsNullOrWhiteSpace(alert.Model) ? alert.ProfileId : alert.Model!;
            string? routingReason = RoutingUnavailableReason(plan, personaName);

            ModelAlertSuggestedAction moveDown = new ModelAlertSuggestedAction
            {
                Action = ModelAlertActionEnum.MoveDown,
                Label = "Move down in " + personaName + " priorities",
                Description = "Moves the " + personaName + " captain priority entries that select only " + model + " captains to the bottom of the list."
            };
            int count = persona?.CaptainPriorities.Count ?? 0;
            bool alreadyLast = plan.EntryIndexes.Count > 0 && plan.EntryIndexes.SequenceEqual(Enumerable.Range(count - plan.EntryIndexes.Count, plan.EntryIndexes.Count));
            string? moveReason = routingReason ?? (plan.EntryIndexes.Count == 0
                ? "No " + personaName + " captain priority entry selects only " + model + " captains."
                : (alreadyLast ? "The entries are already at the bottom of the " + personaName + " priorities." : null));
            SetAvailability(moveDown, moveReason);
            plan.Actions.Add(moveDown);

            ModelAlertSuggestedAction remove = new ModelAlertSuggestedAction
            {
                Action = ModelAlertActionEnum.RemoveFromPersona,
                Label = "Remove from " + personaName,
                Description = "Removes the " + personaName + " captain priority entries that select only " + model + " captains, and clears the default captain when it is on this profile."
            };
            SetAvailability(remove, routingReason ?? (plan.EntryIndexes.Count == 0 && !plan.DefaultCaptainOnProfile
                ? "No " + personaName + " captain priority entry or default captain selects only " + model + " captains."
                : null));
            plan.Actions.Add(remove);

            plan.Actions.Add(new ModelAlertSuggestedAction
            {
                Action = ModelAlertActionEnum.CompactTemplate,
                Label = "Switch to the compact template",
                Description = "Render the Compact prompt variant for " + model + " as " + personaName + ".",
                Available = false,
                UnavailableReason = CompactUnavailableReason,
                Recommended = alert.Rule == ModelAlertRuleEnum.ProtocolRepairRate
            });

            Captain? evaluateCaptain = plan.ProfileCaptains.FirstOrDefault();
            ModelAlertSuggestedAction reevaluate = new ModelAlertSuggestedAction
            {
                Action = ModelAlertActionEnum.Reevaluate,
                Label = "Re-evaluate",
                Description = evaluateCaptain == null
                    ? "Starts a Quick Evaluate of a captain on this profile, within the evaluation budgets."
                    : "Starts a Quick Evaluate of captain " + evaluateCaptain.Name + " (" + evaluateCaptain.Id + "), within the evaluation budgets."
            };
            SetAvailability(reevaluate, evaluateCaptain == null ? "No captain is linked to this profile." : null);
            plan.Actions.Add(reevaluate);

            plan.Actions.Add(new ModelAlertSuggestedAction
            {
                Action = ModelAlertActionEnum.Ignore,
                Label = "Ignore for " + defaultIgnoreDays + " days",
                Description = "Hides this alert from the inbox for " + defaultIgnoreDays + " days (or the days given); it shows again afterwards if the condition still holds."
            });
            return plan;
        }

        #endregion

        #region Private-Methods

        private static string? RoutingUnavailableReason(ModelAlertActionPlan plan, string personaName)
        {
            if (plan.Persona == null) return "Persona " + personaName + " was not found.";
            if (plan.ProfileCaptains.Count == 0) return "No captain is linked to this profile.";
            return null;
        }

        private static void SetAvailability(ModelAlertSuggestedAction action, string? reason)
        {
            action.Available = reason == null;
            action.UnavailableReason = reason;
        }

        private static bool Visible(Persona persona, ModelAlert alert, AuthContext? viewer)
        {
            if (viewer != null) return ScopedVisibility.CanView(viewer, persona.Scope, persona.TenantId, persona.UserId);
            return String.IsNullOrEmpty(persona.TenantId) || String.Equals(persona.TenantId, alert.TenantId, StringComparison.Ordinal);
        }

        #endregion
    }
}
