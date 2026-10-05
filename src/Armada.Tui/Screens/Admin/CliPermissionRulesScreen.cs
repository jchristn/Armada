namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Approvals;
    using Armada.Tui.Modals;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// CLI Tool Permissions, Rules tab (dashboard <c>CliPermissions.tsx</c>, <c>RulesPanel</c>): the allow and deny rules
    /// that decide matching CLI tool calls without asking, filtered by scope (All, Global, Vessel, Captain), with pattern,
    /// action, what the rule applies to, description, and age. Admins and tenant admins create rules (pattern, Allow or
    /// Deny, scope Global, Vessel with a vessel picker, or Captain with a captain picker, optional description) with the
    /// dashboard's validation, edit a rule's pattern, action, and description, and delete with the dashboard's
    /// confirmation. Other users see the list read-only. Not thread-safe.
    /// </summary>
    public class CliPermissionRulesScreen : GridScreen<CliPermissionRule>
    {
        #region Public-Members

        /// <summary>
        /// Filters.
        /// </summary>
        public FilterStrip Filters { get; } = new FilterStrip();

        /// <summary>
        /// Scope filter: empty for all, otherwise Global, Vessel, or Captain.
        /// </summary>
        public SelectField<string> ScopeFilter { get; } = new SelectField<string>();

        /// <summary>
        /// Rules from the last load (after the scope filter).
        /// </summary>
        public IReadOnlyList<CliPermissionRule> Items
        {
            get { return _Items; }
        }

        /// <summary>
        /// Vessels offered by the vessel picker.
        /// </summary>
        public IReadOnlyList<Vessel> Vessels
        {
            get { return _Vessels; }
        }

        /// <summary>
        /// Captains offered by the captain picker.
        /// </summary>
        public IReadOnlyList<Captain> Captains
        {
            get { return _Captains; }
        }

        /// <summary>
        /// True when the user may create, edit, and delete rules (an admin or a tenant admin).
        /// </summary>
        public bool CanEdit
        {
            get { return Context.Session.IsGlobalAdmin || Context.Session.IsTenantAdmin; }
        }

        #endregion

        #region Private-Members

        private readonly Button _Create;
        private List<CliPermissionRule> _Items = new List<CliPermissionRule>();
        private List<Vessel> _Vessels = new List<Vessel>();
        private List<Captain> _Captains = new List<Captain>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and load.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Context.</param>
        public CliPermissionRulesScreen(RouteMatch route, TuiContext context)
            : base(route, context, "Rules", "Rules use Claude Code permission rule syntax, for example Bash(git status:*), Bash(npm run *), WebFetch(domain:example.com), Edit(src/**), or mcp__server__tool. Deny rules win over allow rules; every part of a compound shell command must be allowed.", "cli-permission-rules", r => r.Id)
        {
            _Create = Header.AddButton("+ Rule", OnCreate, "n");
            Header.AddButton("Refresh", Refresh, "F5");
            AddFixed(Header, w => Header.HeightFor(w));
            ScopeFilter.ModalHost = context.Modals;
            ScopeFilter.PickerTitle = "Scope";
            ScopeFilter.Options = new List<SelectOption<string>>
            {
                new SelectOption<string>("", context.Loc.T("All")),
                new SelectOption<string>(CliPermissionRuleScopeEnum.Global.ToString(), context.Loc.T("Global")),
                new SelectOption<string>(CliPermissionRuleScopeEnum.Vessel.ToString(), context.Loc.T("Vessel")),
                new SelectOption<string>(CliPermissionRuleScopeEnum.Captain.ToString(), context.Loc.T("Captain")),
            };
            ScopeFilter.SetValue("");
            ScopeFilter.ValueChanged += (s, e) => Load();
            Filters.Add("Scope", ScopeFilter, 16);
            AddFixed(Filters, w => Filters.HeightFor(w));
            Grid.MultiSelect = false;
            Grid.AddColumn(new GridColumn<CliPermissionRule>("pattern", "Pattern", r => r.Pattern) { Weight = 2, Sortable = true, Style = (r, t) => t.Code });
            Grid.AddColumn(new GridColumn<CliPermissionRule>("action", "Action", r => CliPermissionText.RuleAction(Context.Loc, r.Action)) { Width = 8, Sortable = true, Style = (r, t) => r.Action == CliPermissionRuleActionEnum.Deny ? t.Error : t.Success });
            Grid.AddColumn(new GridColumn<CliPermissionRule>("target", "Applies to", r => TargetOf(r)) { Weight = 1 });
            Grid.AddColumn(new GridColumn<CliPermissionRule>("description", "Description", r => String.IsNullOrEmpty(r.Description) ? "-" : r.Description!) { Weight = 1, Style = (r, t) => t.Muted });
            Grid.AddColumn(new GridColumn<CliPermissionRule>("createdIso", "Created (UTC)", r => UserAdminOps.SortStamp(r.CreatedUtc)) { Width = 20, Sortable = true, DefaultVisible = false });
            Grid.AddColumn(new GridColumn<CliPermissionRule>("created", "Created", r => ScreenOps.Relative(Context, r.CreatedUtc)) { Width = 12, Sortable = true, SortKey = "createdIso", Style = (r, t) => t.Muted });
            Grid.EmptyText = "No rules yet.";
            BindPreferences("createdIso", true, 25);
            AddFill(Grid);
            Scope.Focus(Grid);
            LoadReferences();
            Load();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void Refresh()
        {
            Load();
        }

        /// <summary>
        /// Load the rules for the scope filter.
        /// </summary>
        public void Load()
        {
            Grid.SetLoading();
            CliPermissionRuleQuery query = new CliPermissionRuleQuery();
            CliPermissionRuleScopeEnum? scope = ParseScope(ScopeFilter.Value);
            if (scope.HasValue) query.Scope = scope.Value;
            ScreenOps.Run(Context, () => Context.Client.ListCliPermissionRulesAsync(query), r =>
            {
                _Items = r ?? new List<CliPermissionRule>();
                Grid.EmptyText = "No rules yet.";
                Grid.SetLocalRows(_Items);
            }, "Failed to load CLI permission rules.", ex => Grid.SetError(Context.Loc.T("Failed to load CLI permission rules.")));
        }

        /// <summary>
        /// Open the New rule form (null) or the edit form for a rule (pattern, action, and description; the scope of an
        /// existing rule does not change).
        /// </summary>
        /// <param name="editing">Rule to edit, or null to create.</param>
        /// <returns>The form, or null when the user may not edit rules.</returns>
        public FormModal? OpenForm(CliPermissionRule? editing)
        {
            if (!CanEdit) return null;
            FormView form = new FormView();
            InputField pattern = form.AddField("Pattern", new InputField(), "Claude Code permission rule syntax, for example Bash(git status:*) or WebFetch(domain:example.com).");
            pattern.Placeholder = "Bash(git status:*)";
            pattern.Value = editing?.Pattern ?? "";
            SelectField<string> action = form.AddField("Action", new SelectField<string>());
            action.ModalHost = Context.Modals;
            action.PickerTitle = "Action";
            action.Options = new List<SelectOption<string>>
            {
                new SelectOption<string>(CliPermissionRuleActionEnum.Allow.ToString(), Context.Loc.T("Allow")),
                new SelectOption<string>(CliPermissionRuleActionEnum.Deny.ToString(), Context.Loc.T("Deny")),
            };
            action.SetValue((editing?.Action ?? CliPermissionRuleActionEnum.Allow).ToString());

            SelectField<string> scope = new SelectField<string>();
            SelectField<string> vessel = new SelectField<string>();
            SelectField<string> captain = new SelectField<string>();
            if (editing == null)
            {
                scope = form.AddField("Scope", scope);
                scope.ModalHost = Context.Modals;
                scope.PickerTitle = "Scope";
                scope.Options = new List<SelectOption<string>>
                {
                    new SelectOption<string>(CliPermissionRuleScopeEnum.Global.ToString(), Context.Loc.T("Global")),
                    new SelectOption<string>(CliPermissionRuleScopeEnum.Vessel.ToString(), Context.Loc.T("Vessel")),
                    new SelectOption<string>(CliPermissionRuleScopeEnum.Captain.ToString(), Context.Loc.T("Captain")),
                };
                scope.SetValue(CliPermissionRuleScopeEnum.Global.ToString());
                vessel = form.AddField("Vessel", vessel);
                vessel.ModalHost = Context.Modals;
                vessel.PickerTitle = "Vessel";
                vessel.Placeholder = "Select a vessel...";
                vessel.Options = _Vessels.Select(v => new SelectOption<string>(v.Id, v.Name, v.Id)).ToList();
                captain = form.AddField("Captain", captain);
                captain.ModalHost = Context.Modals;
                captain.PickerTitle = "Captain";
                captain.Placeholder = "Select a captain...";
                captain.Options = _Captains.Select(c => new SelectOption<string>(c.Id, c.Name, c.Id)).ToList();
                Action applyScope = () =>
                {
                    vessel.Visible = scope.Value == CliPermissionRuleScopeEnum.Vessel.ToString();
                    captain.Visible = scope.Value == CliPermissionRuleScopeEnum.Captain.ToString();
                };
                scope.ValueChanged += (s, e) => applyScope();
                applyScope();
            }
            else
            {
                TextBlock target = new TextBlock(TargetOf(editing), t => t.Muted);
                target.Translate = false;
                form.AddField("Applies to", target);
            }

            InputField description = form.AddField("Description", new InputField());
            description.Placeholder = "Optional";
            description.Value = editing?.Description ?? "";
            form.MarkClean();

            FormModal modal = new FormModal(editing != null ? "Edit rule" : "New rule", form, Context, editing != null ? "Save" : "Create rule");
            string savedPattern = "";
            modal.SubmitAsync = async () =>
            {
                string? error = DraftError(pattern.Value, scope.Value, vessel.Value, captain.Value, editing != null);
                if (error != null) return error;
                CliPermissionRule body = new CliPermissionRule();
                body.Pattern = pattern.Value.Trim();
                body.Action = action.Value == CliPermissionRuleActionEnum.Deny.ToString() ? CliPermissionRuleActionEnum.Deny : CliPermissionRuleActionEnum.Allow;
                body.Description = description.Value.Trim().Length > 0 ? description.Value.Trim() : null;
                savedPattern = body.Pattern;
                if (editing != null)
                {
                    await Context.Client.UpdateCliPermissionRuleAsync(editing.Id, body).ConfigureAwait(false);
                    return null;
                }

                CliPermissionRuleScopeEnum chosen = ParseScope(scope.Value) ?? CliPermissionRuleScopeEnum.Global;
                body.Scope = chosen;
                body.VesselId = chosen == CliPermissionRuleScopeEnum.Vessel ? vessel.Value : null;
                body.CaptainId = chosen == CliPermissionRuleScopeEnum.Captain ? captain.Value : null;
                await Context.Client.CreateCliPermissionRuleAsync(body).ConfigureAwait(false);
                return null;
            };
            Context.Modals.Show(modal, result =>
            {
                if (!(result is bool ok) || !ok) return;
                ScreenOps.Toast(Context, NotificationSeverityEnum.Success, editing != null ? "Rule saved." : "Rule created.");
                Load();
            });
            return modal;
        }

        /// <summary>
        /// The dashboard's validation of a rule draft: a pattern, and the vessel or captain its scope needs.
        /// </summary>
        /// <param name="pattern">Pattern.</param>
        /// <param name="scope">Scope value (Global, Vessel, Captain).</param>
        /// <param name="vesselId">Chosen vessel, or null.</param>
        /// <param name="captainId">Chosen captain, or null.</param>
        /// <param name="editing">True for an existing rule (its scope is fixed).</param>
        /// <returns>English error, or null.</returns>
        public static string? DraftError(string? pattern, string? scope, string? vesselId, string? captainId, bool editing)
        {
            if (String.IsNullOrWhiteSpace(pattern)) return "Enter a rule pattern.";
            if (editing) return null;
            if (scope == CliPermissionRuleScopeEnum.Vessel.ToString() && String.IsNullOrEmpty(vesselId)) return "Choose a vessel.";
            if (scope == CliPermissionRuleScopeEnum.Captain.ToString() && String.IsNullOrEmpty(captainId)) return "Choose a captain.";
            return null;
        }

        /// <summary>
        /// Ask to delete a rule with the dashboard's confirmation, then delete it.
        /// </summary>
        /// <param name="rule">Rule.</param>
        /// <returns>The confirmation, or null when the user may not delete rules.</returns>
        public ConfirmDialog? ConfirmDelete(CliPermissionRule rule)
        {
            if (rule == null || !CanEdit) return null;
            string id = rule.Id;
            string message = Context.Loc.T("Delete the {{action}} rule {{pattern}}? Matching CLI tool calls go back to the policy (asking in Armada, refused, or bypassed).",
                LocalizationArgs.Of("action", CliPermissionText.RuleAction(Context.Loc, rule.Action), "pattern", rule.Pattern));
            ConfirmDialog dialog = new ConfirmDialog("Delete rule", message, "Delete", "Cancel", null, Context.Loc, Context.Theme.Current);
            dialog.Destructive = true;
            Context.Modals.Show(dialog, result =>
            {
                if (!(result is bool ok) || !ok) return;
                ScreenOps.RunVoid(Context, () => Context.Client.DeleteCliPermissionRuleAsync(id), () =>
                {
                    ScreenOps.Toast(Context, NotificationSeverityEnum.Success, "Rule deleted.");
                    Load();
                }, "The rule could not be deleted.");
            });
            return dialog;
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override void BeforeRender(int width, int height)
        {
            _Create.Visible = CanEdit;
        }

        /// <inheritdoc />
        protected override void OnActivate(CliPermissionRule row)
        {
            if (CanEdit) OpenForm(row);
            else ScreenOps.ShowJson(Context, JsonTitle(row), row);
        }

        /// <inheritdoc />
        protected override IEnumerable<ActionMenuItem> RowMenu(CliPermissionRule row)
        {
            List<ActionMenuItem> items = new List<ActionMenuItem>();
            if (CanEdit) items.Add(new ActionMenuItem("Edit", () => OpenForm(row), "Enter"));
            items.Add(new ActionMenuItem("View JSON", () => ScreenOps.ShowJson(Context, JsonTitle(row), row), "j"));
            if (CanEdit)
            {
                ActionMenuItem delete = new ActionMenuItem("Delete", () => ConfirmDelete(row), "Del");
                delete.Destructive = true;
                items.Add(delete);
            }

            return items;
        }

        /// <inheritdoc />
        protected override string JsonTitle(CliPermissionRule row)
        {
            return "Rule: " + row.Pattern;
        }

        /// <inheritdoc />
        protected override bool SupportsCreate()
        {
            return true;
        }

        /// <inheritdoc />
        protected override bool CanCreate()
        {
            return CanEdit;
        }

        /// <inheritdoc />
        protected override void OnCreate()
        {
            OpenForm(null);
        }

        /// <inheritdoc />
        protected override bool SupportsDelete()
        {
            return true;
        }

        /// <inheritdoc />
        protected override bool CanDeleteRows()
        {
            return CanEdit && Grid.Current != null;
        }

        /// <inheritdoc />
        protected override void OnDeleteRow(CliPermissionRule row)
        {
            ConfirmDelete(row);
        }

        /// <inheritdoc />
        protected override void OnDeleteSelected()
        {
            CliPermissionRule? row = Grid.Current;
            if (row != null) ConfirmDelete(row);
        }

        #endregion

        #region Private-Methods

        private static CliPermissionRuleScopeEnum? ParseScope(string? value)
        {
            if (String.IsNullOrEmpty(value)) return null;
            return Enum.TryParse<CliPermissionRuleScopeEnum>(value, false, out CliPermissionRuleScopeEnum parsed) ? parsed : (CliPermissionRuleScopeEnum?)null;
        }

        private string TargetOf(CliPermissionRule rule)
        {
            string? vessel = rule.VesselId != null ? _Vessels.FirstOrDefault(v => v.Id == rule.VesselId)?.Name : null;
            string? captain = rule.CaptainId != null ? _Captains.FirstOrDefault(c => c.Id == rule.CaptainId)?.Name : null;
            return CliPermissionText.Target(Context.Loc, rule, vessel, captain);
        }

        private void LoadReferences()
        {
            ScreenOps.Quiet(Context, () => Context.Client.ListVesselsAsync(new ArmadaPageQuery(1, 1000)), r =>
            {
                // The Applies to column reads the names when it renders.
                _Vessels = (r?.Objects ?? new List<Vessel>()).OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
            });
            ScreenOps.Quiet(Context, () => Context.Client.ListCaptainsAsync(new ArmadaPageQuery(1, 1000)), r =>
            {
                _Captains = (r?.Objects ?? new List<Captain>()).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
            });
        }

        #endregion
    }
}
