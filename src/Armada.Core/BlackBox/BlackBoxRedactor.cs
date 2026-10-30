namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;

    /// <summary>
    /// Removes secrets from black box text before it is stored and again before it is rendered: private key blocks,
    /// Authorization headers, provider tokens (GitHub, OpenAI, Anthropic, AWS, Azure, Google, Slack, JWT),
    /// password=/secret=/token= pairs, URLs with embedded credentials, and any registered secret value (setting values
    /// marked secret, model endpoint keys). Reports rule names and counts in a <see cref="BlackBoxRedaction"/>, never the
    /// values. Thread-safe.
    /// </summary>
    public class BlackBoxRedactor
    {
        #region Public-Members

        /// <summary>
        /// Replacement text.
        /// </summary>
        public const string Marker = "[REDACTED]";

        /// <summary>
        /// Rule name for registered secret values.
        /// </summary>
        public const string KnownSecretRule = "KnownSecretValue";

        /// <summary>
        /// The pattern rules, in the order they run.
        /// </summary>
        public IReadOnlyList<BlackBoxRedactionRule> Rules => _Rules;

        #endregion

        #region Private-Members

        private static readonly RegexOptions _Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
        private static readonly TimeSpan _Timeout = TimeSpan.FromMilliseconds(250);
        private readonly List<BlackBoxRedactionRule> _Rules;
        private readonly ConcurrentDictionary<string, byte> _KnownSecrets = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with the built-in rules.
        /// </summary>
        public BlackBoxRedactor()
        {
            _Rules = new List<BlackBoxRedactionRule>
            {
                Rule("PrivateKeyBlock", @"-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----[\s\S]*?(-----END [A-Z0-9 ]*PRIVATE KEY-----|$)"),
                Rule("AuthorizationHeader", @"(?i)(?<keep>\bauthorization[""']?\s*[:=]\s*[""']?(?:bearer|basic|token|bot)?\s*)[^\s""',;]+"),
                Rule("UrlCredentials", @"(?i)(?<keep>\b[a-z][a-z0-9+.\-]*://)[^\s/:@""']+:[^\s/@""']+(?=@)"),
                Rule("GitHubToken", @"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})"),
                Rule("AnthropicKey", @"\bsk-ant-[A-Za-z0-9_\-]{16,}"),
                Rule("OpenAiKey", @"\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_\-]{20,}"),
                Rule("AwsAccessKeyId", @"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b"),
                Rule("AwsSecretAccessKey", @"(?i)(?<keep>aws_secret_access_key[""']?\s*[:=]\s*[""']?)[A-Za-z0-9/+=]{30,}"),
                Rule("AzureKey", @"(?i)(?<keep>\b(?:AccountKey|SharedAccessKey|SharedAccessSignature|sig)=)[A-Za-z0-9%/+=]{16,}"),
                Rule("GoogleApiKey", @"\bAIza[0-9A-Za-z_\-]{35}"),
                Rule("SlackToken", @"\bxox[abposr]-[A-Za-z0-9\-]{10,}"),
                Rule("JsonWebToken", @"\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}"),
                Rule("SecretPair", @"(?i)(?<keep>\b(?:password|passwd|pwd|secret|client_secret|token|access_token|refresh_token|api[_\-]?key|apikey|access[_\-]?key|secret[_\-]?key)[""']?\s*[:=]\s*)(?:""[^""\r\n]*""|'[^'\r\n]*'|[^\s&,;""']+)")
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register exact secret values to redact wherever they appear (values shorter than 6 characters are ignored).
        /// </summary>
        /// <param name="values">Values.</param>
        public void RegisterSecrets(IEnumerable<string?>? values)
        {
            if (values == null) return;
            foreach (string? value in values)
            {
                if (!String.IsNullOrEmpty(value) && value.Length >= 6) _KnownSecrets[value] = 0;
            }
        }

        /// <summary>
        /// Redact a string. Null stays null.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="report">Report to add rule counts to, or null.</param>
        /// <returns>The redacted text.</returns>
        public string? Redact(string? text, BlackBoxRedaction? report = null)
        {
            if (String.IsNullOrEmpty(text)) return text;
            string result = text;
            foreach (string secret in _KnownSecrets.Keys)
            {
                int count = Count(result, secret);
                if (count > 0)
                {
                    result = result.Replace(secret, Marker, StringComparison.Ordinal);
                    Record(report, KnownSecretRule, count);
                }
            }
            foreach (BlackBoxRedactionRule rule in _Rules)
            {
                int count = 0;
                try
                {
                    result = rule.Pattern.Replace(result, m =>
                    {
                        count++;
                        Group keep = m.Groups["keep"];
                        return keep.Success ? keep.Value + Marker : Marker;
                    });
                }
                catch (RegexMatchTimeoutException)
                {
                    Record(report, rule.Name + "Timeout", 1);
                    continue;
                }
                if (count > 0) Record(report, rule.Name, count);
            }
            return result;
        }

        /// <summary>
        /// Redact UTF-8 text bytes.
        /// </summary>
        /// <param name="data">Bytes.</param>
        /// <param name="report">Report, or null.</param>
        /// <returns>The redacted bytes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when data is null.</exception>
        public byte[] RedactUtf8(byte[] data, BlackBoxRedaction? report = null)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return Encoding.UTF8.GetBytes(Redact(Encoding.UTF8.GetString(data), report) ?? String.Empty);
        }

        /// <summary>
        /// Redact every string in a black box model graph in place (string properties, string lists, string dictionary
        /// values, and nested Armada models and lists of them). The record's own Redaction section is updated.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <exception cref="ArgumentNullException">Thrown when record is null.</exception>
        public void RedactRecord(BlackBoxRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            BlackBoxRedaction report = record.Redaction;
            Walk(record, report, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
            record.Redaction = report;
        }

        #endregion

        #region Private-Methods

        private static BlackBoxRedactionRule Rule(string name, string pattern)
        {
            return new BlackBoxRedactionRule(name, new Regex(pattern, _Options, _Timeout));
        }

        private static int Count(string text, string value)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }

        private static void Record(BlackBoxRedaction? report, string rule, int count)
        {
            if (report == null) return;
            report.RedactedCount += count;
            BlackBoxRedactionRuleCount? existing = report.RulesApplied.FirstOrDefault(r => r.Rule == rule);
            if (existing != null) existing.Count += count;
            else if (report.RulesApplied.Count < BlackBoxLimits.MaxRedactionRules) report.RulesApplied.Add(new BlackBoxRedactionRuleCount { Rule = rule, Count = count });
        }

        private void Walk(object? node, BlackBoxRedaction report, int depth, HashSet<object> seen)
        {
            if (node == null || depth > 12 || !seen.Add(node)) return;
            if (node is BlackBoxRedaction || node is BlackBoxTruncation) return;
            foreach (PropertyInfo prop in node.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0 || !prop.CanRead) continue;
                Type type = prop.PropertyType;
                object? value = prop.GetValue(node);
                if (value == null) continue;
                if (type == typeof(string))
                {
                    if (prop.CanWrite) prop.SetValue(node, Redact((string)value, report));
                }
                else if (value is List<string> strings)
                {
                    for (int i = 0; i < strings.Count; i++) strings[i] = Redact(strings[i], report) ?? String.Empty;
                }
                else if (value is Dictionary<string, string> dict)
                {
                    foreach (string key in dict.Keys.ToList()) dict[key] = Redact(dict[key], report) ?? String.Empty;
                }
                else if (value is IList list && IsModel(ElementType(type)))
                {
                    foreach (object? item in list) Walk(item, report, depth + 1, seen);
                }
                else if (IsModel(type))
                {
                    Walk(value, report, depth + 1, seen);
                }
            }
        }

        private static Type? ElementType(Type type)
        {
            return type.IsGenericType ? type.GetGenericArguments().FirstOrDefault() : null;
        }

        private static bool IsModel(Type? type)
        {
            return type != null && type.IsClass && type != typeof(string) && type.Namespace != null && type.Namespace.StartsWith("Armada.Core", StringComparison.Ordinal);
        }

        #endregion
    }
}
