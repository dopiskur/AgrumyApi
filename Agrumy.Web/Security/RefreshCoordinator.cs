using System.Collections.Concurrent;
using System.Collections.Generic;

namespace api.Security
{
    /// Deduplicates concurrent refreshes per stale-refresh-token (not one global lock/slot) - the API's refresh token is single-use, so two callers presenting the same spent token must share one in-flight call, without serializing behind or evicting unrelated users' entries; in-process only, a multi-instance Web deployment would need a distributed lock instead.
    public sealed class RefreshCoordinator
    {
        private readonly ConcurrentDictionary<string, Lazy<Task<(string AccessToken, string RefreshToken)?>>> _inFlight = new();

        public async Task<(string AccessToken, string RefreshToken)?> RefreshAsync(
            string staleRefreshToken,
            Func<string, Task<(string AccessToken, string RefreshToken)?>> callApi,
            CancellationToken ct = default)
        {
            var lazy = _inFlight.GetOrAdd(staleRefreshToken,
                key => new Lazy<Task<(string AccessToken, string RefreshToken)?>>(
                    () => callApi(key), LazyThreadSafetyMode.ExecutionAndPublication));

            try
            {
                return await lazy.Value.ConfigureAwait(false);
            }
            finally
            {
                // Compare-and-remove: only drops this entry if it's still ours, never a concurrent awaiter's.
                ((ICollection<KeyValuePair<string, Lazy<Task<(string AccessToken, string RefreshToken)?>>>>)_inFlight)
                    .Remove(new KeyValuePair<string, Lazy<Task<(string AccessToken, string RefreshToken)?>>>(staleRefreshToken, lazy));
            }
        }
    }
}
