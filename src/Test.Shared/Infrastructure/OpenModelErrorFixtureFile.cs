namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>The open-model error fixture file (src/Test.Shared/Fixtures/OpenModelErrors/open-model-errors.json).</summary>
    public sealed class OpenModelErrorFixtureFile
    {
        /// <summary>Fixture rows.</summary>
        [JsonPropertyName("cases")]
        public List<OpenModelErrorFixtureCase> Cases { get; set; } = new List<OpenModelErrorFixtureCase>();
    }
}
