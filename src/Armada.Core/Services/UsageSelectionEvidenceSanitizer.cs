namespace Armada.Core.Services
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>Bound diagnostic labels without retaining account emails, credential paths or secret-shaped values.</summary>
    public static class UsageSelectionEvidenceSanitizer
    {
        private static readonly Regex _Email = new Regex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        private static readonly Regex _CredentialPath = new Regex(@"(?:[A-Za-z]:[\\/]|~[\\/]|/)[^\s""']*(?:\.claude|\.codex|credentials|auth\.json|tokens?\.json|\.ssh)[^\s""']*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

        /// <summary>Return an allowably bounded diagnostic label, never a credential or account-email excerpt.</summary>
        public static string? Label(string? value, int maximumLength = 1024)
        {
            if (maximumLength < 16 || maximumLength > 4096) throw new ArgumentOutOfRangeException(nameof(maximumLength));
            if (value == null) return null;
            string sanitized = _Email.Replace(_CredentialPath.Replace(SecretRedactor.Redact(value), "[REDACTED PATH]"), "[REDACTED EMAIL]");
            return sanitized.Length <= maximumLength ? sanitized : sanitized.Substring(0, maximumLength - 1) + "…";
        }
    }
}
