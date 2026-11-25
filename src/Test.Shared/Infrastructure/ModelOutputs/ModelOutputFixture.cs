namespace Test.Shared.Infrastructure.ModelOutputs
{
    using System;

    /// <summary>
    /// One canned stage output in a shape a model really produces, with the result Armada should reach for it
    /// (RELIABILITY_SIMPLICITY_PLAN.md RS-1.2). <see cref="ModelOutputFixtures"/> holds the catalog.
    /// </summary>
    public sealed class ModelOutputFixture
    {
        /// <summary>Stable id, used as the test case id.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Persona whose contract the output is read against: Architect, Judge, or Worker.</summary>
        public string Persona { get; set; } = String.Empty;

        /// <summary>What the output gets wrong (or right), in a few words.</summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>The output text.</summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>
        /// The intended result: for a Judge the verdict name (Pass, Fail, NeedsRevision, SpecConflict, None); for a
        /// Worker the completion classification (None, CompletionLineMalformed, CompletionLineMissing); for an
        /// Architect <c>missions:N</c> or <c>rejected</c>.
        /// </summary>
        public string Expected { get; set; } = String.Empty;

        /// <summary>The plan item that makes Armada reach <see cref="Expected"/>, or null when it already does.</summary>
        public string? PendingItem { get; set; }
    }
}
