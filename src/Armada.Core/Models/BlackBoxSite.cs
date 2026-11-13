namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Site section: where a failure happened, down to the Armada source line.
    /// </summary>
    public class BlackBoxSite
    {
        /// <summary>
        /// Phase (derived from Step).
        /// </summary>
        public BlackBoxPhaseEnum Phase { get; set; } = BlackBoxPhaseEnum.Unknown;

        /// <summary>
        /// Step the capture site reported.
        /// </summary>
        public BlackBoxStepEnum Step { get; set; } = BlackBoxStepEnum.Unknown;

        /// <summary>
        /// Which host the failure happened on.
        /// </summary>
        public BlackBoxHostKindEnum HostKind { get; set; } = BlackBoxHostKindEnum.Admiral;

        /// <summary>
        /// Host label: Admiral, or Harbor:{harborId}.
        /// </summary>
        public string? Host { get; set; } = null;

        /// <summary>
        /// Component (class) that captured the failure.
        /// </summary>
        public string? Component { get; set; } = null;

        /// <summary>
        /// Method that captured the failure ([CallerMemberName]).
        /// </summary>
        public string? Method { get; set; } = null;

        /// <summary>
        /// Repository-relative source file ([CallerFilePath], trimmed).
        /// </summary>
        public string? SourceFile { get; set; } = null;

        /// <summary>
        /// Source line ([CallerLineNumber]).
        /// </summary>
        public int? SourceLine { get; set; } = null;

        /// <summary>
        /// Armada build version.
        /// </summary>
        public string? ArmadaVersion { get; set; } = null;

        /// <summary>
        /// Armada build commit, or null when unknown.
        /// </summary>
        public string? ArmadaCommit { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxSite()
        {
        }
    }
}
