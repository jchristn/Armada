namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Security.Cryptography;
    using Armada.Core.Models;

    /// <summary>
    /// Single-use confirm tokens for black box recovery runs and recommendation applies (docs/BLACKBOX.md, "Recovery
    /// contract"). A dry run issues a token bound to the entry, the recovery path or recommendation, the subject's version,
    /// the plan and the caller; the confirming call must present it before it expires (<see cref="Lifetime"/>, default
    /// 10 minutes). A token is removed the moment it is presented, whether or not the call then succeeds. Tokens live in
    /// memory (an Admiral restart invalidates them). Thread-safe.
    /// </summary>
    public class BlackBoxConfirmationStore
    {
        #region Public-Members

        /// <summary>Kind of a recovery run token.</summary>
        public const string RecoveryKind = "recovery";

        /// <summary>Kind of a recommendation apply token.</summary>
        public const string RecommendationKind = "recommendation";

        /// <summary>How long a token is valid. Default 10 minutes, minimum 1 second, maximum 1 hour.</summary>
        public TimeSpan Lifetime
        {
            get => _Lifetime;
            set => _Lifetime = value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : (value > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : value);
        }

        /// <summary>Most outstanding tokens; the oldest are dropped beyond it. Default 10000, minimum 10.</summary>
        public int MaxPending
        {
            get => _MaxPending;
            set => _MaxPending = Math.Max(10, value);
        }

        /// <summary>Clock (tests replace it).</summary>
        public Func<DateTime> Clock
        {
            get => _Clock;
            set => _Clock = value ?? (() => DateTime.UtcNow);
        }

        /// <summary>Outstanding tokens.</summary>
        public int Count => _Pending.Count;

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, BlackBoxPendingConfirmation> _Pending = new ConcurrentDictionary<string, BlackBoxPendingConfirmation>(StringComparer.Ordinal);
        private TimeSpan _Lifetime = TimeSpan.FromMinutes(10);
        private int _MaxPending = 10000;
        private Func<DateTime> _Clock = () => DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxConfirmationStore()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Issue a token.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="entryId">Entry id.</param>
        /// <param name="targetId">Path or recommendation id.</param>
        /// <param name="subjectVersion">Subject version.</param>
        /// <param name="planHash">Plan fingerprint.</param>
        /// <param name="caller">Caller.</param>
        /// <returns>The pending confirmation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxPendingConfirmation Issue(string kind, string entryId, string targetId, string subjectVersion, string planHash, AuthContext caller)
        {
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            if (entryId == null) throw new ArgumentNullException(nameof(entryId));
            if (targetId == null) throw new ArgumentNullException(nameof(targetId));
            if (caller == null) throw new ArgumentNullException(nameof(caller));
            Prune();
            BlackBoxPendingConfirmation pending = new BlackBoxPendingConfirmation
            {
                Token = "bbc_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
                Kind = kind,
                EntryId = entryId,
                TargetId = targetId,
                SubjectVersion = subjectVersion ?? "",
                PlanHash = planHash ?? "",
                UserId = caller.UserId,
                TenantId = caller.TenantId,
                ExpiresUtc = _Clock() + _Lifetime
            };
            _Pending[pending.Token] = pending;
            return pending;
        }

        /// <summary>
        /// Present a token: it is removed whatever the outcome, then checked against the call.
        /// </summary>
        /// <param name="token">Token text.</param>
        /// <param name="kind">Kind expected.</param>
        /// <param name="entryId">Entry id of the call.</param>
        /// <param name="targetId">Path or recommendation id of the call.</param>
        /// <param name="caller">Caller.</param>
        /// <returns>The pending confirmation (the caller still checks the subject version and plan).</returns>
        /// <exception cref="BlackBoxConfirmationException">Thrown when the token is unknown, already used, expired, or
        /// issued for another entry, path, recommendation or caller.</exception>
        public BlackBoxPendingConfirmation Consume(string? token, string kind, string entryId, string targetId, AuthContext caller)
        {
            if (String.IsNullOrWhiteSpace(token)) throw new BlackBoxConfirmationException("A confirmToken is required: run a dry run first and confirm with the token it returns.");
            if (!_Pending.TryRemove(token!.Trim(), out BlackBoxPendingConfirmation? pending))
                throw new BlackBoxConfirmationException("The confirmToken is unknown or was already used; run a new dry run.");
            if (pending.ExpiresUtc <= _Clock())
                throw new BlackBoxConfirmationException("The confirmToken expired at " + pending.ExpiresUtc.ToString("u") + "; run a new dry run.");
            if (!String.Equals(pending.Kind, kind, StringComparison.Ordinal) || !String.Equals(pending.EntryId, entryId, StringComparison.Ordinal) || !String.Equals(pending.TargetId, targetId, StringComparison.Ordinal))
                throw new BlackBoxConfirmationException("The confirmToken was issued for " + pending.Kind + " " + pending.TargetId + " of entry " + pending.EntryId + ", not for " + kind + " " + targetId + " of entry " + entryId + "; run a dry run for this one.");
            if (!String.Equals(pending.UserId, caller?.UserId, StringComparison.Ordinal) || !String.Equals(pending.TenantId, caller?.TenantId, StringComparison.Ordinal))
                throw new BlackBoxConfirmationException("The confirmToken was issued to another caller; run your own dry run.");
            return pending;
        }

        #endregion

        #region Private-Methods

        private void Prune()
        {
            DateTime now = _Clock();
            foreach (string expired in _Pending.Where(p => p.Value.ExpiresUtc <= now).Select(p => p.Key).ToList()) _Pending.TryRemove(expired, out _);
            if (_Pending.Count < _MaxPending) return;
            foreach (string old in _Pending.OrderBy(p => p.Value.ExpiresUtc).Take(_Pending.Count - _MaxPending + 1).Select(p => p.Key).ToList()) _Pending.TryRemove(old, out _);
        }

        #endregion
    }
}
