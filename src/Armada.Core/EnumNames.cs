namespace Armada.Core
{
    using System;

    /// <summary>
    /// Strict enum parsing by member name. <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> also accepts
    /// numeric text ("7") and comma-separated flag lists, and yields undefined values for them; wire fields that carry
    /// enum names must accept only the defined names.
    /// </summary>
    public static class EnumNames
    {
        #region Public-Methods

        /// <summary>
        /// Parse a defined enum member name.
        /// </summary>
        /// <typeparam name="TEnum">Enum type.</typeparam>
        /// <param name="text">Text (surrounding whitespace is ignored).</param>
        /// <param name="ignoreCase">Whether name matching ignores case.</param>
        /// <param name="value">Parsed value, or default when parsing fails.</param>
        /// <returns>True when the text is exactly one defined member name.</returns>
        public static bool TryParse<TEnum>(string? text, bool ignoreCase, out TEnum value) where TEnum : struct, Enum
        {
            value = default;
            if (String.IsNullOrWhiteSpace(text)) return false;
            string trimmed = text!.Trim();
            if (!IsIdentifier(trimmed)) return false;

            if (!Enum.TryParse<TEnum>(trimmed, ignoreCase, out TEnum parsed)) return false;
            if (!Enum.IsDefined(typeof(TEnum), parsed)) return false;
            value = parsed;
            return true;
        }

        /// <summary>
        /// Parse a defined enum member name, or return null.
        /// </summary>
        /// <typeparam name="TEnum">Enum type.</typeparam>
        /// <param name="text">Text.</param>
        /// <param name="ignoreCase">Whether name matching ignores case.</param>
        /// <returns>Parsed value, or null.</returns>
        public static TEnum? ParseOrNull<TEnum>(string? text, bool ignoreCase = false) where TEnum : struct, Enum
        {
            return TryParse<TEnum>(text, ignoreCase, out TEnum value) ? value : (TEnum?)null;
        }

        #endregion

        #region Private-Methods

        private static bool IsIdentifier(string text)
        {
            if (text.Length == 0) return false;
            if (!Char.IsLetter(text[0]) && text[0] != '_') return false;
            for (int i = 1; i < text.Length; i++)
            {
                char c = text[i];
                if (!Char.IsLetterOrDigit(c) && c != '_') return false;
            }

            return true;
        }

        #endregion
    }
}
