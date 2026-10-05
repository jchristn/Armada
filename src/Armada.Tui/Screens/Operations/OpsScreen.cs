namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Socket;
    using Armada.Tui.Modals;
    using Armada.Tui.Routing;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Base for the OPERATIONS screens (W3): background API calls whose results are posted back to the UI loop and
    /// dropped once the screen is gone, the dashboard's error dialog and toasts, socket subscriptions that end with
    /// the screen, and shared viewers (JSON, diff, log, Markdown) and confirmations. Not thread-safe; use on the UI loop.
    /// </summary>
    public abstract class OpsScreen : ScreenBase
    {
        #region Public-Members

        /// <summary>
        /// True while the screen is the current screen (results arriving after it closes are dropped).
        /// </summary>
        public bool IsLive { get; private set; } = true;

        /// <summary>
        /// Reference data shared by pickers and name lookups on this screen.
        /// </summary>
        public OpsReferenceData Reference { get; }

        /// <summary>
        /// True for global admins.
        /// </summary>
        public bool IsAdmin
        {
            get { return Context.Session.IsGlobalAdmin; }
        }

        /// <summary>
        /// True for tenant admins and global admins (the dashboard's <c>isAdmin || isTenantAdmin</c>).
        /// </summary>
        public bool IsTenantAdmin
        {
            get { return Context.Session.IsGlobalAdmin || Context.Session.IsTenantAdmin; }
        }

        /// <inheritdoc />
        public override string ScreenKey
        {
            get { return _ScreenName ?? base.ScreenKey; }
        }

        /// <inheritdoc />
        public override string Title
        {
            get { return _Title ?? base.Title; }
        }

        #endregion

        #region Private-Members

        private readonly List<IDisposable> _Subscriptions = new List<IDisposable>();
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly string? _ScreenName;
        private readonly string? _Title;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="route">Route.</param>
        /// <param name="context">Services.</param>
        /// <param name="screenName">Screen name for preferences and command ids (hub tabs share the hub's route), or null.</param>
        /// <param name="title">English title, or null for the route's.</param>
        protected OpsScreen(RouteMatch route, TuiContext context, string? screenName = null, string? title = null)
            : base(route, context)
        {
            _ScreenName = screenName;
            _Title = title;
            Reference = new OpsReferenceData(context, () => IsLive);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override void OnDeactivated()
        {
            IsLive = false;
            foreach (IDisposable d in _Subscriptions)
            {
                try { d.Dispose(); } catch (Exception) { }
            }

            _Subscriptions.Clear();
            try { _Cts.Cancel(); } catch (ObjectDisposedException) { }
            base.OnDeactivated();
        }

        /// <summary>
        /// Translate English text through the localizer with arguments.
        /// </summary>
        /// <param name="text">English text.</param>
        /// <param name="args">Arguments.</param>
        /// <returns>Translated text.</returns>
        public string Tr(string text, IDictionary<string, object?> args)
        {
            return Context.Loc.T(text, args);
        }

        /// <summary>
        /// Translate English text.
        /// </summary>
        /// <param name="text">English text.</param>
        /// <returns>Translated text.</returns>
        public string Tr(string text)
        {
            return Context.Loc.T(text);
        }

        /// <summary>
        /// Run an API call off the UI loop; the success or failure handler runs on the loop while the screen is live.
        /// Failures without a handler show the dashboard's error dialog titled <paramref name="failTitle"/>.
        /// </summary>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="call">The call.</param>
        /// <param name="onSuccess">Success handler.</param>
        /// <param name="failTitle">English error title, or null to stay silent.</param>
        /// <param name="onError">Failure handler (replaces the dialog), or null.</param>
        public void Call<TResult>(Func<ArmadaClient, CancellationToken, Task<TResult>> call, Action<TResult> onSuccess, string? failTitle, Action<Exception>? onError = null)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            ArmadaClient client = Context.Client;
            CancellationToken token = _Cts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    TResult result = await call(client, token).ConfigureAwait(false);
                    Post(() => onSuccess?.Invoke(result));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // The screen closed.
                }
                catch (Exception ex)
                {
                    Post(() =>
                    {
                        if (onError != null) onError(ex);
                        else if (failTitle != null) ShowFailure(failTitle, ex);
                    });
                }
            });
        }

        /// <summary>
        /// Run an API call that returns nothing.
        /// </summary>
        /// <param name="call">The call.</param>
        /// <param name="onSuccess">Success handler.</param>
        /// <param name="failTitle">English error title, or null to stay silent.</param>
        /// <param name="onError">Failure handler, or null.</param>
        public void Run(Func<ArmadaClient, CancellationToken, Task> call, Action? onSuccess, string? failTitle, Action<Exception>? onError = null)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            Call<bool>(async (c, t) =>
            {
                await call(c, t).ConfigureAwait(false);
                return true;
            }, ok => onSuccess?.Invoke(), failTitle, onError);
        }

        /// <summary>
        /// Show an error dialog for any exception.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="ex">Error.</param>
        public void ShowFailure(string title, Exception ex)
        {
            if (ex is ArmadaApiException api)
            {
                Context.ShowError(title, api);
                return;
            }

            ErrorDialog dialog = new ErrorDialog(title, ex?.Message ?? "", null, null, 0, false, Context.Loc, Context.Theme.Current);
            Context.Modals.Show(dialog, result =>
            {
                if (result as string == "copy") Context.Clipboard.Copy(dialog.Details(), "Error details");
            });
        }

        /// <summary>
        /// Show an error message (the dashboard's ErrorModal with a plain message).
        /// </summary>
        /// <param name="message">Message (translated).</param>
        public void ShowMessage(string message)
        {
            ErrorDialog dialog = new ErrorDialog("Error", message ?? "", null, null, 0, false, Context.Loc, Context.Theme.Current);
            Context.Modals.Show(dialog, result =>
            {
                if (result as string == "copy") Context.Clipboard.Copy(dialog.Details(), "Error details");
            });
        }

        /// <summary>
        /// Raise a toast.
        /// </summary>
        /// <param name="severity">Severity.</param>
        /// <param name="text">Text (translated).</param>
        public void Toast(NotificationSeverityEnum severity, string text)
        {
            Context.Notifications.Toast(severity, text);
        }

        /// <summary>
        /// Show a record as JSON (View JSON); <c>y</c> copies it.
        /// </summary>
        /// <param name="title">Title (already translated or a record name).</param>
        /// <param name="data">Record.</param>
        /// <returns>The viewer.</returns>
        public ViewerModal ShowJson(string title, object? data)
        {
            string json = ArmadaJson.Serialize(data);
            JsonViewer viewer = new JsonViewer(json);
            ViewerModal modal = new ViewerModal(title, viewer, Context.Loc, Context.Theme.Current);
            modal.CopyRequested += (s, e) => Context.Clipboard.Copy(viewer.PlainText, "JSON");
            Context.Modals.Show(modal);
            return modal;
        }

        /// <summary>
        /// Show a raw JSON string pretty-printed.
        /// </summary>
        /// <param name="title">Title.</param>
        /// <param name="json">JSON text.</param>
        /// <returns>The viewer.</returns>
        public ViewerModal ShowJsonText(string title, string? json)
        {
            JsonViewer viewer = new JsonViewer(json ?? "");
            ViewerModal modal = new ViewerModal(title, viewer, Context.Loc, Context.Theme.Current);
            modal.CopyRequested += (s, e) => Context.Clipboard.Copy(viewer.PlainText, "JSON");
            Context.Modals.Show(modal);
            return modal;
        }

        /// <summary>
        /// Show a unified diff in the diff viewer (<c>[</c>/<c>]</c> move between files, <c>y</c> copies the raw diff).
        /// </summary>
        /// <param name="title">Title.</param>
        /// <param name="diff">Raw diff.</param>
        /// <returns>The viewer.</returns>
        public ViewerModal ShowDiff(string title, string? diff)
        {
            DiffViewer viewer = new DiffViewer(diff ?? "");
            ViewerModal modal = new ViewerModal(title, viewer, Context.Loc, Context.Theme.Current);
            modal.WidthRatio = 0.95;
            modal.HeightRatio = 0.92;
            modal.FooterHint = " [ ] " + Tr("Files") + "  / " + Tr("Search") + "  y " + Tr("Copy") + "  Esc " + Tr("Close") + " ";
            modal.CopyRequested += (s, e) => Context.Clipboard.Copy(viewer.PlainText, "Diff");
            Context.Modals.Show(modal);
            return modal;
        }

        /// <summary>
        /// Show Markdown text (for example mission instructions); <c>y</c> copies the raw text.
        /// </summary>
        /// <param name="title">Title.</param>
        /// <param name="markdown">Markdown.</param>
        /// <returns>The viewer.</returns>
        public ViewerModal ShowMarkdown(string title, string? markdown)
        {
            MarkdownView viewer = new MarkdownView(markdown ?? "");
            ViewerModal modal = new ViewerModal(title, viewer, Context.Loc, Context.Theme.Current);
            modal.WidthRatio = 0.9;
            modal.HeightRatio = 0.9;
            modal.CopyRequested += (s, e) => Context.Clipboard.Copy(viewer.PlainText, "Text");
            Context.Modals.Show(modal);
            return modal;
        }

        /// <summary>
        /// Show plain text; <c>y</c> copies it.
        /// </summary>
        /// <param name="title">Title.</param>
        /// <param name="text">Text.</param>
        /// <returns>The viewer.</returns>
        public ViewerModal ShowText(string title, string? text)
        {
            JsonOrTextViewer viewer = new JsonOrTextViewer(text ?? "");
            ViewerModal modal = new ViewerModal(title, viewer, Context.Loc, Context.Theme.Current);
            modal.CopyRequested += (s, e) => Context.Clipboard.Copy(viewer.PlainText, "Text");
            Context.Modals.Show(modal);
            return modal;
        }

        /// <summary>
        /// Ask for confirmation (the dashboard's ConfirmDialog); <paramref name="onConfirm"/> runs on the loop.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="message">Message (translated).</param>
        /// <param name="onConfirm">Action.</param>
        /// <param name="confirmLabel">English confirm label.</param>
        /// <param name="requiredText">Word to type, or null.</param>
        /// <returns>The dialog.</returns>
        public ConfirmDialog Confirm(string title, string message, Action onConfirm, string confirmLabel = "Confirm", string? requiredText = null)
        {
            return Context.Confirm(title, message, onConfirm, confirmLabel, requiredText);
        }

        /// <summary>
        /// Create a form dialog wired to this screen's modal host.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="submitLabel">English submit label.</param>
        /// <returns>The dialog (not yet shown).</returns>
        public OpsFormDialog NewForm(string title, string submitLabel)
        {
            OpsFormDialog dialog = new OpsFormDialog(title, submitLabel, Context.Loc, Context.Theme.Current);
            dialog.AfterProgrammaticClose = DropClosedModals;
            return dialog;
        }

        /// <summary>
        /// Drop modals that closed without a key press from the modal stack (TUIKit removes closed modals only on
        /// the next key, which it would otherwise swallow).
        /// </summary>
        public void DropClosedModals()
        {
            try { Context.App.Modals.RemoveClosed(); } catch (Exception) { }
        }

        /// <summary>
        /// A select field wired to this screen's modal host.
        /// </summary>
        /// <param name="title">English picker title.</param>
        /// <param name="options">Options.</param>
        /// <param name="placeholder">English placeholder, or null.</param>
        /// <returns>The field.</returns>
        public SelectField<string> NewSelect(string title, List<SelectOption<string>> options, string? placeholder = null)
        {
            SelectField<string> select = new SelectField<string>();
            select.ModalHost = Context.Modals;
            select.PickerTitle = title;
            select.Options = options ?? new List<SelectOption<string>>();
            if (placeholder != null) select.Placeholder = placeholder;
            return select;
        }

        /// <summary>
        /// Open a row or screen action menu.
        /// </summary>
        /// <param name="title">English title.</param>
        /// <param name="items">Items.</param>
        public void ShowMenu(string title, IEnumerable<ActionMenuItem> items)
        {
            ActionMenu.Show(Context.Modals, title, items, Context.Loc, Context.Theme.Current);
        }

        /// <summary>
        /// Copy text and confirm with a toast.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="label">English label.</param>
        public void Copy(string? text, string label)
        {
            if (String.IsNullOrEmpty(text)) return;
            Context.Clipboard.Copy(text!, label);
        }

        /// <summary>
        /// Open <c>$EDITOR</c> on a text and deliver the result on the loop.
        /// </summary>
        /// <param name="initial">Initial text.</param>
        /// <param name="done">Receives the edited text.</param>
        /// <param name="extension">File extension.</param>
        public void EditExternally(string initial, Action<string> done, string extension = ".md")
        {
            string start = initial ?? "";
            _ = Task.Run(async () =>
            {
                string edited;
                try { edited = await Context.External.EditTextAsync(start, extension).ConfigureAwait(false); }
                catch (Exception) { edited = start; }
                Context.Dispatcher.Post(() => done(edited.TrimEnd('\n', '\r')));
            });
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Post an action to the UI loop; dropped when the screen has closed.
        /// </summary>
        /// <param name="action">Action.</param>
        protected void Post(Action action)
        {
            Context.Dispatcher.Post(() =>
            {
                if (IsLive) action();
            });
        }

        /// <summary>
        /// Subscribe to socket events for the life of the screen.
        /// </summary>
        /// <param name="filter">Event type, prefix ending in <c>.</c> or <c>*</c>, or <c>*</c>.</param>
        /// <param name="handler">Handler (runs on the UI loop).</param>
        protected void Subscribe(string filter, Action<ArmadaSocketMessage> handler)
        {
            _Subscriptions.Add(Context.Events.Subscribe(filter, m =>
            {
                if (IsLive) handler(m);
            }));
        }

        /// <summary>
        /// Subscribe to socket events with burst coalescing for the life of the screen.
        /// </summary>
        /// <param name="filter">Event filter.</param>
        /// <param name="callback">Callback (runs on the UI loop at most once per coalescing window).</param>
        protected void SubscribeCoalesced(string filter, Action callback)
        {
            _Subscriptions.Add(Context.Events.SubscribeCoalesced(filter, () =>
            {
                if (IsLive) callback();
            }));
        }

        /// <summary>
        /// Keep a disposable for the life of the screen.
        /// </summary>
        /// <param name="disposable">Disposable.</param>
        protected void Track(IDisposable disposable)
        {
            if (disposable != null) _Subscriptions.Add(disposable);
        }

        #endregion
    }
}
