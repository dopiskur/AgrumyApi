namespace api.Security
{
    /// <summary>
    /// Serializes concurrent access-token refresh attempts so parallel requests that all hit the
    /// same expired access token don't each spend the refresh token. The API's refresh token is
    /// single-use (rotated on redemption), so a second caller presenting the same now-stale token
    /// would look like theft to the API and get every session revoked. The first caller through the
    /// lock refreshes for real; anyone who queued up behind it reuses that result instead of calling
    /// the API again with a token that's already been spent.
    ///
    /// Singleton by design - this app is a small internal admin tool, not a multi-instance service,
    /// so one global lock is simpler than per-user locking and correct at this scale.
    /// </summary>
    public sealed class RefreshCoordinator
    {
        private readonly SemaphoreSlim _lock = new(1, 1);
        private (string StaleRefreshToken, string AccessToken, string RefreshToken)? _lastResult;

        public async Task<(string AccessToken, string RefreshToken)?> RefreshAsync(
            string staleRefreshToken,
            Func<string, Task<(string AccessToken, string RefreshToken)?>> callApi,
            CancellationToken ct = default)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_lastResult is { } cached && cached.StaleRefreshToken == staleRefreshToken)
                {
                    return (cached.AccessToken, cached.RefreshToken);
                }

                var result = await callApi(staleRefreshToken).ConfigureAwait(false);
                if (result is null)
                {
                    return null;
                }

                _lastResult = (staleRefreshToken, result.Value.AccessToken, result.Value.RefreshToken);
                return result;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
