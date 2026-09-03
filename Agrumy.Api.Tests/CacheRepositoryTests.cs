using api.Dal;
using api.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Agrumy.Api.Tests;

/// <summary>
/// Roadmap #72: CacheRepository moved from a process-local <c>System.Runtime.Caching.MemoryCache</c>
/// to <see cref="IDistributedCache"/> so a real distributed backend (Redis, SQL Server) is a DI swap,
/// not a rewrite. These tests exercise it against <see cref="MemoryDistributedCache"/> - the same
/// implementation Program.cs wires up today via <c>AddDistributedMemoryCache()</c> - so they cover
/// the serialization round-trip a real network-backed store would also require.
/// </summary>
public class CacheRepositoryTests
{
    private static IDistributedCache NewBackingStore() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static CacheRepository NewRepository(IDistributedCache cache) =>
        new(cache, NullLogger<CacheRepository>.Instance);

    [Fact]
    public async Task GetDeviceCacheAsync_Miss_ReturnsDefaultInstance_NeverNull()
    {
        var repo = NewRepository(NewBackingStore());

        DeviceCache result = await repo.GetDeviceCacheAsync("no-such-key");

        Assert.NotNull(result);
        Assert.Null(result.apiAuth);
    }

    [Fact]
    public async Task SetItemAsync_Then_GetDeviceCacheAsync_RoundTrips()
    {
        var repo = NewRepository(NewBackingStore());

        await repo.SetItemAsync("api-guid", new DeviceCache { apiAuth = "session-token" });
        DeviceCache result = await repo.GetDeviceCacheAsync("api-guid");

        Assert.Equal("session-token", result.apiAuth);
    }

    /// <summary>
    /// The actual point of #72: two CacheRepository instances over the SAME backing store see each
    /// other's writes - unlike the old `static readonly MemoryCache`, state now lives in whatever
    /// IDistributedCache is registered, which is exactly what lets a real distributed backend make
    /// this true across separate application instances, not just separate objects in one process.
    /// </summary>
    [Fact]
    public async Task TwoRepositoryInstances_OverSharedBackingStore_SeeEachOthersWrites()
    {
        IDistributedCache sharedStore = NewBackingStore();
        var writer = NewRepository(sharedStore);
        var reader = NewRepository(sharedStore);

        await writer.SetItemAsync("shared-key", new DeviceCache { apiAuth = "auth-abc" });
        DeviceCache result = await reader.GetDeviceCacheAsync("shared-key");

        Assert.Equal("auth-abc", result.apiAuth);
    }

    [Fact]
    public async Task SetItemAsync_Overwrites_ExistingEntry()
    {
        var repo = NewRepository(NewBackingStore());

        await repo.SetItemAsync("api-guid", new DeviceCache { apiAuth = "old" });
        await repo.SetItemAsync("api-guid", new DeviceCache { apiAuth = "new" });
        DeviceCache result = await repo.GetDeviceCacheAsync("api-guid");

        Assert.Equal("new", result.apiAuth);
    }

    // ---- Generic GetAsync<T>/SetAsync<T> (roadmap #118) ------------------------------

    private sealed record FleetSnapshot(int IDDevice, bool Online);

    [Fact]
    public async Task GetAsync_Miss_ReturnsNull()
    {
        var repo = NewRepository(NewBackingStore());

        List<FleetSnapshot>? result = await repo.GetAsync<List<FleetSnapshot>>("fleet:no-such-key");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_Then_GetAsync_RoundTrips()
    {
        var repo = NewRepository(NewBackingStore());
        var snapshot = new List<FleetSnapshot> { new(1, true), new(2, false) };

        await repo.SetAsync("fleet:1", snapshot, TimeSpan.FromSeconds(6));
        List<FleetSnapshot>? result = await repo.GetAsync<List<FleetSnapshot>>("fleet:1");

        Assert.Equal(snapshot, result);
    }

    /// <summary>Same distributed-visibility guarantee as
    /// <see cref="TwoRepositoryInstances_OverSharedBackingStore_SeeEachOthersWrites"/> above, for the
    /// generic path - this is what lets several concurrently open admin tabs (roadmap #90's 10s poll,
    /// each its own HTTP request/scope) share one real DB query instead of one each.</summary>
    [Fact]
    public async Task GetAsync_OverSharedBackingStore_SeesAnotherInstancesSetAsync()
    {
        IDistributedCache sharedStore = NewBackingStore();
        var writer = NewRepository(sharedStore);
        var reader = NewRepository(sharedStore);
        var snapshot = new List<FleetSnapshot> { new(7, true) };

        await writer.SetAsync("fleet:shared", snapshot, TimeSpan.FromSeconds(6));
        List<FleetSnapshot>? result = await reader.GetAsync<List<FleetSnapshot>>("fleet:shared");

        Assert.Equal(snapshot, result);
    }

    // ---- Roadmap #119: backend outage degrades to miss/no-op instead of throwing -----

    /// <summary>Stands in for a Redis client throwing a connection/timeout exception - CacheRepository
    /// must not care which backend or which exception type, only that the call failed.</summary>
    private sealed class ThrowingCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("backend unreachable");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("backend unreachable");
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new InvalidOperationException("backend unreachable");
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            throw new InvalidOperationException("backend unreachable");
        public void Refresh(string key) => throw new InvalidOperationException("backend unreachable");
        public Task RefreshAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("backend unreachable");
        public void Remove(string key) => throw new InvalidOperationException("backend unreachable");
        public Task RemoveAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("backend unreachable");
    }

    [Fact]
    public async Task GetDeviceCacheAsync_BackendThrows_ReturnsMissInsteadOfPropagating()
    {
        var repo = NewRepository(new ThrowingCache());

        DeviceCache result = await repo.GetDeviceCacheAsync("api-guid");

        Assert.NotNull(result);
        Assert.Null(result.apiAuth);
    }

    [Fact]
    public async Task SetItemAsync_BackendThrows_CompletesWithoutPropagating()
    {
        var repo = NewRepository(new ThrowingCache());

        await repo.SetItemAsync("api-guid", new DeviceCache { apiAuth = "session-token" });
        // No assert needed beyond "did not throw" - that IS the graceful-degradation contract.
    }

    [Fact]
    public async Task GetAsync_BackendThrows_ReturnsNullInsteadOfPropagating()
    {
        var repo = NewRepository(new ThrowingCache());

        List<FleetSnapshot>? result = await repo.GetAsync<List<FleetSnapshot>>("fleet:1");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_BackendThrows_CompletesWithoutPropagating()
    {
        var repo = NewRepository(new ThrowingCache());

        await repo.SetAsync("fleet:1", new List<FleetSnapshot> { new(1, true) }, TimeSpan.FromSeconds(6));
    }
}
