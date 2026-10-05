namespace Armada.Tui.Approvals
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Tui.Modals;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The CLI tool permission policy picker shared by the server settings, the captain form, and the Ask header (the
    /// dashboard's <c>CliPermissionPolicySelect</c>): Inherit (where it applies), Refuse, Approve in Armada, and Bypass,
    /// which is offered only to callers allowed to choose it (a stored Bypass is always shown, disabled, so the field
    /// reflects the stored state) and must first be confirmed in the strong warning dialog; cancelling keeps the previous
    /// value. Option values are the policy names, with the empty string for Inherit. Call on the UI loop.
    /// </summary>
    public static class CliPermissionPolicyChoice
    {
        #region Public-Members

        /// <summary>
        /// Title of the Bypass confirmation.
        /// </summary>
        public const string BypassTitle = "Allow every CLI tool call without asking?";

        /// <summary>
        /// Confirm button of the Bypass confirmation.
        /// </summary>
        public const string BypassConfirmLabel = "Use Bypass";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Option value of a policy (the policy name, or empty for Inherit).
        /// </summary>
        /// <param name="policy">Policy, or null for Inherit.</param>
        /// <returns>Value.</returns>
        public static string ValueOf(CliPermissionPolicyEnum? policy)
        {
            return policy.HasValue ? policy.Value.ToString() : "";
        }

        /// <summary>
        /// Policy of an option value (null for Inherit or an unknown value).
        /// </summary>
        /// <param name="value">Value.</param>
        /// <returns>Policy or null.</returns>
        public static CliPermissionPolicyEnum? Parse(string? value)
        {
            if (String.IsNullOrEmpty(value)) return null;
            return Enum.TryParse<CliPermissionPolicyEnum>(value, false, out CliPermissionPolicyEnum parsed) ? parsed : (CliPermissionPolicyEnum?)null;
        }

        /// <summary>
        /// Picker options: Inherit (when allowed), Refuse, Approve in Armada, and Bypass when it may be chosen or is the
        /// current value (then disabled for a caller who may not choose it).
        /// </summary>
        /// <param name="loc">Localizer.</param>
        /// <param name="allowInherit">Offer Inherit (captains and threads; server defaults have none).</param>
        /// <param name="inheritLabel">Label for Inherit (English, localized here), or null for "Inherit".</param>
        /// <param name="allowBypass">The caller may choose Bypass.</param>
        /// <param name="current">Current value, or null.</param>
        /// <returns>Options.</returns>
        public static List<SelectOption<string>> Options(ITextLocalizer loc, bool allowInherit, string? inheritLabel, bool allowBypass, CliPermissionPolicyEnum? current)
        {
            if (loc == null) throw new ArgumentNullException(nameof(loc));
            List<SelectOption<string>> options = new List<SelectOption<string>>();
            if (allowInherit) options.Add(new SelectOption<string>("", inheritLabel != null ? inheritLabel : CliPermissionText.Policy(loc, null)));
            options.Add(new SelectOption<string>(ValueOf(CliPermissionPolicyEnum.Refuse), CliPermissionText.Policy(loc, CliPermissionPolicyEnum.Refuse)));
            options.Add(new SelectOption<string>(ValueOf(CliPermissionPolicyEnum.ApproveInArmada), CliPermissionText.Policy(loc, CliPermissionPolicyEnum.ApproveInArmada)));
            if (allowBypass || current == CliPermissionPolicyEnum.Bypass)
            {
                SelectOption<string> bypass = new SelectOption<string>(ValueOf(CliPermissionPolicyEnum.Bypass), CliPermissionText.Policy(loc, CliPermissionPolicyEnum.Bypass), allowBypass ? "" : loc.T("Only admins can change this."));
                bypass.Enabled = allowBypass;
                options.Add(bypass);
            }

            return options;
        }

        /// <summary>
        /// Show the Bypass warning; <paramref name="onConfirm"/> runs only when the user confirms.
        /// </summary>
        /// <param name="context">Services.</param>
        /// <param name="onConfirm">Runs after confirmation.</param>
        /// <param name="onCancel">Runs when the user cancels, or null.</param>
        /// <returns>The dialog.</returns>
        public static ConfirmDialog ConfirmBypass(TuiContext context, Action onConfirm, Action? onCancel = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (onConfirm == null) throw new ArgumentNullException(nameof(onConfirm));
            ConfirmDialog dialog = new ConfirmDialog(BypassTitle, CliPermissionText.BypassWarning(context.Loc), BypassConfirmLabel, "Cancel", null, context.Loc, context.Theme.Current);
            dialog.Destructive = true;
            context.Modals.Show(dialog, result =>
            {
                if (result is bool ok && ok) onConfirm();
                else onCancel?.Invoke();
            });
            return dialog;
        }

        /// <summary>
        /// Guard a policy field: choosing Bypass reverts to the previous value and asks for the strong confirmation (or,
        /// for a caller who may not choose it, says only admins can); confirming selects Bypass.
        /// </summary>
        /// <param name="context">Services.</param>
        /// <param name="field">Field whose option values are policy names.</param>
        /// <param name="allowBypass">Whether the caller may choose Bypass now.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static void GuardBypass(TuiContext context, SelectField<string> field, Func<bool> allowBypass)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (allowBypass == null) throw new ArgumentNullException(nameof(allowBypass));
            bool confirmed = false;
            field.ValueChanged += (s, e) =>
            {
                if (confirmed || !String.Equals(e.NewValue, ValueOf(CliPermissionPolicyEnum.Bypass), StringComparison.Ordinal)) return;
                string? previous = e.OldValue;
                field.SetValue(previous);
                if (!allowBypass())
                {
                    context.Notifications.Toast(NotificationSeverityEnum.Warning, context.Loc.T("Only admins can change this."));
                    return;
                }

                ConfirmBypass(context, () =>
                {
                    SelectOption<string>? bypass = field.Options.FirstOrDefault(o => String.Equals(o.Value, ValueOf(CliPermissionPolicyEnum.Bypass), StringComparison.Ordinal));
                    if (bypass == null) return;
                    confirmed = true;
                    try { field.Choose(bypass); }
                    finally { confirmed = false; }
                });
            };
        }

        #endregion
    }
}
