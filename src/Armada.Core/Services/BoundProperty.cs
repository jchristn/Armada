namespace Armada.Core.Services
{
    using System;
    using System.Reflection;

    /// <summary>
    /// A model property that <see cref="CollectionBounds"/> inspects, with its declared bound (null when it has none).
    /// </summary>
    internal sealed class BoundProperty
    {
        /// <summary>
        /// The property.
        /// </summary>
        public PropertyInfo Property { get; }

        /// <summary>
        /// The declared bound, or null.
        /// </summary>
        public CollectionBoundAttribute? Attribute { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="property">Property.</param>
        /// <param name="attribute">Declared bound, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="property"/> is null.</exception>
        public BoundProperty(PropertyInfo property, CollectionBoundAttribute? attribute)
        {
            Property = property ?? throw new ArgumentNullException(nameof(property));
            Attribute = attribute;
        }
    }
}
