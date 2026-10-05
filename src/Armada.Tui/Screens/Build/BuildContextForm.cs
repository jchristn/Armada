namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The dashboard's Build Model Context / Refine Model Context dialog: pick a captain (the first is preselected) and
    /// optional focus notes, then launch the captain to write or refine the vessel's Model Context. The call runs
    /// synchronously on the server and can take minutes; the dialog shows the dashboard's progress line meanwhile.
    /// </summary>
    public static class BuildContextForm
    {
        #region Public-Methods

        /// <summary>
        /// True when the vessel already has Model Context (the dialog refines instead of building).
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <returns>True to refine.</returns>
        public static bool IsRefine(Vessel vessel)
        {
            return vessel != null && !String.IsNullOrWhiteSpace(vessel.ModelContext);
        }

        /// <summary>
        /// Open the dialog.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="vessel">Vessel.</param>
        /// <param name="built">Called on the loop with the updated vessel.</param>
        /// <returns>The dialog.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static OpsFormDialog Open(OpsScreen screen, Vessel vessel, Action<Vessel> built)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            bool refine = IsRefine(vessel);
            OpsFormDialog dialog = screen.NewForm(refine ? "Refine Model Context" : "Build Model Context", refine ? "Refine Context" : "Build Context");
            dialog.Intro = refine
                ? screen.Tr("Launch a captain to inspect {{name}} and refine its existing Model Context.", LocalizationArgs.Of("name", vessel.Name))
                : screen.Tr("Launch a captain to inspect {{name}} and write its Model Context.", LocalizationArgs.Of("name", vessel.Name));
            dialog.Notes.Add(screen.Tr("The captain follows the editable \"vessel.build_context\" prompt (Configuration > Prompts). This runs synchronously and can take a few minutes."));
            SelectField<string> captain = screen.NewSelect("Captain", new List<SelectOption<string>>(), "No captains available");
            captain.Required = true;
            OpsTextArea notes = new OpsTextArea();
            notes.Placeholder = "e.g. Emphasize the build and test commands, and how the plugin system works.";
            notes.ExternalEditor = (text, done) => screen.EditExternally(text, done);
            dialog.AddField("Captain", captain);
            dialog.AddField("Focus / guidance (optional)", notes, null, 4);
            screen.Call((c, t) => c.ListCaptainsAsync(new ArmadaPageQuery(1, 200), t), result =>
            {
                List<Captain> captains = result?.Objects ?? new List<Captain>();
                captain.Options = captains.Select(c => new SelectOption<string>(c.Id, c.Name + " (" + (String.IsNullOrEmpty(c.Model) ? c.Runtime.ToString() : c.Model) + ")")).ToList();
                if (captains.Count > 0 && String.IsNullOrEmpty(captain.Value)) captain.SetValue(captains[0].Id);
            }, null, ex => dialog.Error = String.IsNullOrEmpty(ex.Message) ? screen.Tr("Failed to load captains.") : ex.Message);
            dialog.Submit = d =>
            {
                BuildVesselContextRequest request = new BuildVesselContextRequest();
                request.CaptainId = captain.Value ?? "";
                request.Notes = String.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim();
                d.Notes.Add(refine ? screen.Tr("Refining Model Context... this can take a few minutes.") : screen.Tr("Building Model Context... this can take a few minutes."));
                screen.Call((c, t) => c.BuildVesselContextAsync(vessel.Id, request, t), updated =>
                {
                    d.Complete();
                    if (updated == null) return;
                    screen.Toast(NotificationSeverityEnum.Success, screen.Tr("Model Context updated for \"{{name}}\".", LocalizationArgs.Of("name", updated.Name)));
                    built?.Invoke(updated);
                }, null, ex =>
                {
                    if (d.Notes.Count > 1) d.Notes.RemoveAt(d.Notes.Count - 1);
                    d.Fail(ex is ArmadaApiException api && !String.IsNullOrEmpty(api.Message) ? api.Message : screen.Tr("Failed to build Model Context."));
                });
                return false;
            };
            screen.Context.Modals.Show(dialog);
            return dialog;
        }

        #endregion
    }
}
