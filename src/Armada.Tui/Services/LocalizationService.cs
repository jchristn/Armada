namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;

    /// <summary>
    /// The TUI's i18n runtime over the dashboard's shared catalog (<c>/dashboard/i18n/armada.json</c>): exact phrases,
    /// section labels, single-word terms, the dashboard's dynamic patterns for counts and relative times, ICU plural
    /// blocks with locale plural rules, <c>{{name}}</c> interpolation, and locale formatting. English is the source
    /// locale and the fallback. Reads are thread-safe; <see cref="SetLocale"/> and <see cref="SetCatalog"/> should be
    /// called on the UI loop thread.
    /// </summary>
    public class LocalizationService : ITextLocalizer
    {
        #region Public-Members

        /// <inheritdoc />
        public string Locale
        {
            get { return _Locale; }
        }

        /// <summary>
        /// Culture used for number and date formatting.
        /// </summary>
        public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

        /// <summary>
        /// Loaded catalog, or null before <see cref="LoadAsync"/> succeeds (English only).
        /// </summary>
        public I18nCatalog? Catalog
        {
            get { return _Catalog; }
        }

        /// <summary>
        /// Supported locales (the catalog's list, or the dashboard's built-in list). Never null.
        /// </summary>
        public IReadOnlyList<I18nLocaleMeta> SupportedLocales
        {
            get { return _Catalog != null && _Catalog.SupportedLocales.Count > 0 ? _Catalog.SupportedLocales : DefaultLocales; }
        }

        /// <summary>
        /// Raised after the locale or catalog changes.
        /// </summary>
        public event EventHandler? Changed;

        /// <summary>
        /// The dashboard's built-in locale list, used until the catalog loads.
        /// </summary>
        public static IReadOnlyList<I18nLocaleMeta> DefaultLocales { get; } = BuildDefaultLocales();

        #endregion

        #region Private-Members

        private static readonly Regex _IcuPluralStart = new Regex(@"\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*,\s*plural\s*,", RegexOptions.Compiled);
        private static readonly Regex _CountPattern = new Regex(@"^(\d+) (selected|unread|records|idle|working|stalled|failed|lines|chars)$", RegexOptions.Compiled);
        private static readonly Regex _AgoPattern = new Regex(@"^(\d+)(s|m|h|d) ago$", RegexOptions.Compiled);
        private volatile I18nCatalog? _Catalog = null;
        private volatile string _Locale = "en";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate in English with no catalog.
        /// </summary>
        public LocalizationService()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Fetch the catalog from the server. Failures leave the service in English and return false.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a catalog was loaded.</returns>
        public async Task<bool> LoadAsync(ArmadaClient client, CancellationToken token = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            try
            {
                I18nCatalog? catalog = await client.GetI18nCatalogAsync(token).ConfigureAwait(false);
                if (catalog == null) return false;
                _Catalog = catalog;
                return true;
            }
            catch (ArmadaApiException)
            {
                return false;
            }
        }

        /// <summary>
        /// Replace the catalog (tests, offline use) and raise <see cref="Changed"/>.
        /// </summary>
        /// <param name="catalog">Catalog, or null for English only.</param>
        public void SetCatalog(I18nCatalog? catalog)
        {
            _Catalog = catalog;
            SetLocale(_Locale);
        }

        /// <summary>
        /// Select a locale (aliases such as <c>zh-CN</c> and <c>ja-JP</c> are normalized; unknown codes fall back to the
        /// catalog default) and raise <see cref="Changed"/>.
        /// </summary>
        /// <param name="locale">Locale code or alias.</param>
        /// <returns>The normalized locale code.</returns>
        public string SetLocale(string? locale)
        {
            _Locale = Normalize(locale);
            Culture = ResolveCulture(_Locale);
            EventHandler? handler = Changed;
            if (handler != null) handler(this, EventArgs.Empty);
            return _Locale;
        }

        /// <summary>
        /// Normalize a locale code or alias to a supported code (the dashboard's <c>normalizeLocale</c>).
        /// </summary>
        /// <param name="locale">Code or alias.</param>
        /// <returns>Supported code, or the default locale.</returns>
        public string Normalize(string? locale)
        {
            string source = (locale ?? "").Trim();
            string fallback = _Catalog != null && !String.IsNullOrEmpty(_Catalog.DefaultLocale) ? _Catalog.DefaultLocale : "en";
            if (source.Length == 0) return fallback;
            foreach (I18nLocaleMeta meta in SupportedLocales)
            {
                if (String.Equals(meta.Code, source, StringComparison.OrdinalIgnoreCase)) return meta.Code;
                if (meta.Aliases != null && meta.Aliases.Any(a => String.Equals(a, source, StringComparison.OrdinalIgnoreCase))) return meta.Code;
            }

            string lang = source.Split('-', '_')[0];
            foreach (I18nLocaleMeta meta in SupportedLocales)
            {
                if (String.Equals(meta.Code.Split('-')[0], lang, StringComparison.OrdinalIgnoreCase)) return meta.Code;
            }

            return fallback;
        }

        /// <inheritdoc />
        public string T(string text)
        {
            return Translate(text);
        }

        /// <inheritdoc />
        public string T(string text, IDictionary<string, object?> args)
        {
            string translated = Translate(text);
            string plural = FormatIcuPlurals(translated, args);
            return Interpolate(plural, args);
        }

        /// <inheritdoc />
        public string FormatNumber(long value)
        {
            return value.ToString("N0", Culture);
        }

        /// <inheritdoc />
        public string FormatDateTime(DateTime utc)
        {
            DateTime local = (utc.Kind == DateTimeKind.Local ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToLocalTime();
            return local.ToString("g", Culture);
        }

        /// <inheritdoc />
        public string FormatRelative(DateTime utc, DateTime nowUtc)
        {
            TimeSpan span = nowUtc - utc;
            if (span.TotalSeconds < 5) return Translate("just now");
            if (span.TotalSeconds < 60) return Translate(((int)span.TotalSeconds) + "s ago");
            if (span.TotalMinutes < 60) return Translate(((int)span.TotalMinutes) + "m ago");
            if (span.TotalHours < 24) return Translate(((int)span.TotalHours) + "h ago");
            return Translate(((int)span.TotalDays) + "d ago");
        }

        /// <summary>
        /// Format ICU plural blocks: <c>#</c> becomes the locale-formatted count, <c>=N</c> exact branches win, and
        /// malformed messages are returned unchanged (the dashboard's <c>formatIcuPlurals</c>).
        /// </summary>
        /// <param name="text">Message.</param>
        /// <param name="args">Values; the plural variable must be numeric.</param>
        /// <returns>Formatted text.</returns>
        public string FormatIcuPlurals(string text, IDictionary<string, object?>? args)
        {
            if (String.IsNullOrEmpty(text) || !text.Contains("plural")) return text ?? "";
            StringBuilder result = new StringBuilder();
            int cursor = 0;
            Match match = _IcuPluralStart.Match(text, 0);
            while (match.Success)
            {
                Dictionary<string, string>? branches = ParsePluralBranches(text, match.Index + match.Length, out int end);
                if (branches == null) break;
                long count = 0;
                if (args != null && args.TryGetValue(match.Groups[1].Value, out object? raw) && raw != null)
                {
                    Int64.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out count);
                }

                string category = PluralRules.Select(_Locale, count);
                string branch = branches.TryGetValue("=" + count, out string? exact) ? exact
                    : branches.TryGetValue(category, out string? cat) ? cat
                    : branches.TryGetValue("other", out string? other) ? other : "";
                result.Append(text.Substring(cursor, match.Index - cursor));
                result.Append(FormatIcuPlurals(branch, args).Replace("#", FormatNumber(count)));
                cursor = end;
                match = _IcuPluralStart.Match(text, end);
            }

            result.Append(text.Substring(cursor));
            return result.ToString();
        }

        #endregion

        #region Private-Methods

        private string Translate(string text)
        {
            if (String.IsNullOrEmpty(text) || _Locale == "en") return text ?? "";
            I18nCatalog? catalog = _Catalog;
            if (catalog == null || !catalog.Locales.TryGetValue(_Locale, out I18nLocalePack? pack) || pack == null) return text;
            string trimmed = text.Trim();
            if (trimmed.Length == 0) return text;

            if (pack.Phrases != null && pack.Phrases.TryGetValue(trimmed, out string? phrase)) return Preserve(text, phrase);
            if (pack.Sections != null && pack.Sections.TryGetValue(trimmed, out string? section)) return Preserve(text, section);

            string? dynamic = TranslateDynamic(trimmed, pack);
            if (dynamic != null) return Preserve(text, dynamic);

            if (!trimmed.Contains(' ') && !trimmed.Contains('\n') && pack.Terms != null && pack.Terms.TryGetValue(trimmed, out string? term))
                return Preserve(text, term);

            return text;
        }

        private string? TranslateDynamic(string text, I18nLocalePack pack)
        {
            Match count = _CountPattern.Match(text);
            if (count.Success)
            {
                string word = Lookup(pack, count.Groups[2].Value);
                return FormatNumber(Int64.Parse(count.Groups[1].Value, CultureInfo.InvariantCulture)) + " " + word;
            }

            Match ago = _AgoPattern.Match(text);
            if (ago.Success)
            {
                string phrase = Lookup(pack, text);
                if (!String.Equals(phrase, text, StringComparison.Ordinal)) return phrase;
            }

            return null;
        }

        private static string Lookup(I18nLocalePack pack, string key)
        {
            if (pack.Phrases != null && pack.Phrases.TryGetValue(key, out string? phrase)) return phrase;
            if (pack.Terms != null && pack.Terms.TryGetValue(key, out string? term)) return term;
            return key;
        }

        private static string Preserve(string original, string translated)
        {
            int lead = original.Length - original.TrimStart().Length;
            int trail = original.Length - original.TrimEnd().Length;
            return original.Substring(0, lead) + translated.Trim() + original.Substring(original.Length - trail);
        }

        private static string Interpolate(string template, IDictionary<string, object?>? args)
        {
            if (args == null || args.Count == 0 || String.IsNullOrEmpty(template)) return template ?? "";
            string result = template;
            foreach (KeyValuePair<string, object?> kvp in args)
            {
                result = result.Replace("{{" + kvp.Key + "}}", kvp.Value == null ? "" : Convert.ToString(kvp.Value, CultureInfo.InvariantCulture));
            }

            return result;
        }

        private static Dictionary<string, string>? ParsePluralBranches(string text, int start, out int end)
        {
            Dictionary<string, string> branches = new Dictionary<string, string>(StringComparer.Ordinal);
            int i = start;
            end = start;
            while (i < text.Length)
            {
                while (i < text.Length && Char.IsWhiteSpace(text[i])) i++;
                if (i < text.Length && text[i] == '}')
                {
                    end = i + 1;
                    return branches;
                }

                Match selector = Regex.Match(text.Substring(i), @"^(=\d+|[A-Za-z]+)\s*\{");
                if (!selector.Success) return null;
                string key = selector.Groups[1].Value;
                i += selector.Length;
                int depth = 1;
                int bodyStart = i;
                while (i < text.Length && depth > 0)
                {
                    if (text[i] == '{') depth++;
                    else if (text[i] == '}') depth--;
                    if (depth > 0) i++;
                }

                if (depth != 0) return null;
                branches[key] = text.Substring(bodyStart, i - bodyStart);
                i++;
            }

            return null;
        }

        private static CultureInfo ResolveCulture(string locale)
        {
            string name = locale switch
            {
                "en" => "en-US",
                "zh-Hans" => "zh-CN",
                "zh-Hant" => "zh-TW",
                "yue-Hant" => "zh-HK",
                _ => locale
            };
            try
            {
                return CultureInfo.GetCultureInfo(name);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }

        private static IReadOnlyList<I18nLocaleMeta> BuildDefaultLocales()
        {
            List<I18nLocaleMeta> list = new List<I18nLocaleMeta>();
            list.Add(Meta("en", "English", "English", "en-US", "en-GB", "en-CA", "en-AU"));
            list.Add(Meta("es", "Spanish", "Espa\u00f1ol", "es-ES", "es-MX", "es-419"));
            list.Add(Meta("zh-Hans", "Mandarin (Simplified)", "\u7b80\u4f53\u4e2d\u6587", "zh", "zh-CN", "zh-SG", "cmn-Hans"));
            list.Add(Meta("zh-Hant", "Mandarin (Traditional)", "\u7e41\u9ad4\u4e2d\u6587", "zh-TW", "cmn-Hant"));
            list.Add(Meta("yue-Hant", "Cantonese", "\u7cb5\u8a9e", "zh-HK", "zh-MO", "yue", "yue-HK"));
            list.Add(Meta("ja", "Japanese", "\u65e5\u672c\u8a9e", "ja-JP"));
            list.Add(Meta("de", "German", "Deutsch", "de-DE"));
            list.Add(Meta("fr", "French", "Fran\u00e7ais", "fr-FR", "fr-CA"));
            list.Add(Meta("it", "Italian", "Italiano", "it-IT"));
            return list.AsReadOnly();
        }

        private static I18nLocaleMeta Meta(string code, string label, string native, params string[] aliases)
        {
            I18nLocaleMeta meta = new I18nLocaleMeta();
            meta.Code = code;
            meta.Label = label;
            meta.NativeLabel = native;
            meta.Dir = "ltr";
            meta.Aliases = new List<string>(aliases);
            return meta;
        }

        #endregion
    }
}
