namespace Armada.Server
{
    using System;
    using System.Text;

    /// <summary>
    /// Helper for keeping live text buffers bounded without dropping all early context.
    /// </summary>
    internal static class BoundedTextBuffer
    {
        private const string TruncationMarker = "\n" + Armada.Core.Services.ProtocolLines.TruncationMarker + "\n";

        public static void AppendLine(StringBuilder builder, string line, int maxChars)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            if (maxChars < 1) throw new ArgumentOutOfRangeException(nameof(maxChars));

            if (builder.Length > 0) builder.AppendLine();
            builder.Append(line ?? String.Empty);
            Trim(builder, maxChars);
        }

        public static void Trim(StringBuilder builder, int maxChars)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            if (maxChars < 1) throw new ArgumentOutOfRangeException(nameof(maxChars));
            if (builder.Length <= maxChars) return;

            int markerLength = TruncationMarker.Length;
            int headChars = Math.Max(0, (maxChars - markerLength) / 2);
            int tailChars = Math.Max(0, maxChars - markerLength - headChars);
            string current = builder.ToString();

            string head = headChars > 0
                ? current.Substring(0, Math.Min(headChars, current.Length))
                : String.Empty;
            string tail = tailChars > 0
                ? current.Substring(Math.Max(0, current.Length - tailChars))
                : String.Empty;

            builder.Clear();
            builder.Append(head);
            if (builder.Length + markerLength + tail.Length > maxChars && tail.Length > 0)
            {
                int allowedTail = Math.Max(0, maxChars - builder.Length - markerLength);
                tail = tail.Substring(Math.Max(0, tail.Length - allowedTail));
            }
            builder.Append(TruncationMarker);
            builder.Append(tail);
        }
    }
}
