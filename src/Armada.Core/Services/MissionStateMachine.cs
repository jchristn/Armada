namespace Armada.Core.Services
{
    using Armada.Core.Enums;

    /// <summary>
    /// Single authoritative definition of the mission lifecycle: which status transitions are legal
    /// and how statuses are classified. Previously the transition table and the terminal/post-work/
    /// active status sets were duplicated across several services and an agent handler, which drifted
    /// out of sync. Centralizing them here removes that drift and gives one place to reason about and
    /// test the state machine.
    /// </summary>
    public static class MissionStateMachine
    {
        /// <summary>
        /// Whether a transition from one status to another is legal. Terminal statuses
        /// (Complete, Failed, Cancelled) permit no further transitions.
        /// </summary>
        /// <param name="current">Current status.</param>
        /// <param name="target">Proposed next status.</param>
        /// <returns>True if the transition is allowed.</returns>
        public static bool IsValidTransition(MissionStatusEnum current, MissionStatusEnum target)
        {
            switch (current)
            {
                case MissionStatusEnum.Pending:
                    return target == MissionStatusEnum.Assigned
                        || target == MissionStatusEnum.Blocked
                        || target == MissionStatusEnum.Cancelled;

                case MissionStatusEnum.Blocked:
                    return target == MissionStatusEnum.Pending
                        || target == MissionStatusEnum.Cancelled;

                case MissionStatusEnum.Assigned:
                    return target == MissionStatusEnum.InProgress
                        || target == MissionStatusEnum.Cancelled;

                case MissionStatusEnum.Verifying:
                    return target == MissionStatusEnum.InProgress || target == MissionStatusEnum.WorkProduced || target == MissionStatusEnum.Complete
                        || target == MissionStatusEnum.Failed || target == MissionStatusEnum.Cancelled;

                case MissionStatusEnum.InProgress:
                    return target == MissionStatusEnum.WorkProduced
                        || target == MissionStatusEnum.Testing
                        || target == MissionStatusEnum.Review
                        || target == MissionStatusEnum.Complete
                        || target == MissionStatusEnum.Failed
                        || target == MissionStatusEnum.Cancelled;

                case MissionStatusEnum.WorkProduced:
                    return target == MissionStatusEnum.PullRequestOpen
                        || target == MissionStatusEnum.Complete
                        || target == MissionStatusEnum.LandingFailed
                        || target == MissionStatusEnum.Cancelled;

                case MissionStatusEnum.PullRequestOpen:
                    return target == MissionStatusEnum.Complete
                        || target == MissionStatusEnum.LandingFailed
                        || target == MissionStatusEnum.Cancelled;

                case MissionStatusEnum.Testing:
                    return target == MissionStatusEnum.Review
                        || target == MissionStatusEnum.InProgress
                        || target == MissionStatusEnum.Complete
                        || target == MissionStatusEnum.Failed;

                case MissionStatusEnum.Review:
                    return target == MissionStatusEnum.Complete
                        || target == MissionStatusEnum.InProgress
                        || target == MissionStatusEnum.Failed;

                case MissionStatusEnum.LandingFailed:
                    // Complete: the work reached the target branch after all (it was landed by another path, or a
                    // later retry published it), proven by commit ancestry or the mission trailer on the remote.
                    return target == MissionStatusEnum.WorkProduced
                        || target == MissionStatusEnum.Complete
                        || target == MissionStatusEnum.Failed
                        || target == MissionStatusEnum.Cancelled;

                default:
                    // Complete, Failed, Cancelled are terminal.
                    return false;
            }
        }

        /// <summary>
        /// Whether Armada itself may move a mission from <paramref name="current"/> to <paramref name="target"/>. This is
        /// <see cref="IsValidTransition"/> (the operator table) plus the edges only Armada's own handlers take: requeueing
        /// an in-flight or produced mission (redispatch, escalation, rework, output repair, no-op retry), restarting a
        /// finished one, verifying after a clean exit, failing or holding produced work for review, failing an approved
        /// stage whose handoff failed, and settling an incident. A non-terminal status may also be rewritten to itself to update the mission's other fields.
        /// </summary>
        /// <param name="current">Current status.</param>
        /// <param name="target">Proposed next status.</param>
        /// <returns>True if Armada may make the transition.</returns>
        public static bool IsValidInternalTransition(MissionStatusEnum current, MissionStatusEnum target)
        {
            if (current == target) return !IsTerminal(current);
            if (IsValidTransition(current, target)) return true;

            switch (target)
            {
                case MissionStatusEnum.Pending:
                    // Requeue (redispatch, escalation, rework, output repair, no-op retry) or restart.
                    return current == MissionStatusEnum.Assigned
                        || current == MissionStatusEnum.InProgress
                        || current == MissionStatusEnum.Testing
                        || current == MissionStatusEnum.Review
                        || current == MissionStatusEnum.Verifying
                        || current == MissionStatusEnum.WorkProduced
                        || current == MissionStatusEnum.LandingFailed
                        || current == MissionStatusEnum.Failed
                        || current == MissionStatusEnum.Cancelled;

                case MissionStatusEnum.Verifying:
                    return current == MissionStatusEnum.InProgress || current == MissionStatusEnum.Testing;

                case MissionStatusEnum.Failed:
                    // Complete: a stage approved in review whose handoff to the next stage then failed.
                    return current == MissionStatusEnum.Complete
                        || current == MissionStatusEnum.Pending
                        || current == MissionStatusEnum.Blocked
                        || current == MissionStatusEnum.Assigned
                        || current == MissionStatusEnum.WorkProduced
                        || current == MissionStatusEnum.PullRequestOpen;

                case MissionStatusEnum.Review:
                    return current == MissionStatusEnum.WorkProduced || current == MissionStatusEnum.PullRequestOpen;

                case MissionStatusEnum.WorkProduced:
                    // Completion from any in-flight status, approval of a review, a manual land, and a verifier restart.
                    return current == MissionStatusEnum.Assigned
                        || current == MissionStatusEnum.Testing
                        || current == MissionStatusEnum.Review
                        || current == MissionStatusEnum.PullRequestOpen
                        || current == MissionStatusEnum.Failed;

                case MissionStatusEnum.LandingFailed:
                    return current == MissionStatusEnum.Review;

                case MissionStatusEnum.Cancelled:
                    // Failed: a cancellation (of the mission or its voyage) dismissing a failed mission.
                    return current == MissionStatusEnum.Testing || current == MissionStatusEnum.Review || current == MissionStatusEnum.Failed;

                case MissionStatusEnum.Complete:
                    // An incident resolved by hand or by a rescue.
                    return current == MissionStatusEnum.Failed;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether an agent's own <c>[ARMADA:STATUS]</c> protocol line may move a mission from
        /// <paramref name="current"/> to <paramref name="target"/>. Only the informational in-flight phase toggle
        /// InProgress to Testing and back is agent-reportable. Every other status (Review, WorkProduced, Complete,
        /// Failed, Cancelled, and the landing states) is decided by Armada from the process exit and the completion
        /// pipeline, so a status line in output (for example a file the agent printed) can never complete, fail, or
        /// queue a mission for review.
        /// </summary>
        /// <param name="current">Current mission status.</param>
        /// <param name="target">Status named by the agent.</param>
        /// <returns>True when the agent may make this transition.</returns>
        public static bool IsAgentReportableTransition(MissionStatusEnum current, MissionStatusEnum target)
        {
            return (current == MissionStatusEnum.InProgress && target == MissionStatusEnum.Testing)
                || (current == MissionStatusEnum.Testing && target == MissionStatusEnum.InProgress);
        }

        /// <summary>
        /// Terminal statuses: the mission is finished and will not transition again.
        /// </summary>
        /// <param name="status">Status to classify.</param>
        /// <returns>True if terminal.</returns>
        public static bool IsTerminal(MissionStatusEnum status)
        {
            return status == MissionStatusEnum.Complete
                || status == MissionStatusEnum.Failed
                || status == MissionStatusEnum.Cancelled;
        }

        /// <summary>
        /// Statuses at or past the point where the agent has produced work: terminal statuses plus
        /// WorkProduced, PullRequestOpen, and LandingFailed. Used by completion idempotency guards
        /// to avoid re-processing a mission whose work already exists.
        /// </summary>
        /// <param name="status">Status to classify.</param>
        /// <returns>True if the mission is terminal or in a post-work state.</returns>
        public static bool IsTerminalOrPostWork(MissionStatusEnum status)
        {
            return IsTerminal(status)
                || status == MissionStatusEnum.WorkProduced
                || status == MissionStatusEnum.PullRequestOpen
                || status == MissionStatusEnum.LandingFailed;
        }

        /// <summary>
        /// Statuses in which a captain is actively responsible for the mission
        /// (InProgress, Assigned, Review, Testing).
        /// </summary>
        /// <param name="status">Status to classify.</param>
        /// <returns>True if the mission is actively in progress.</returns>
        public static bool IsActive(MissionStatusEnum status)
        {
            return status == MissionStatusEnum.Verifying
                || status == MissionStatusEnum.InProgress
                || status == MissionStatusEnum.Assigned
                || status == MissionStatusEnum.Review
                || status == MissionStatusEnum.Testing;
        }
    }
}
