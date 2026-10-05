namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Helpers shared by the Activity and System screens: run a server call off the UI loop and come back on it,
    /// show JSON, toast with the dashboard's wording, and format times. Call from the UI loop.
    /// </summary>
    public static class ScreenOps
    {
        #region Public-Methods

        /// <summary>
        /// Run <paramref name="work"/> off the UI loop; on success run <paramref name="onSuccess"/> on the loop, on an
        /// API error show the error dialog titled <paramref name="errorTitle"/> (other errors toast).
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="context">Context.</param>
        /// <param name="work">Work.</param>
        /// <param name="onSuccess">Success continuation (UI loop).</param>
        /// <param name="errorTitle">English error title.</param>
        /// <param name="onError">Optional error continuation (UI loop) after the dialog is shown.</param>
        /// <returns>The background task.</returns>
        public static Task Run<T>(TuiContext context, Func<Task<T>> work, Action<T> onSuccess, string errorTitle, Action<Exception>? onError = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (work == null) throw new ArgumentNullException(nameof(work));
            return Task.Run(async () =>
            {
                try
                {
                    T result = await work().ConfigureAwait(false);
                    context.Dispatcher.Post(() => onSuccess?.Invoke(result));
                }
                catch (ArmadaApiException ex)
                {
                    context.Dispatcher.Post(() =>
                    {
                        if (ex.StatusCode != 401) context.ShowError(errorTitle, ex);
                        onError?.Invoke(ex);
                    });
                }
                catch (Exception ex)
                {
                    context.Dispatcher.Post(() =>
                    {
                        context.Notifications.Toast(NotificationSeverityEnum.Error, context.Loc.T(errorTitle) + ": " + ex.Message);
                        onError?.Invoke(ex);
                    });
                }
            });
        }

        /// <summary>
        /// Run <paramref name="work"/> off the UI loop and pass the result to <paramref name="onSuccess"/> on the loop;
        /// failures are ignored (lookups whose absence is not an error, such as name maps and pickers).
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="context">Context.</param>
        /// <param name="work">Work.</param>
        /// <param name="onSuccess">Success continuation (UI loop).</param>
        /// <returns>The background task.</returns>
        public static Task Quiet<T>(TuiContext context, Func<Task<T>> work, Action<T> onSuccess)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (work == null) throw new ArgumentNullException(nameof(work));
            return Task.Run(async () =>
            {
                try
                {
                    T result = await work().ConfigureAwait(false);
                    context.Dispatcher.Post(() => onSuccess?.Invoke(result));
                }
                catch (Exception)
                {
                    // Non-fatal by design.
                }
            });
        }

        /// <summary>
        /// Run a call with no result (see <see cref="Run{T}"/>).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="work">Work.</param>
        /// <param name="onSuccess">Success continuation (UI loop).</param>
        /// <param name="errorTitle">English error title.</param>
        /// <param name="onError">Optional error continuation.</param>
        /// <returns>The background task.</returns>
        public static Task RunVoid(TuiContext context, Func<Task> work, Action onSuccess, string errorTitle, Action<Exception>? onError = null)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            return Run<bool>(context, async () =>
            {
                await work().ConfigureAwait(false);
                return true;
            }, b => onSuccess?.Invoke(), errorTitle, onError);
        }

        /// <summary>
        /// Show a value as highlighted JSON (<c>y</c> copies it).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="title">English title (ids may be appended).</param>
        /// <param name="value">Value to serialize.</param>
        /// <returns>The modal.</returns>
        public static ViewerModal ShowJson(TuiContext context, string title, object? value)
        {
            JsonViewer viewer = JsonViewer.FromObject(value);
            return ShowViewer(context, title, viewer, viewer.PlainText, "JSON");
        }

        /// <summary>
        /// Show raw JSON or text (pretty-printed when it parses).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="title">English title.</param>
        /// <param name="text">Text.</param>
        /// <returns>The modal.</returns>
        public static ViewerModal ShowText(TuiContext context, string title, string text)
        {
            JsonOrTextViewer viewer = new JsonOrTextViewer(text ?? "");
            return ShowViewer(context, title, viewer, text ?? "", "Text");
        }

        /// <summary>
        /// Show a scrolling viewer in a modal with copy.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="title">English title.</param>
        /// <param name="viewer">Viewer.</param>
        /// <param name="copyText">Text copied with <c>y</c>.</param>
        /// <param name="copyLabel">English label for the copy toast.</param>
        /// <returns>The modal.</returns>
        public static ViewerModal ShowViewer(TuiContext context, string title, ArmadaWidget viewer, string copyText, string copyLabel)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            ViewerModal modal = new ViewerModal(title, viewer, context.Loc, context.Theme.Current);
            modal.CopyRequested += (s, e) => context.Clipboard.Copy(copyText, copyLabel);
            context.Modals.Show(modal);
            return modal;
        }

        /// <summary>
        /// Toast an English message with optional interpolation arguments.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="severity">Severity.</param>
        /// <param name="text">English text.</param>
        /// <param name="args">Arguments, or null.</param>
        public static void Toast(TuiContext context, NotificationSeverityEnum severity, string text, IDictionary<string, object?>? args = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            string message = args != null ? context.Loc.T(text, args) : context.Loc.T(text);
            context.Notifications.Toast(severity, message);
        }

        /// <summary>
        /// Relative time ("5m ago"), or "-" for null.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="utc">UTC time.</param>
        /// <returns>Text.</returns>
        public static string Relative(TuiContext context, DateTime? utc)
        {
            if (utc == null || utc.Value == DateTime.MinValue) return "-";
            return context.Loc.FormatRelative(utc.Value, context.Clock.UtcNow);
        }

        /// <summary>
        /// Local date and time, or "-" for null.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="utc">UTC time.</param>
        /// <returns>Text.</returns>
        public static string DateTimeText(TuiContext context, DateTime? utc)
        {
            if (utc == null || utc.Value == DateTime.MinValue) return "-";
            return context.Loc.FormatDateTime(utc.Value);
        }

        /// <summary>
        /// Relative time followed by the local date and time in parentheses.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="utc">UTC time.</param>
        /// <returns>Text.</returns>
        public static string Both(TuiContext context, DateTime? utc)
        {
            if (utc == null || utc.Value == DateTime.MinValue) return "-";
            return Relative(context, utc) + " (" + DateTimeText(context, utc) + ")";
        }

        /// <summary>
        /// Route for an event's entity, chosen by its <c>EntityType</c> (the dashboard's <c>entityRoute</c>), or null when
        /// the type is missing or has no detail page. Entity type spellings vary across producers ("merge_entry",
        /// "merge-entry", "MergeEntry"), so the type is compared without case, dashes, or underscores; the id is never
        /// inspected.
        /// </summary>
        /// <param name="entityType">Entity type.</param>
        /// <param name="entityId">Entity id.</param>
        /// <returns>Route or null.</returns>
        public static string? EntityRoute(string? entityType, string? entityId)
        {
            if (String.IsNullOrEmpty(entityType) || String.IsNullOrEmpty(entityId)) return null;
            string id = Uri.EscapeDataString(entityId);
            switch (NormalizeEntityType(entityType!))
            {
                case "fleet": return "/fleets/" + id;
                case "vessel": return "/vessels/" + id;
                case "captain": return "/captains/" + id;
                case "mission": return "/missions/" + id;
                case "voyage": return "/voyages/" + id;
                case "signal": return "/signals/" + id;
                case "event": return "/events/" + id;
                case "dock": return "/docks/" + id;
                case "deployment": return "/deployments/" + id;
                case "mergeentry": return "/merge-queue/" + id;
                default: return null;
            }
        }

        #endregion

        #region Private-Methods

        private static string NormalizeEntityType(string entityType)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder(entityType.Length);
            foreach (char c in entityType)
            {
                if (c == '-' || c == '_' || c == ' ') continue;
                sb.Append(Char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }

        #endregion
    }
}
