namespace Armada.Core.Services
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Estimates tokens from text length. Code and JSON tokenize at about 3.2 characters per token on current open and
    /// frontier tokenizers; four characters per token, the earlier estimate, undercounts them by about a fifth.
    /// </summary>
    public sealed class CharacterRatioTokenEstimator : ITokenEstimator
    {
        /// <summary>
        /// Characters per token assumed for code, JSON and mixed prompts.
        /// </summary>
        public const double CodeCharactersPerToken = 3.2;

        /// <summary>
        /// The shared estimator at <see cref="CodeCharactersPerToken"/>.
        /// </summary>
        public static readonly CharacterRatioTokenEstimator Default = new CharacterRatioTokenEstimator(CodeCharactersPerToken);

        /// <inheritdoc />
        public string Name
        {
            get { return "chars/" + _CharactersPerToken.ToString("0.0##", CultureInfo.InvariantCulture); }
        }

        /// <summary>
        /// Characters per token this estimator assumes.
        /// </summary>
        public double CharactersPerToken
        {
            get { return _CharactersPerToken; }
        }

        private readonly double _CharactersPerToken;

        /// <summary>
        /// Create an estimator for a ratio.
        /// </summary>
        /// <param name="charactersPerToken">Characters per token, greater than zero.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the ratio is not greater than zero.</exception>
        public CharacterRatioTokenEstimator(double charactersPerToken)
        {
            if (!(charactersPerToken > 0)) throw new ArgumentOutOfRangeException(nameof(charactersPerToken));
            _CharactersPerToken = charactersPerToken;
        }

        /// <inheritdoc />
        public long CountTokens(string? text)
        {
            if (String.IsNullOrEmpty(text)) return 0;
            return FromCharacters(text.Length);
        }

        /// <summary>
        /// Estimated tokens of a character count, rounded up.
        /// </summary>
        /// <param name="characters">Characters; zero or less gives zero.</param>
        /// <returns>Estimated tokens.</returns>
        public long FromCharacters(long characters)
        {
            if (characters <= 0) return 0;
            return (long)Math.Ceiling(characters / _CharactersPerToken);
        }
    }
}
