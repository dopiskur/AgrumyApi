using api.Dal;
using api.Dal.Interface;
using api.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Agrumy.Api.Tests;

/// Health checks (DB + cache-backend, the latter tied into the cache's graceful-degradation behavior) and the per-route metrics aggregate.
public class DiagnosticsTests
{
    private sealed class FakeSystemRepository(bool canConnect, Exception? throwOnConnect = null) : ISystemRepository
    {
        public Task<bool> TestConnectionAsync() =>
            throwOnConnect is not null ? throw throwOnConnect : Task.FromResult(canConnect);
        public Task EnsureSchemaAsync() => Task.CompletedTask;
        public DbFailureKind ClassifyException(Exception ex) => DbFailureKind.Unknown;
    }

    private static readonly HealthCheckContext Context = new() { Registration = new HealthCheckRegistration("test", sp => null!, null, null) };

    [Fact]
    public async Task DatabaseHealthCheck_ConnectionOk_ReturnsHealthy()
    {
        var check = new DatabaseHealthCheck(new FakeSystemRepository(canConnect: true));

        HealthCheckResult result = await check.CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task DatabaseHealthCheck_ConnectionFails_ReturnsUnhealthy()
    {
        var check = new DatabaseHealthCheck(new FakeSystemRepository(canConnect: false));

        HealthCheckResult result = await check.CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task DatabaseHealthCheck_Throws_ReturnsUnhealthyNotPropagating()
    {
        var check = new DatabaseHealthCheck(new FakeSystemRepository(false, new InvalidOperationException("db down")));

        HealthCheckResult result = await check.CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task CacheHealthCheck_BackendOk_ReturnsHealthy()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var check = new CacheHealthCheck(cache);

        HealthCheckResult result = await check.CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    /// Same fake as CacheRepositoryTests' ThrowingCache - stands in for a Redis client throwing a connection/timeout exception.
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
    public async Task CacheHealthCheck_BackendThrows_ReturnsDegraded_NotUnhealthy()
    {
        // A dead cache must not fail the overall health check (the API keeps working without it) - Degraded, not Unhealthy.
        var check = new CacheHealthCheck(new ThrowingCache());

        HealthCheckResult result = await check.CheckHealthAsync(Context);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    [Fact]
    public void AgrumyMetrics_RecordRequest_AggregatesCountAndErrorsPerRouteAndMethod()
    {
        var metrics = new AgrumyMetrics();

        metrics.RecordRequest("/api/Device/Fleet", "GET", 200, 10.0);
        metrics.RecordRequest("/api/Device/Fleet", "GET", 200, 20.0);
        metrics.RecordRequest("/api/Device/Fleet", "GET", 500, 30.0);
        metrics.RecordRequest("/api/User/Login", "POST", 200, 5.0);

        MetricsSnapshot snapshot = metrics.GetSnapshot();

        RouteMetricsSnapshot fleet = Assert.Single(snapshot.Routes, r => r.Route == "/api/Device/Fleet" && r.Method == "GET");
        Assert.Equal(3, fleet.RequestCount);
        Assert.Equal(1, fleet.ErrorCount);
        Assert.Equal(20.0, fleet.AvgDurationMs);
        Assert.Equal(10.0, fleet.MinDurationMs);
        Assert.Equal(30.0, fleet.MaxDurationMs);

        RouteMetricsSnapshot login = Assert.Single(snapshot.Routes, r => r.Route == "/api/User/Login" && r.Method == "POST");
        Assert.Equal(1, login.RequestCount);
        Assert.Equal(0, login.ErrorCount);
    }

    [Fact]
    public void AgrumyMetrics_DifferentMethods_SameRoute_TrackedSeparately()
    {
        var metrics = new AgrumyMetrics();

        metrics.RecordRequest("/api/Device/{id}", "GET", 200, 10.0);
        metrics.RecordRequest("/api/Device/{id}", "DELETE", 200, 10.0);

        MetricsSnapshot snapshot = metrics.GetSnapshot();

        Assert.Equal(2, snapshot.Routes.Count);
    }
}
