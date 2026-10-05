namespace Test.Shared.Suites.Services
{
    using System.Text.Json;
    using Test.Shared.Infrastructure;

    /// <summary>
    /// The ask.tool event payload fields tests check, read back from the recorded object.
    /// </summary>
    public class AskToolEventPayload
    {
        #region Public-Members

        /// <summary>
        /// Thread id.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Turn id.
        /// </summary>
        public string? TurnId { get; set; } = null;

        /// <summary>
        /// Tool phase.
        /// </summary>
        public string? Phase { get; set; } = null;

        /// <summary>
        /// Tool name.
        /// </summary>
        public string? Name { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Read a recorded payload object (an anonymous object in production) as this type.
        /// </summary>
        /// <param name="payload">Recorded payload.</param>
        /// <returns>Typed payload.</returns>
        public static AskToolEventPayload From(object? payload)
        {
            return JsonHelper.Deserialize<AskToolEventPayload>(JsonSerializer.Serialize(payload));
        }

        #endregion
    }
}
