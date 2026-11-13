namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Services;

    /// <summary>
    /// Alert thresholds: defaults plus per-persona overrides (body of GET and PUT /api/v1/model-alerts/thresholds).
    /// </summary>
    public class ModelAlertThresholds
    {
        #region Public-Members

        /// <summary>
        /// Most per-persona overrides (100).
        /// </summary>
        public const int MaxOverrides = 100;

        /// <summary>
        /// Defaults for every persona. A null value uses the built-in default.
        /// </summary>
        public ModelAlertThreshold Defaults
        {
            get => _Defaults;
            set => _Defaults = value ?? new ModelAlertThreshold();
        }

        /// <summary>
        /// Per-persona overrides; a null value in an override keeps the default.
        /// </summary>
        [CollectionBound(MaxOverrides)]
        public List<ModelAlertThreshold> Overrides
        {
            get => _Overrides;
            set => _Overrides = value ?? new List<ModelAlertThreshold>();
        }

        #endregion

        #region Private-Members

        private ModelAlertThreshold _Defaults = new ModelAlertThreshold();
        private List<ModelAlertThreshold> _Overrides = new List<ModelAlertThreshold>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelAlertThresholds()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Effective thresholds of a persona: the override's values, then the configured defaults, then the built-in
        /// defaults. Every value of the result is set.
        /// </summary>
        /// <param name="persona">Persona name (case-insensitive).</param>
        /// <returns>The effective thresholds.</returns>
        public ModelAlertThreshold EffectiveFor(string? persona)
        {
            ModelAlertThreshold builtIn = ModelAlertThreshold.BuiltInDefaults();
            ModelAlertThreshold? over = String.IsNullOrWhiteSpace(persona) ? null
                : Overrides.FirstOrDefault(o => PersonaCatalog.Matches(o.Persona, persona));
            return new ModelAlertThreshold
            {
                Persona = persona,
                FirstPassMinRate = over?.FirstPassMinRate ?? Defaults.FirstPassMinRate ?? builtIn.FirstPassMinRate,
                FirstPassMinMissions = over?.FirstPassMinMissions ?? Defaults.FirstPassMinMissions ?? builtIn.FirstPassMinMissions,
                EscalationsInARow = over?.EscalationsInARow ?? Defaults.EscalationsInARow ?? builtIn.EscalationsInARow,
                ProtocolRepairMaxRate = over?.ProtocolRepairMaxRate ?? Defaults.ProtocolRepairMaxRate ?? builtIn.ProtocolRepairMaxRate,
                ProtocolRepairMinAttempts = over?.ProtocolRepairMinAttempts ?? Defaults.ProtocolRepairMinAttempts ?? builtIn.ProtocolRepairMinAttempts,
                CostMedianMultiplier = over?.CostMedianMultiplier ?? Defaults.CostMedianMultiplier ?? builtIn.CostMedianMultiplier,
                CostMinAccepted = over?.CostMinAccepted ?? Defaults.CostMinAccepted ?? builtIn.CostMinAccepted
            };
        }

        /// <summary>
        /// Check every value and the override personas.
        /// </summary>
        /// <returns>The first problem, or null when valid.</returns>
        public string? Validate()
        {
            string? error = ValidateOne(Defaults, "Defaults");
            if (error != null) return error;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Overrides.Count; i++)
            {
                ModelAlertThreshold? item = Overrides[i];
                string label = "Overrides[" + i + "]";
                if (item == null) return label + " is null.";
                if (String.IsNullOrWhiteSpace(item.Persona)) return label + ".Persona is required.";
                if (item.Persona.Trim().Length > 256) return label + ".Persona must be at most 256 characters.";
                if (!seen.Add(PersonaCatalog.NormalizeName(item.Persona))) return "Persona " + item.Persona.Trim() + " has more than one override.";
                error = ValidateOne(item, label);
                if (error != null) return error;
            }
            if (Overrides.Count > MaxOverrides) return "At most " + MaxOverrides + " overrides.";
            return null;
        }

        #endregion

        #region Private-Methods

        private static string? ValidateOne(ModelAlertThreshold t, string label)
        {
            if (!InRange(t.FirstPassMinRate, 0, 1)) return label + ".FirstPassMinRate must be 0 to 1.";
            if (!InRange(t.ProtocolRepairMaxRate, 0, 1)) return label + ".ProtocolRepairMaxRate must be 0 to 1.";
            if (!InRange(t.CostMedianMultiplier, 1, 100)) return label + ".CostMedianMultiplier must be 1 to 100.";
            if (!InRange(t.FirstPassMinMissions, 1, 1000)) return label + ".FirstPassMinMissions must be 1 to 1000.";
            if (!InRange(t.EscalationsInARow, 1, 1000)) return label + ".EscalationsInARow must be 1 to 1000.";
            if (!InRange(t.ProtocolRepairMinAttempts, 1, 1000)) return label + ".ProtocolRepairMinAttempts must be 1 to 1000.";
            if (!InRange(t.CostMinAccepted, 1, 1000)) return label + ".CostMinAccepted must be 1 to 1000.";
            return null;
        }

        private static bool InRange(double? value, double min, double max)
        {
            return !value.HasValue || (!Double.IsNaN(value.Value) && value.Value >= min && value.Value <= max);
        }

        private static bool InRange(int? value, int min, int max)
        {
            return !value.HasValue || (value.Value >= min && value.Value <= max);
        }

        #endregion
    }
}
