using api.Dal;
using api.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
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

    [Fact]
    public async Task GetDeviceCacheAsync_Miss_ReturnsDefaultInstance_NeverNull()
    {
        var repo = new CacheRepository(NewBackingStore());

        DeviceCache result = await repo.GetDeviceCacheAsync("no-such-key");

        Assert.NotNull(result);
        Assert.Null(result.apiAuth);
    }

    [Fact]
    public async Task SetItemAsync_Then_GetDeviceCacheAsync_RoundTrips()
    {
        var repo = new CacheRepository(NewBackingStore());

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
        var writer = new CacheRepository(sharedStore);
        var reader = new CacheRepository(sharedStore);

        await writer.SetItemAsync("shared-key", new DeviceCache { apiAuth = "auth-abc" });
        DeviceCache result = await reader.GetDeviceCacheAsync("shared-key");

        Assert.Equal("auth-abc", result.apiAuth);
    }

    [Fact]
    public async Task SetItemAsync_Overwrites_ExistingEntry()
    {
        var repo = new CacheRepository(NewBackingStore());

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
        var repo = new CacheRepository(NewBackingStore());

        List<FleetSnapshot>? result = await repo.GetAsync<List<FleetSnapshot>>("fleet:no-such-key");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_Then_GetAsync_RoundTrips()
    {
        var repo = new CacheRepository(NewBackingStore());
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
        var writer = new CacheRepository(sharedStore);
        var reader = new CacheRepository(sharedStore);
        var snapshot = new List<FleetSnapshot> { new(7, true) };

        await writer.SetAsync("fleet:shared", snapshot, TimeSpan.FromSeconds(6));
        List<FleetSnapshot>? result = await reader.GetAsync<List<FleetSnapshot>>("fleet:shared");

        Assert.Equal(snapshot, result);
    }
}
