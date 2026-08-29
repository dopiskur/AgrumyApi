using System.Text.Json;
using api.Dal.Entities;

namespace api.Dal
{
    /// <summary>
    /// Pure port of the <c>SensorDataReportBuilder</c> stored procedure's shaping step (roadmap #42).
    /// The caller (<see cref="EfRepository.SensorDataGetAsync"/>) is responsible for the row filter
    /// - device, tenant, <c>Co2 &lt; 8000</c> and the time window - exactly as the proc's WHERE did;
    /// this class only does the time-bucket grouping and JSON assembly, so it needs no database and
    /// is unit-tested directly.
    ///
    /// Bucket granularity by <c>timeMDMY</c> matches the proc's <c>DATE_FORMAT</c> masks:
    ///   0 =&gt; minute, 1 =&gt; hour, 2 =&gt; day, 3 =&gt; day.
    /// One representative row per bucket. The proc relied on MySQL returning an arbitrary row from a
    /// non-aggregated <c>GROUP BY</c>; this port pins that to "latest by DateCreated" so the output
    /// is deterministic.
    /// </summary>
    internal static class SensorReportShaper
    {
        /// <returns>
        /// A JSON string <c>{"sensorData":[ {record}, ... ]}</c>, or an empty string when no rows
        /// match - mirroring the proc, whose <c>SELECT sensorDataResult</c> came back as SQL NULL
        /// (read as <c>""</c> by the old SqlRepository) when the grouped set was empty.
        /// </returns>
        public static string Build(IEnumerable<SensorDataRow> filteredRows, int timeMDMY)
        {
            var buckets = filteredRows
                .GroupBy(r => BucketKey(r.DateCreated, timeMDMY))
                .OrderBy(g => g.Key)
                .Select(g => g.OrderByDescending(r => r.DateCreated).First())
                .ToList();

            if (buckets.Count == 0)
            {
                return "";
            }

            var records = buckets.Select(r => new Dictionary<string, object?>
            {
                ["battery"] = r.Battery,
                ["temperature"] = r.Temperature,
                ["soilTemperature"] = r.SoilTemperature,
                ["humidity"] = r.Humidity,
                ["moisture"] = r.Moisture,
                ["light"] = r.Light,
                ["co2"] = r.Co2,
                ["tvoc"] = r.Tvoc,
                ["barometer"] = r.Barometer,
                ["liquidPH"] = r.LiquidPH,
                ["rainLevel"] = r.RainLevel,
                ["waterLevel"] = r.WaterLevel,
                ["wind"] = r.Wind,
                ["dateCreated"] = r.DateCreated?.ToString("yyyy-MM-dd HH:mm:ss"),
            });

            return JsonSerializer.Serialize(new Dictionary<string, object?> { ["sensorData"] = records });
        }

        /// <summary>Truncates a timestamp to the bucket boundary for the given range mode.</summary>
        private static DateTime BucketKey(DateTime? dt, int timeMDMY)
        {
            DateTime d = dt ?? DateTime.MinValue;
            return timeMDMY switch
            {
                0 => new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0),
                1 => new DateTime(d.Year, d.Month, d.Day, d.Hour, 0, 0),
                _ => new DateTime(d.Year, d.Month, d.Day, 0, 0, 0),
            };
        }
    }
}
