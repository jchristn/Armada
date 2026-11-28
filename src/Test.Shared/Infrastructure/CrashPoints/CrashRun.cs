namespace Test.Shared.Infrastructure.CrashPoints
{
    using System;
    using System.Threading.Tasks;
    using Armada.Core.Services;

    /// <summary>
    /// Runs an operation with one crash point armed and reports how it ended. When the point fires, the operation is
    /// frozen there and abandoned, so nothing after the point runs. The caller then builds fresh services over the same
    /// database, as an Admiral restart would, and checks that recovery reaches the uninterrupted end state.
    /// </summary>
    public static class CrashRun
    {
        /// <summary>
        /// Run <paramref name="operation"/> with <paramref name="crashPoint"/> armed.
        /// </summary>
        /// <param name="crashPoint">One of the <see cref="CrashPointNames"/> constants.</param>
        /// <param name="operation">The operation to interrupt.</param>
        /// <param name="timeout">How long to wait for the operation or the crash. Default 120 seconds.</param>
        /// <returns>How the operation ended.</returns>
        /// <exception cref="ArgumentNullException">When an argument is null.</exception>
        /// <exception cref="TimeoutException">When neither the crash nor the end of the operation happens in time.</exception>
        public static async Task<CrashRunOutcomeEnum> RunAsync(string crashPoint, Func<Task> operation, TimeSpan? timeout = null)
        {
            if (String.IsNullOrEmpty(crashPoint)) throw new ArgumentNullException(nameof(crashPoint));
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            CrashPointInjector injector = new CrashPointInjector(crashPoint);
            Task running;
            using (CrashPoints.Install(injector))
            {
                // Task.Run captures the execution context, so the injector flows into the operation and everything it starts.
                running = Task.Run(operation);
            }

            Task first = await Task.WhenAny(running, injector.FiredTask, Task.Delay(timeout ?? TimeSpan.FromSeconds(120))).ConfigureAwait(false);
            if (first == injector.FiredTask) return CrashRunOutcomeEnum.Crashed;
            if (first == running)
            {
                await running.ConfigureAwait(false);
                return injector.Fired ? CrashRunOutcomeEnum.Crashed : CrashRunOutcomeEnum.NotReached;
            }

            throw new TimeoutException("Neither crash point " + crashPoint + " nor the end of the operation was reached.");
        }
    }
}
