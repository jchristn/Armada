namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;

    /// <summary>
    /// Stores uploaded acceptance gate reports (RS-1.5) as JSON files under the Admiral's data directory
    /// (<c>gate-reports/</c>), newest kept first. A report names models and pipelines only, never endpoint addresses or
    /// credentials (the gate runner writes it that way). At most <see cref="MaxReports"/> are kept.
    /// </summary>
    public class GateReportStore
    {
        #region Public-Members

        /// <summary>
        /// Largest accepted report, in bytes (1 MiB).
        /// </summary>
        public const int MaxReportBytes = 1024 * 1024;

        /// <summary>
        /// Reports kept; older ones are deleted on upload (200).
        /// </summary>
        public const int MaxReports = 200;

        #endregion

        #region Private-Members

        private static readonly Regex _IdPattern = new Regex("^gate-[0-9]{8}-[0-9]{9}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _Directory;
        private readonly object _Lock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="dataDirectory">Admiral data directory.</param>
        /// <exception cref="ArgumentNullException">Thrown when dataDirectory is null or empty.</exception>
        public GateReportStore(string dataDirectory)
        {
            if (String.IsNullOrEmpty(dataDirectory)) throw new ArgumentNullException(nameof(dataDirectory));
            _Directory = Path.Combine(dataDirectory, "gate-reports");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Store a report under a new id and return it as stored.
        /// </summary>
        /// <param name="report">Report.</param>
        /// <returns>The stored report with Id and UploadedUtc set.</returns>
        /// <exception cref="ArgumentNullException">Thrown when report is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the report has no measures and no pipelines, or is larger than 1 MiB.</exception>
        public GateReport Save(GateReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (report.Measures.Count == 0 && report.Pipelines.Count == 0) throw new ArgumentException("A gate report needs at least one measure or pipeline.");
            lock (_Lock)
            {
                Directory.CreateDirectory(_Directory);
                DateTime now = DateTime.UtcNow;
                string id = "gate-" + now.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
                while (File.Exists(PathFor(id)))
                {
                    now = now.AddMilliseconds(1);
                    id = "gate-" + now.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
                }

                report.Id = id;
                report.UploadedUtc = now;
                string json = JsonSerializer.Serialize(report, _Json);
                if (json.Length > MaxReportBytes) throw new ArgumentException("A gate report must be at most 1 MiB.");
                File.WriteAllText(PathFor(id), json);
                foreach (string old in Ids().Skip(MaxReports)) File.Delete(PathFor(old));
                return report;
            }
        }

        /// <summary>
        /// One page of stored reports, newest first.
        /// </summary>
        /// <param name="pageNumber">Page number (from 1).</param>
        /// <param name="pageSize">Page size (1 to 100).</param>
        /// <returns>The page.</returns>
        public EnumerationResult<GateReport> List(int pageNumber, int pageSize)
        {
            int size = Math.Clamp(pageSize, 1, 100);
            int number = Math.Max(1, pageNumber);
            List<string> ids;
            lock (_Lock) ids = Ids();
            EnumerationResult<GateReport> result = new EnumerationResult<GateReport>();
            result.PageNumber = number;
            result.PageSize = size;
            result.TotalRecords = ids.Count;
            result.TotalPages = ids.Count == 0 ? 0 : (ids.Count + size - 1) / size;
            result.Objects = ids.Skip((number - 1) * size).Take(size).Select(Read).Where(r => r != null).Select(r => r!).ToList();
            return result;
        }

        /// <summary>
        /// Read one report.
        /// </summary>
        /// <param name="id">Report id ("latest" for the newest).</param>
        /// <returns>The report, or null when it does not exist.</returns>
        public GateReport? Read(string? id)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            string trimmed = id.Trim();
            if (String.Equals(trimmed, "latest", StringComparison.OrdinalIgnoreCase))
            {
                string? newest;
                lock (_Lock) newest = Ids().FirstOrDefault();
                if (newest == null) return null;
                trimmed = newest;
            }

            if (!_IdPattern.IsMatch(trimmed)) return null;
            string path = PathFor(trimmed);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonSerializer.Deserialize<GateReport>(File.ReadAllText(path), _Json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private string PathFor(string id)
        {
            return Path.Combine(_Directory, id + ".json");
        }

        private List<string> Ids()
        {
            if (!Directory.Exists(_Directory)) return new List<string>();
            return Directory.GetFiles(_Directory, "gate-*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(n => n != null && _IdPattern.IsMatch(n))
                .Select(n => n!)
                .OrderByDescending(n => n, StringComparer.Ordinal)
                .ToList();
        }

        #endregion
    }
}
