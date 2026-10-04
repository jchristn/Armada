namespace Armada.Tui.Screens
{
    /// <summary>
    /// Step of the email login flow.
    /// </summary>
    public enum LoginStepEnum
    {
        /// <summary>
        /// Enter the email.
        /// </summary>
        Email = 0,

        /// <summary>
        /// Pick a tenant (more than one matched).
        /// </summary>
        Tenant = 1,

        /// <summary>
        /// Enter the password.
        /// </summary>
        Password = 2,

        /// <summary>
        /// Change the default password (the server requires it before the session can use the API).
        /// </summary>
        ChangePassword = 3
    }
}
