namespace Armada.Tui.Screens.Entities
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Shared helpers for the entity screens: dashboard-style formatting (relative and full dates, dashes for empty
    /// values, line lists), background calls that report API errors in the error dialog, toasts, and View JSON. Use on
    /// the UI loop thread unless a member says otherwise.
    /// </summary>
    public static class EntityUi
    {
        #region Public-Methods

        /// <summary>
        /// Relative time ("5m ago") or "-".
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="utc">UTC time.</param>
        /// <returns>Text.</returns>
        public static string When(TuiContext context, DateTime? utc)
        {
            if (!utc.HasValue) return "-";
            return context.Loc.FormatRelative(utc.Value, context.Clock.UtcNow);
        }

        /// <summary>
        /// Locale date and time or "-".
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="utc">UTC time.</param>
        /// <returns>Text.</returns>
        public static string Date(TuiContext context, DateTime? utc)
        {
            if (!utc.HasValue) return "-";
            return context.Loc.FormatDateTime(utc.Value);
        }

        /// <summary>
        /// The value, or "-" when blank.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Text.</returns>
        public static string Dash(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? "-" : value!;
        }

        /// <summary>
        /// Trimmed value, or null when blank (the dashboard's <c>value.trim() || null</c>).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Trimmed value or null.</returns>
        public static string? Blank(string? value)
        {
            if (value == null) return null;
            string trimmed = value.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }

        /// <summary>
        /// Join a list one item per line.
        /// </summary>
        /// <param name="values">Values.</param>
        /// <returns>Text.</returns>
        public static string JoinLines(IEnumerable<string>? values)
        {
            return values == null ? "" : String.Join("\n", values);
        }

        /// <summary>
        /// Split text on newlines and commas into trimmed, distinct, non-empty items (the dashboard's list fields).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Items.</returns>
        public static List<string> SplitLines(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return new List<string>();
            return text!.Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Format a number for the current locale.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="value">Value.</param>
        /// <returns>Text.</returns>
        public static string Number(TuiContext context, long value)
        {
            return context.Loc.FormatNumber(value);
        }

        /// <summary>
        /// Format a byte count ("12.3 KB").
        /// </summary>
        /// <param name="bytes">Bytes.</param>
        /// <returns>Text.</returns>
        public static string Bytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.0", CultureInfo.InvariantCulture) + " MB";
            return (mb / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }

        /// <summary>
        /// Format a duration in milliseconds ("850 ms", "12.4 s", "3m 5s").
        /// </summary>
        /// <param name="ms">Milliseconds.</param>
        /// <returns>Text.</returns>
        public static string Duration(double? ms)
        {
            if (!ms.HasValue) return "-";
            double v = ms.Value;
            if (v < 1000) return Math.Round(v).ToString(CultureInfo.InvariantCulture) + " ms";
            if (v < 60000) return (v / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " s";
            TimeSpan span = TimeSpan.FromMilliseconds(v);
            return ((int)span.TotalMinutes).ToString(CultureInfo.InvariantCulture) + "m " + span.Seconds.ToString(CultureInfo.InvariantCulture) + "s";
        }

        /// <summary>
        /// Status text with the badge marker (never color alone).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="status">Status.</param>
        /// <returns>Text.</returns>
        public static string Badge(TuiContext context, string? status)
        {
            if (String.IsNullOrEmpty(status)) return "-";
            return StatusBadge.Marker(status) + " " + context.Loc.T(status!);
        }

        /// <summary>
        /// Translate with <c>{{name}}</c> arguments.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="text">English text.</param>
        /// <param name="name">Argument name.</param>
        /// <param name="value">Argument value.</param>
        /// <returns>Text.</returns>
        public static string T(TuiContext context, string text, string name, object? value)
        {
            return context.Loc.T(text, LocalizationArgs.Of(name, value));
        }

        /// <summary>
        /// Translate with two <c>{{name}}</c> arguments.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="text">English text.</param>
        /// <param name="name1">First name.</param>
        /// <param name="value1">First value.</param>
        /// <param name="name2">Second name.</param>
        /// <param name="value2">Second value.</param>
        /// <returns>Text.</returns>
        public static string T(TuiContext context, string text, string name1, object? value1, string name2, object? value2)
        {
            return context.Loc.T(text, LocalizationArgs.Of(name1, value1, name2, value2));
        }

        /// <summary>
        /// Show a toast (already translated text).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="severity">Severity.</param>
        /// <param name="text">Text.</param>
        public static void Toast(TuiContext context, NotificationSeverityEnum severity, string text)
        {
            context.Notifications.Toast(severity, text);
        }

        /// <summary>
        /// Open View JSON for an object (y copies).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="title">Title (already translated or a record name).</param>
        /// <param name="value">Object.</param>
        /// <returns>The modal.</returns>
        public static ViewerModal ShowJson(TuiContext context, string title, object? value)
        {
            JsonViewer viewer = JsonViewer.FromObject(value);
            ViewerModal modal = new ViewerModal(String.IsNullOrEmpty(title) ? "JSON" : title, viewer, context.Loc, context.Theme.Current);
            modal.CopyRequested += (s, e) => context.Clipboard.Copy(viewer.PlainText, "JSON");
            context.Modals.Show(modal);
            return modal;
        }

        /// <summary>
        /// Run an API call off the UI loop; the result is posted back to <paramref name="onSuccess"/>, and an API error
        /// opens the error dialog titled <paramref name="errorTitle"/>.
        /// </summary>
        /// <typeparam name="TResult">Result type.</typeparam>
        /// <param name="context">Context.</param>
        /// <param name="work">Work.</param>
        /// <param name="onSuccess">Success callback (UI loop).</param>
        /// <param name="errorTitle">English error dialog title.</param>
        /// <param name="onError">Optional error callback (UI loop), after the dialog opens.</param>
        /// <returns>The background task.</returns>
        public static Task Run<TResult>(TuiContext context, Func<CancellationToken, Task<TResult>> work, Action<TResult> onSuccess, string errorTitle, Action<Exception>? onError = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (work == null) throw new ArgumentNullException(nameof(work));
            return Task.Run(async () =>
            {
                try
                {
                    TResult result = await work(CancellationToken.None).ConfigureAwait(false);
                    context.Dispatcher.Post(() => onSuccess?.Invoke(result));
                }
                catch (ArmadaApiException ex)
                {
                    context.Dispatcher.Post(() =>
                    {
                        context.ShowError(errorTitle, ex);
                        onError?.Invoke(ex);
                    });
                }
                catch (Exception ex)
                {
                    ArmadaApiException wrapped = new ArmadaApiException(ex.Message, 0, null, null, null, "", "", null, null, ex);
                    context.Dispatcher.Post(() =>
                    {
                        context.ShowError(errorTitle, wrapped);
                        onError?.Invoke(ex);
                    });
                }
            });
        }

        /// <summary>
        /// Run an API call with no result.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="work">Work.</param>
        /// <param name="onSuccess">Success callback (UI loop).</param>
        /// <param name="errorTitle">English error dialog title.</param>
        /// <returns>The background task.</returns>
        public static Task Run(TuiContext context, Func<CancellationToken, Task> work, Action onSuccess, string errorTitle)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            return Run<bool>(context, async ct =>
            {
                await work(ct).ConfigureAwait(false);
                return true;
            }, r => onSuccess?.Invoke(), errorTitle);
        }

        /// <summary>
        /// Show a form dialog wired to the context (dispatcher, localizer, theme).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="title">English title.</param>
        /// <param name="form">Form.</param>
        /// <param name="saveLabel">English Save label.</param>
        /// <param name="submit">Submit (returns an English error or null; may throw <see cref="ArmadaApiException"/>).</param>
        /// <param name="onSaved">Runs on the UI loop after a successful save.</param>
        /// <returns>The dialog.</returns>
        public static FormDialog ShowForm(TuiContext context, string title, EntityForm form, string saveLabel, Func<CancellationToken, Task<string?>> submit, Action? onSaved = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (form == null) throw new ArgumentNullException(nameof(form));
            FormDialog dialog = new FormDialog(title, form.View, context.Dispatcher, saveLabel, context.Loc, context.Theme.Current);
            dialog.Submit = submit;
            dialog.AfterAsyncClose = () => context.App.Modals.RemoveClosed();
            context.Modals.Show(dialog, result =>
            {
                if (result is bool ok && ok) onSaved?.Invoke();
            });
            return dialog;
        }

        #endregion
    }
}
