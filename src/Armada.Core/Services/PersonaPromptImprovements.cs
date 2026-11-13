namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text.Json;

    internal static class PersonaPromptImprovements
    {
        internal const string Marker = "## Evidence and execution contract";

        internal static IReadOnlyDictionary<string, string> Additions { get; } = Load();

        internal static IReadOnlyDictionary<string, string> Briefs { get; } = Load("persona-briefs.json");

        internal static IReadOnlyDictionary<string, string> PreviousBriefs { get; } = Load("persona-briefs-v1.json");

        internal static IReadOnlyDictionary<string, string> RecentBriefs { get; } = Load("persona-briefs-v2.json");

        internal static IReadOnlyDictionary<string, string> EvidenceBriefs { get; } = Load("persona-briefs-v3.json");

        internal static IReadOnlyDictionary<string, string> ScopedBriefs { get; } = Load("persona-briefs-v4.json");

        internal static IReadOnlyDictionary<string, string> DecisionBriefs { get; } = Load("persona-briefs-v5.json");

        internal static IReadOnlyDictionary<string, string> ConcurrencyBriefs { get; } = Load("persona-briefs-v6.json");

        internal static string Apply(string name, string content)
        {
            if (!Additions.TryGetValue(name, out string? addition)) return content;
            if (content.Contains(Marker, StringComparison.Ordinal)) return content;
            return content + addition;
        }

        private static Dictionary<string, string> Load(string resource = "persona-improvements.json")
        {
            Assembly assembly = typeof(PersonaPromptImprovements).Assembly;
            using Stream stream = assembly.GetManifestResourceStream("Armada.Core.Prompts." + resource)
                ?? throw new InvalidOperationException("Embedded persona improvements are missing.");
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidOperationException("Embedded persona improvements are invalid.");
        }
    }
}
