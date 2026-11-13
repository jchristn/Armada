namespace Test.Shared.Infrastructure.BlackBoxStorage
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>
    /// A live black box storage target for the contract suites: how to point settings at it and what backs it.
    /// </summary>
    public sealed class BlackBoxTargetEnvironment
    {
        /// <summary>
        /// Target.
        /// </summary>
        public BlackBoxStorageTargetEnum Target { get; }

        /// <summary>
        /// What backs it (emulator image and port, or the in-process server).
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Whether the target lists keys reliably (false for fake-gcs-server, whose timestamps Google.Apis cannot always parse).
        /// </summary>
        public bool EnumerationReliable { get; }

        private readonly Action<BlackBoxStorageSettings> _Apply;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="target">Target.</param>
        /// <param name="description">Description.</param>
        /// <param name="apply">Configures storage settings for the target (not the Target itself).</param>
        /// <param name="enumerationReliable">Whether listing is reliable.</param>
        public BlackBoxTargetEnvironment(BlackBoxStorageTargetEnum target, string description, Action<BlackBoxStorageSettings> apply, bool enumerationReliable = true)
        {
            Target = target;
            Description = description ?? String.Empty;
            _Apply = apply ?? throw new ArgumentNullException(nameof(apply));
            EnumerationReliable = enumerationReliable;
        }

        /// <summary>
        /// Point settings at this target: sets the target, its connection settings and a unique key prefix (so cases do
        /// not see each other's keys).
        /// </summary>
        /// <param name="settings">Black box settings.</param>
        /// <returns>The settings.</returns>
        public BlackBoxSettings Apply(BlackBoxSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            settings.Storage.Target = Target;
            settings.Storage.KeyPrefix = "armada/bbtest/" + Guid.NewGuid().ToString("N").Substring(0, 12);
            _Apply(settings.Storage);
            return settings;
        }
    }
}
