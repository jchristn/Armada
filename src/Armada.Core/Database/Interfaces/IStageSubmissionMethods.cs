namespace Armada.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// Database operations for stage submissions (plans, verdicts and results submitted through the submit tools).
    /// </summary>
    public interface IStageSubmissionMethods
    {
        /// <summary>
        /// Store a submission.
        /// </summary>
        /// <param name="submission">Submission.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored submission.</returns>
        Task<StageSubmission> CreateAsync(StageSubmission submission, CancellationToken token = default);

        /// <summary>
        /// The latest submission of an attempt, or null when it submitted nothing.
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <param name="attemptId">Attempt id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The submission, or null.</returns>
        Task<StageSubmission?> ReadLatestForAttemptAsync(string missionId, string attemptId, CancellationToken token = default);

        /// <summary>
        /// Every submission of a mission, newest first.
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Submissions.</returns>
        Task<List<StageSubmission>> EnumerateByMissionAsync(string missionId, CancellationToken token = default);
    }
}
