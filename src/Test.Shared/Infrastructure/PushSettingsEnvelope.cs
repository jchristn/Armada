namespace Test.Shared.Infrastructure
{
    using Armada.Core.Settings;

    /// <summary>
    /// The Push part of a GET /api/v1/settings response.
    /// </summary>
    public sealed class PushSettingsEnvelope
    {
        #region Public-Members

        /// <summary>Push settings.</summary>
        public PushSettings? Push { get; set; } = null;

        #endregion
    }
}
