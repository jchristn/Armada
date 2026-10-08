namespace Armada.Core.Connectivity
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// The status line and headers of an HTTP/1.x response, as read by <see cref="UrlProbe"/>.
    /// </summary>
    public class UrlProbeHttpResponse
    {
        #region Public-Members

        /// <summary>
        /// Status code, for example 200 or 101.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Reason phrase from the status line (may be empty).
        /// </summary>
        public string ReasonPhrase
        {
            get { return _ReasonPhrase; }
            set { _ReasonPhrase = value ?? String.Empty; }
        }

        /// <summary>
        /// Response headers, names compared without regard to case. A repeated header keeps its last value.
        /// </summary>
        public Dictionary<string, string> Headers
        {
            get { return _Headers; }
            set { _Headers = value ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        }

        #endregion

        #region Private-Members

        private string _ReasonPhrase = String.Empty;
        private Dictionary<string, string> _Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public-Methods

        /// <summary>
        /// The status as text: "200 OK", or just the code when the reason phrase is empty.
        /// </summary>
        /// <returns>Text.</returns>
        public string DescribeStatus()
        {
            string code = StatusCode.ToString(CultureInfo.InvariantCulture);
            return _ReasonPhrase.Length > 0 ? code + " " + _ReasonPhrase : code;
        }

        /// <summary>
        /// Find where the header block ends: the index just past the blank line, or -1 when it has not arrived yet.
        /// </summary>
        /// <param name="buffer">Bytes received.</param>
        /// <param name="count">Number of valid bytes in <paramref name="buffer"/>.</param>
        /// <returns>Index past the terminating blank line, or -1.</returns>
        public static int FindHeaderEnd(byte[] buffer, int count)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            for (int i = 3; i < count; i++)
            {
                if (buffer[i - 3] == '\r' && buffer[i - 2] == '\n' && buffer[i - 1] == '\r' && buffer[i] == '\n') return i + 1;
            }

            return -1;
        }

        /// <summary>
        /// Read an HTTP/1.x status line and headers.
        /// </summary>
        /// <param name="buffer">Bytes received.</param>
        /// <param name="count">Number of valid bytes in <paramref name="buffer"/>.</param>
        /// <param name="response">The response, when the bytes start with a valid status line.</param>
        /// <returns>True when the bytes are an HTTP response.</returns>
        public static bool TryParse(byte[] buffer, int count, out UrlProbeHttpResponse? response)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            response = null;
            int end = FindHeaderEnd(buffer, count);
            string head = Encoding.Latin1.GetString(buffer, 0, end > 0 ? end : count);
            string[] lines = head.Split("\r\n");
            if (lines.Length == 0) return false;

            // Status line: HTTP/1.1 200 OK (the reason phrase may be empty or contain spaces).
            string[] status = lines[0].Split(' ', 3);
            if (status.Length < 2 || !status[0].StartsWith("HTTP/1.", StringComparison.Ordinal)) return false;
            if (!Int32.TryParse(status[1], NumberStyles.None, CultureInfo.InvariantCulture, out int code) || code < 100 || code > 999) return false;

            UrlProbeHttpResponse parsed = new UrlProbeHttpResponse { StatusCode = code, ReasonPhrase = status.Length > 2 ? status[2].Trim() : String.Empty };
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                parsed.Headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }

            response = parsed;
            return true;
        }

        #endregion
    }
}
