namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Turns what a failure site holds (an exception, a runtime exit classification, a merge-queue failure) into black
    /// box facts and steps, so every site records git facts and failing commands the same way.
    /// </summary>
    public static class BlackBoxSiteFacts
    {
        #region Public-Methods

        /// <summary>
        /// Record an exception on a capture context: the exception itself, the git facts it carries (conflicted files,
        /// dirty paths, the git command and its stderr) and the failing command of a landing verification. Facts already
        /// on the context are kept and extended.
        /// </summary>
        /// <param name="context">Capture context.</param>
        /// <param name="exception">Exception, or null (no-op).</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public static void ApplyException(BlackBoxCaptureContext context, Exception? exception)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (exception == null) return;
            context.Exception ??= exception;

            BlackBoxGitFacts? facts = GitFactsFrom(exception);
            if (facts != null) context.GitFacts = Merge(context.GitFacts, facts);

            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is LandingVerificationException verification)
                {
                    context.FailingCommand ??= verification.Command;
                    context.CommandExitCode ??= verification.ExitCode;
                    break;
                }

                if (current is GitCommandException command)
                {
                    context.FailingCommand ??= CommandLine(command);
                    context.CommandExitCode ??= command.ExitCode;
                    if (String.IsNullOrEmpty(context.CommandOutput))
                        context.CommandOutput = String.IsNullOrEmpty(command.StandardError) ? command.StandardOutput : command.StandardError;
                    break;
                }
            }
        }

        /// <summary>
        /// The git facts an exception carries (GitMergeConflictException, GitDirtyCheckoutException, GitCommandException
        /// anywhere in its inner-exception chain), or null when it carries none.
        /// </summary>
        /// <param name="exception">Exception, or null.</param>
        /// <returns>The facts, or null.</returns>
        public static BlackBoxGitFacts? GitFactsFrom(Exception? exception)
        {
            BlackBoxGitFacts? facts = null;
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is GitMergeConflictException conflict)
                {
                    facts ??= new BlackBoxGitFacts();
                    foreach (string file in conflict.ConflictedFiles)
                        if (!facts.ConflictedFiles.Contains(file, StringComparer.Ordinal)) facts.ConflictedFiles.Add(file);
                    facts.GitOperation ??= "merge";
                    if (String.IsNullOrEmpty(facts.GitStderr) && !String.IsNullOrEmpty(conflict.StandardError)) facts.GitStderr = conflict.StandardError;
                }
                else if (current is GitDirtyCheckoutException dirty)
                {
                    facts ??= new BlackBoxGitFacts();
                    foreach (string path in dirty.ModifiedPaths)
                        if (!facts.DirtyPaths.Contains(path, StringComparer.Ordinal)) facts.DirtyPaths.Add(path);
                    facts.CheckoutPath ??= dirty.CheckoutPath;
                    facts.GitOperation ??= "status --porcelain";
                }
                else if (current is GitCommandException command)
                {
                    facts ??= new BlackBoxGitFacts();
                    facts.GitOperation ??= command.Arguments.Count > 0 ? String.Join(" ", command.Arguments.Take(4)) : "git";
                    if (String.IsNullOrEmpty(facts.GitStderr) && !String.IsNullOrEmpty(command.StandardError)) facts.GitStderr = command.StandardError;
                }
            }
            return facts;
        }

        /// <summary>
        /// The landing step an exception points at: a merge conflict while integrating the target, a dirty checkout, an
        /// unpublished target ancestry, or a verification refusal. Anything else is the fallback.
        /// </summary>
        /// <param name="exception">Exception, or null.</param>
        /// <param name="fallback">Step when the exception names none.</param>
        /// <returns>The step.</returns>
        public static BlackBoxStepEnum LandingStepOf(Exception? exception, BlackBoxStepEnum fallback)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is GitDirtyCheckoutException) return BlackBoxStepEnum.DirtyCheckout;
                if (current is GitUnpublishedCommitsException) return BlackBoxStepEnum.UnpublishedAncestry;
                if (current is LandingVerificationException) return BlackBoxStepEnum.Verification;
            }
            return fallback;
        }

        /// <summary>
        /// The step of a terminal agent exit, from its runtime classification: a run that ended without finishing its task
        /// (TaskFailure, recorded as InvalidOutput) is a Completion step; the rest are Execution steps.
        /// </summary>
        /// <param name="kind">Runtime failure classification.</param>
        /// <returns>The step.</returns>
        public static BlackBoxStepEnum ExecutionStepOf(RuntimeFailureKindEnum kind)
        {
            switch (kind)
            {
                case RuntimeFailureKindEnum.UsageLimit: return BlackBoxStepEnum.UsageLimit;
                case RuntimeFailureKindEnum.AuthFailure: return BlackBoxStepEnum.AuthFailure;
                case RuntimeFailureKindEnum.ModelUnavailable: return BlackBoxStepEnum.ModelUnavailable;
                case RuntimeFailureKindEnum.Transient: return BlackBoxStepEnum.RecoveryExhausted;
                case RuntimeFailureKindEnum.TaskFailure: return BlackBoxStepEnum.InvalidOutput;
                default: return BlackBoxStepEnum.ProcessCrash;
            }
        }

        /// <summary>
        /// The merge-queue step of a structured merge-queue failure.
        /// </summary>
        /// <param name="failure">Failure, or null (QueueProcessing).</param>
        /// <returns>The step.</returns>
        public static BlackBoxStepEnum MergeQueueStepOf(MergeQueueFailure? failure)
        {
            if (failure == null) return BlackBoxStepEnum.QueueProcessing;
            switch (failure.Step)
            {
                case MergeQueueFailureStepEnum.Resolution: return BlackBoxStepEnum.QueueResolveRepo;
                case MergeQueueFailureStepEnum.Merge: return BlackBoxStepEnum.QueueMergeConflict;
                case MergeQueueFailureStepEnum.Verification: return BlackBoxStepEnum.QueueTests;
                case MergeQueueFailureStepEnum.Publication: return BlackBoxStepEnum.QueuePush;
                default: return BlackBoxStepEnum.QueueProcessing;
            }
        }

        /// <summary>
        /// Combine two sets of git facts: lists are unioned, single values keep the first one set.
        /// </summary>
        /// <param name="first">First facts, or null.</param>
        /// <param name="second">Second facts, or null.</param>
        /// <returns>The combined facts, or null when both are null.</returns>
        public static BlackBoxGitFacts? Merge(BlackBoxGitFacts? first, BlackBoxGitFacts? second)
        {
            if (first == null) return second;
            if (second == null) return first;
            foreach (string file in second.ConflictedFiles)
                if (!first.ConflictedFiles.Contains(file, StringComparer.Ordinal)) first.ConflictedFiles.Add(file);
            foreach (string path in second.DirtyPaths)
                if (!first.DirtyPaths.Contains(path, StringComparer.Ordinal)) first.DirtyPaths.Add(path);
            first.TargetTipBefore ??= second.TargetTipBefore;
            first.TargetTipAfter ??= second.TargetTipAfter;
            first.WorktreeExisted ??= second.WorktreeExisted;
            first.CheckoutPath ??= second.CheckoutPath;
            first.BranchTip ??= second.BranchTip;
            first.GitOperation ??= second.GitOperation;
            first.GitStderr ??= second.GitStderr;
            return first;
        }

        #endregion

        #region Private-Methods

        private static string CommandLine(GitCommandException command)
        {
            List<string> parts = new List<string> { String.IsNullOrEmpty(command.Executable) ? "git" : command.Executable };
            parts.AddRange(command.Arguments.Take(12));
            return String.Join(" ", parts);
        }

        #endregion
    }
}
