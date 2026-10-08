namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The dashboard's Create Fleet / Edit Fleet dialog (name, description, default pipeline), shared by the Fleets
    /// tab and the fleet page. Edits send the full record so nothing the form does not show is cleared.
    /// </summary>
    public static class FleetForm
    {
        #region Public-Methods

        /// <summary>
        /// Open the dialog.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="editing">Fleet to edit, or null to create.</param>
        /// <param name="pipelines">Pipelines for the default pipeline picker.</param>
        /// <param name="saved">Called on the loop with the saved fleet.</param>
        /// <returns>The dialog.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="screen"/> is null.</exception>
        public static OpsFormDialog Open(OpsScreen screen, Fleet? editing, IEnumerable<Pipeline> pipelines, Action<Fleet?> saved)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            OpsFormDialog dialog = screen.NewForm(editing != null ? "Edit Fleet" : "Create Fleet", "Save");
            InputField name = new InputField();
            name.Value = editing?.Name ?? "";
            name.Validator = v => String.IsNullOrWhiteSpace(v) ? "Name is required." : null;
            InputField description = new InputField();
            description.Value = editing?.Description ?? "";
            SelectField<string> pipeline = screen.NewSelect("Default Pipeline", BuildText.PipelineOptions(pipelines, screen.Context.Loc));
            pipeline.SetValue(editing?.DefaultPipelineId ?? "");
            dialog.AddField("Name", name);
            dialog.AddField("Description", description);
            dialog.AddField("Default Pipeline", pipeline);
            dialog.Submit = d =>
            {
                Fleet body = editing != null ? Copy(editing) : new Fleet(name.Value.Trim());
                body.Name = name.Value.Trim();
                body.Description = String.IsNullOrEmpty(description.Value) ? null : description.Value;
                body.DefaultPipelineId = String.IsNullOrEmpty(pipeline.Value) ? null : pipeline.Value;
                string label = body.Name;
                if (editing != null)
                {
                    screen.Call((c, t) => c.UpdateFleetAsync(editing.Id, body, t), r =>
                    {
                        d.Complete();
                        screen.Toast(NotificationSeverityEnum.Success, screen.Tr("Fleet \"{{name}}\" saved.", LocalizationArgs.Of("name", label)));
                        saved?.Invoke(r);
                    }, null, ex => d.Fail(ex, screen.Tr("Save failed.")));
                }
                else
                {
                    screen.Call((c, t) => c.CreateFleetAsync(body, t), r =>
                    {
                        d.Complete();
                        screen.Toast(NotificationSeverityEnum.Success, screen.Tr("Fleet \"{{name}}\" created.", LocalizationArgs.Of("name", label)));
                        saved?.Invoke(r);
                    }, null, ex => d.Fail(ex, screen.Tr("Save failed.")));
                }

                return false;
            };
            screen.Context.Modals.Show(dialog);
            return dialog;
        }

        /// <summary>
        /// The dashboard's duplicate payload for a fleet: <c>Name (Copy)</c>, description, and default pipeline.
        /// </summary>
        /// <param name="fleet">Fleet.</param>
        /// <returns>New fleet body.</returns>
        public static Fleet DuplicatePayload(Fleet fleet)
        {
            if (fleet == null) throw new ArgumentNullException(nameof(fleet));
            Fleet copy = new Fleet(BuildText.DuplicateName(fleet.Name));
            copy.Description = fleet.Description;
            copy.DefaultPipelineId = fleet.DefaultPipelineId;
            return copy;
        }

        #endregion

        #region Private-Methods

        private static Fleet Copy(Fleet source)
        {
            Fleet copy = Armada.Client.ArmadaJson.Deserialize<Fleet>(Armada.Client.ArmadaJson.Serialize(source)) ?? new Fleet(source.Name);
            return copy;
        }

        #endregion
    }
}
