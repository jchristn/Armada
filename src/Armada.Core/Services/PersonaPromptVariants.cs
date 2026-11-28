namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text.Json;
    using Armada.Core.Enums;

    /// <summary>
    /// Built-in Lean and Compact persona prompt variants (docs/MODEL_HETEROGENEITY.md, section 3.2 and Phase 4) for the
    /// Worker, Test Engineer, Judge and Architect. Lean puts one demand on each line and drops emphasis wording and
    /// boilerplate. Compact is a shorter tier for readers with a small context. Like every persona template, a variant
    /// carries no output contract: <see cref="StageOutputContract"/> appends the same contract to every variant. The
    /// templates live in <c>Prompts/persona-variants.json</c> and are seeded into the prompt template store as
    /// <c>persona.&lt;name&gt;.lean</c> and <c>persona.&lt;name&gt;.compact</c>; a persona without its own variant
    /// renders its Current template.
    /// </summary>
    public static class PersonaPromptVariants
    {
        #region Public-Members

        /// <summary>
        /// Persona template names (without the variant suffix) that ship Lean and Compact variants.
        /// </summary>
        public static IReadOnlyList<string> PersonaTemplateNames { get; } = new List<string>
        {
            "persona.worker",
            "persona.test_engineer",
            "persona.judge",
            "persona.architect"
        };

        #endregion

        #region Private-Members

        private static readonly Dictionary<string, string> _Current = Load<Dictionary<string, string>>("persona-variants.json");

        // Earlier built-in contents, newest first, kept so untouched stored copies are upgraded and edited ones are left alone.
        private static readonly Dictionary<string, List<string>> _Previous = Load<Dictionary<string, List<string>>>("persona-variants-previous.json");

        #endregion

        #region Public-Methods

        /// <summary>
        /// The built-in content of a persona's variant template, or null when the persona ships no such variant (and
        /// always for <see cref="PromptVariantEnum.Current"/>, whose content is the persona's own template).
        /// </summary>
        /// <param name="personaTemplateName">Persona template name without suffix, for example <c>persona.worker</c>.</param>
        /// <param name="variant">Variant.</param>
        /// <returns>Template content, or null.</returns>
        public static string? GetContent(string? personaTemplateName, PromptVariantEnum variant)
        {
            string? key = Key(personaTemplateName, variant);
            if (key == null) return null;
            return _Current.TryGetValue(key, out string? content) ? content : null;
        }

        /// <summary>
        /// Earlier built-in contents of a persona's variant template that an untouched stored copy may still hold, newest
        /// first. Empty when the variant never changed.
        /// </summary>
        /// <param name="personaTemplateName">Persona template name without suffix.</param>
        /// <param name="variant">Variant.</param>
        /// <returns>Previous contents.</returns>
        public static IReadOnlyList<string> GetPreviousContents(string? personaTemplateName, PromptVariantEnum variant)
        {
            string? key = Key(personaTemplateName, variant);
            if (key == null) return new List<string>();
            return _Previous.TryGetValue(key, out List<string>? previous) ? previous : new List<string>();
        }

        #endregion

        #region Private-Methods

        private static string? Key(string? personaTemplateName, PromptVariantEnum variant)
        {
            if (String.IsNullOrEmpty(personaTemplateName)) return null;
            if (variant != PromptVariantEnum.Lean && variant != PromptVariantEnum.Compact) return null;
            return personaTemplateName + "." + variant.ToString().ToLowerInvariant();
        }

        private static T Load<T>(string resource) where T : class
        {
            Assembly assembly = typeof(PersonaPromptVariants).Assembly;
            using Stream stream = assembly.GetManifestResourceStream("Armada.Core.Prompts." + resource)
                ?? throw new InvalidOperationException("Embedded prompt variants are missing: " + resource);
            return JsonSerializer.Deserialize<T>(stream)
                ?? throw new InvalidOperationException("Embedded prompt variants are invalid: " + resource);
        }

        #endregion
    }
}
