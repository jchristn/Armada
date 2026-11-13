namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// One row of GET /api/v1/doctor, as the E2E suites read it.
    /// </summary>
    public sealed class DoctorRow
    {
        /// <summary>
        /// Check name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Pass, Warn or Fail.
        /// </summary>
        public string Status { get; set; } = String.Empty;

        /// <summary>
        /// Message.
        /// </summary>
        public string Message { get; set; } = String.Empty;
    }
}
