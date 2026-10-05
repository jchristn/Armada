namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Shared wording and option lists for the BUILD screens, mirroring the dashboard's helpers (pipeline labels with
    /// their stage chain, the "(Copy)" duplicate names of <c>lib/duplicates.ts</c>, line lists, and short ids).
    /// </summary>
    public static class BuildText
    {
        #region Public-Methods

        /// <summary>
        /// A pipeline label with its stage chain, for example <c>Default (Worker -&gt; Judge)</c>.
        /// </summary>
        /// <param name="pipeline">Pipeline.</param>
        /// <returns>Label.</returns>
        public static string PipelineLabel(Pipeline pipeline)
        {
            if (pipeline == null) return "";
            List<PipelineStage> stages = pipeline.Stages ?? new List<PipelineStage>();
            return pipeline.Name + " (" + String.Join(" -> ", stages.OrderBy(s => s.Order).Select(s => s.PersonaName)) + ")";
        }

        /// <summary>
        /// Default pipeline options with the dashboard's "None (WorkerOnly)" first.
        /// </summary>
        /// <param name="pipelines">Pipelines.</param>
        /// <param name="loc">Localizer.</param>
        /// <returns>Options.</returns>
        public static List<SelectOption<string>> PipelineOptions(IEnumerable<Pipeline> pipelines, LocalizationService loc)
        {
            List<SelectOption<string>> options = new List<SelectOption<string>>();
            options.Add(new SelectOption<string>("", loc.T("None (WorkerOnly)")));
            foreach (Pipeline p in pipelines ?? Enumerable.Empty<Pipeline>()) options.Add(new SelectOption<string>(p.Id, PipelineLabel(p)));
            return options;
        }

        /// <summary>
        /// The dashboard's duplicate display name: <c>Name (Copy)</c>, or <c>Copy</c> for an empty name.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>Duplicate name.</returns>
        public static string DuplicateName(string? name)
        {
            string trimmed = (name ?? "").Trim();
            return trimmed.Length > 0 ? trimmed + " (Copy)" : "Copy";
        }

        /// <summary>
        /// Split text into trimmed, non-empty lines (the dashboard's one-per-line list fields).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Lines.</returns>
        public static List<string> Lines(string? text)
        {
            return (text ?? "").Split('\n').Select(s => s.Trim().TrimEnd('\r').Trim()).Where(s => s.Length > 0).ToList();
        }

        /// <summary>
        /// The first eight characters of an id (the dashboard's short ids).
        /// </summary>
        /// <param name="id">Id.</param>
        /// <returns>Short id.</returns>
        public static string Short(string? id)
        {
            if (String.IsNullOrEmpty(id)) return "";
            return id.Length > 8 ? id.Substring(0, 8) : id;
        }

        /// <summary>
        /// Yes or No (translated), or "-" for null.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="loc">Localizer.</param>
        /// <returns>Text.</returns>
        public static string YesNo(bool? value, LocalizationService loc)
        {
            if (!value.HasValue) return "-";
            return loc.T(value.Value ? "Yes" : "No");
        }

        #endregion
    }
}
