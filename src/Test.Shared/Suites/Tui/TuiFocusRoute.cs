namespace Test.Shared.Suites.Tui
{
    using System;
    using Test.Shared.Infrastructure;

    /// <summary>
    /// A route the focus sweep visits with data (see <see cref="TuiFocusSweepSuite"/>): the path, the terminal size, and
    /// the stub server that serves its records.
    /// </summary>
    public sealed class TuiFocusRoute
    {
        #region Public-Members

        /// <summary>
        /// Route path (with query).
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Columns.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Rows.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Builds the stub server.
        /// </summary>
        public Func<StubHttpHandler> Stub { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="path">Route path.</param>
        /// <param name="width">Columns.</param>
        /// <param name="height">Rows.</param>
        /// <param name="stub">Stub factory.</param>
        public TuiFocusRoute(string path, int width, int height, Func<StubHttpHandler> stub)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Width = width;
            Height = height;
            Stub = stub ?? throw new ArgumentNullException(nameof(stub));
        }

        #endregion
    }
}
