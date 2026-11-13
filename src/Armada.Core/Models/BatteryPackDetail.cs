namespace Armada.Core.Models
{
    using Armada.Core.Services;
    using System;
    using System.Collections.Generic;

    /// <summary>A pack's editable JSON, validation state, and relative file tree.</summary>
    public class BatteryPackDetail
    {
        /// <summary>Directory identifier, independent of the manifest's editable id.</summary>
        public string Id { get; set; } = String.Empty;
        /// <summary>Original JSON, including user formatting and unknown properties.</summary>
        public string ManifestJson { get; set; } = String.Empty;
        /// <summary>Validated pack summary.</summary>
        public BatteryPackInfo Info { get; set; } = new BatteryPackInfo();
        /// <summary>Relative paths and byte sizes. Symlinks are refused.</summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "Pack management tree is capped; oversized trees are rejected")]
        public List<BatteryPackFile> Files { get; set; } = new List<BatteryPackFile>();
    }

    /// <summary>One regular file under a battery pack.</summary>
    public class BatteryPackFile
    {
        /// <summary>Relative slash-separated path.</summary>
        public string Path { get; set; } = String.Empty;
        /// <summary>File length in bytes.</summary>
        public long Size { get; set; }
    }

    /// <summary>Pack creation or manifest replacement request. ManifestJson must be parseable JSON.</summary>
    public class BatteryPackEditRequest
    {
        /// <summary>Safe directory slug, required for creation.</summary>
        public string PackId { get; set; } = String.Empty;
        /// <summary>Raw manifest JSON, saved even if semantically invalid.</summary>
        public string ManifestJson { get; set; } = String.Empty;
    }

    /// <summary>One UTF-8 text file, capped at one MiB.</summary>
    public class BatteryPackTextFile
    {
        /// <summary>Relative file path.</summary>
        public string Path { get; set; } = String.Empty;
        /// <summary>Text to read or write.</summary>
        public string Content { get; set; } = String.Empty;
    }
}
