namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The dashboard's Create Captain / Edit Captain dialog: name, runtime, model, inference endpoint (API Endpoint
    /// captains), reasoning effort, capability tier, the auto-approve switch (CLI runtimes), the CLI tool permission policy
    /// (Inherit, Refuse, Approve in Armada, Bypass; admins only, Bypass after the strong warning, saved through its own
    /// endpoint on edit), the Mux fields (config
    /// directory, endpoint with discovery and refresh, base URL, adapter type, temperature, max tokens, system prompt
    /// path, approval policy), and system instructions; the captain page adds allowed personas (JSON array) and the
    /// preferred persona. Runtime options are written the way <c>lib/mux.ts</c> and <c>lib/captainApproval.ts</c>
    /// write them. Edits send the full record so fields the form does not show are kept.
    /// </summary>
    public static class CaptainForm
    {
        #region Public-Members

        /// <summary>
        /// Runtimes offered by the create and list edit form, in the dashboard's order.
        /// </summary>
        public static readonly AgentRuntimeEnum[] Runtimes = new AgentRuntimeEnum[] { AgentRuntimeEnum.ClaudeCode, AgentRuntimeEnum.Codex, AgentRuntimeEnum.Gemini, AgentRuntimeEnum.Cursor, AgentRuntimeEnum.Mux, AgentRuntimeEnum.OpenCode, AgentRuntimeEnum.ApiEndpoint };

        #endregion

        #region Public-Methods

        /// <summary>
        /// True for runtimes launched as a CLI with an auto-approve flag (<c>supportsAutoApproveSwitch</c>).
        /// </summary>
        /// <param name="runtime">Runtime, or null.</param>
        /// <returns>True when supported.</returns>
        public static bool SupportsAutoApprove(AgentRuntimeEnum? runtime)
        {
            switch (runtime)
            {
                case AgentRuntimeEnum.ClaudeCode:
                case AgentRuntimeEnum.Codex:
                case AgentRuntimeEnum.Gemini:
                case AgentRuntimeEnum.Cursor:
                case AgentRuntimeEnum.Mux:
                case AgentRuntimeEnum.OpenCode:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The dashboard's runtime option label.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>Label.</returns>
        public static string RuntimeLabel(AgentRuntimeEnum runtime)
        {
            if (runtime == AgentRuntimeEnum.ClaudeCode) return "Claude Code";
            if (runtime == AgentRuntimeEnum.ApiEndpoint) return "API Endpoint";
            return runtime.ToString();
        }

        /// <summary>
        /// Build runtime options JSON from the Mux fields and the auto-approve switch (<c>buildMuxRuntimeOptionsJson</c>
        /// then <c>applyAutoApprove</c>).
        /// </summary>
        /// <param name="runtime">Runtime, or null.</param>
        /// <param name="mux">Mux options (used only for the Mux runtime).</param>
        /// <param name="autoApprove">Auto-approve switch.</param>
        /// <returns>JSON or null.</returns>
        public static string? RuntimeOptionsJson(AgentRuntimeEnum? runtime, MuxCaptainOptions mux, bool autoApprove)
        {
            string? json = runtime == AgentRuntimeEnum.Mux ? CaptainRuntimeOptions.Serialize(mux) : null;
            bool effective = autoApprove || !SupportsAutoApprove(runtime);
            return CaptainRuntimeOptions.WithAutoApprove(json, effective ? (bool?)null : false);
        }

        /// <summary>
        /// The dashboard's duplicate payload for a captain.
        /// </summary>
        /// <param name="captain">Captain.</param>
        /// <returns>New captain body.</returns>
        public static Captain DuplicatePayload(Captain captain)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            Captain copy = new Captain(BuildText.DuplicateName(captain.Name), captain.Runtime);
            copy.SystemInstructions = captain.SystemInstructions;
            copy.Model = captain.Model;
            copy.AllowedPersonas = captain.AllowedPersonas;
            copy.PreferredPersona = captain.PreferredPersona;
            copy.RuntimeOptionsJson = captain.RuntimeOptionsJson;
            return copy;
        }

        /// <summary>
        /// Open the dialog.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        /// <param name="editing">Captain to edit, or null to create.</param>
        /// <param name="detail">True for the captain page's form (adds allowed and preferred personas and Custom).</param>
        /// <param name="saved">Called on the loop with the saved captain.</param>
        /// <returns>The dialog.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="screen"/> is null.</exception>
        public static OpsFormDialog Open(OpsScreen screen, Captain? editing, bool detail, Action<Captain?> saved)
        {
            if (screen == null) throw new ArgumentNullException(nameof(screen));
            LocalizationService loc = screen.Context.Loc;
            OpsFormDialog dialog = screen.NewForm(editing != null ? "Edit Captain" : "Create Captain", "Save");
            dialog.WidthRatio = 0.8;

            InputField name = new InputField();
            name.Value = editing?.Name ?? "";
            name.Validator = v => String.IsNullOrWhiteSpace(v) ? "Name is required." : null;

            List<AgentRuntimeEnum> runtimes = Runtimes.ToList();
            if (detail) runtimes.Add(AgentRuntimeEnum.Custom);
            AgentRuntimeEnum? currentRuntime = editing?.Runtime;
            if (currentRuntime != null && !runtimes.Contains(currentRuntime.Value)) runtimes.Add(currentRuntime.Value);
            SelectField<AgentRuntimeEnum?> runtime = screen.NewSelect("Runtime", runtimes.Select(r => new SelectOption<AgentRuntimeEnum?>(r, RuntimeLabel(r))).ToList(), "Select runtime...");
            runtime.Required = true;
            if (currentRuntime != null) runtime.SetValue(currentRuntime);

            InputField model = new InputField();
            model.Value = editing?.Model ?? "";
            model.Placeholder = "e.g., gpt-5.4-mini";

            SelectField<string> endpoint = screen.NewSelect("Inference Endpoint", new List<SelectOption<string>>(), "Select an inference endpoint...");
            List<ModelEndpoint> inference = new List<ModelEndpoint>();

            SelectField<string> effort = screen.NewSelect("Reasoning effort", new List<SelectOption<string>>
            {
                new SelectOption<string>("", loc.T("Runtime default")),
                new SelectOption<string>("Off", loc.T("Off")),
                new SelectOption<string>("Minimal", loc.T("Minimal")),
                new SelectOption<string>("Low", loc.T("Low")),
                new SelectOption<string>("Medium", loc.T("Medium")),
                new SelectOption<string>("High", loc.T("High")),
            });
            effort.SetValue(editing?.ReasoningEffort?.ToString() ?? "");

            SelectField<string> tier = screen.NewSelect("Capability tier", new List<SelectOption<string>>
            {
                new SelectOption<string>("", loc.T("Auto (classify from model)")),
                new SelectOption<string>("Economy", loc.T("Economy")),
                new SelectOption<string>("Standard", loc.T("Standard")),
                new SelectOption<string>("Premium", loc.T("Premium")),
            });
            tier.SetValue(editing?.Tier?.ToString() ?? "");

            bool canManagePolicy = screen.Context.Session.IsGlobalAdmin || screen.Context.Session.IsTenantAdmin;
            CliPermissionPolicyEnum? storedPolicy = editing?.CliPermissionPolicy;
            SelectField<string> cliPolicy = screen.NewSelect("CLI tool permissions", Armada.Tui.Approvals.CliPermissionPolicyChoice.Options(loc, true, null, canManagePolicy, storedPolicy));
            cliPolicy.SetValue(Armada.Tui.Approvals.CliPermissionPolicyChoice.ValueOf(storedPolicy));
            cliPolicy.CanFocus = canManagePolicy;
            Armada.Tui.Approvals.CliPermissionPolicyChoice.GuardBypass(screen.Context, cliPolicy, () => canManagePolicy);

            OpsCheckField autoApprove = new OpsCheckField("Auto-approve agent tool use (runs the CLI with its permission-bypass flag)", editing == null || CaptainRuntimeOptions.GetAutoApprove(editing));

            MuxCaptainOptions mux = (editing != null && editing.Runtime == AgentRuntimeEnum.Mux ? SafeMux(editing) : null) ?? new MuxCaptainOptions();
            InputField muxConfig = new InputField();
            muxConfig.Value = mux.ConfigDirectory ?? "";
            muxConfig.Placeholder = "Optional path, e.g. C:\\Users\\you\\.mux";
            InputField muxEndpoint = new InputField();
            muxEndpoint.Value = mux.Endpoint ?? "";
            muxEndpoint.Placeholder = "Required endpoint name";
            List<MuxEndpointInfo> discovered = new List<MuxEndpointInfo>();
            Button muxPick = new Button("Choose discovered endpoint", null);
            Button muxRefresh = new Button("Refresh Mux Endpoints", null);
            InputField muxBase = new InputField();
            muxBase.Value = mux.BaseUrl ?? "";
            muxBase.Placeholder = "Optional override";
            InputField muxAdapter = new InputField();
            muxAdapter.Value = mux.AdapterType ?? "";
            muxAdapter.Placeholder = "Optional override";
            InputField muxTemperature = new InputField();
            muxTemperature.Value = mux.Temperature.HasValue ? mux.Temperature.Value.ToString(CultureInfo.InvariantCulture) : "";
            muxTemperature.Placeholder = "Optional number";
            InputField muxMaxTokens = new InputField();
            muxMaxTokens.Value = mux.MaxTokens.HasValue ? mux.MaxTokens.Value.ToString(CultureInfo.InvariantCulture) : "";
            muxMaxTokens.Placeholder = "Optional integer";
            InputField muxPrompt = new InputField();
            muxPrompt.Value = mux.SystemPromptPath ?? "";
            muxPrompt.Placeholder = "Optional path";
            SelectField<string> muxPolicy = screen.NewSelect("Mux Approval Policy", new List<SelectOption<string>>
            {
                new SelectOption<string>("", loc.T("Default (auto)")),
                new SelectOption<string>("auto", "auto"),
                new SelectOption<string>("autoapprove", "autoapprove"),
                new SelectOption<string>("deny", "deny"),
                new SelectOption<string>("ask", "ask"),
            });
            muxPolicy.SetValue(mux.ApprovalPolicy ?? "");

            OpsTextArea instructions = new OpsTextArea();
            instructions.Text = editing?.SystemInstructions ?? "";
            instructions.Placeholder = "e.g., You are a testing specialist. Always run tests before committing...";
            instructions.ExternalEditor = (text, done) => screen.EditExternally(text, done);

            InputField allowed = new InputField();
            allowed.Value = editing?.AllowedPersonas ?? "";
            allowed.Placeholder = "[\"Worker\", \"Judge\"]";
            InputField preferred = new InputField();
            preferred.Value = editing?.PreferredPersona ?? "";
            preferred.Placeholder = "e.g., Worker";

            dialog.AddField("Name", name);
            dialog.AddField("Runtime", runtime);
            dialog.AddField("Model", model);
            dialog.AddField("Inference Endpoint", endpoint);
            dialog.AddField("Reasoning effort", effort);
            dialog.AddField("Capability tier", tier, detail ? "Missions requiring a tier route to captains at or above it. Leave on Auto to classify from the model name." : null);
            dialog.AddField("Auto-approve", autoApprove);
            dialog.AddField("CLI tool permissions", cliPolicy, "How this captain handles shell commands, file edits, and fetches that need permission. Inherit: missions follow the auto-approve option when it is set, then the server default; Ask conversations use the server default (Settings > CLI Tool Permissions). A conversation can override it." + (canManagePolicy ? "" : " Only admins can change this."));
            dialog.AddField("Mux Config Directory", muxConfig);
            dialog.AddField("Mux Endpoint", muxEndpoint);
            dialog.AddField("", muxPick);
            dialog.AddField(" ", muxRefresh);
            dialog.AddField("Mux Base URL", muxBase);
            dialog.AddField("Mux Adapter Type", muxAdapter);
            dialog.AddField("Mux Temperature", muxTemperature);
            dialog.AddField("Mux Max Tokens", muxMaxTokens);
            dialog.AddField("Mux System Prompt Path", muxPrompt);
            dialog.AddField("Mux Approval Policy", muxPolicy);
            dialog.AddField("System Instructions", instructions, null, 5);
            if (detail)
            {
                dialog.AddField("Allowed Personas (JSON array)", allowed);
                dialog.AddField("Preferred Persona", preferred);
            }

            FormRow endpointRow = dialog.Form.Rows.First(r => ReferenceEquals(r.Field, endpoint));
            FormRow muxRow = dialog.Form.Rows.First(r => ReferenceEquals(r.Field, muxRefresh));
            string muxHint = "";
            bool muxLoading = false;

            Action applyRuntime = () =>
            {
                AgentRuntimeEnum? r = runtime.Value;
                bool isMux = r == AgentRuntimeEnum.Mux;
                bool isApi = r == AgentRuntimeEnum.ApiEndpoint;
                model.Placeholder = isApi ? "Optional; overrides the endpoint model" : "e.g., gpt-5.4-mini";
                endpoint.Visible = isApi;
                endpointRow.Hint = isApi && inference.Count == 0 ? "No inference endpoints configured. Add one under Configuration > Endpoints first." : null;
                autoApprove.Visible = SupportsAutoApprove(r);
                muxConfig.Visible = isMux;
                muxEndpoint.Visible = isMux;
                muxPick.Visible = isMux && discovered.Count > 0;
                muxRefresh.Visible = isMux;
                muxRow.Hint = isMux && muxHint.Length > 0 ? muxHint : null;
                muxBase.Visible = isMux;
                muxAdapter.Visible = isMux;
                muxTemperature.Visible = isMux;
                muxMaxTokens.Visible = isMux;
                muxPrompt.Visible = isMux;
                muxPolicy.Visible = isMux;
            };

            Action loadMux = () =>
            {
                if (runtime.Value != AgentRuntimeEnum.Mux || muxLoading) return;
                muxLoading = true;
                muxRefresh.Label = loc.T("Refreshing...");
                muxHint = loc.T("Loading saved Mux endpoints...");
                applyRuntime();
                string dir = muxConfig.Value.Trim();
                screen.Call((c, t) => c.ListMuxEndpointsAsync(dir.Length > 0 ? dir : null, t), result =>
                {
                    muxLoading = false;
                    muxRefresh.Label = loc.T("Refresh Mux Endpoints");
                    if (result == null || !result.Success)
                    {
                        discovered.Clear();
                        string? message = result?.ErrorMessage;
                        if (String.IsNullOrEmpty(message)) message = result?.ErrorCode;
                        muxHint = String.IsNullOrEmpty(message) ? loc.T("Mux endpoint discovery failed.") : message!;
                    }
                    else
                    {
                        discovered.Clear();
                        discovered.AddRange(result.Endpoints ?? new List<MuxEndpointInfo>());
                        muxHint = discovered.Count == 0
                            ? loc.T("No saved Mux endpoints were found for this config directory.")
                            : loc.T("{{count}} saved Mux endpoint(s) available.", LocalizationArgs.Of("count", discovered.Count));
                    }

                    applyRuntime();
                }, null, ex =>
                {
                    muxLoading = false;
                    muxRefresh.Label = loc.T("Refresh Mux Endpoints");
                    discovered.Clear();
                    muxHint = String.IsNullOrEmpty(ex.Message) ? loc.T("Mux endpoint discovery failed.") : ex.Message;
                    applyRuntime();
                });
            };

            muxRefresh.Pressed += (s, e) => loadMux();
            muxPick.Pressed += (s, e) =>
            {
                List<SelectOption<string>> options = discovered.Select(d => new SelectOption<string>(d.Name, d.Name, d.AdapterType + (String.IsNullOrEmpty(d.Model) ? "" : " (" + d.Model + ")"))).ToList();
                SelectField<string> picker = screen.NewSelect("Mux Endpoint", options);
                picker.ValueChanged += (s2, e2) =>
                {
                    if (!String.IsNullOrEmpty(picker.Value)) muxEndpoint.Value = picker.Value!;
                };
                picker.Open();
            };
            runtime.ValueChanged += (s, e) =>
            {
                applyRuntime();
                if (runtime.Value == AgentRuntimeEnum.Mux && discovered.Count == 0) loadMux();
            };

            screen.Call((c, t) => c.ListModelEndpointsAsync(t), list =>
            {
                inference.Clear();
                inference.AddRange((list ?? new List<ModelEndpoint>()).Where(e => e.Kind == ModelEndpointKindEnum.Inference));
                endpoint.Options = inference.Select(e => new SelectOption<string>(e.Id, e.Name + " (" + e.Provider + (String.IsNullOrEmpty(e.Model) ? "" : " / " + e.Model) + ")")).ToList();
                endpoint.SetValue(editing?.ModelEndpointId ?? "");
                applyRuntime();
            }, null, ex =>
            {
                inference.Clear();
                applyRuntime();
            });

            applyRuntime();
            if (runtime.Value == AgentRuntimeEnum.Mux) loadMux();

            dialog.Validate = () =>
            {
                AgentRuntimeEnum? r = runtime.Value;
                if (r == null) return loc.T("Select a runtime.");
                if (r == AgentRuntimeEnum.Mux && String.IsNullOrWhiteSpace(muxEndpoint.Value)) return loc.T("Mux captains require a named Mux endpoint.");
                if (r == AgentRuntimeEnum.ApiEndpoint && String.IsNullOrEmpty(endpoint.Value)) return loc.T("API-endpoint captains require an inference endpoint. Select one, or add it under Configuration > Endpoints.");
                return null;
            };

            dialog.Submit = d =>
            {
                AgentRuntimeEnum r = runtime.Value ?? AgentRuntimeEnum.ClaudeCode;
                Captain body = editing != null ? CopyOf(editing) : new Captain(name.Value.Trim());
                body.Name = name.Value.Trim();
                body.Runtime = r;
                body.SystemInstructions = String.IsNullOrEmpty(instructions.Text) ? null : instructions.Text;
                body.Model = String.IsNullOrWhiteSpace(model.Value) ? null : model.Value.Trim();
                body.ModelEndpointId = r == AgentRuntimeEnum.ApiEndpoint && !String.IsNullOrEmpty(endpoint.Value) ? endpoint.Value : null;
                body.ReasoningEffort = Enum.TryParse<ReasoningEffortEnum>(effort.Value ?? "", out ReasoningEffortEnum re) ? re : (ReasoningEffortEnum?)null;
                body.Tier = Enum.TryParse<CaptainTierEnum>(tier.Value ?? "", out CaptainTierEnum ct) ? ct : (CaptainTierEnum?)null;
                if (detail)
                {
                    body.AllowedPersonas = String.IsNullOrEmpty(allowed.Value) ? null : allowed.Value;
                    body.PreferredPersona = String.IsNullOrEmpty(preferred.Value) ? null : preferred.Value;
                }

                MuxCaptainOptions options = new MuxCaptainOptions();
                options.SchemaVersion = 1;
                options.ConfigDirectory = muxConfig.Value;
                options.Endpoint = muxEndpoint.Value;
                options.BaseUrl = muxBase.Value;
                options.AdapterType = muxAdapter.Value;
                options.Temperature = Double.TryParse(muxTemperature.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double temp) ? temp : (double?)null;
                options.MaxTokens = Int32.TryParse(muxMaxTokens.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max) ? max : (int?)null;
                options.SystemPromptPath = muxPrompt.Value;
                options.ApprovalPolicy = muxPolicy.Value;
                body.RuntimeOptionsJson = RuntimeOptionsJson(r, options, autoApprove.Checked);
                CliPermissionPolicyEnum? chosenPolicy = Armada.Tui.Approvals.CliPermissionPolicyChoice.Parse(cliPolicy.Value);
                string label = body.Name;
                if (editing != null)
                {
                    // A captain update keeps the stored CLI tool permission policy; it changes through its own (admin)
                    // endpoint, after the update.
                    body.CliPermissionPolicy = storedPolicy;
                    Action<Captain?> finish = result =>
                    {
                        d.Complete();
                        screen.Toast(NotificationSeverityEnum.Success, screen.Tr("Captain \"{{name}}\" saved.", LocalizationArgs.Of("name", label)));
                        saved?.Invoke(result);
                    };
                    Action<Exception> fail = ex => d.Fail(ex, screen.Tr("Save failed."));
                    screen.Call((c, t) => c.UpdateCaptainAsync(editing.Id, body, t), result =>
                    {
                        if (!canManagePolicy || chosenPolicy == storedPolicy)
                        {
                            finish(result);
                            return;
                        }

                        screen.Call((c, t) => c.SetCaptainCliPermissionPolicyAsync(editing.Id, chosenPolicy, t), updated => finish(updated ?? result), null, fail);
                    }, null, fail);
                }
                else
                {
                    body.CliPermissionPolicy = canManagePolicy ? chosenPolicy : null;
                    screen.Call((c, t) => c.CreateCaptainAsync(body, t), result =>
                    {
                        d.Complete();
                        screen.Toast(NotificationSeverityEnum.Success, screen.Tr("Captain \"{{name}}\" created.", LocalizationArgs.Of("name", label)));
                        saved?.Invoke(result);
                    }, null, ex => d.Fail(ex, screen.Tr("Save failed.")));
                }

                return false;
            };

            screen.Context.Modals.Show(dialog);
            return dialog;
        }

        #endregion

        #region Private-Methods

        private static MuxCaptainOptions? SafeMux(Captain captain)
        {
            try { return CaptainRuntimeOptions.GetMuxOptions(captain); }
            catch (Exception) { return null; }
        }

        private static Captain CopyOf(Captain source)
        {
            return ArmadaJson.Deserialize<Captain>(ArmadaJson.Serialize(source)) ?? new Captain(source.Name, source.Runtime);
        }

        #endregion
    }
}
