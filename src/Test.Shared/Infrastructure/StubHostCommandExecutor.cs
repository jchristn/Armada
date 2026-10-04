namespace Test.Shared.Infrastructure
{
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// Host command executor that returns a fixed result.
    /// </summary>
    public sealed class StubHostCommandExecutor : IHostCommandExecutor
    {
        #region Private-Members

        private readonly HostCommandResult _Result;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="result">Result returned for every command.</param>
        public StubHostCommandExecutor(HostCommandResult result)
        {
            _Result = result;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<HostCommandResult> RunAsync(HostCommandRequest request, CancellationToken token = default)
        {
            return Task.FromResult(_Result);
        }

        #endregion
    }
}
