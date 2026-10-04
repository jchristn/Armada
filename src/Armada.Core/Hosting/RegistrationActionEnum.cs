namespace Armada.Core.Hosting
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Registration action requested on the command line of the Admiral server or Harbor.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RegistrationActionEnum
    {
        /// <summary>
        /// No registration flag was given; the program runs normally.
        /// </summary>
        [EnumMember(Value = "None")]
        None,

        /// <summary>
        /// --install-service: register the Admiral with the platform service manager.
        /// </summary>
        [EnumMember(Value = "InstallService")]
        InstallService,

        /// <summary>
        /// --uninstall-service: remove the Admiral service registration.
        /// </summary>
        [EnumMember(Value = "UninstallService")]
        UninstallService,

        /// <summary>
        /// --run-service: run the Admiral under the platform service manager.
        /// </summary>
        [EnumMember(Value = "RunService")]
        RunService,

        /// <summary>
        /// --install-startup: register Harbor to start at user login.
        /// </summary>
        [EnumMember(Value = "InstallStartup")]
        InstallStartup,

        /// <summary>
        /// --uninstall-startup: remove the Harbor login registration.
        /// </summary>
        [EnumMember(Value = "UninstallStartup")]
        UninstallStartup
    }
}
