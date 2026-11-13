namespace Armada.Server.Routes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Turns System.Text.Json binding failures into messages that name the field and the values it accepts.
    /// The raw exception text names .NET types (for example <c>System.Nullable`1[System.Int64]</c>) and is never shown.
    /// </summary>
    public static class JsonValueErrorTranslator
    {
        #region Private-Members

        private static readonly Regex _Conversion = new Regex(@"could not be converted to (?<type>\S+?)\.\s+Path: (?<path>[^|]+?)\s*(\||$)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        private static readonly Regex _Path = new Regex(@"Path: (?<path>[^|]+?)\s*(\||$)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        private static readonly HashSet<string> _Numbers = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Byte", "System.SByte", "System.Int16", "System.UInt16", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64",
            "System.Single", "System.Double", "System.Decimal"
        };

        #endregion

        #region Public-Methods

        /// <summary>Describe a JSON binding failure.</summary>
        /// <param name="error">The exception thrown while reading a request body.</param>
        /// <returns>The field, code and a message that says what the value must be.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="error"/> is null.</exception>
        public static JsonValueError Describe(JsonException error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            string text = error.Message ?? "";
            Match conversion = SafeMatch(_Conversion, text);
            string? field = Field(conversion.Success ? conversion.Groups["path"].Value : error.Path ?? PathFrom(text));
            if (!conversion.Success)
                return new JsonValueError
                {
                    Code = JsonValueError.MalformedCode, Field = field,
                    Message = field == null ? "The request body is not valid JSON." : "The request body is not valid JSON near " + field + "."
                };
            string expectation = Expectation(conversion.Groups["type"].Value);
            return new JsonValueError
            {
                Field = field,
                Message = field == null ? "The request body must be " + expectation + "." : field + " must be " + expectation + "."
            };
        }

        #endregion

        #region Private-Methods

        private static Match SafeMatch(Regex regex, string text)
        {
            try { return regex.Match(text); }
            catch (RegexMatchTimeoutException) { return Match.Empty; }
        }

        private static string? PathFrom(string text)
        {
            Match match = SafeMatch(_Path, text);
            return match.Success ? match.Groups["path"].Value : null;
        }

        private static string? Field(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            string trimmed = path.Trim();
            if (trimmed == "$") return null;
            if (trimmed.StartsWith("$.", StringComparison.Ordinal)) trimmed = trimmed.Substring(2);
            else if (trimmed.StartsWith("$", StringComparison.Ordinal)) trimmed = trimmed.Substring(1);
            return trimmed.Length == 0 ? null : trimmed;
        }

        private static string Expectation(string typeName)
        {
            string name = typeName;
            if (name.StartsWith("System.Nullable`1[", StringComparison.Ordinal) && name.EndsWith("]", StringComparison.Ordinal))
                name = name.Substring("System.Nullable`1[".Length, name.Length - "System.Nullable`1[".Length - 1);
            if (_Numbers.Contains(name)) return "a number";
            if (name == "System.Boolean") return "true or false";
            if (name == "System.String" || name == "System.Char") return "text";
            if (name == "System.DateTime" || name == "System.DateTimeOffset") return "an ISO 8601 date and time";
            if (name == "System.TimeSpan") return "a duration such as 00:05:00";
            if (name == "System.Guid") return "a GUID";
            Type? type = Resolve(name);
            if (type != null && type.IsEnum)
            {
                List<string> names = Enum.GetNames(type).ToList();
                return names.Count == 1 ? names[0] : "one of " + String.Join(", ", names.Take(names.Count - 1)) + " or " + names[names.Count - 1];
            }
            if (type != null && type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type)) return "a JSON array";
            if (name.Contains("List`", StringComparison.Ordinal) || name.EndsWith("[]", StringComparison.Ordinal)) return "a JSON array";
            return "a JSON object";
        }

        private static Type? Resolve(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? type;
                try { type = assembly.GetType(name, false); }
                catch (Exception ex) when (ex is ArgumentException || ex is TypeLoadException || ex is BadImageFormatException) { continue; }
                if (type != null) return type;
            }
            return null;
        }

        #endregion
    }
}
