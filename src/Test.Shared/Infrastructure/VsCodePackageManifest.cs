namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// The parts of src/Armada.VSCode/package.json the parity suite reads.
    /// </summary>
    public sealed class VsCodePackageManifest
    {
        /// <summary>Publisher id.</summary>
        public string Publisher { get; set; } = "";

        /// <summary>Extension name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Contributions.</summary>
        public VsCodeContributions Contributes { get; set; } = new VsCodeContributions();
    }
}
