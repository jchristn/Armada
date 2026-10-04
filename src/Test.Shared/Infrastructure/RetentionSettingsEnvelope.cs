namespace Test.Shared.Infrastructure
{
    using Armada.Core.Settings;

    /// <summary>
    /// The part of the GET/PUT /api/v1/settings response the retention tests read.
    /// </summary>
    public sealed class RetentionSettingsEnvelope
    {
        /// <summary>
        /// Retention settings group.
        /// </summary>
        public RetentionSettings? Retention { get; set; } = null;
    }
}
