#if NET10_0_OR_GREATER
namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Publisher.Build;
    using Armada.Publisher.Channels;
    using Armada.Publisher.Manifest;
    using Armada.Publisher.Preflight;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Covers the installer Publisher (src/Armada.Publisher): publisher.json gives every shipped component a native
    /// installer on Windows, macOS and Linux, the Windows signing tool is required only when signing is configured, the
    /// CLI .msi installs the command name on PATH, and Linux tray packages declare the desktop libraries they load.
    /// Compiled for net10.0 only because the Publisher targets net10.0.
    /// </summary>
    public sealed class PublisherSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.Publisher";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("manifest_channels_resolve", "Every enabled channel names a known artifact and a real recipe", TestTags.Positive, () =>
            {
                PublisherManifest manifest = LoadManifest();
                foreach (ChannelDefinition channel in manifest.Channels.Where(c => c.Enabled))
                {
                    ArtifactDefinition artifact = manifest.ResolveArtifact(channel);
                    AssertNotNull(artifact, channel.Name + " artifact");
                    IChannel implementation = ChannelFactory.Create(channel.Kind);
                    AssertFalse(implementation is StubChannel, channel.Name + " is enabled but its recipe is a stub");
                    foreach (string runtime in channel.Runtimes)
                    {
                        AssertTrue(manifest.RuntimeIdentifiers.Contains(runtime), channel.Name + " runtime " + runtime + " is in the RID matrix");
                    }
                }
            }));

            cases.Add(Case("every_component_installs_on_every_os", "Every artifact has a native installer on Windows, macOS and Linux", TestTags.Positive, () =>
            {
                PublisherManifest manifest = LoadManifest();
                foreach (ArtifactDefinition artifact in manifest.Artifacts)
                {
                    HashSet<string> covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (ChannelDefinition channel in manifest.Channels.Where(c => c.Enabled && c.Kind != ChannelKindEnum.NuGet))
                    {
                        if (!string.Equals(channel.Artifact, artifact.Id, StringComparison.OrdinalIgnoreCase)) continue;
                        List<string> runtimes = channel.Runtimes.Count > 0 ? channel.Runtimes : manifest.RuntimeIdentifiers;
                        foreach (string runtime in runtimes) covered.Add(runtime.Split('-')[0]);
                    }

                    foreach (string os in new[] { "win", "osx", "linux" })
                    {
                        AssertTrue(covered.Contains(os), artifact.Id + " has no native installer for " + os);
                    }
                }
            }));

            cases.Add(Case("signtool_required_only_when_signing", "signtool is required only when the Windows certificate secret is set", TestTags.Positive, () =>
            {
                string secret = "ARMADA_TEST_CERT_" + Guid.NewGuid().ToString("N");
                PublisherManifest manifest = new PublisherManifest();
                manifest.Signing.Windows.CertBase64Secret = secret;

                foreach (IChannel channel in new IChannel[] { new InnoChannel(), new WixChannel() })
                {
                    ToolRequirement? signtool = channel.Requirements().FirstOrDefault(r => r.Executable == "signtool");
                    AssertNotNull(signtool, channel.Kind + " declares signtool");
                    AssertNotNull(signtool.RequiredWhen, channel.Kind + " signtool is conditional");
                    AssertFalse(signtool.RequiredWhen(manifest), channel.Kind + " without the secret");
                    AssertFalse(string.IsNullOrEmpty(signtool.NotRequiredReason), channel.Kind + " skip reason");

                    Environment.SetEnvironmentVariable(secret, "MIIB");
                    try
                    {
                        AssertTrue(signtool.RequiredWhen(manifest), channel.Kind + " with the secret");
                    }
                    finally
                    {
                        Environment.SetEnvironmentVariable(secret, null);
                    }
                }
            }));

            cases.Add(Case("wix_cli_installs_command_on_path", "The CLI .msi installs armada.exe and adds its folder to PATH", TestTags.Positive, () =>
            {
                PublisherManifest manifest = LoadManifest();
                string cliWxs = BuildWxs(manifest, "wix-cli", "Armada.Helm.exe");
                AssertContains("Name=\"armada.exe\"", cliWxs);
                AssertContains("<Environment Id=\"PathEntry\" Name=\"PATH\" Value=\"[INSTALLFOLDER]\" Permanent=\"no\" Part=\"last\" Action=\"set\" System=\"yes\" />", cliWxs);
                AssertFalse(cliWxs.Contains("CustomAction"), "the CLI registers no service or startup entry");

                string serverWxs = BuildWxs(manifest, "wix-server", "Armada.Server.exe");
                AssertFalse(serverWxs.Contains("Environment"), "the server .msi does not touch PATH");
                AssertFalse(serverWxs.Contains("Name=\"armada-server.exe\""), "the server keeps its published file name");
                AssertContains("RegisterArtifact", serverWxs);
            }));

            cases.Add(Case("linux_tray_desktop_dependencies", "Linux tray packages depend on the X11 libraries; other packages do not", TestTags.Positive, () =>
            {
                List<string> deb = DebRpmChannel.DesktopDependencies("deb");
                List<string> rpm = DebRpmChannel.DesktopDependencies("rpm");
                foreach (string library in new[] { "libx11-6", "libice6", "libsm6", "libfontconfig1" }) AssertTrue(deb.Contains(library), "deb " + library);
                foreach (string library in new[] { "libX11", "libICE", "libSM", "fontconfig" }) AssertTrue(rpm.Contains(library), "rpm " + library);
                AssertFalse(DebRpmChannel.Dependencies("deb").Any(d => d.Contains("libx11", StringComparison.OrdinalIgnoreCase)), "base deb dependencies stay headless");
                AssertTrue(DebRpmChannel.Dependencies("deb").Contains("git"), "git");
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Installer Publisher",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static PublisherManifest LoadManifest()
        {
            return ManifestLoader.Load(Path.Combine(FindRepositoryRoot(), "publisher.json"));
        }

        private static string BuildWxs(PublisherManifest manifest, string channelName, string publishedExecutable)
        {
            ChannelDefinition? channel = manifest.FindChannel(channelName);
            AssertNotNull(channel, channelName);
            ArtifactDefinition artifact = manifest.ResolveArtifact(channel);
            string root = Path.Combine(Path.GetTempPath(), "armada-publisher-test");
            ChannelContext context = new ChannelContext(manifest, channel, artifact, "1.2.3", root, Path.Combine(root, "out"));
            PublishedArtifact published = new PublishedArtifact
            {
                RuntimeIdentifier = "win-x64",
                OutputDirectory = Path.Combine(root, "publish"),
                BinaryPath = Path.Combine(root, "publish", publishedExecutable)
            };
            return WixChannel.BuildWxs(context, published);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "publisher.json")) && Directory.Exists(Path.Combine(current.FullName, "src")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
            throw new DirectoryNotFoundException("Repository root with publisher.json not found above " + AppContext.BaseDirectory);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
#endif
