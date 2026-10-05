namespace Armada.Core
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Produces one canonical form of a URL path for security decisions (route policies, allowlists). The canonical
    /// form percent-decodes each segment, collapses repeated slashes, drops a trailing slash, and re-encodes the
    /// segments. Spellings that could resolve differently downstream are rejected outright instead of being guessed
    /// at: percent-encoded slashes, backslashes, dots, percent signs (double encoding), semicolons and control
    /// characters; literal backslashes, semicolons, control characters and query or fragment delimiters; and "." or
    /// ".." segments. A gate should evaluate <see cref="UrlPathCanonicalizationResult.Segments"/> and forward
    /// <see cref="UrlPathCanonicalizationResult.Path"/>, so the request that passes the gate is the request that runs.
    /// </summary>
    public static class UrlPathCanonicalizer
    {
        #region Private-Members

        private static readonly UTF8Encoding _StrictUtf8 = new UTF8Encoding(false, true);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Canonicalize a raw (still percent-encoded) URL path without a query string.
        /// </summary>
        /// <param name="rawPath">Raw path as received, for example <c>/api/v1/fleets/flt_abc</c>.</param>
        /// <returns>The canonical path and segments, or the rejection reason.</returns>
        public static UrlPathCanonicalizationResult Canonicalize(string? rawPath)
        {
            if (String.IsNullOrWhiteSpace(rawPath))
            {
                return Reject(UrlPathRejectionEnum.Empty);
            }

            List<string> segments = new List<string>();
            foreach (string rawSegment in rawPath.Split('/'))
            {
                if (rawSegment.Length == 0)
                {
                    continue;
                }

                if (String.Equals(rawSegment, ".", StringComparison.Ordinal) || String.Equals(rawSegment, "..", StringComparison.Ordinal))
                {
                    return Reject(UrlPathRejectionEnum.DotSegment);
                }

                UrlPathRejectionEnum rejection = TryDecodeSegment(rawSegment, out string decoded);
                if (rejection != UrlPathRejectionEnum.None)
                {
                    return Reject(rejection);
                }

                segments.Add(decoded);
            }

            StringBuilder canonical = new StringBuilder();
            foreach (string segment in segments)
            {
                canonical.Append('/').Append(Uri.EscapeDataString(segment));
            }

            return new UrlPathCanonicalizationResult
            {
                Rejection = UrlPathRejectionEnum.None,
                Segments = segments,
                Path = canonical.Length == 0 ? "/" : canonical.ToString()
            };
        }

        #endregion

        #region Private-Methods

        private static UrlPathCanonicalizationResult Reject(UrlPathRejectionEnum rejection)
        {
            return new UrlPathCanonicalizationResult
            {
                Rejection = rejection
            };
        }

        private static UrlPathRejectionEnum TryDecodeSegment(string rawSegment, out string decoded)
        {
            decoded = String.Empty;
            List<byte> bytes = new List<byte>(rawSegment.Length);
            int index = 0;
            while (index < rawSegment.Length)
            {
                char ch = rawSegment[index];
                if (ch == '%')
                {
                    if (index + 2 >= rawSegment.Length)
                    {
                        return UrlPathRejectionEnum.MalformedEncoding;
                    }

                    int high = HexValue(rawSegment[index + 1]);
                    int low = HexValue(rawSegment[index + 2]);
                    if (high < 0 || low < 0)
                    {
                        return UrlPathRejectionEnum.MalformedEncoding;
                    }

                    byte value = (byte)((high << 4) | low);
                    if (IsAmbiguousEncodedByte(value))
                    {
                        return UrlPathRejectionEnum.AmbiguousEncoding;
                    }

                    bytes.Add(value);
                    index += 3;
                    continue;
                }

                if (IsInvalidLiteral(ch))
                {
                    return UrlPathRejectionEnum.InvalidCharacter;
                }

                if (Char.IsSurrogate(ch))
                {
                    if (!Char.IsHighSurrogate(ch) || index + 1 >= rawSegment.Length || !Char.IsLowSurrogate(rawSegment[index + 1]))
                    {
                        return UrlPathRejectionEnum.MalformedEncoding;
                    }

                    bytes.AddRange(_StrictUtf8.GetBytes(rawSegment.Substring(index, 2)));
                    index += 2;
                    continue;
                }

                bytes.AddRange(_StrictUtf8.GetBytes(ch.ToString()));
                index++;
            }

            try
            {
                decoded = _StrictUtf8.GetString(bytes.ToArray());
            }
            catch (DecoderFallbackException)
            {
                return UrlPathRejectionEnum.MalformedEncoding;
            }

            foreach (char decodedChar in decoded)
            {
                if (Char.IsControl(decodedChar))
                {
                    return UrlPathRejectionEnum.AmbiguousEncoding;
                }
            }

            return UrlPathRejectionEnum.None;
        }

        private static bool IsInvalidLiteral(char ch)
        {
            return ch <= ' ' || ch == (char)0x7F || ch == '\\' || ch == ';' || ch == '?' || ch == '#' || Char.IsControl(ch);
        }

        private static bool IsAmbiguousEncodedByte(byte value)
        {
            return value < 0x20 || value == 0x7F || value == (byte)'/' || value == (byte)'\\' || value == (byte)'.' ||
                value == (byte)'%' || value == (byte)';' || value == (byte)'?' || value == (byte)'#';
        }

        private static int HexValue(char ch)
        {
            if (ch >= '0' && ch <= '9') return ch - '0';
            if (ch >= 'a' && ch <= 'f') return ch - 'a' + 10;
            if (ch >= 'A' && ch <= 'F') return ch - 'A' + 10;
            return -1;
        }

        #endregion
    }
}
