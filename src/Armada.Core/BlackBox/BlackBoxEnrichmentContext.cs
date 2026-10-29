namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// What an enricher works on: the entry, its record (mutable), the subject rows, and collectors for file rows, link rows and evidence objects the pipeline writes afterwards.
    /// </summary>
    public class BlackBoxEnrichmentContext
    {
        /// <summary>
        /// Entry (mutable; the pipeline saves it).
        /// </summary>
        public BlackBoxEntry Entry { get; set; } = new BlackBoxEntry();

        /// <summary>
        /// Record (mutable; the pipeline bounds, redacts and saves it).
        /// </summary>
        public BlackBoxRecord Record { get; set; } = new BlackBoxRecord();

        /// <summary>
        /// The mission, or null (voyage entry or purged mission).
        /// </summary>
        public Mission? Mission { get; set; } = null;

        /// <summary>
        /// The voyage, or null.
        /// </summary>
        public Voyage? Voyage { get; set; } = null;

        /// <summary>
        /// The vessel, or null.
        /// </summary>
        public Vessel? Vessel { get; set; } = null;

        /// <summary>
        /// File rows to add (blackbox_files).
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems)]
        public List<BlackBoxFile> Files
        {
            get => _Files;
            set => _Files = value ?? new List<BlackBoxFile>();
        }

        /// <summary>
        /// Link rows to add (blackbox_links).
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems)]
        public List<BlackBoxLink> Links
        {
            get => _Links;
            set => _Links = value ?? new List<BlackBoxLink>();
        }

        /// <summary>
        /// Evidence objects to write.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxEvidenceItems)]
        public List<BlackBoxPendingObject> Objects
        {
            get => _Objects;
            set => _Objects = value ?? new List<BlackBoxPendingObject>();
        }

        private List<BlackBoxFile> _Files = new List<BlackBoxFile>();
        private List<BlackBoxLink> _Links = new List<BlackBoxLink>();
        private List<BlackBoxPendingObject> _Objects = new List<BlackBoxPendingObject>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxEnrichmentContext()
        {
        }
    }
}
