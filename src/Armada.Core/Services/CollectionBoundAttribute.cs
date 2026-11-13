namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Declares the most items a collection property (list, array, set, or dictionary) of a model returned by a REST
    /// route or MCP tool may hold. <see cref="CollectionBounds.Validate(object?, string?)"/> enforces it on write paths,
    /// and the E2E.ResponseShape guard fails for any collection property reachable from a response that does not carry
    /// it. Use the <see cref="CollectionLimits"/> constants, for example
    /// <c>[CollectionBound(CollectionLimits.MaxConfigItems)]</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class CollectionBoundAttribute : Attribute
    {
        #region Public-Members

        /// <summary>
        /// Most items the collection may hold. Minimum 0.
        /// </summary>
        public int Limit { get; }

        /// <summary>
        /// Optional note on what bounds the collection when it is produced by the server rather than stored from input,
        /// for example "One per CheckRunTypeEnum value". Null when the limit alone says it.
        /// </summary>
        public string? Reason { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="limit">Most items the collection may hold. Minimum 0.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the limit is negative.</exception>
        public CollectionBoundAttribute(int limit)
        {
            if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit), "Limit must not be negative.");
            Limit = limit;
        }

        #endregion
    }
}
