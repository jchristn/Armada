namespace Armada.Core.Metrics.Charts
{
    /// <summary>
    /// One drawn piece of a stacked bar: a series' share of one bucket, as a rectangle in plot coordinates (origin at the
    /// top left, y growing down).
    /// </summary>
    public class ChartBarSegment
    {
        #region Public-Members

        /// <summary>
        /// Bucket index.
        /// </summary>
        public int BucketIndex { get; set; } = 0;

        /// <summary>
        /// Series index.
        /// </summary>
        public int SeriesIndex { get; set; } = 0;

        /// <summary>
        /// Left edge.
        /// </summary>
        public double X { get; set; } = 0;

        /// <summary>
        /// Top edge.
        /// </summary>
        public double Y { get; set; } = 0;

        /// <summary>
        /// Width.
        /// </summary>
        public double Width { get; set; } = 0;

        /// <summary>
        /// Height.
        /// </summary>
        public double Height { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ChartBarSegment()
        {
        }

        #endregion
    }
}
