namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// One source in a <see cref="SectionedPaging"/> enumeration: a run of items read from a source that pages itself,
    /// placed after the sections before it.
    /// </summary>
    /// <typeparam name="T">Item type.</typeparam>
    public sealed class PagedSection<T>
    {
        #region Public-Members

        /// <summary>
        /// Section name, for diagnostics.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Reads one page of the section's source.
        /// </summary>
        public PageFetchDelegate<T> Fetch { get; }

        /// <summary>
        /// Largest page size the source accepts. Default <see cref="CollectionLimits.MaxPageSize"/>; minimum 1.
        /// </summary>
        public int MaxPageSize { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="name">Section name.</param>
        /// <param name="fetch">Page reader.</param>
        /// <param name="maxPageSize">Largest page size the source accepts.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="fetch"/> is null.</exception>
        public PagedSection(string name, PageFetchDelegate<T> fetch, int maxPageSize = CollectionLimits.MaxPageSize)
        {
            Name = name ?? "";
            Fetch = fetch ?? throw new ArgumentNullException(nameof(fetch));
            MaxPageSize = maxPageSize < 1 ? 1 : maxPageSize;
        }

        #endregion
    }
}
