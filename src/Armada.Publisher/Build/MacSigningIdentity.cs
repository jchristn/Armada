namespace Armada.Publisher.Build
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One valid code signing identity reported by <c>security find-identity -v</c>: its SHA-1 hash column and its
    /// certificate common name.
    /// </summary>
    public class MacSigningIdentity
    {
        #region Public-Members

        /// <summary>
        /// SHA-1 hash of the certificate (40 upper-case hex digits). codesign, productbuild, and productsign accept it
        /// as the identity.
        /// </summary>
        public string Sha1 { get; }

        /// <summary>
        /// Certificate common name, for example "Developer ID Application: Name (TEAM123456)".
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Certificate type: the common name before the first ": " (for example "Developer ID Application"), or the
        /// whole name when it has no type separator.
        /// </summary>
        public string CertificateType
        {
            get
            {
                int sep = Name.IndexOf(": ", StringComparison.Ordinal);
                return sep > 0 ? Name.Substring(0, sep) : Name;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="sha1">SHA-1 hash.</param>
        /// <param name="name">Common name.</param>
        public MacSigningIdentity(string sha1, string name)
        {
            Sha1 = sha1 ?? throw new ArgumentNullException(nameof(sha1));
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse the output of <c>security find-identity -v</c>. Each identity line has the fixed shape
        /// <c>N) SHA1 "common name"</c>; other lines (such as the "N valid identities found" summary) are skipped.
        /// </summary>
        /// <param name="output">Command output.</param>
        /// <returns>Identities in output order.</returns>
        public static List<MacSigningIdentity> ParseFindIdentityOutput(string? output)
        {
            List<MacSigningIdentity> result = new List<MacSigningIdentity>();
            if (String.IsNullOrEmpty(output)) return result;
            foreach (string raw in output!.Split('\n'))
            {
                string line = raw.Trim();
                int paren = line.IndexOf(") ", StringComparison.Ordinal);
                if (paren <= 0 || !AllDigits(line.Substring(0, paren))) continue;
                string rest = line.Substring(paren + 2).TrimStart();
                if (rest.Length < 42 || !IsSha1(rest.Substring(0, 40)) || rest[40] != ' ') continue;
                string quoted = rest.Substring(41).Trim();
                if (quoted.Length < 2 || quoted[0] != '"' || quoted[quoted.Length - 1] != '"') continue;
                result.Add(new MacSigningIdentity(rest.Substring(0, 40).ToUpperInvariant(), quoted.Substring(1, quoted.Length - 2)));
            }

            return result;
        }

        /// <summary>
        /// Choose the identity to sign with. A configured identity is used only when it names an identity that is
        /// actually present (by SHA-1 or by exact common name), so a manifest placeholder never reaches codesign;
        /// otherwise the first identity whose certificate type (the common name before ": ") equals
        /// <paramref name="certificateType"/> is chosen. Returns the identity's SHA-1, or empty when none matches.
        /// </summary>
        /// <param name="identities">Identities from <see cref="ParseFindIdentityOutput"/>.</param>
        /// <param name="configured">Configured identity (name or SHA-1), or empty.</param>
        /// <param name="certificateType">Certificate type, for example "Developer ID Application".</param>
        /// <returns>SHA-1, or empty.</returns>
        public static string Select(List<MacSigningIdentity> identities, string? configured, string certificateType)
        {
            if (identities == null) throw new ArgumentNullException(nameof(identities));
            if (String.IsNullOrEmpty(certificateType)) throw new ArgumentNullException(nameof(certificateType));
            if (!String.IsNullOrWhiteSpace(configured))
            {
                string wanted = configured!.Trim();
                foreach (MacSigningIdentity identity in identities)
                {
                    if (String.Equals(identity.Sha1, wanted, StringComparison.OrdinalIgnoreCase)) return identity.Sha1;
                    if (String.Equals(identity.Name, wanted, StringComparison.Ordinal)) return identity.Sha1;
                }
            }

            foreach (MacSigningIdentity identity in identities)
            {
                if (String.Equals(identity.CertificateType, certificateType, StringComparison.Ordinal)) return identity.Sha1;
            }

            return String.Empty;
        }

        #endregion

        #region Private-Methods

        private static bool AllDigits(string text)
        {
            if (text.Length == 0) return false;
            foreach (char c in text)
            {
                if (c < '0' || c > '9') return false;
            }

            return true;
        }

        private static bool IsSha1(string text)
        {
            if (text.Length != 40) return false;
            foreach (char c in text)
            {
                bool hex = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
                if (!hex) return false;
            }

            return true;
        }

        #endregion
    }
}
