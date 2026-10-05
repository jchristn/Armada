namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Approvals;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Mission actions shared by the Missions list, the Mission screen, Voyage detail, Home, and the Merge Queue, with
    /// the dashboard's calls, confirmations, toasts, and error text for each place they appear (the list and the
    /// detail page word some confirmations differently, so callers pick the variant). Use on the UI loop.
    /// </summary>
    public class MissionOps
    {
        #region Public-Members

        /// <summary>
        /// Statuses offered by the Missions list (no LandingFailed; the current status is excluded).
        /// </summary>
        public static readonly string[] ListStatuses = new string[] { "Pending", "Assigned", "InProgress", "WorkProduced", "Testing", "Review", "Complete", "Failed", "Cancelled" };

        /// <summary>
        /// Statuses offered by the Mission screen.
        /// </summary>
        public static readonly string[] DetailStatuses = new string[] { "Pending", "Assigned", "InProgress", "WorkProduced", "Testing", "Review", "Complete", "Failed", "LandingFailed", "Cancelled" };

        /// <summary>
        /// Statuses after which the log viewer reports Done (the dashboard's completed list).
        /// </summary>
        public static readonly string[] LogCompleteStatuses = new string[] { "Complete", "Failed", "Cancelled", "WorkProduced", "LandingFailed", "Review" };

        #endregion

        #region Private-Members

        private readonly OpsScreen _Screen;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a screen.
        /// </summary>
        /// <param name="screen">Owning screen.</param>
        public MissionOps(OpsScreen screen)
        {
            _Screen = screen ?? throw new ArgumentNullException(nameof(screen));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// View a mission's diff.
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="title">Viewer title (translated).</param>
        public void ViewDiff(string id, string title)
        {
            _Screen.Call((c, t) => c.GetMissionDiffAsync(id, t), result => _Screen.ShowDiff(title, result?.Diff ?? ""), null, ex => _Screen.ShowDiff(title, ""));
        }

        /// <summary>
        /// View a mission's log (line count, follow, copy).
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="title">Viewer title (translated).</param>
        /// <param name="completed">True once the mission is terminal, or null for always.</param>
        /// <param name="onTick">Called every fifth follow refresh, or null.</param>
        /// <returns>The modal.</returns>
        public OpsLogModal ViewLog(string id, string title, Func<bool>? completed = null, Action? onTick = null)
        {
            TuiContext ctx = _Screen.Context;
            OpsLogModal modal = new OpsLogModal(
                title,
                (lines, token) => ctx.Client.GetMissionLogAsync(id, lines, token),
                completed ?? (() => false),
                ctx.Dispatcher,
                text => ctx.Clipboard.Copy(text, "Log"),
                ctx.Loc,
                ctx.Theme.Current,
                onTick);
            ctx.Modals.Show(modal, r => modal.Dispose());
            return modal;
        }

        /// <summary>
        /// View a mission's instructions as Markdown.
        /// </summary>
        /// <param name="id">Mission id.</param>
        public void ViewInstructions(string id)
        {
            _Screen.Call((c, t) => c.GetMissionInstructionsAsync(id, t), result =>
            {
                string file = result != null && !String.IsNullOrEmpty(result.FileName) ? result.FileName : _Screen.Tr("Mission Instructions");
                string content = result != null && !String.IsNullOrEmpty(result.Content) ? result.Content : _Screen.Tr("No mission instructions found.");
                _Screen.ShowMarkdown(_Screen.Tr("Instructions: {{fileName}}", LocalizationArgs.Of("fileName", file)), content);
            }, null, ex => _Screen.ShowMarkdown(_Screen.Tr("Mission Instructions"), _Screen.Tr("Instructions unavailable: {{message}}", LocalizationArgs.Of("message", ex.Message))));
        }

        /// <summary>
        /// Restart a mission: without confirmation (Missions list) or after one (Mission screen).
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="title">Title.</param>
        /// <param name="confirm">Ask first (the detail page variant).</param>
        /// <param name="after">Runs after success.</param>
        public void Restart(string id, string title, bool confirm, Action? after)
        {
            Action run = () => _Screen.Call((c, t) => c.RestartMissionAsync(id, t), m =>
            {
                _Screen.Toast(NotificationSeverityEnum.Success, _Screen.Tr("Mission \"{{title}}\" restarted.", LocalizationArgs.Of("title", title)));
                after?.Invoke();
            }, null, ex => _Screen.ShowMessage(confirm ? _Screen.Tr("Restart failed: {{message}}", LocalizationArgs.Of("message", ex.Message)) : _Screen.Tr("Restart failed.")));
            if (!confirm)
            {
                run();
                return;
            }

            _Screen.Confirm("Restart Mission", _Screen.Tr("Restart mission \"{{title}}\"? This will reset the mission to Pending status.", LocalizationArgs.Of("title", title)), run, "Restart");
        }

        /// <summary>
        /// Retry landing (or Land) a mission.
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="title">Title.</param>
        /// <param name="detailToast">Use the Mission screen's toast ("Landing succeeded! Mission status updated.").</param>
        /// <param name="after">Runs after success.</param>
        public void RetryLanding(string id, string title, bool detailToast, Action? after)
        {
            _Screen.Call((c, t) => c.RetryMissionLandingAsync(id, t), r =>
            {
                string text = detailToast
                    ? _Screen.Tr("Landing succeeded! Mission status updated.")
                    : _Screen.Tr("Landing succeeded for \"{{title}}\"", LocalizationArgs.Of("title", title));
                _Screen.Toast(NotificationSeverityEnum.Success, text);
                after?.Invoke();
            }, null, ex => _Screen.ShowMessage(String.IsNullOrEmpty(ex.Message) ? _Screen.Tr(detailToast ? "Landing failed." : "Retry landing failed.") : ex.Message));
        }

        /// <summary>
        /// The Transition Mission Status dialog.
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="title">Title, or null.</param>
        /// <param name="current">Current status.</param>
        /// <param name="detail">Detail page variant (LandingFailed offered, current not excluded, message in errors).</param>
        /// <param name="after">Runs after success.</param>
        /// <returns>The dialog.</returns>
        public OpsFormDialog Transition(string id, string? title, string current, bool detail, Action? after)
        {
            TuiContext ctx = _Screen.Context;
            OpsFormDialog dialog = _Screen.NewForm("Transition Mission Status", "Transition");
            dialog.WidthRatio = 0.5;
            dialog.Notes.Add(_Screen.Tr(detail ? "Current status" : "Current status:") + (detail ? ": " : " ") + _Screen.Tr(current));
            SelectField<string> status = new SelectField<string>();
            status.ModalHost = ctx.Modals;
            status.PickerTitle = "New Status";
            status.Placeholder = "Select status...";
            status.Required = true;
            string[] statuses = detail ? DetailStatuses : ListStatuses.Where(s => s != current).ToArray();
            status.Options = statuses.Select(s => new SelectOption<string>(s, _Screen.Tr(s))).ToList();
            dialog.AddField("New Status", status);
            dialog.Submit = d =>
            {
                string target = status.Value ?? "";
                TransitionRequest req = new TransitionRequest();
                req.Status = target;
                _Screen.Call((c, t) => c.TransitionMissionAsync(id, req, t), m =>
                {
                    d.Complete();
                    string text = !String.IsNullOrEmpty(title)
                        ? _Screen.Tr("Mission \"{{title}}\" moved to {{status}}.", LocalizationArgs.Of("title", title, "status", target))
                        : _Screen.Tr("Mission status changed to {{status}}.", LocalizationArgs.Of("status", target));
                    _Screen.Toast(NotificationSeverityEnum.Success, text);
                    after?.Invoke();
                }, null, ex => d.Fail(detail ? _Screen.Tr("Transition failed: {{message}}", LocalizationArgs.Of("message", ex.Message)) : _Screen.Tr("Transition failed.")));
                return false;
            };
            ctx.Modals.Show(dialog);
            return dialog;
        }

        /// <summary>
        /// Cancel a mission (the list's Cancel: <c>deleteMission</c>, which sets Cancelled).
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="title">Title.</param>
        /// <param name="after">Runs after success.</param>
        public void Cancel(string id, string title, Action? after)
        {
            _Screen.Confirm("Cancel Mission", _Screen.Tr("Cancel mission \"{{title}}\"? The mission will be set to Cancelled status but remains in the database. Use Purge to permanently remove it.", LocalizationArgs.Of("title", title)), () =>
            {
                _Screen.Run((c, t) => c.DeleteMissionAsync(id, t), () =>
                {
                    _Screen.Toast(NotificationSeverityEnum.Warning, _Screen.Tr("Mission \"{{title}}\" cancelled.", LocalizationArgs.Of("title", title)));
                    after?.Invoke();
                }, null, ex => _Screen.ShowMessage(_Screen.Tr("Cancel failed.")));
            }, "Cancel Mission");
        }

        /// <summary>
        /// Purge a mission (permanent).
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="title">Title.</param>
        /// <param name="detail">Mission screen wording.</param>
        /// <param name="after">Runs after success.</param>
        public void Purge(string id, string title, bool detail, Action? after)
        {
            string message = detail
                ? _Screen.Tr("Purge mission \"{{title}}\"? This will clean up all associated resources (branches, worktrees, etc.) and cannot be undone.", LocalizationArgs.Of("title", title))
                : _Screen.Tr("Purge mission \"{{title}}\"? This will permanently remove it and clean up all associated resources. This cannot be undone.", LocalizationArgs.Of("title", title));
            _Screen.Confirm("Purge Mission", message, () =>
            {
                _Screen.Run((c, t) => c.PurgeMissionAsync(id, t), () =>
                {
                    _Screen.Toast(NotificationSeverityEnum.Warning, _Screen.Tr("Mission \"{{title}}\" purged.", LocalizationArgs.Of("title", title)));
                    after?.Invoke();
                }, null, ex => _Screen.ShowMessage(detail ? _Screen.Tr("Purge failed: {{message}}", LocalizationArgs.Of("message", ex.Message)) : _Screen.Tr("Purge failed.")));
            }, "Purge");
        }

        /// <summary>
        /// Delete a mission (the Mission screen's Delete).
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="title">Title.</param>
        /// <param name="after">Runs after success.</param>
        public void Delete(string id, string title, Action? after)
        {
            _Screen.Confirm("Delete Mission", _Screen.Tr("Permanently delete mission \"{{title}}\"? This cannot be undone.", LocalizationArgs.Of("title", title)), () =>
            {
                _Screen.Run((c, t) => c.DeleteMissionAsync(id, t), () =>
                {
                    _Screen.Toast(NotificationSeverityEnum.Warning, _Screen.Tr("Mission \"{{title}}\" deleted.", LocalizationArgs.Of("title", title)));
                    after?.Invoke();
                }, null, ex => _Screen.ShowMessage(_Screen.Tr("Delete failed: {{message}}", LocalizationArgs.Of("message", ex.Message))));
            }, "Delete");
        }

        /// <summary>
        /// Mark a Review mission (without a review gate) Complete.
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="title">Title.</param>
        /// <param name="after">Runs after success.</param>
        public void MarkComplete(string id, string title, Action? after)
        {
            _Screen.Confirm("Mark Complete", _Screen.Tr("Mark mission \"{{title}}\" as Complete? Use this when the work has already landed and the mission just needs to graduate out of Review.", LocalizationArgs.Of("title", title)), () =>
            {
                TransitionRequest req = new TransitionRequest();
                req.Status = "Complete";
                _Screen.Call((c, t) => c.TransitionMissionAsync(id, req, t), m =>
                {
                    _Screen.Toast(NotificationSeverityEnum.Success, _Screen.Tr("Mission \"{{title}}\" marked Complete.", LocalizationArgs.Of("title", title)));
                    after?.Invoke();
                }, null, ex => _Screen.ShowMessage(_Screen.Tr("Mark Complete failed: {{message}}", LocalizationArgs.Of("message", ex.Message))));
            }, "Mark Complete");
        }

        /// <summary>
        /// The Resolve Review dialog (shared with the Approvals center).
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="title">Title.</param>
        /// <param name="comment">Existing review comment, or null.</param>
        /// <param name="after">Runs after a decision succeeds.</param>
        /// <returns>The dialog.</returns>
        public ReviewDecisionModal ResolveReview(string id, string title, string? comment, Action? after)
        {
            TuiContext ctx = _Screen.Context;
            ReviewDecisionModal modal = new ReviewDecisionModal(title, ReviewVerdictEnum.Approve, comment, ctx.Loc, ctx.Theme.Current);
            ctx.Modals.Show(modal, result =>
            {
                if (result is ReviewDecision decision)
                {
                    new ApprovalActions(ctx).SubmitReview(id, title, decision, () =>
                    {
                        ctx.Approvals.Remove(ApprovalKindEnum.MissionReview, id);
                        ctx.Status.NudgeInbox();
                        after?.Invoke();
                    });
                }
            });
            return modal;
        }

        /// <summary>
        /// The Edit Mission dialog (title, description, priority 0-1000); saves the full record so other fields are
        /// kept.
        /// </summary>
        /// <param name="mission">Mission as loaded.</param>
        /// <param name="after">Runs after saving.</param>
        /// <returns>The dialog.</returns>
        public OpsFormDialog Edit(Mission mission, Action? after)
        {
            TuiContext ctx = _Screen.Context;
            OpsFormDialog dialog = _Screen.NewForm("Edit Mission", "Save");
            InputField title = new InputField();
            title.Value = mission.Title;
            title.Validator = v => String.IsNullOrWhiteSpace(v) ? "Title is required." : null;
            OpsTextArea description = new OpsTextArea();
            description.Text = mission.Description ?? "";
            description.ExternalEditor = (text, done) => _Screen.EditExternally(text, done);
            InputField priority = new InputField();
            priority.Value = mission.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture);
            priority.Validator = v => Int32.TryParse(v, out int p) && p >= 0 && p <= 1000 ? null : "Enter a number from 0 to 1000.";
            dialog.AddField("Title", title);
            dialog.AddField("Description", description, null, 6);
            dialog.AddField("Priority", priority);
            dialog.Submit = d =>
            {
                Mission updated = mission;
                updated.Title = title.Value.Trim();
                updated.Description = description.Text;
                updated.Priority = Int32.Parse(priority.Value, System.Globalization.CultureInfo.InvariantCulture);
                string newTitle = updated.Title;
                _Screen.Call((c, t) => c.UpdateMissionAsync(mission.Id, updated, t), m =>
                {
                    d.Complete();
                    _Screen.Toast(NotificationSeverityEnum.Success, _Screen.Tr("Mission \"{{title}}\" saved.", LocalizationArgs.Of("title", newTitle)));
                    after?.Invoke();
                }, null, ex => d.Fail(_Screen.Tr("Save failed: {{message}}", LocalizationArgs.Of("message", ex.Message))));
                return false;
            };
            ctx.Modals.Show(dialog);
            return dialog;
        }

        /// <summary>
        /// The Run Check handoff: opens Delivery, Checks with the mission's vessel, ids, branch, commit, and label
        /// pre-filled through the route query (the TUI's form of the dashboard's router state).
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="missionId">Mission id.</param>
        /// <param name="voyageId">Voyage id, or null.</param>
        /// <param name="branchName">Branch, or null.</param>
        /// <param name="commitHash">Commit, or null.</param>
        /// <param name="label">Label.</param>
        public void RunCheck(string vesselId, string missionId, string? voyageId, string? branchName, string? commitHash, string label)
        {
            Dictionary<string, string> q = new Dictionary<string, string>(StringComparer.Ordinal);
            q["tab"] = "checks";
            q["prefill"] = "1";
            q["vesselId"] = vesselId;
            q["missionId"] = missionId;
            if (!String.IsNullOrEmpty(voyageId)) q["voyageId"] = voyageId!;
            if (!String.IsNullOrEmpty(branchName)) q["branchName"] = branchName!;
            if (!String.IsNullOrEmpty(commitHash)) q["commitHash"] = commitHash!;
            q["label"] = label ?? "";
            _Screen.Context.Navigate("/delivery" + Armada.Tui.Routing.RouteMatch.BuildQuery(q));
        }

        /// <summary>
        /// True when a status counts as finished for the log viewer.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>True when done.</returns>
        public static bool LogCompleted(MissionStatusEnum status)
        {
            return LogCompleteStatuses.Contains(status.ToString());
        }

        #endregion
    }
}
