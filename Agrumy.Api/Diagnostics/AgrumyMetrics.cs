using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace api.Diagnostics
{
    public sealed record RouteMetricsSnapshot(string Route, string Method, long RequestCount, long ErrorCount, double AvgDurationMs, double MinDurationMs, double MaxDurationMs);

    public sealed record MetricsSnapshot(DateTimeOffset GeneratedAt, IReadOnlyList<RouteMetricsSnapshot> Routes);

    /// <summary>Roadmap #143. Emits through a real <see cref="Meter"/> so any future OpenTelemetry
    /// exporter can attach to it (meter name "Agrumy.Api") with no application-code change - same
    /// "swap the backend later" shape as #72/#119's cache abstraction. The in-memory
    /// per-route/method aggregate below is the "basic emission ... without an external package" half
    /// of #143: it is what GET /metrics actually reads, independent of whether anything is listening
    /// to the Meter.</summary>
    public sealed class AgrumyMetrics
    {
        public const string MeterName = "Agrumy.Api";

        private readonly Counter<long> requestCounter;
        private readonly Histogram<double> requestDuration;
        private readonly ConcurrentDictionary<(string Route, string Method), RouteStat> stats = new();

        public AgrumyMetrics()
        {
            var meter = new Meter(MeterName, "1.0");
            requestCounter = meter.CreateCounter<long>("agrumy.api.requests", unit: "{request}",
                description: "HTTP requests handled, tagged by route/method/status_code.");
            requestDuration = meter.CreateHistogram<double>("agrumy.api.request.duration", unit: "ms",
                description: "HTTP request duration, tagged by route/method.");
        }

        public void RecordRequest(string route, string method, int statusCode, double elapsedMs)
        {
            requestCounter.Add(1,
                new KeyValuePair<string, object?>("route", route),
                new KeyValuePair<string, object?>("method", method),
                new KeyValuePair<string, object?>("status_code", statusCode));
            requestDuration.Record(elapsedMs,
                new KeyValuePair<string, object?>("route", route),
                new KeyValuePair<string, object?>("method", method));

            stats.GetOrAdd((route, method), static _ => new RouteStat())
                .Record(elapsedMs, statusCode >= 500);
        }

        public MetricsSnapshot GetSnapshot()
        {
            var routes = stats
                .Select(kv => kv.Value.ToSnapshot(kv.Key.Route, kv.Key.Method))
                .OrderByDescending(r => r.RequestCount)
                .ToList();
            return new MetricsSnapshot(DateTimeOffset.UtcNow, routes);
        }

        // Plain lock, not Interlocked-per-field: min/max/avg must reflect one consistent snapshot of
        // count+total together (interleaved lock-free field updates could produce e.g. a count that
        // doesn't match total, throwing avg off) - request volume here doesn't warrant a lock-free design.
        private sealed class RouteStat
        {
            private readonly object gate = new();
            private long count;
            private long errorCount;
            private double totalMs;
            private double minMs = double.MaxValue;
            private double maxMs;

            public void Record(double elapsedMs, bool isError)
            {
                lock (gate)
                {
                    count++;
                    if (isError) errorCount++;
                    totalMs += elapsedMs;
                    if (elapsedMs < minMs) minMs = elapsedMs;
                    if (elapsedMs > maxMs) maxMs = elapsedMs;
                }
            }

            public RouteMetricsSnapshot ToSnapshot(string route, string method)
            {
                lock (gate)
                {
                    double avg = count == 0 ? 0 : totalMs / count;
                    double min = count == 0 ? 0 : minMs;
                    return new RouteMetricsSnapshot(route, method, count, errorCount,
                        Math.Round(avg, 2), Math.Round(min, 2), Math.Round(maxMs, 2));
                }
            }
        }
    }
}
