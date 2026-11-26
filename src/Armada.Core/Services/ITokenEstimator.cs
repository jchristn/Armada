namespace Armada.Core.Services
{
    /// <summary>
    /// Counts the tokens a text costs a model. The default is <see cref="CharacterRatioTokenEstimator"/>; a profile that
    /// names a tokenizer can register an exact counter with <see cref="TokenEstimators.Register"/>.
    /// </summary>
    public interface ITokenEstimator
    {
        /// <summary>
        /// Name of the tokenizer, or "chars/3.2" style text for a ratio estimate.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Estimated tokens of a text.
        /// </summary>
        /// <param name="text">Text, or null (zero tokens).</param>
        /// <returns>Estimated tokens.</returns>
        long CountTokens(string? text);
    }
}
