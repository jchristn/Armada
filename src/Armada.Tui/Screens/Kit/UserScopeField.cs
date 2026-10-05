namespace Armada.Tui.Screens.Kit
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The dashboard's admin-only "view as user" picker for user-owned tables: "All users" or one user of the tenant.
    /// Hidden for regular users (their lists are already scoped to them server-side). The selected id is sent as the
    /// <c>userId</c> filter. Not thread-safe.
    /// </summary>
    public class UserScopeField : SelectField<string>
    {
        #region Public-Members

        /// <summary>
        /// Selected user id, or empty for all users.
        /// </summary>
        public string UserId
        {
            get { return Value ?? ""; }
        }

        #endregion

        #region Private-Members

        private readonly TuiContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and start loading users when the signed-in user is an admin.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
        public UserScopeField(TuiContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            ModalHost = context.Modals;
            PickerTitle = "View records for a specific user";
            Options = new List<SelectOption<string>> { new SelectOption<string>("", context.Loc.T("All users")) };
            SetValue("");
            Visible = context.Session.IsGlobalAdmin || context.Session.IsTenantAdmin;
            if (!Visible) return;
            _ = ScreenOps.Quiet(context, () => context.Client.ListUsersAsync(), result =>
            {
                List<SelectOption<string>> options = new List<SelectOption<string>> { new SelectOption<string>("", context.Loc.T("All users")) };
                foreach (UserMaster user in result?.Objects ?? new List<UserMaster>())
                {
                    options.Add(new SelectOption<string>(user.Id, Label(user)));
                }

                string current = UserId;
                Options = options;
                SetValue(current);
            });
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Display label for a user ("First Last (email)" or the email).
        /// </summary>
        /// <param name="user">User.</param>
        /// <returns>Label.</returns>
        public static string Label(UserMaster user)
        {
            if (user == null) return "";
            string name = String.Join(" ", new string?[] { user.FirstName, user.LastName }.Where(s => !String.IsNullOrWhiteSpace(s))).Trim();
            return name.Length > 0 ? name + " (" + user.Email + ")" : user.Email;
        }

        #endregion
    }
}
