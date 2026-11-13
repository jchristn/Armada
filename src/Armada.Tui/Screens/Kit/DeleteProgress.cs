namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;

    /// <summary>
    /// Runs every TUI delete, bulk delete, and purge behind <see cref="DeleteProgressDialog"/>, matching the
    /// dashboard's useDeleteProgress: the notice opens as soon as the delete starts ("Deleting vessel", "This may take
    /// some time while related records are cleaned up."), cannot be dismissed while the call is pending, closes on
    /// success (then <c>onSuccess</c> runs), and on failure becomes an error dialog with Retry, Copy details, and
    /// Close. A second delete started while one is pending or its failure is still shown is ignored. Call from the UI
    /// loop; the call itself runs off the loop and every continuation is posted back to it.
    /// </summary>
    public static class DeleteProgress
    {
        #region Public-Members

        /// <summary>
        /// English progress message shown under every delete title.
        /// </summary>
        public const string Message = "This may take some time while related records are cleaned up.";

        #endregion

        #region Private-Members

        private static readonly ConditionalWeakTable<TuiContext, StrongBox<bool>> _Busy = new ConditionalWeakTable<TuiContext, StrongBox<bool>>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// True while a delete started through this helper is pending or its failure is still shown.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <returns>True when busy.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public static bool IsBusy(TuiContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return _Busy.GetOrCreateValue(context).Value;
        }

        /// <summary>
        /// Run a delete with a result behind the progress notice.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="context">Context.</param>
        /// <param name="title">English title, for example "Deleting vessel", or an ICU plural title such as
        /// "{count, plural, one {Deleting # vessel} other {Deleting # vessels}}" with <paramref name="count"/>.</param>
        /// <param name="work">The delete. Runs off the UI loop; Retry runs it again.</param>
        /// <param name="onSuccess">Runs on the UI loop after the notice closes.</param>
        /// <param name="errorTitle">English title for the error dialog.</param>
        /// <param name="count">Record count for an ICU plural title, or null for a single record.</param>
        /// <param name="onDismissed">Runs on the UI loop when the delete failed and the user closed the error without
        /// retrying (or the session expired), or null.</param>
        /// <returns>True when the delete started; false when another delete is still pending or showing its failure.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> or <paramref name="work"/> is null.</exception>
        public static bool Run<T>(TuiContext context, string title, Func<CancellationToken, Task<T>> work, Action<T>? onSuccess, string errorTitle, int? count = null, Action? onDismissed = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (work == null) throw new ArgumentNullException(nameof(work));
            StrongBox<bool> busy = _Busy.GetOrCreateValue(context);
            if (busy.Value) return false;
            busy.Value = true;
            string translatedTitle = count.HasValue
                ? context.Loc.T(title ?? "", LocalizationArgs.Of("count", count.Value))
                : context.Loc.T(title ?? "");
            Attempt(context, busy, translatedTitle, work, onSuccess, errorTitle ?? "Delete failed.", onDismissed);
            return true;
        }

        /// <summary>
        /// Run a delete with no result behind the progress notice (see <see cref="Run{T}"/>).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="title">English title (or ICU plural title with <paramref name="count"/>).</param>
        /// <param name="work">The delete.</param>
        /// <param name="onSuccess">Runs on the UI loop after the notice closes.</param>
        /// <param name="errorTitle">English title for the error dialog.</param>
        /// <param name="count">Record count for an ICU plural title, or null.</param>
        /// <param name="onDismissed">Runs on the UI loop when the user closed the failure without retrying, or null.</param>
        /// <returns>True when the delete started; false when another delete is still pending or showing its failure.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> or <paramref name="work"/> is null.</exception>
        public static bool RunVoid(TuiContext context, string title, Func<CancellationToken, Task> work, Action? onSuccess, string errorTitle, int? count = null, Action? onDismissed = null)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            return Run<bool>(context, title, async token =>
            {
                await work(token).ConfigureAwait(false);
                return true;
            }, ok => onSuccess?.Invoke(), errorTitle, count, onDismissed);
        }

        #endregion

        #region Private-Methods

        private static void Attempt<T>(TuiContext context, StrongBox<bool> busy, string title, Func<CancellationToken, Task<T>> work, Action<T>? onSuccess, string errorTitle, Action? onDismissed)
        {
            DeleteProgressDialog dialog = new DeleteProgressDialog(title, context.Loc.T(Message), context.Loc, context.Theme.Current);
            context.Modals.Show(dialog);
            _ = Task.Run(async () =>
            {
                try
                {
                    T result = await work(CancellationToken.None).ConfigureAwait(false);
                    context.Dispatcher.Post(() =>
                    {
                        busy.Value = false;
                        dialog.Finish();
                        onSuccess?.Invoke(result);
                    });
                }
                catch (Exception ex)
                {
                    context.Dispatcher.Post(() =>
                    {
                        dialog.Finish();
                        ShowFailure(context, busy, title, work, onSuccess, errorTitle, onDismissed, ex);
                    });
                }
            });
        }

        private static void ShowFailure<T>(TuiContext context, StrongBox<bool> busy, string title, Func<CancellationToken, Task<T>> work, Action<T>? onSuccess, string errorTitle, Action? onDismissed, Exception ex)
        {
            ArmadaApiException? api = ex as ArmadaApiException;
            if (api != null && api.StatusCode == 401)
            {
                // The session expired; the shell takes the user back to sign-in.
                busy.Value = false;
                onDismissed?.Invoke();
                return;
            }

            ErrorDialog error = api != null
                ? ErrorDialog.From(errorTitle, api, true, context.Loc, context.Theme.Current)
                : new ErrorDialog(errorTitle, ex.Message ?? "", null, null, 0, true, context.Loc, context.Theme.Current);
            context.Modals.Show(error, result =>
            {
                string? choice = result as string;
                if (choice == "retry")
                {
                    Attempt(context, busy, title, work, onSuccess, errorTitle, onDismissed);
                    return;
                }

                if (choice == "copy") context.Clipboard.Copy(error.Details(), "Error details");
                busy.Value = false;
                onDismissed?.Invoke();
            });
        }

        #endregion
    }
}
