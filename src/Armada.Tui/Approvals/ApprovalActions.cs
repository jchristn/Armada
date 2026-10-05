namespace Armada.Tui.Approvals
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Decisions on approval items with the same API calls, confirmations, and toasts as the dashboard screen each
    /// item comes from: Ask proposals (approve, reject), mission reviews (the Resolve Review dialog), deployments
    /// (approve, deny with confirm), failed landings (retry landing), stalled captains (stop, recall, restart with
    /// confirm), and CLI permission requests (allow once, allow and remember through a rule dialog, deny with an
    /// optional message). A decided item leaves the queue and the inbox is re-polled. Call on the UI loop.
    /// </summary>
    public class ApprovalActions
    {
        #region Private-Members

        private readonly TuiContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Services.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public ApprovalActions(TuiContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Approve (true) or reject (false) an Ask proposal.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="approve">Approve.</param>
        /// <returns>True when the call started.</returns>
        public bool DecideProposal(ApprovalItem item, bool approve)
        {
            if (item == null || item.Kind != ApprovalKindEnum.AskProposal || _Context.Ask == null || String.IsNullOrEmpty(item.ParentId)) return false;
            _Context.Ask.Decide(item.ParentId!, item.EntityId, approve);
            return true;
        }

        /// <summary>
        /// Show an Ask proposal's exact arguments (copy with <c>y</c>).
        /// </summary>
        /// <param name="item">Item.</param>
        /// <returns>The viewer, or null.</returns>
        public ViewerModal? ShowArguments(ApprovalItem item)
        {
            if (item == null || item.Kind != ApprovalKindEnum.AskProposal) return null;
            string args = Pretty(item.Arguments);
            ViewerModal viewer = new ViewerModal(_Context.Loc.T("Exact arguments") + ": " + (item.ToolName ?? ""), new JsonOrTextViewer(args), _Context.Loc, _Context.Theme.Current);
            viewer.CopyRequested += (s, e) => _Context.Clipboard.Copy(item.Arguments ?? "", "Arguments");
            _Context.Modals.Show(viewer);
            return viewer;
        }

        /// <summary>
        /// Open the Resolve Review dialog for a mission review with a verdict preselected, then submit the decision.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="verdict">Preselected verdict.</param>
        /// <returns>The dialog, or null.</returns>
        public ReviewDecisionModal? ResolveReview(ApprovalItem item, ReviewVerdictEnum verdict)
        {
            if (item == null || item.Kind != ApprovalKindEnum.MissionReview) return null;
            ReviewDecisionModal modal = new ReviewDecisionModal(item.EntityName ?? item.Title, verdict, null, _Context.Loc, _Context.Theme.Current);
            _Context.Modals.Show(modal, result =>
            {
                if (result is ReviewDecision decision) SubmitReview(item, decision);
            });
            return modal;
        }

        /// <summary>
        /// Submit a review decision (the dashboard's <c>submitReview</c>).
        /// </summary>
        /// <param name="item">Mission review item.</param>
        /// <param name="decision">Decision.</param>
        public void SubmitReview(ApprovalItem item, ReviewDecision decision)
        {
            Record(item, ReviewDecisionName(decision.Verdict));
            SubmitReview(item.EntityId, item.EntityName ?? item.Title, decision, () => Resolved(item));
        }

        /// <summary>
        /// Submit a review decision for a mission by id (the dashboard's <c>submitReview</c>, shared by the Approvals
        /// center and the Mission screen): the same calls, toasts, and error text.
        /// </summary>
        /// <param name="id">Mission id.</param>
        /// <param name="title">Mission title.</param>
        /// <param name="decision">Decision.</param>
        /// <param name="onDone">Runs on the UI loop after success, or null.</param>
        public void SubmitReview(string id, string title, ReviewDecision decision, Action? onDone)
        {
            ArmadaClient client = _Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    string comment = decision.Comment;
                    string toast;
                    NotificationSeverityEnum severity;
                    if (decision.Verdict == ReviewVerdictEnum.Approve)
                    {
                        MissionReviewApproveRequest req = new MissionReviewApproveRequest();
                        req.Comment = comment.Length > 0 ? comment : null;
                        await client.ApproveMissionReviewAsync(id, req).ConfigureAwait(false);
                        toast = "Review approved for \"{{title}}\".";
                        severity = NotificationSeverityEnum.Success;
                    }
                    else if (decision.Verdict == ReviewVerdictEnum.Conditional)
                    {
                        MissionReviewApproveRequest req = new MissionReviewApproveRequest();
                        req.Comment = comment;
                        req.Conditional = true;
                        await client.ApproveMissionReviewAsync(id, req).ConfigureAwait(false);
                        toast = "Conditionally approved \"{{title}}\". The next step will consider your feedback.";
                        severity = NotificationSeverityEnum.Success;
                    }
                    else if (decision.Verdict == ReviewVerdictEnum.MoreWork)
                    {
                        MissionReviewDenyRequest req = new MissionReviewDenyRequest();
                        req.Comment = comment;
                        req.Action = "RetryStage";
                        await client.DenyMissionReviewAsync(id, req).ConfigureAwait(false);
                        toast = "Sent \"{{title}}\" back for more work with your feedback.";
                        severity = NotificationSeverityEnum.Warning;
                    }
                    else
                    {
                        MissionReviewDenyRequest req = new MissionReviewDenyRequest();
                        req.Comment = comment.Length > 0 ? comment : null;
                        req.Action = "FailPipeline";
                        await client.DenyMissionReviewAsync(id, req).ConfigureAwait(false);
                        toast = "Review denied for \"{{title}}\".";
                        severity = NotificationSeverityEnum.Warning;
                    }

                    _Context.Dispatcher.Post(() =>
                    {
                        _Context.Notifications.Toast(severity, _Context.Loc.T(toast, LocalizationArgs.Of("title", title)));
                        onDone?.Invoke();
                    });
                }
                catch (ArmadaApiException ex)
                {
                    _Context.Dispatcher.Post(() => _Context.ShowError(_Context.Loc.T("Review decision failed: {{message}}", LocalizationArgs.Of("message", ex.Message)), ex));
                }
            });
        }

        /// <summary>
        /// Approve (true) or deny (false) a deployment after the dashboard's confirmation.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="approve">Approve.</param>
        /// <returns>The dialog, or null.</returns>
        public ConfirmDialog? DecideDeployment(ApprovalItem item, bool approve)
        {
            if (item == null || item.Kind != ApprovalKindEnum.DeploymentApproval) return null;
            string title = item.EntityName ?? item.Title;
            string message = approve
                ? _Context.Loc.T("Approve and execute \"{{title}}\"?", LocalizationArgs.Of("title", title))
                : _Context.Loc.T("Deny \"{{title}}\" without executing it?", LocalizationArgs.Of("title", title));
            return _Context.Confirm(approve ? "Approve Deployment" : "Deny Deployment", message, () =>
            {
                Record(item, approve ? "approve" : "deny");
                ArmadaClient client = _Context.Client;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Deployment? updated = approve
                            ? await client.ApproveDeploymentAsync(item.EntityId).ConfigureAwait(false)
                            : await client.DenyDeploymentAsync(item.EntityId).ConfigureAwait(false);
                        string name = updated?.Title ?? title;
                        _Context.Dispatcher.Post(() =>
                        {
                            _Context.Notifications.Toast(NotificationSeverityEnum.Success, _Context.Loc.T("Deployment \"{{title}}\" updated.", LocalizationArgs.Of("title", name)));
                            Resolved(item);
                        });
                    }
                    catch (ArmadaApiException ex)
                    {
                        _Context.Dispatcher.Post(() => _Context.ShowError("Action failed.", ex));
                    }
                });
            }, approve ? "Approve" : "Deny");
        }

        /// <summary>
        /// Retry a failed landing (no confirmation, like the dashboard's Missions list).
        /// </summary>
        /// <param name="item">Item.</param>
        /// <returns>True when the call started.</returns>
        public bool RetryLanding(ApprovalItem item)
        {
            if (item == null || item.Kind != ApprovalKindEnum.FailedLanding) return false;
            string title = item.EntityName ?? item.Title;
            Record(item, "retry");
            ArmadaClient client = _Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    await client.RetryMissionLandingAsync(item.EntityId).ConfigureAwait(false);
                    _Context.Dispatcher.Post(() =>
                    {
                        _Context.Notifications.Toast(NotificationSeverityEnum.Success, _Context.Loc.T("Landing succeeded for \"{{title}}\"", LocalizationArgs.Of("title", title)));
                        Resolved(item);
                    });
                }
                catch (ArmadaApiException ex)
                {
                    _Context.Dispatcher.Post(() => _Context.ShowError("Retry landing failed.", ex));
                }
            });
            return true;
        }

        /// <summary>
        /// Stop, recall, or restart a stalled captain after the dashboard's confirmation.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="action">"stop", "recall", or "restart".</param>
        /// <returns>The dialog, or null.</returns>
        public ConfirmDialog? CaptainAction(ApprovalItem item, string action)
        {
            if (item == null || item.Kind != ApprovalKindEnum.StalledCaptain) return null;
            string name = item.EntityName ?? item.Title;
            string title;
            string message;
            string label;
            if (action == "stop")
            {
                title = "Stop Captain";
                message = _Context.Loc.T("Stop captain \"{{name}}\"? The captain process will be terminated.", LocalizationArgs.Of("name", name));
                label = "Stop";
            }
            else if (action == "recall")
            {
                title = "Recall Captain";
                message = _Context.Loc.T("Recall captain \"{{name}}\"? The captain will be recalled from its current mission.", LocalizationArgs.Of("name", name));
                label = "Recall";
            }
            else if (action == "restart")
            {
                title = "Restart Captain";
                message = _Context.Loc.T("Restart captain \"{{name}}\"? The captain will be deleted and recreated with the same saved configuration.", LocalizationArgs.Of("name", name));
                label = "Restart";
            }
            else
            {
                return null;
            }

            return _Context.Confirm(title, message, () =>
            {
                Record(item, action);
                ArmadaClient client = _Context.Client;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        string toast;
                        NotificationSeverityEnum severity = NotificationSeverityEnum.Warning;
                        if (action == "stop")
                        {
                            await client.StopCaptainAsync(item.EntityId).ConfigureAwait(false);
                            toast = "Captain \"{{name}}\" stopped.";
                        }
                        else if (action == "recall")
                        {
                            await client.RecallCaptainAsync(item.EntityId).ConfigureAwait(false);
                            toast = "Captain \"{{name}}\" recalled.";
                        }
                        else
                        {
                            await client.RestartCaptainAsync(item.EntityId).ConfigureAwait(false);
                            toast = "Captain \"{{name}}\" restarted.";
                            severity = NotificationSeverityEnum.Success;
                        }

                        _Context.Dispatcher.Post(() =>
                        {
                            _Context.Notifications.Toast(severity, _Context.Loc.T(toast, LocalizationArgs.Of("name", name)));
                            Resolved(item);
                        });
                    }
                    catch (ArmadaApiException ex)
                    {
                        string failed = action == "stop" ? "Stop failed." : action == "recall" ? "Recall failed." : "Restart failed.";
                        _Context.Dispatcher.Post(() => _Context.ShowError(failed, ex));
                    }
                });
            }, label);
        }

        /// <summary>
        /// Allow a CLI permission request once (no dialog). Requests the user cannot decide show a notice instead.
        /// </summary>
        /// <param name="item">CLI permission item.</param>
        /// <returns>True when the call started.</returns>
        public bool AllowCliPermissionOnce(ApprovalItem item)
        {
            return AllowCliPermissionOnce(item, null);
        }

        /// <summary>
        /// Allow a CLI permission request once (no dialog), then run <paramref name="finished"/>.
        /// </summary>
        /// <param name="item">CLI permission item.</param>
        /// <param name="finished">Runs on the UI loop when the call ends: the decided request, or null when it failed
        /// or was already decided. Null for none.</param>
        /// <returns>True when the call started.</returns>
        public bool AllowCliPermissionOnce(ApprovalItem item, Action<CliPermissionRequest?>? finished)
        {
            if (!CanDecideCliPermission(item)) return false;
            CliPermissionDecisionRequest decision = new CliPermissionDecisionRequest();
            decision.Decision = CliPermissionDecisionEnum.AllowOnce;
            SubmitCliPermission(item, decision, finished);
            return true;
        }

        /// <summary>
        /// Open the allow-and-remember dialog (rule pattern prefilled with the suggested rule, and the rule scope) for a
        /// CLI permission request, then submit. Only when the request allows remembering (admins).
        /// </summary>
        /// <param name="item">CLI permission item.</param>
        /// <returns>The dialog, or null.</returns>
        public CliPermissionDecisionModal? RememberCliPermission(ApprovalItem item)
        {
            return RememberCliPermission(item, null);
        }

        /// <summary>
        /// Open the allow-and-remember dialog for a CLI permission request, then submit and run
        /// <paramref name="finished"/>.
        /// </summary>
        /// <param name="item">CLI permission item.</param>
        /// <param name="finished">Runs on the UI loop when the call ends (see <see cref="AllowCliPermissionOnce(ApprovalItem, Action{CliPermissionRequest})"/>),
        /// or null.</param>
        /// <returns>The dialog, or null.</returns>
        public CliPermissionDecisionModal? RememberCliPermission(ApprovalItem item, Action<CliPermissionRequest?>? finished)
        {
            if (!CanDecideCliPermission(item)) return null;
            if (!item.CliPermission!.CanRemember)
            {
                _Context.Notifications.Toast(NotificationSeverityEnum.Warning, _Context.Loc.T("Only an admin can save a permission rule. Allow once or deny instead."));
                return null;
            }

            return ShowCliPermissionModal(item, true, finished);
        }

        /// <summary>
        /// Open the deny dialog (optional message for the captain) for a CLI permission request, then submit.
        /// </summary>
        /// <param name="item">CLI permission item.</param>
        /// <returns>The dialog, or null.</returns>
        public CliPermissionDecisionModal? DenyCliPermission(ApprovalItem item)
        {
            return DenyCliPermission(item, null);
        }

        /// <summary>
        /// Open the deny dialog for a CLI permission request, then submit and run <paramref name="finished"/>.
        /// </summary>
        /// <param name="item">CLI permission item.</param>
        /// <param name="finished">Runs on the UI loop when the call ends, or null.</param>
        /// <returns>The dialog, or null.</returns>
        public CliPermissionDecisionModal? DenyCliPermission(ApprovalItem item, Action<CliPermissionRequest?>? finished)
        {
            if (!CanDecideCliPermission(item)) return null;
            return ShowCliPermissionModal(item, false, finished);
        }

        /// <summary>
        /// Submit a CLI permission decision (POST /api/v1/cli-permissions/requests/{id}/decide), toast the outcome, and
        /// drop the item from the queue. A request that is no longer pending (409) also leaves the queue.
        /// </summary>
        /// <param name="item">CLI permission item.</param>
        /// <param name="decision">Decision.</param>
        public void SubmitCliPermission(ApprovalItem item, CliPermissionDecisionRequest decision)
        {
            SubmitCliPermission(item, decision, null);
        }

        /// <summary>
        /// Submit a CLI permission decision, toast the outcome, drop the item from the queue, and run
        /// <paramref name="finished"/> on the UI loop with the decided request (null when the call failed or the request
        /// was already decided).
        /// </summary>
        /// <param name="item">CLI permission item.</param>
        /// <param name="decision">Decision.</param>
        /// <param name="finished">Callback, or null.</param>
        public void SubmitCliPermission(ApprovalItem item, CliPermissionDecisionRequest decision, Action<CliPermissionRequest?>? finished)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            Record(item, decision.Decision == CliPermissionDecisionEnum.AllowOnce ? "allow_once" : decision.Decision == CliPermissionDecisionEnum.AllowAndRemember ? "allow_remember" : "deny");
            string tool = String.IsNullOrEmpty(item.ToolName) ? (item.EntityName ?? item.Title) : item.ToolName!;
            ArmadaClient client = _Context.Client;
            _ = Task.Run(async () =>
            {
                try
                {
                    CliPermissionRequest? updated = await client.DecideCliPermissionRequestAsync(item.EntityId, decision).ConfigureAwait(false);
                    string toast = "Denied {{tool}}.";
                    NotificationSeverityEnum severity = NotificationSeverityEnum.Warning;
                    if (decision.Decision == CliPermissionDecisionEnum.AllowOnce)
                    {
                        toast = "Allowed {{tool}} once.";
                        severity = NotificationSeverityEnum.Success;
                    }
                    else if (decision.Decision == CliPermissionDecisionEnum.AllowAndRemember)
                    {
                        toast = "Allowed {{tool}} and saved the rule {{pattern}}.";
                        severity = NotificationSeverityEnum.Success;
                    }

                    Dictionary<string, object?> args = LocalizationArgs.Of("tool", tool);
                    args["pattern"] = decision.RulePattern ?? "";
                    _Context.Dispatcher.Post(() =>
                    {
                        _Context.Notifications.Toast(severity, _Context.Loc.T(toast, args));
                        Resolved(item);
                        finished?.Invoke(updated);
                    });
                }
                catch (ArmadaApiException ex)
                {
                    _Context.Dispatcher.Post(() =>
                    {
                        if (ex.StatusCode == 409)
                        {
                            _Context.Notifications.Toast(NotificationSeverityEnum.Warning, _Context.Loc.T("This permission request was already decided or expired."));
                            Resolved(item);
                            finished?.Invoke(null);
                            return;
                        }

                        _Context.ShowError("Permission decision failed.", ex);
                        finished?.Invoke(null);
                    });
                }
            });
        }

        /// <summary>
        /// Pretty-print JSON text, or return it as-is.
        /// </summary>
        /// <param name="raw">Text.</param>
        /// <returns>Pretty text.</returns>
        public static string Pretty(string? raw)
        {
            if (String.IsNullOrWhiteSpace(raw)) return "";
            try
            {
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(raw!))
                {
                    return System.Text.Json.JsonSerializer.Serialize(doc.RootElement, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch (System.Text.Json.JsonException)
            {
                return raw!;
            }
        }

        #endregion

        #region Private-Methods

        private void Record(ApprovalItem item, string decision)
        {
            TuiTelemetry.RecordApproval(item.Kind, decision, item.CreatedUtc, _Context.Clock.UtcNow);
        }

        private static string ReviewDecisionName(ReviewVerdictEnum verdict)
        {
            switch (verdict)
            {
                case ReviewVerdictEnum.Approve: return "approve";
                case ReviewVerdictEnum.Conditional: return "conditional";
                case ReviewVerdictEnum.MoreWork: return "more_work";
                default: return "deny";
            }
        }

        private bool CanDecideCliPermission(ApprovalItem item)
        {
            if (item == null || item.Kind != ApprovalKindEnum.CliPermission || item.CliPermission == null) return false;
            if (item.CliPermission.CanDecide) return true;
            _Context.Notifications.Toast(NotificationSeverityEnum.Info, _Context.Loc.T("An admin must decide this request."));
            return false;
        }

        private CliPermissionDecisionModal ShowCliPermissionModal(ApprovalItem item, bool remember, Action<CliPermissionRequest?>? finished)
        {
            CliPermissionDecisionModal modal = new CliPermissionDecisionModal(item.CliPermission!, remember, _Context.Loc, _Context.Theme.Current);
            _Context.Modals.Show(modal, result =>
            {
                if (result is CliPermissionDecisionRequest decision) SubmitCliPermission(item, decision, finished);
            });
            return modal;
        }

        private void Resolved(ApprovalItem item)
        {
            _Context.Approvals.Remove(item.Kind, item.EntityId);
            _Context.Status.NudgeInbox();
        }

        #endregion
    }
}
