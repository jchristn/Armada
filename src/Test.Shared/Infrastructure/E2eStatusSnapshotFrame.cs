namespace Test.Shared.Infrastructure
{
    using System;
    using System.Text.Json;
    using Armada.Core.Models;

    /// <summary>
    /// The status.snapshot frame the Admiral sends on /ws after a subscribe: its type and the status summary.
    /// </summary>
    public class E2eStatusSnapshotFrame
    {
        #region Public-Members

        /// <summary>
        /// Frame type ("status.snapshot").
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Status summary.
        /// </summary>
        public ArmadaStatus? Data { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Parse a frame, or return null when the text is not a JSON object.
        /// </summary>
        /// <param name="text">Frame text.</param>
        /// <returns>Frame or null.</returns>
        public static E2eStatusSnapshotFrame? Parse(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            try
            {
                return JsonSerializer.Deserialize<E2eStatusSnapshotFrame>(text, JsonHelper.Options);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion
    }
}
