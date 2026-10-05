namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Decodes the C-style quoting git applies to unusual path names in text output
    /// (for example "a/caf\303\251.txt" or "a/tab\there"). Unquoted input is returned unchanged.
    /// </summary>
    public static class GitPathUnquoter
    {
        #region Public-Methods

        /// <summary>
        /// Decode a path token that may be C-quoted.
        /// </summary>
        /// <param name="token">The path token as git printed it.</param>
        /// <returns>The decoded path.</returns>
        public static string Unquote(string token)
        {
            if (String.IsNullOrEmpty(token)) return String.Empty;
            if (token.Length < 2 || token[0] != '"' || token[token.Length - 1] != '"') return token;

            int index = 0;
            string? decoded = TryReadQuoted(token, ref index);
            return decoded != null && index == token.Length ? decoded : token;
        }

        /// <summary>
        /// Read one C-quoted token starting at <paramref name="index"/> (which must point at the opening quote).
        /// On success, <paramref name="index"/> is left just past the closing quote.
        /// </summary>
        /// <param name="text">Source text.</param>
        /// <param name="index">Start index; advanced past the token on success.</param>
        /// <returns>The decoded token, or null when the text at index is not a well-formed quoted token.</returns>
        public static string? TryReadQuoted(string text, ref int index)
        {
            if (text == null || index < 0 || index >= text.Length || text[index] != '"') return null;

            List<byte> bytes = new List<byte>();
            int i = index + 1;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '"')
                {
                    index = i + 1;
                    return Encoding.UTF8.GetString(bytes.ToArray());
                }

                if (c != '\\')
                {
                    bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
                    i++;
                    continue;
                }

                if (i + 1 >= text.Length) return null;
                char e = text[i + 1];
                switch (e)
                {
                    case 'a': bytes.Add(0x07); i += 2; break;
                    case 'b': bytes.Add(0x08); i += 2; break;
                    case 't': bytes.Add(0x09); i += 2; break;
                    case 'n': bytes.Add(0x0A); i += 2; break;
                    case 'v': bytes.Add(0x0B); i += 2; break;
                    case 'f': bytes.Add(0x0C); i += 2; break;
                    case 'r': bytes.Add(0x0D); i += 2; break;
                    case '"': bytes.Add((byte)'"'); i += 2; break;
                    case '\\': bytes.Add((byte)'\\'); i += 2; break;
                    default:
                        if (i + 3 < text.Length && IsOctal(e) && IsOctal(text[i + 2]) && IsOctal(text[i + 3]))
                        {
                            int value = ((e - '0') * 64) + ((text[i + 2] - '0') * 8) + (text[i + 3] - '0');
                            if (value > 255) return null;
                            bytes.Add((byte)value);
                            i += 4;
                        }
                        else
                        {
                            return null;
                        }
                        break;
                }
            }

            return null;
        }

        #endregion

        #region Private-Methods

        private static bool IsOctal(char c)
        {
            return c >= '0' && c <= '7';
        }

        #endregion
    }
}
