namespace Test.Shared.Infrastructure.OpenModel
{
    using System;

    /// <summary>
    /// One HTTP request received by <see cref="OpenModelSimulator"/>.
    /// </summary>
    public sealed class SimulatorRequest
    {
        /// <summary>HTTP method.</summary>
        public string Method { get; set; } = String.Empty;

        /// <summary>Absolute path, for example <c>/api/chat</c>.</summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>Request body text (empty when none).</summary>
        public string Body { get; set; } = String.Empty;

        /// <summary>Value of the <c>Authorization</c> header, or null.</summary>
        public string? Authorization { get; set; }
    }
}
