using api.Security;

namespace Agrumy.Api.Tests;

public class RefreshCoordinatorTests
{
    [Fact]
    public async Task RefreshAsync_ConcurrentCallsWithSameToken_CallsApiExactlyOnce()
    {
        var coordinator = new RefreshCoordinator();
        int callCount = 0;
        var gate = new TaskCompletionSource();

        async Task<(string AccessToken, string RefreshToken)?> CallApi(string token)
        {
            Interlocked.Increment(ref callCount);
            await gate.Task; // hold every concurrent caller here until both have started
            return ("new-access", "new-refresh");
        }

        Task<(string, string)?> first = coordinator.RefreshAsync("stale-token", CallApi);
        Task<(string, string)?> second = coordinator.RefreshAsync("stale-token", CallApi);
        gate.SetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, callCount);
        Assert.Equal(("new-access", "new-refresh"), results[0]);
        Assert.Equal(("new-access", "new-refresh"), results[1]);
    }

    [Fact]
    public async Task RefreshAsync_DifferentTokens_BothCallApi_IndependentResults()
    {
        var coordinator = new RefreshCoordinator();

        Task<(string, string)?> CallApiFor(string token) =>
            Task.FromResult<(string, string)?>((token + "-access", token + "-refresh"));

        var resultA = await coordinator.RefreshAsync("token-a", CallApiFor);
        var resultB = await coordinator.RefreshAsync("token-b", CallApiFor);

        Assert.Equal(("token-a-access", "token-a-refresh"), resultA);
        Assert.Equal(("token-b-access", "token-b-refresh"), resultB);
    }

    [Fact]
    public async Task RefreshAsync_ApiReturnsNull_PropagatesNull()
    {
        var coordinator = new RefreshCoordinator();

        var result = await coordinator.RefreshAsync("dead-token", _ => Task.FromResult<(string, string)?>(null));

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_SameTokenAgainAfterCompletion_CallsApiAgain_NoPermanentCaching()
    {
        var coordinator = new RefreshCoordinator();
        int callCount = 0;

        Task<(string, string)?> CallApi(string token)
        {
            callCount++;
            return Task.FromResult<(string, string)?>(("access-" + callCount, "refresh-" + callCount));
        }

        var first = await coordinator.RefreshAsync("token", CallApi);
        var second = await coordinator.RefreshAsync("token", CallApi);

        Assert.Equal(2, callCount);
        Assert.NotEqual(first, second);
    }
}
