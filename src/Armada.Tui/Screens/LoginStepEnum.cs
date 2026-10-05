namespace Armada.Tui.Screens
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Step of the email login flow.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
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
        Password = 2
    }
}
