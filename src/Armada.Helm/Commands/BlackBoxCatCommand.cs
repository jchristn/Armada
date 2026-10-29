namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client.Models;
    using Spectre.Console.Cli;

    /// <summary>
    /// <c>armada blackbox cat &lt;entryId&gt; &lt;name&gt; [--offset] [--length]</c>: writes an evidence object's bytes to
    /// stdout as UTF-8 text with range reads (at most 1 MB per request, so a large log streams in pieces). When
    /// <c>--length</c> stops before the end, the next offset is printed to stderr. Use <c>download</c> for binary objects.
    /// </summary>
    [Description("Print a black box evidence object (or a byte range of it) to stdout")]
    public class BlackBoxCatCommand : BlackBoxCommandBase<BlackBoxCatSettings>
    {
        /// <summary>
        /// Bytes per request.
        /// </summary>
        public const long ChunkBytes = 1048576;

        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BlackBoxCatSettings settings, CancellationToken cancellationToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.Offset.HasValue && settings.Offset.Value < 0) { Status("Error: --offset must be 0 or more."); return 1; }
            if (settings.Length.HasValue && settings.Length.Value < 1) { Status("Error: --length must be at least 1."); return 1; }
            return await RunBlackBoxAsync(true, async client =>
            {
                long offset = settings.Offset ?? 0;
                long remaining = settings.Length ?? Int64.MaxValue;
                Decoder decoder = new UTF8Encoding(false).GetDecoder();
                while (remaining > 0)
                {
                    long length = Math.Min(remaining, ChunkBytes);
                    BlackBoxObjectContent chunk = await client.ReadBlackBoxObjectAsync(settings.Id, settings.Name, offset, length, cancellationToken).ConfigureAwait(false);
                    if (chunk.Content.Length == 0) break;
                    char[] chars = new char[decoder.GetCharCount(chunk.Content, 0, chunk.Content.Length)];
                    decoder.GetChars(chunk.Content, 0, chunk.Content.Length, chars, 0);
                    Console.Out.Write(chars);
                    remaining -= chunk.Content.Length;
                    if (chunk.NextOffset == null) break;
                    offset = chunk.NextOffset.Value;
                    if (remaining <= 0) Status("armada: more bytes follow; continue with --offset " + offset + " (of " + chunk.TotalBytes + " bytes).");
                }

                Console.Out.Flush();
                return 0;
            }).ConfigureAwait(false);
        }
    }
}
