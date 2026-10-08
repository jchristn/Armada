namespace Armada.Core.Metrics.Charts
{
    /// <summary>
    /// A point of a chart line in plot coordinates (origin at the top left, y growing down).
    /// </summary>
    public class ChartPoint
    {
        #region Public-Members

        /// <summary>
        /// Bucket index the point stands for.
        /// </summary>
        public int BucketIndex { get; set; } = 0;

        /// <summary>
        /// Horizontal position.
        /// </summary>
        public double X { get; set; } = 0;

        /// <summary>
        /// Vertical position.
        /// </summary>
        public double Y { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ChartPoint()
        {
        }

        /// <summary>
        /// Instantiate with values.
        /// </summary>
        /// <param name="bucketIndex">Bucket index.</param>
        /// <param name="x">Horizontal position.</param>
        /// <param name="y">Vertical position.</param>
        public ChartPoint(int bucketIndex, double x, double y)
        {
            BucketIndex = bucketIndex;
            X = x;
            Y = y;
        }

        #endregion
    }
}
