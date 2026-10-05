namespace Armada.Proxy.Settings
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using SyslogLogging;

    /// <summary>
    /// Typed shape of a proxysettings.json section. Every property is optional; a property that is present but has the
    /// wrong JSON type makes <see cref="ProxySettings.Load(string?)"/> fail instead of being silently ignored.
    /// </summary>
    public class ProxySettingsFile
    {
        #region Public-Members

        /// <summary>
        /// See <see cref="ProxySettings.DataDirectory"/>.
        /// </summary>
        public string? DataDirectory { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.LogDirectory"/>.
        /// </summary>
        public string? LogDirectory { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.Hostname"/>.
        /// </summary>
        public string? Hostname { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.Port"/>.
        /// </summary>
        public int? Port { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.RequireEnrollmentToken"/>.
        /// </summary>
        [JsonConverter(typeof(StrictBooleanJsonConverter))]
        public bool? RequireEnrollmentToken { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.EnrollmentTokens"/>.
        /// </summary>
        public List<string>? EnrollmentTokens { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.Password"/>.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.AllowDefaultPassword"/>.
        /// </summary>
        [JsonConverter(typeof(StrictBooleanJsonConverter))]
        public bool? AllowDefaultPassword { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.TrustForwardedHeaders"/>.
        /// </summary>
        [JsonConverter(typeof(StrictBooleanJsonConverter))]
        public bool? TrustForwardedHeaders { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.SecureCookie"/>.
        /// </summary>
        [JsonConverter(typeof(StrictBooleanJsonConverter))]
        public bool? SecureCookie { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.LoginMaxFailures"/>.
        /// </summary>
        public int? LoginMaxFailures { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.LoginFailureWindowSeconds"/>.
        /// </summary>
        public int? LoginFailureWindowSeconds { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.LoginLockoutSeconds"/>.
        /// </summary>
        public int? LoginLockoutSeconds { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.HandshakeTimeoutSeconds"/>.
        /// </summary>
        public int? HandshakeTimeoutSeconds { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.StaleAfterSeconds"/>.
        /// </summary>
        public int? StaleAfterSeconds { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.RequestTimeoutSeconds"/>.
        /// </summary>
        public int? RequestTimeoutSeconds { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.MaxRecentEvents"/>.
        /// </summary>
        public int? MaxRecentEvents { get; set; } = null;

        /// <summary>
        /// See <see cref="ProxySettings.SyslogServers"/>.
        /// </summary>
        public List<SyslogServer>? SyslogServers { get; set; } = null;

        #endregion
    }
}
