namespace Test.Shared.Infrastructure.ApiSurface
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    /// <summary>
    /// Reads and writes the API surface files (docs/api-surface-1.0.json and docs/API_SURFACE_1.0.md) and regenerates
    /// them from a fresh in-process Admiral (scripts/common/generate-api-surface.sh).
    /// </summary>
    public static class ApiSurfaceFiles
    {
        #region Public-Members

        /// <summary>
        /// File name of the machine-readable surface.
        /// </summary>
        public const string JsonFileName = "api-surface-1.0.json";

        /// <summary>
        /// File name of the human-readable surface.
        /// </summary>
        public const string MarkdownFileName = "API_SURFACE_1.0.md";

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize a surface to JSON.
        /// </summary>
        /// <param name="document">Surface.</param>
        /// <returns>Indented JSON with a trailing newline and LF line endings.</returns>
        public static string ToJson(ApiSurfaceDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            return JsonSerializer.Serialize(document, _Options).Replace("\r\n", "\n") + "\n";
        }

        /// <summary>
        /// Deserialize a surface from JSON.
        /// </summary>
        /// <param name="json">JSON text.</param>
        /// <returns>Surface.</returns>
        public static ApiSurfaceDocument FromJson(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) throw new ArgumentNullException(nameof(json));
            return JsonSerializer.Deserialize<ApiSurfaceDocument>(json, _Options) ?? throw new InvalidDataException("API surface file is empty.");
        }

        /// <summary>
        /// Locate the repository root (the directory holding src/ and docs/) from the test binaries.
        /// </summary>
        /// <returns>Repository root path.</returns>
        public static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "src")) && Directory.Exists(Path.Combine(current.FullName, "docs")))
                    return current.FullName;
                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root from " + AppContext.BaseDirectory);
        }

        /// <summary>
        /// Load the frozen baseline from docs/.
        /// </summary>
        /// <returns>Baseline surface.</returns>
        public static ApiSurfaceDocument LoadBaseline()
        {
            string path = Path.Combine(FindRepositoryRoot(), "docs", JsonFileName);
            if (!File.Exists(path)) throw new FileNotFoundException("API surface baseline not found; run scripts/common/generate-api-surface.sh", path);
            return FromJson(File.ReadAllText(path));
        }

        /// <summary>
        /// Boot a throwaway in-process Admiral, build the live surface, and write both surface files.
        /// </summary>
        /// <param name="outputDirectory">Directory to write into (normally the repository's docs/).</param>
        /// <returns>The generated surface.</returns>
        public static async Task<ApiSurfaceDocument> GenerateAsync(string outputDirectory)
        {
            if (String.IsNullOrEmpty(outputDirectory)) throw new ArgumentNullException(nameof(outputDirectory));
            Directory.CreateDirectory(outputDirectory);
            E2EServerFixture fixture = await E2EServerFixture.AcquireAsync(new object()).ConfigureAwait(false);
            ApiSurfaceDocument document = ApiSurfaceBuilder.Build(fixture.Server);
            File.WriteAllText(Path.Combine(outputDirectory, JsonFileName), ToJson(document), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outputDirectory, MarkdownFileName), ApiSurfaceMarkdown.Render(document), new UTF8Encoding(false));
            return document;
        }

        #endregion
    }
}
