namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// In-memory black box store for tests, registered for an external target: it can fail writes, fail deletes, or delay
    /// writes (honoring cancellation) to exercise fallback, two-phase delete and the capture time budget.
    /// </summary>
    public sealed class ScriptedBlackBoxStore : IBlackBoxStore
    {
        /// <summary>Stored bytes by key.</summary>
        public ConcurrentDictionary<string, byte[]> Objects { get; } = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);

        /// <summary>When true, writes throw BlackBoxStoreUnavailableException.</summary>
        public bool FailWrites { get; set; } = false;

        /// <summary>When true, deletes throw BlackBoxStoreUnavailableException.</summary>
        public bool FailDeletes { get; set; } = false;

        /// <summary>Delay applied to each write (honors cancellation).</summary>
        public TimeSpan WriteDelay { get; set; } = TimeSpan.Zero;

        /// <summary>Writes attempted.</summary>
        public int WriteAttempts;

        /// <summary>Instantiate.</summary>
        /// <param name="target">Target the store claims.</param>
        public ScriptedBlackBoxStore(BlackBoxStorageTargetEnum target)
        {
            Target = target;
        }

        /// <inheritdoc />
        public BlackBoxStorageTargetEnum Target { get; }

        /// <inheritdoc />
        public async Task WriteAsync(BlackBoxObjectWrite write, CancellationToken token = default)
        {
            Interlocked.Increment(ref WriteAttempts);
            if (WriteDelay > TimeSpan.Zero) await Task.Delay(WriteDelay, token).ConfigureAwait(false);
            if (FailWrites) throw new BlackBoxStoreUnavailableException("scripted write failure");
            Objects[write.Reference.StorageKey ?? write.Reference.ObjectId] = write.Data;
        }

        /// <inheritdoc />
        public Task<Stream> OpenReadAsync(BlackBoxObjectRef reference, long offset, long? length, CancellationToken token = default)
        {
            if (!Objects.TryGetValue(reference.StorageKey ?? reference.ObjectId, out byte[]? data)) throw new BlackBoxObjectMissingException("missing");
            long start = Math.Min(offset, data.LongLength);
            long take = length.HasValue ? Math.Min(length.Value, data.LongLength - start) : data.LongLength - start;
            byte[] slice = new byte[take];
            Array.Copy(data, start, slice, 0, take);
            return Task.FromResult<Stream>(new MemoryStream(slice));
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(BlackBoxObjectRef reference, CancellationToken token = default)
        {
            return Task.FromResult(Objects.ContainsKey(reference.StorageKey ?? reference.ObjectId));
        }

        /// <inheritdoc />
        public Task DeleteAsync(BlackBoxObjectRef reference, CancellationToken token = default)
        {
            if (FailDeletes) throw new BlackBoxStoreUnavailableException("scripted delete failure");
            Objects.TryRemove(reference.StorageKey ?? reference.ObjectId, out _);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task<BlackBoxStoreDeleteResult> DeleteManyAsync(BlackBoxObjectRefSet references, CancellationToken token = default)
        {
            BlackBoxStoreDeleteResult result = new BlackBoxStoreDeleteResult { Requested = references.References.Count };
            foreach (BlackBoxObjectRef reference in references.References)
            {
                try
                {
                    await DeleteAsync(reference, token).ConfigureAwait(false);
                    result.Deleted++;
                }
                catch (BlackBoxStoreUnavailableException e)
                {
                    result.Failed++;
                    result.FailedNames.Add(reference.Name);
                    result.FirstError ??= e.Message;
                }
            }
            return result;
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<BlackBoxStoredKey> EnumerateAsync(string prefix, [EnumeratorCancellation] CancellationToken token = default)
        {
            foreach (KeyValuePair<string, byte[]> kv in Objects)
            {
                if (kv.Key.StartsWith(prefix, StringComparison.Ordinal)) yield return new BlackBoxStoredKey { Key = kv.Key, SizeBytes = kv.Value.LongLength };
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <inheritdoc />
        public Task<BlackBoxStoreHealth> CheckHealthAsync(CancellationToken token = default)
        {
            return Task.FromResult(new BlackBoxStoreHealth { Target = Target, Healthy = !FailWrites, CheckedUtc = DateTime.UtcNow });
        }
    }
}
