namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The dashboard's FleetActionFormModal: create, edit, and duplicate a fleet action (name, description, kind,
    /// command text or prompt template with pipeline and persona, timeout 5-7200, concurrency 1-32, clean-tree check,
    /// and the template variable list with insert). Kind switches the fields shown; validation matches the
    /// dashboard (including unknown template variables), and server errors stay in the dialog. Use on the UI loop.
    /// </summary>
    public static class FleetActionForm
    {
        #region Public-Methods

        /// <summary>
        /// Open the form.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="source">Action to edit, the duplicate source, or null for a blank form.</param>
        /// <param name="edit">True to edit <paramref name="source"/>; false to create.</param>
        /// <param name="defaultTimeout">Default timeout for new actions (FleetActions.DefaultTimeoutSeconds).</param>
        /// <param name="saved">Runs after the server saved the action.</param>
        /// <returns>The dialog.</returns>
        public static OpsFormDialog Open(OpsScreen screen, FleetAction? source, bool edit, int defaultTimeout, Action<FleetAction> saved)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            string title = edit ? "Edit fleet action" : source != null ? "Duplicate fleet action" : "New fleet action";
            OpsFormDialog dialog = screen.NewForm(title, "Save");
            dialog.WidthRatio = 0.8;
            if (edit && source != null) dialog.Notes.Add(source.Id);
            else dialog.Notes.Add(screen.Tr("Define a reusable action to run across many vessels."));
            if (edit && source != null && source.IsBuiltIn) dialog.Notes.Add(screen.Tr("This is a built-in action. Your edits are kept; it is never re-seeded over your changes."));

            InputField name = new InputField();
            name.MaxLength = 200;
            name.Validator = v => v.Trim().Length == 0 ? "Name is required." : v.Trim().Length > 200 ? "Name must be 200 characters or fewer." : null;
            InputField description = new InputField();
            description.Placeholder = "Optional";
            SelectField<string> kind = screen.NewSelect("Kind", new List<SelectOption<string>>
            {
                new SelectOption<string>("Command", screen.Tr("Command"), screen.Tr(FleetActionLabels.KindDescription(FleetActionKindEnum.Command))),
                new SelectOption<string>("Mission", screen.Tr("Mission"), screen.Tr(FleetActionLabels.KindDescription(FleetActionKindEnum.Mission))),
            });
            OpsTextArea command = new OpsTextArea();
            command.Placeholder = "git pull --ff-only";
            command.ExternalEditor = (text, done) => screen.EditExternally(text, done, ".sh");
            command.Validator = v => v.Trim().Length == 0 ? "Command text is required." : null;
            OpsTextArea prompt = new OpsTextArea();
            prompt.ExternalEditor = (text, done) => screen.EditExternally(text, done);
            prompt.Validator = v => v.Trim().Length == 0 ? "Prompt template is required." : null;
            SelectField<string> pipeline = screen.NewSelect("Pipeline", screen.Reference.PipelineOptions("Vessel default"));
            pipeline.SetValue("");
            SelectField<string> persona = screen.NewSelect("Persona", screen.Reference.PersonaOptions("None"));
            persona.SetValue("");
            InputField timeout = new InputField();
            timeout.Validator = v => Int32.TryParse(v.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 5 && n <= 7200 ? null : "Timeout must be a whole number of seconds from 5 to 7200.";
            InputField concurrency = new InputField();
            concurrency.Validator = v => Int32.TryParse(v.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= 32 ? null : "Concurrency must be a whole number from 1 to 32.";
            OpsCheckField clean = new OpsCheckField("Requires a clean working tree", true);

            if (source == null)
            {
                kind.SetValue("Command");
                timeout.Value = defaultTimeout.ToString(CultureInfo.InvariantCulture);
                concurrency.Value = "4";
            }
            else
            {
                name.Value = edit ? source.Name : (source.Name + " " + screen.Tr("(copy)")).Substring(0, Math.Min(200, (source.Name + " " + screen.Tr("(copy)")).Length));
                description.Value = source.Description ?? "";
                kind.SetValue(source.Kind.ToString());
                command.Text = source.CommandText ?? "";
                prompt.Text = source.PromptTemplate ?? "";
                if (!String.IsNullOrEmpty(source.Persona) && !persona.Options.Any(o => o.Value == source.Persona))
                    persona.Options.Add(new SelectOption<string>(source.Persona!, source.Persona!));
                pipeline.Options = screen.Reference.PipelineOptions("Vessel default");
                if (!String.IsNullOrEmpty(source.PipelineId) && !pipeline.Options.Any(o => o.Value == source.PipelineId))
                    pipeline.Options.Add(new SelectOption<string>(source.PipelineId!, source.PipelineId!));
                pipeline.SetValue(source.PipelineId ?? "");
                persona.SetValue(source.Persona ?? "");
                timeout.Value = (source.TimeoutSeconds > 0 ? source.TimeoutSeconds : defaultTimeout).ToString(CultureInfo.InvariantCulture);
                concurrency.Value = (source.DefaultConcurrency > 0 ? source.DefaultConcurrency : 4).ToString(CultureInfo.InvariantCulture);
                clean.Checked = source.RequiresCleanWorkingTree;
            }

            Button variables = new Button("Template variables", () =>
            {
                bool isCommand = kind.Value != "Mission";
                OpsTextArea target = isCommand ? command : prompt;
                ShowVariables(screen, token => target.Editor.InsertText(token));
            });

            dialog.AddField("Name", name);
            dialog.AddField("Description", description);
            dialog.AddField("Kind", kind, FleetActionLabels.KindDescription(FleetActionKindEnum.Command));
            dialog.AddField("Command text", command, "Runs through the platform shell (/bin/sh on Linux and macOS, PowerShell on Windows) in each vessel working directory.", 6);
            dialog.AddField("Prompt template", prompt, null, 8);
            dialog.AddField("Pipeline", pipeline);
            dialog.AddField("Persona", persona, "Stored with the action; not yet applied at dispatch. Use a pipeline to choose personas.");
            dialog.AddField("Template variables", variables, "Variables are substituted per vessel in a single pass, without shell escaping. Names are case-insensitive. Any other {{name}} is rejected.");
            dialog.AddField("Timeout (seconds)", timeout, "5 to 7200 seconds");
            dialog.AddField("Default concurrency", concurrency, "1 to 32");
            dialog.AddField("", clean);

            Action apply = () =>
            {
                bool isCommand = kind.Value != "Mission";
                command.Visible = isCommand;
                timeout.Visible = isCommand;
                clean.Visible = isCommand;
                prompt.Visible = !isCommand;
                pipeline.Visible = !isCommand;
                persona.Visible = !isCommand;
                Armada.Tui.Widgets.FormRow? kindRow = dialog.Form.Rows.FirstOrDefault(r => ReferenceEquals(r.Field, kind));
                if (kindRow != null) kindRow.Hint = FleetActionLabels.KindDescription(isCommand ? FleetActionKindEnum.Command : FleetActionKindEnum.Mission);
            };
            apply();
            kind.ValueChanged += (s, e) =>
            {
                bool isCommand = e.NewValue != "Mission";
                clean.Checked = isCommand && (source != null && source.Kind == FleetActionKindEnum.Command ? source.RequiresCleanWorkingTree : isCommand);
                apply();
            };

            EventHandler<string> arrived = (s, list) =>
            {
                if (list == "pipelines")
                {
                    string? current = pipeline.Value;
                    pipeline.Options = screen.Reference.PipelineOptions("Vessel default");
                    if (!String.IsNullOrEmpty(current) && !pipeline.Options.Any(o => o.Value == current)) pipeline.Options.Add(new SelectOption<string>(current!, current!));
                    pipeline.SetValue(current ?? "");
                }
                else if (list == "personas")
                {
                    string? current = persona.Value;
                    persona.Options = screen.Reference.PersonaOptions("None");
                    if (!String.IsNullOrEmpty(current) && !persona.Options.Any(o => o.Value == current)) persona.Options.Add(new SelectOption<string>(current!, current!));
                    persona.SetValue(current ?? "");
                }
            };
            screen.Reference.Changed += arrived;
            screen.Reference.Ensure(true, "pipelines", "personas");

            dialog.Validate = () =>
            {
                string body = kind.Value == "Mission" ? prompt.Text : command.Text;
                List<string> unknown = FleetActionLabels.FindUnknownVariables(body);
                return unknown.Count > 0 ? screen.Tr("Unknown template variable: {{names}}", LocalizationArgs.Of("names", String.Join(", ", unknown))) : null;
            };
            dialog.Submit = d =>
            {
                bool isCommand = kind.Value != "Mission";
                string? clearValue = edit ? "" : null;
                FleetActionUpsertRequest payload = new FleetActionUpsertRequest();
                payload.Name = name.Value.Trim();
                payload.Description = description.Value.Trim().Length > 0 ? description.Value.Trim() : clearValue;
                payload.Kind = isCommand ? FleetActionKindEnum.Command : FleetActionKindEnum.Mission;
                payload.CommandText = isCommand ? command.Text : null;
                payload.PromptTemplate = isCommand ? null : prompt.Text;
                payload.PipelineId = !isCommand && !String.IsNullOrEmpty(pipeline.Value) ? pipeline.Value : clearValue;
                payload.Persona = !isCommand && !String.IsNullOrEmpty(persona.Value) ? persona.Value : clearValue;
                payload.TimeoutSeconds = isCommand ? Int32.Parse(timeout.Value.Trim(), CultureInfo.InvariantCulture) : (int?)null;
                payload.DefaultConcurrency = Int32.Parse(concurrency.Value.Trim(), CultureInfo.InvariantCulture);
                payload.RequiresCleanWorkingTree = isCommand && clean.Checked;
                string? editId = edit && source != null ? source.Id : null;
                screen.Call((c, t) => editId != null ? c.UpdateFleetActionAsync(editId, payload, t) : c.CreateFleetActionAsync(payload, t), result =>
                {
                    d.Complete();
                    if (result != null) saved(result);
                }, null, ex => d.Fail(String.IsNullOrEmpty(ex.Message) ? screen.Tr("Save failed.") : ex.Message));
                return false;
            };
            screen.Context.Modals.Show(dialog, r => screen.Reference.Changed -= arrived);
            return dialog;
        }

        /// <summary>
        /// The template variable list (the dashboard's TemplateVariableHelp): choosing one inserts its token.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="insert">Receives the token, or null for a read-only list.</param>
        public static void ShowVariables(OpsScreen screen, Action<string>? insert)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            for (int i = 0; i < FleetActionLabels.TemplateVariableNames.Length; i++)
            {
                string token = "{{" + FleetActionLabels.TemplateVariableNames[i] + "}}";
                ActionMenuItem item = new ActionMenuItem(token, () => insert?.Invoke(token), screen.Tr(FleetActionLabels.TemplateVariableDescriptions[i]));
                items.Add(item);
            }

            screen.ShowMenu("Template variables", items);
        }

        #endregion
    }
}
