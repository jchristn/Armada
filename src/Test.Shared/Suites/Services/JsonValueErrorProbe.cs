namespace Test.Shared.Suites.Services
{
    /// <summary>Request shape with the member types <see cref="JsonValueErrorTranslatorSuite"/> describes.</summary>
    public sealed class JsonValueErrorProbe
    {
        /// <summary>Nested group with an enum.</summary>
        public JsonValueErrorSelectionProbe? UsageSelection { get; set; }

        /// <summary>Nullable number.</summary>
        public long? ExpectedUsageSelectionRevision { get; set; }

        /// <summary>Boolean.</summary>
        public bool Flag { get; set; }

        /// <summary>Text.</summary>
        public string? Name { get; set; }
    }
}
