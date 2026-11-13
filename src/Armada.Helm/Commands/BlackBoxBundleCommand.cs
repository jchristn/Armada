namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Spectre.Console.Cli;

    /// <summary>
    /// <c>armada blackbox bundle [filters] [--max] [--format markdown|json] [-o bundle.md]</c>: the maintainer bundle (up
    /// to 50 entries grouped by fingerprint). Without <c>-o</c> only the bundle goes to stdout.
    /// </summary>
    [Description("Build the maintainer bundle of black box entries")]
    public class BlackBoxBundleCommand : BlackBoxCommandBase<BlackBoxBundleSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxBundleSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            bool toStdout = String.IsNullOrWhiteSpace(settings.Output);
            return await RunBlackBoxAsync(toStdout, async client =>
            {
                BlackBoxBundleRequest request = BlackBoxCli.ApplyFilters(new BlackBoxBundleRequest(), settings, DateTime.UtcNow);
                if (settings.Max.HasValue) request.Max = settings.Max.Value;
                request.Format = BlackBoxCli.ParseEnum<BlackBoxBundleFormatEnum>("--format", settings.Format) ?? (settings.Json ? BlackBoxBundleFormatEnum.Json : BlackBoxBundleFormatEnum.Markdown);
                request.RedactHosts = settings.RedactHosts;
                BlackBoxBundleResult? bundle = await client.GetBlackBoxBundleAsync(request, cancellationToken).ConfigureAwait(false);
                if (bundle == null) return 1;
                if (toStdout)
                {
                    Console.Out.Write(bundle.Content);
                    if (!bundle.Content.EndsWith("\n", StringComparison.Ordinal)) Console.Out.WriteLine();
                }
                else
                {
                    string path = Path.GetFullPath(settings.Output!);
                    string? dir = Path.GetDirectoryName(path);
                    if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    await File.WriteAllTextAsync(path, bundle.Content, cancellationToken).ConfigureAwait(false);
                    Status("Saved " + path + " (" + bundle.EntryCount + " entries, " + bundle.FingerprintCount + " fingerprints" + (bundle.Truncated ? ", truncated" : "") + ").");
                }

                return 0;
            }).ConfigureAwait(false);
        }
    }
}
