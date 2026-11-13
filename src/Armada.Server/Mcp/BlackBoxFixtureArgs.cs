namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments of export_blackbox_fixture.
    /// </summary>
    public class BlackBoxFixtureArgs : BlackBoxTargetArgs
    {
        /// <summary>
        /// Fixture name (file name without .json), or null for the mission id.
        /// </summary>
        public string? Name { get; set; } = null;
    }
}
