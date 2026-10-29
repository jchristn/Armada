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
    /// An object an enricher or the capture wants written to an entry (evidence). The writer redacts text objects, caps them at MaxEvidenceObjectBytes (head and tail kept with a marker) and lists them in the record's Evidence section.
    /// </summary>
    public class BlackBoxPendingObject
    {
        /// <summary>
        /// Object name (for example evidence/git-status.txt).
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Content type.
        /// </summary>
        public string ContentType { get; set; } = "text/plain";

        /// <summary>
        /// Uncompressed bytes.
        /// </summary>
        public byte[] Data { get; set; } = Array.Empty<byte>();

        /// <summary>
        /// Whether the bytes are UTF-8 text (redacted and excerpted).
        /// </summary>
        public bool IsText { get; set; } = true;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxPendingObject()
        {
        }
    }
}
