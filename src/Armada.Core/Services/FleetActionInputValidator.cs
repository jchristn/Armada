namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;

    /// <summary>Validates declared names, defaults and runtime input values before a run is written.</summary>
    public static class FleetActionInputValidator
    {
        private static readonly Regex Name = new(@"^[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        // Package identifiers are substituted into shell commands, so the allowed characters exclude every shell metacharacter.
        private static readonly Regex Package = new(@"^[A-Za-z0-9@][A-Za-z0-9@/._+:-]{0,199}\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
        private static readonly Regex Semver = new(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

        /// <summary>
        /// Reject invalid names, types, duplicate definitions and defaults, and lists over
        /// <see cref="CollectionLimits.MaxConfigItems"/> (Inputs and each input's AllowedValues).
        /// </summary>
        /// <exception cref="ArgumentException">Thrown for an invalid definition.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when Inputs or an input's AllowedValues is over the limit.</exception>
        public static void ValidateDefinitions(IEnumerable<FleetActionInput>? definitions)
        {
            List<FleetActionInput> list = (definitions ?? Enumerable.Empty<FleetActionInput>()).ToList();
            CollectionLimits.RequireConfigItems(list, "Inputs");
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (FleetActionInput input in list)
            {
                if (input == null || !Name.IsMatch(input.Name ?? "") || !names.Add(input.Name!)) throw new ArgumentException("Input names must be unique identifiers.");
                CollectionLimits.RequireConfigItems(input.AllowedValues, "Inputs[" + input.Name + "].AllowedValues");
                if (input.Type != "string" && input.Type != "semver" && input.Type != "enum" && input.Type != "package") throw new ArgumentException("Input " + input.Name + " has an unsupported type.");
                if (input.Type == "enum" && (input.AllowedValues == null || input.AllowedValues.Count == 0)) throw new ArgumentException("Input " + input.Name + " requires allowed values.");
                if (input.Default != null) ValidateValue(input, input.Default);
            }
        }

        /// <summary>
        /// Apply defaults and validate all supplied values, rejecting unknown inputs and more than
        /// <see cref="CollectionLimits.MaxConfigItems"/> supplied values.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown for an invalid or unknown value.</exception>
        /// <exception cref="CollectionLimitExceededException">Thrown when a list or the supplied values are over the limit.</exception>
        public static Dictionary<string, string> Resolve(IEnumerable<FleetActionInput>? definitions, Dictionary<string, string>? values)
        {
            List<FleetActionInput> inputs = (definitions ?? Enumerable.Empty<FleetActionInput>()).ToList();
            ValidateDefinitions(inputs);
            CollectionLimits.RequireConfigEntries(values, "InputValues");
            Dictionary<string, string> supplied = new(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values ?? new Dictionary<string, string>())
            {
                if (!inputs.Any(i => String.Equals(i.Name, pair.Key, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Unknown input: " + pair.Key);
                if (!supplied.TryAdd(pair.Key, pair.Value)) throw new ArgumentException("Duplicate input: " + pair.Key);
            }
            Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
            foreach (FleetActionInput input in inputs)
            {
                string? value = supplied.TryGetValue(input.Name, out string? explicitValue) ? explicitValue : input.Default;
                if (String.IsNullOrEmpty(value))
                {
                    if (input.Required) throw new ArgumentException("Required input is missing: " + input.Name);
                    result[input.Name] = "";
                    continue;
                }
                ValidateValue(input, value!);
                result[input.Name] = value!;
            }
            return result;
        }

        private static void ValidateValue(FleetActionInput input, string value)
        {
            if (input.Type == "semver" && !Semver.IsMatch(value)) throw new ArgumentException("Input " + input.Name + " must be a semantic version (for example 1.2.3).");
            if (input.Type == "package" && !Package.IsMatch(value)) throw new ArgumentException("Input " + input.Name + " must be a package identifier (letters, digits, and @ / . _ + : -; for example Newtonsoft.Json or @scope/name).");
            if (input.Type == "enum" && !input.AllowedValues.Contains(value, StringComparer.Ordinal)) throw new ArgumentException("Input " + input.Name + " must be one of: " + String.Join(", ", input.AllowedValues));
        }
    }
}
