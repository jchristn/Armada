namespace Armada.Core.BlackBox
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>
    /// One black box redaction rule: a name (reported) and a pattern whose match is replaced. When the pattern has a group
    /// named "keep", that group is kept and only the rest of the match is replaced.
    /// </summary>
    public class BlackBoxRedactionRule
    {
        /// <summary>
        /// Rule name, reported in the record's Redaction section.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Pattern.
        /// </summary>
        public Regex Pattern { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Rule name.</param>
        /// <param name="pattern">Pattern.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        public BlackBoxRedactionRule(string name, Regex pattern)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            Name = name;
            Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        }
    }
}
