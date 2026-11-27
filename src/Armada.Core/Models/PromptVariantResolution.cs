namespace Armada.Core.Models
{
    using Armada.Core.Enums;

    /// <summary>
    /// The persona prompt variant a mission renders with and what chose it.
    /// </summary>
    public class PromptVariantResolution
    {
        /// <summary>
        /// The variant to render.
        /// </summary>
        public PromptVariantEnum Variant { get; set; } = PromptVariantEnum.Current;

        /// <summary>
        /// What chose the variant.
        /// </summary>
        public PromptVariantSourceEnum Source { get; set; } = PromptVariantSourceEnum.Default;

        /// <summary>
        /// The served context, in tokens, the automatic choice compared, or null when it was not known or not consulted.
        /// </summary>
        public int? ServedContextTokens { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PromptVariantResolution()
        {
        }
    }
}
