namespace Armada.Server.Routes
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Runs the doctor's database query with a timeout, so the Database check reports Pass only after the database
    /// actually answered.
    /// </summary>
    internal static class DoctorDatabaseProbe
    {
        /// <summary>
        /// Run a query and wait for it, at most the timeout.
        /// </summary>
        /// <param name="query">The query to run; it receives a token that is cancelled at the timeout.</param>
        /// <param name="timeout">How long to wait for the answer.</param>
        /// <returns>Null when the query answered in time; otherwise why it did not.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null.</exception>
        internal static async Task<string?> ProbeAsync(Func<CancellationToken, Task> query, TimeSpan timeout)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));

            string timedOut = "no response within " + timeout.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " seconds";
            using (CancellationTokenSource cts = new CancellationTokenSource(timeout))
            {
                Task running;
                try
                {
                    running = query(cts.Token);
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }

                // A driver that ignores the token must not hold the doctor response open.
                Task finished = await Task.WhenAny(running, Task.Delay(timeout)).ConfigureAwait(false);
                if (finished != running)
                {
                    cts.Cancel();
                    _ = running.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
                    return timedOut;
                }

                try
                {
                    await running.ConfigureAwait(false);
                    return null;
                }
                catch (OperationCanceledException)
                {
                    return timedOut;
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            }
        }
    }
}
