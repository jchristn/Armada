namespace Armada.Core.Services
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The JSON Schema of each submit tool's arguments, generated from the submission models (their
    /// <see cref="JsonPropertyNameAttribute"/>, <see cref="DescriptionAttribute"/>, <see cref="RequiredAttribute"/> and
    /// <see cref="AllowedValuesAttribute"/>) so the tool schema, the validator and the stored record cannot drift apart. The
    /// plan schema is generated from <see cref="ArchitectPlan"/> with the field meanings of the prompt's field table.
    /// </summary>
    public static class StageSubmissionSchema
    {
        #region Private-Members

        private static readonly HashSet<string> _RequiredPlanFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "contractVersion", "missions", "missions[].title", "missions[].description"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The input schema of a submit tool, as nested dictionaries and lists ready to serialize.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <returns>The schema.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an undefined kind.</exception>
        public static Dictionary<string, object> For(StageSubmissionKindEnum kind)
        {
            switch (kind)
            {
                case StageSubmissionKindEnum.Plan:
                    return ObjectSchema(typeof(ArchitectPlan), String.Empty, true);
                case StageSubmissionKindEnum.Verdict:
                    return ObjectSchema(typeof(StageVerdictSubmission), String.Empty, false);
                case StageSubmissionKindEnum.Result:
                    return ObjectSchema(typeof(StageResultSubmission), String.Empty, false);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, object> ObjectSchema(Type type, string prefix, bool plan)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>(StringComparer.Ordinal);
            List<string> required = new List<string>();
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                JsonPropertyNameAttribute? jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>();
                if (jsonName == null) continue;
                string path = prefix + jsonName.Name;
                if (plan && StageOutputContract.PlanFieldsNotPrompted.Contains(path)) continue;

                Dictionary<string, object> schema = PropertySchema(property.PropertyType, path, plan);
                string? description = plan ? StageOutputContract.PlanFieldMeaning(path) : property.GetCustomAttribute<DescriptionAttribute>()?.Description;
                if (!String.IsNullOrEmpty(description)) schema["description"] = description!;
                AllowedValuesAttribute? allowed = property.GetCustomAttribute<AllowedValuesAttribute>();
                if (allowed != null) schema["enum"] = allowed.Values.Select(v => v?.ToString() ?? String.Empty).ToList();
                properties[jsonName.Name] = schema;

                bool isRequired = plan ? _RequiredPlanFields.Contains(path) : property.GetCustomAttribute<RequiredAttribute>() != null;
                if (isRequired) required.Add(jsonName.Name);
            }

            Dictionary<string, object> result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["type"] = "object",
                ["properties"] = properties
            };
            if (required.Count > 0) result["required"] = required;
            return result;
        }

        private static Dictionary<string, object> PropertySchema(Type type, string path, bool plan)
        {
            Type core = Nullable.GetUnderlyingType(type) ?? type;
            bool nullable = Nullable.GetUnderlyingType(type) != null;
            if (core == typeof(string)) return Typed("string", false);
            if (core == typeof(int) || core == typeof(long)) return Typed("integer", nullable);
            if (core == typeof(bool)) return Typed("boolean", nullable);
            if (core.IsEnum) return new Dictionary<string, object> { ["type"] = "string", ["enum"] = Enum.GetNames(core).ToList() };

            if (typeof(IEnumerable).IsAssignableFrom(core) && core.IsGenericType)
            {
                Type element = core.GetGenericArguments()[0];
                return new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = PropertySchema(element, path + "[]", plan)
                };
            }

            return ObjectSchema(core, path.EndsWith("[]", StringComparison.Ordinal) ? path + "." : path + ".", plan);
        }

        private static Dictionary<string, object> Typed(string name, bool nullable)
        {
            if (!nullable) return new Dictionary<string, object> { ["type"] = name };
            return new Dictionary<string, object> { ["type"] = new List<string> { name, "null" } };
        }

        #endregion
    }
}
