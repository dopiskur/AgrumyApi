namespace api.Security
{
    // Serializes concurrent refreshes: the API's refresh token is single-use, so a second caller presenting an already-spent token gets every session revoked as theft.
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
