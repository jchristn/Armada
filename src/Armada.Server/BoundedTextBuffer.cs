namespace Armada.Server
{
    using System;
    using System.Text;

    /// <summary>
    /// A live text buffer that keeps at most a fixed number of characters without dropping all early context: once the
    /// text outgrows the limit it holds the first part, a truncation marker, and the most recent part. The recent part
    /// is a ring of characters, so an append costs the length of what is appended rather than a copy of the whole
    /// buffer (RS-10.5). Not thread-safe; callers lock.
    /// </summary>
    internal sealed class BoundedTextBuffer
    {
        private const string TruncationMarker = "\n" + Armada.Core.Services.ProtocolLines.TruncationMarker + "\n";

        private readonly int _MaxChars;
        private readonly int _HeadChars;
        private readonly int _TailChars;
        private readonly StringBuilder _Untruncated = new StringBuilder();
        private string _Head = String.Empty;
        private char[] _Ring = Array.Empty<char>();
        private int _RingStart = 0;
        private int _RingCount = 0;
        private bool _Truncated = false;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="maxChars">Most characters the buffer keeps (the truncation marker included once it truncates).</param>
        /// <exception cref="ArgumentOutOfRangeException">The limit is less than one.</exception>
        public BoundedTextBuffer(int maxChars)
        {
            if (maxChars < 1) throw new ArgumentOutOfRangeException(nameof(maxChars));
            _MaxChars = maxChars;
            _HeadChars = Math.Max(0, (maxChars - TruncationMarker.Length) / 2);
            _TailChars = Math.Max(0, maxChars - TruncationMarker.Length - _HeadChars);
        }

        /// <summary>
        /// Length of the text <see cref="ToString"/> returns.
        /// </summary>
        public int Length => _Truncated ? _Head.Length + TruncationMarker.Length + _RingCount : _Untruncated.Length;

        /// <summary>
        /// Append text as is (for streamed fragments).
        /// </summary>
        /// <param name="text">Text; null appends nothing.</param>
        public void Append(string? text)
        {
            if (String.IsNullOrEmpty(text)) return;
            if (_Truncated)
            {
                WriteRing(text);
                return;
            }

            _Untruncated.Append(text);
            if (_Untruncated.Length <= _MaxChars) return;

            string all = _Untruncated.ToString();
            _Untruncated.Clear();
            _Head = all.Substring(0, Math.Min(_HeadChars, all.Length));
            _Ring = new char[_TailChars];
            _Truncated = true;
            WriteRing(all.Substring(_Head.Length));
        }

        /// <summary>
        /// Append a line, separated from earlier text by a line break.
        /// </summary>
        /// <param name="line">Line; null appends an empty line.</param>
        public void AppendLine(string? line)
        {
            if (Length > 0) Append(Environment.NewLine);
            Append(line ?? String.Empty);
        }

        /// <summary>
        /// The buffered text: everything appended while it fits, else the first part, the marker and the latest part.
        /// </summary>
        /// <returns>Text.</returns>
        public override string ToString()
        {
            if (!_Truncated) return _Untruncated.ToString();
            StringBuilder sb = new StringBuilder(Length);
            sb.Append(_Head);
            sb.Append(TruncationMarker);
            int firstRun = Math.Min(_RingCount, _Ring.Length - _RingStart);
            sb.Append(_Ring, _RingStart, firstRun);
            sb.Append(_Ring, 0, _RingCount - firstRun);
            return sb.ToString();
        }

        private void WriteRing(string text)
        {
            int capacity = _Ring.Length;
            if (capacity == 0) return;
            int start = Math.Max(0, text.Length - capacity);
            for (int i = start; i < text.Length; i++)
            {
                int index = (_RingStart + _RingCount) % capacity;
                _Ring[index] = text[i];
                if (_RingCount < capacity) _RingCount++;
                else _RingStart = (_RingStart + 1) % capacity;
            }
        }
    }
}
