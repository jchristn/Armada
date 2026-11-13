namespace Armada.Core.Database
{
    using System;

    /// <summary>
    /// One parameter of a provider-neutral SQL filter. Each provider binds it with its own command type; dates are
    /// bound in the format the provider stores them in.
    /// </summary>
    internal class SqlFilterParameter
    {
        /// <summary>
        /// Parameter name, including the leading at sign.
        /// </summary>
        internal string Name { get; set; } = String.Empty;

        /// <summary>
        /// Value: a string, or a UTC DateTime when <see cref="IsDate"/> is true.
        /// </summary>
        internal object Value { get; set; } = String.Empty;

        /// <summary>
        /// Whether the value is a timestamp.
        /// </summary>
        internal bool IsDate { get; set; } = false;
    }
}
