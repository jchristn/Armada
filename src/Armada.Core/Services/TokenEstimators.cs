namespace Armada.Core.Services
{
    using System;
    using System.Collections.Concurrent;

    /// <summary>
    /// Resolves the token estimator for a named tokenizer. No tokenizer ships with Armada yet, so every name resolves to
    /// <see cref="CharacterRatioTokenEstimator.Default"/> until a host registers one.
    /// </summary>
    public static class TokenEstimators
    {
        private static readonly ConcurrentDictionary<string, ITokenEstimator> _Registered = new ConcurrentDictionary<string, ITokenEstimator>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Register an exact counter for a tokenizer name, replacing any earlier one.
        /// </summary>
        /// <param name="tokenizer">Tokenizer name, for example "qwen3" or "o200k_base".</param>
        /// <param name="estimator">The counter.</param>
        /// <exception cref="ArgumentNullException">Thrown when tokenizer is null or blank, or estimator is null.</exception>
        public static void Register(string tokenizer, ITokenEstimator estimator)
        {
            if (String.IsNullOrWhiteSpace(tokenizer)) throw new ArgumentNullException(nameof(tokenizer));
            if (estimator == null) throw new ArgumentNullException(nameof(estimator));
            _Registered[tokenizer.Trim()] = estimator;
        }

        /// <summary>
        /// Remove a registered tokenizer.
        /// </summary>
        /// <param name="tokenizer">Tokenizer name.</param>
        /// <returns>True when one was removed.</returns>
        public static bool Unregister(string? tokenizer)
        {
            if (String.IsNullOrWhiteSpace(tokenizer)) return false;
            return _Registered.TryRemove(tokenizer.Trim(), out ITokenEstimator? _);
        }

        /// <summary>
        /// The estimator for a tokenizer name: the registered counter, or the 3.2 characters-per-token estimate when the
        /// name is empty or unknown.
        /// </summary>
        /// <param name="tokenizer">Tokenizer name, or null.</param>
        /// <returns>The estimator.</returns>
        public static ITokenEstimator Resolve(string? tokenizer)
        {
            if (!String.IsNullOrWhiteSpace(tokenizer) && _Registered.TryGetValue(tokenizer.Trim(), out ITokenEstimator? found)) return found;
            return CharacterRatioTokenEstimator.Default;
        }
    }
}
