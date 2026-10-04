namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// A local bare "origin" repository and a working clone of it, for vessel health git tests.
    /// </summary>
    public sealed class VesselHealthRepoPair
    {
        /// <summary>
        /// Path of the bare origin repository.
        /// </summary>
        public string OriginPath { get; set; } = "";

        /// <summary>
        /// Path of the working clone (origin remote configured, main tracking origin/main).
        /// </summary>
        public string WorkingPath { get; set; } = "";
    }
}
