namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The model profile editor: tier, recovery policy and timeouts. A blank number clears that override so the global
    /// setting applies. The limits match the server's.
    /// </summary>
    public static class ModelProfileForms
    {
        /// <summary>
        /// Open the editor for a profile.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="profile">Profile to edit.</param>
        /// <param name="onSaved">Called after a successful save.</param>
        /// <returns>The dialog.</returns>
        /// <exception cref="ArgumentNullException">When context or profile is null.</exception>
        public static Modals.FormDialog Edit(TuiContext context, ModelProfile profile, Action? onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            EntityForm form = new EntityForm(context);
            List<SelectOption<string>> tiers = new List<SelectOption<string>>
            {
                new SelectOption<string>(CaptainTierEnum.Economy.ToString(), context.Loc.T("Economy")),
                new SelectOption<string>(CaptainTierEnum.Standard.ToString(), context.Loc.T("Standard")),
                new SelectOption<string>(CaptainTierEnum.Premium.ToString(), context.Loc.T("Premium"))
            };
            SelectField<string> tier = form.Select("Tier", tiers, profile.Tiering.Tier?.ToString(), "None", false, "Routing tier; the outcome-data suggestion never applies on its own");
            ModelProfileRecovery r = profile.Recovery;
            InputField maxResumes = form.Number("Max resumes", r.MaxResumes, 0, 10, false, "0 to 10; blank uses the global setting");
            InputField unclassified = form.Number("Unclassified resumes", r.UnclassifiedResumes, 0, 3, false, "0 to 3; blank uses the global setting");
            InputField grace = form.Number("Transient grace minutes", r.TransientGraceMinutes, 0, 120, false, "0 to 120; blank uses the global setting");
            InputField rework = form.Number("Judge rework rounds", r.JudgeReworkRounds, 1, 5, false, "1 to 5; raises but never lowers the global value");
            InputField repairs = form.Number("Output repair attempts", r.OutputRepairAttempts, 0, 3, false, "0 to 3; blank uses the global setting");
            InputField escalate = form.Number("Escalate after resumes", r.EscalateAfterResumes, 0, 10, false, "0 to 10; hands a task failure to the persona escalation ladder");
            ModelProfileTimeouts t = profile.Timeouts;
            InputField stall = form.Number("Stall threshold minutes", t.StallThresholdMinutes, 1, 240, false, "1 to 240; blank uses the global setting");
            InputField toolWait = form.Number("Tool wait hard limit minutes", t.ToolWaitHardLimitMinutes, 1, 1440, false, "1 to 1440; blank uses the global setting");
            InputField maxRuntime = form.Number("Max runtime minutes", t.MaxRuntimeMinutes, 1, 2880, false, "1 to 2880; blank uses the global setting");
            InputField firstOutput = form.Number("First output minutes", t.FirstOutputMinutes, 1, 240, false, "1 to 240; blank uses the global setting");
            form.MarkClean();

            return EntityUi.ShowForm(context, "Edit Model Profile", form, "Save", async ct =>
            {
                ModelProfileUpdateRequest request = new ModelProfileUpdateRequest();
                ModelProfileTiering tiering = new ModelProfileTiering();
                if (!String.IsNullOrEmpty(tier.Value) && Enum.TryParse<CaptainTierEnum>(tier.Value, out CaptainTierEnum parsed)) tiering.Tier = parsed;
                request.Tiering = tiering;
                request.Recovery = new ModelProfileRecovery
                {
                    MaxResumes = EntityForm.IntValue(maxResumes),
                    UnclassifiedResumes = EntityForm.IntValue(unclassified),
                    TransientGraceMinutes = EntityForm.IntValue(grace),
                    JudgeReworkRounds = EntityForm.IntValue(rework),
                    OutputRepairAttempts = EntityForm.IntValue(repairs),
                    EscalateAfterResumes = EntityForm.IntValue(escalate)
                };
                request.Timeouts = new ModelProfileTimeouts
                {
                    StallThresholdMinutes = EntityForm.IntValue(stall),
                    ToolWaitHardLimitMinutes = EntityForm.IntValue(toolWait),
                    MaxRuntimeMinutes = EntityForm.IntValue(maxRuntime),
                    FirstOutputMinutes = EntityForm.IntValue(firstOutput)
                };
                await context.Client.ModelProfiles.UpdateAsync(profile.Id, request, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                EntityUi.Toast(context, NotificationSeverityEnum.Success, context.Loc.T("Model profile saved."));
                onSaved?.Invoke();
            });
        }
    }
}
