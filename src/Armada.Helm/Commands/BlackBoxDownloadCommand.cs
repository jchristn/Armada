namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Spectre.Console.Cli;
    using Spectre.Console;

    /// <summary>
    /// <c>armada blackbox download &lt;entryId&gt; [-o file.zip]</c>: saves the entry's .zip (every object plus
    /// manifest.json), by default under the server's file name in the current directory.
    /// </summary>
    [Description("Download a black box entry as a .zip")]
    public class BlackBoxDownloadCommand : BlackBoxCommandBase<BlackBoxDownloadSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxDownloadSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return await RunBlackBoxAsync(false, async client =>
            {
                BlackBoxDownload download = await client.DownloadBlackBoxAsync(settings.Id, cancellationToken).ConfigureAwait(false);
                string name = String.IsNullOrEmpty(download.FileName) ? "blackbox-" + settings.Id + ".zip" : Path.GetFileName(download.FileName);
                string path = Path.GetFullPath(String.IsNullOrWhiteSpace(settings.Output) ? name : settings.Output!);
                string? dir = Path.GetDirectoryName(path);
                if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                await File.WriteAllBytesAsync(path, download.Content, cancellationToken).ConfigureAwait(false);
                AnsiConsole.MarkupLine("[green]Saved[/] " + Markup.Escape(path) + " [dim](" + Markup.Escape(BlackBoxCli.Bytes(download.Content.LongLength)) + ")[/]");
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
