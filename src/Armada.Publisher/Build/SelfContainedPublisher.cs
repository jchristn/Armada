namespace Armada.Publisher.Build
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Armada.Publisher.Manifest;

    /// <summary>
    /// Produces self-contained, single-file publishes per runtime identifier so the target machine needs
    /// no .NET runtime installed. This is the input every channel packages.
    /// </summary>
    public class SelfContainedPublisher
    {
        #region Private-Members

        private readonly PublisherManifest _Manifest;
        private readonly string _RepoRoot;
        private readonly string _Version;
        private readonly string _OutputRoot;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the publisher.
        /// </summary>
        /// <param name="manifest">Loaded manifest.</param>
        /// <param name="repoRoot">Absolute repository root.</param>
        /// <param name="version">Release version string supplied per release.</param>
        /// <param name="outputRoot">Directory under which publish output is written.</param>
        public SelfContainedPublisher(PublisherManifest manifest, string repoRoot, string version, string outputRoot)
        {
            _Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _RepoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));
            _Version = version ?? throw new ArgumentNullException(nameof(version));
            _OutputRoot = outputRoot ?? throw new ArgumentNullException(nameof(outputRoot));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Publish one artifact for one runtime identifier and record its checksum.
        /// </summary>
        /// <param name="artifact">Artifact to publish.</param>
        /// <param name="runtimeIdentifier">Runtime identifier to target.</param>
        /// <returns>The publish result including binary path and SHA-256.</returns>
        public PublishedArtifact Publish(ArtifactDefinition artifact, string runtimeIdentifier)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            if (string.IsNullOrEmpty(runtimeIdentifier)) throw new ArgumentNullException(nameof(runtimeIdentifier));

            string projectPath = Path.Combine(_RepoRoot, artifact.Project);
            string outputDirectory = Path.Combine(_OutputRoot, artifact.Id, runtimeIdentifier);

            List<string> arguments = new List<string>
            {
                "publish",
                projectPath,
                "-c", "Release",
                "-f", _Manifest.Framework,
                "-r", runtimeIdentifier,
                "--self-contained", "true",
                "-p:PublishSingleFile=true",
                "-p:IncludeNativeLibrariesForSelfExtract=true",
                "-p:DebugType=none",
                "-p:Version=" + _Version,
                "-o", outputDirectory
            };

            ProcessRunner.Run("dotnet", arguments, _RepoRoot);

            string binaryName = artifact.BinaryName;
            if (string.IsNullOrEmpty(binaryName)) binaryName = Path.GetFileNameWithoutExtension(artifact.Project);
            string executableName = runtimeIdentifier.StartsWith("win", StringComparison.OrdinalIgnoreCase) ? binaryName + ".exe" : binaryName;
            string binaryPath = Path.Combine(outputDirectory, executableName);

            if (!File.Exists(binaryPath))
            {
                // The published file name follows the assembly name, which may differ from BinaryName.
                // Fall back to the single produced executable when the expected name is absent.
                binaryPath = LocateProducedBinary(outputDirectory, runtimeIdentifier, binaryPath);
            }

            PublishedArtifact result = new PublishedArtifact
            {
                RuntimeIdentifier = runtimeIdentifier,
                OutputDirectory = outputDirectory,
                BinaryPath = binaryPath,
                Sha256 = ChecksumWriter.WriteSidecar(binaryPath)
            };

            Console.WriteLine("[publish] " + artifact.Id + " " + runtimeIdentifier + " sha256=" + result.Sha256);
            return result;
        }

        #endregion

        #region Private-Methods

        private string LocateProducedBinary(string outputDirectory, string runtimeIdentifier, string expected)
        {
            bool isWindows = runtimeIdentifier.StartsWith("win", StringComparison.OrdinalIgnoreCase);
            string pattern = isWindows ? "*.exe" : "*";

            foreach (string candidate in Directory.EnumerateFiles(outputDirectory, pattern))
            {
                string name = Path.GetFileName(candidate);
                if (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase)) continue;
                if (!isWindows && Path.HasExtension(candidate)) continue;
                return candidate;
            }

            throw new FileNotFoundException("Published binary not found under '" + outputDirectory + "'.", expected);
        }

        #endregion
    }
}
