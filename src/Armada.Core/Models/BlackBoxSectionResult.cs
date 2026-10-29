namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One section of an entry's record. Record carries only the requested section (other sections are null).
    /// </summary>
    public class BlackBoxSectionResult
    {
        /// <summary>
        /// Entry.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Section.
        /// </summary>
        public BlackBoxSectionEnum Section { get; set; } = BlackBoxSectionEnum.Subject;

        /// <summary>
        /// Record with only the requested section populated.
        /// </summary>
        public BlackBoxRecord Record { get; set; } = new BlackBoxRecord();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxSectionResult()
        {
        }
    }
}
