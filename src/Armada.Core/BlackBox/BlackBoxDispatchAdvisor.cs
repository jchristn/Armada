namespace Armada.Core.BlackBox
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using SyslogLogging;

    /// <summary>
    /// The black box dispatch advisor (prevention): before a mission is created it compares the mission's likely files
    /// with the vessel's active missions, hotspots and reservations (<see cref="MissionOverlapAnalyzer"/>) and lints its
    /// brief (<see cref="MissionBriefLinter"/>). When the vessel has SerializeOverlappingMissions and the mission has no
    /// dependency, the mission is chained after the strongest overlapping active mission. Thread-safe.
    /// </summary>
    public class BlackBoxDispatchAdvisor : IMissionDispatchAdvisor
    {
        #region Public-Members

        /// <summary>
        /// The overlap analyzer.
        /// </summary>
        public MissionOverlapAnalyzer Analyzer => _Analyzer;

        #endregion

        #region Private-Members

        private readonly string _Header = "[BlackBoxDispatchAdvisor] ";
        private readonly MissionOverlapAnalyzer _Analyzer;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="analyzer">Overlap analyzer.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxDispatchAdvisor(MissionOverlapAnalyzer analyzer, LoggingModule logging)
        {
            _Analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<MissionDispatchAdvice?> AdviseAsync(Mission mission, Vessel vessel, CancellationToken token = default)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            VesselOverlapContext context = await _Analyzer.LoadAsync(vessel, null, token).ConfigureAwait(false);
            MissionFileSet likely = MissionOverlapAnalyzer.LikelyFiles(mission.Title, mission.Description, context.Tracked, out _, out _);
            MissionOverlapPreview preview = MissionOverlapAnalyzer.Compare(context, likely, mission.DependsOnMissionId, null);
            BriefLintResult lint = MissionBriefLinter.Lint(mission.Title, mission.Description, mission.DependsOnMissionId, context.Tracked, mission.Mode);

            MissionDispatchAdvice advice = new MissionDispatchAdvice();
            advice.OverlapWarnings = preview.Warnings;
            advice.BriefLintWarnings = lint.Findings;
            if (vessel.SerializeOverlappingMissions && String.IsNullOrWhiteSpace(mission.DependsOnMissionId) && preview.SuggestedDependsOnMissionId != null)
            {
                mission.DependsOnMissionId = preview.SuggestedDependsOnMissionId;
                advice.SerializedAfterMissionId = preview.SuggestedDependsOnMissionId;
                foreach (MissionOverlapWarning w in advice.OverlapWarnings.Where(w => w.OverlappingMissionId == preview.SuggestedDependsOnMissionId)) w.Serialized = true;
                _Logging.Info(_Header + "chained mission \"" + mission.Title + "\" on vessel " + vessel.Id + " after overlapping active mission " + preview.SuggestedDependsOnMissionId);
            }
            if (advice.OverlapWarnings.Count == 0 && advice.BriefLintWarnings.Count == 0 && advice.SerializedAfterMissionId == null) return null;
            return advice;
        }

        #endregion
    }
}
