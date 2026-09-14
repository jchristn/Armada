namespace Armada.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Armada.Publisher.Manifest;
    using Armada.Publisher.Preflight;

    /// <summary>
    /// Publishes a CLI artifact as a NuGet global tool (dotnet tool install -g). The artifact project is
    /// already marked PackAsTool; this channel packs it at the release version and pushes it to nuget.org.
    /// </summary>
    public class NuGetChannel : IChannel
    {
        #region Public-Members

        /// <inheritdoc />
        public ChannelKindEnum Kind
        {
            get { return ChannelKindEnum.NuGet; }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public IEnumerable<ToolRequirement> Requirements()
        {
            return new List<ToolRequirement>
            {
                new ToolRequirement("dotnet", "Install the .NET SDK from https://dotnet.microsoft.com/download")
            };
        }

        /// <inheritdoc />
        public void Execute(ChannelContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            string projectPath = Path.Combine(context.RepoRoot, context.Artifact.Project);
            Directory.CreateDirectory(context.OutputDirectory);

            List<string> packArguments = new List<string>
            {
                "pack",
                projectPath,
                "-c", "Release",
                "-p:Version=" + context.Version,
                "-p:PackAsTool=true",
                "-o", context.OutputDirectory
            };
            ProcessRunner.Run("dotnet", packArguments, context.RepoRoot);

            string apiKey = ResolveSecret(context.Channel.SecretName);
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("[nuget] " + context.Channel.SecretName + " not set; packed only, skipping push.");
                return;
            }

            string package = Directory.EnumerateFiles(context.OutputDirectory, "*.nupkg")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault() ?? throw new InvalidOperationException("No .nupkg was produced.");

            List<string> pushArguments = new List<string>
            {
                "nuget", "push", package,
                "--api-key", apiKey,
                "--source", "https://api.nuget.org/v3/index.json",
                "--skip-duplicate"
            };
            ProcessRunner.Run("dotnet", pushArguments, context.RepoRoot);
        }

        #endregion

        #region Private-Methods

        private static string ResolveSecret(string? name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return Environment.GetEnvironmentVariable(name) ?? string.Empty;
        }

        #endregion
    }
}
