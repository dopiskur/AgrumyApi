using System.Text.Json;
using api.Dal.Entities;
using api.Utils;

namespace api.Dal
{
    /// <summary>
    /// Time-bucket grouping and JSON assembly only; the caller (<see cref="EfRepository.SensorDataGetAsync"/>)
    /// does the row filter. <c>timeMDMY</c> buckets: 0 =&gt; minute, 1 =&gt; hour, 2/3 =&gt; day. One row
    /// per bucket, pinned to "latest by DateCreated".
    /// </summary>
    internal static class SensorReportShaper
    {
        /// <returns>
        /// JSON <c>{"sensorData":[...]}</c>, or <c>""</c> when no rows match (the proc returned SQL NULL, read as <c>""</c>).
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
                ["vpd"] = VpdCalculator.Compute(r.Temperature, r.Humidity),
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

        /// <summary>Same bucketing as <see cref="Build"/>, but a zone/unit can span several devices - each
        /// bucket averages every contributing row instead of picking the single latest one. A metric only
        /// one device in scope reports averages over just that one value, which is the same number as
        /// showing it directly; nulls are excluded from the average rather than treated as zero.</summary>
        public static string BuildAveraged(IEnumerable<SensorDataRow> filteredRows, int timeMDMY)
        {
            var buckets = filteredRows
                .GroupBy(r => BucketKey(r.DateCreated, timeMDMY))
                .OrderBy(g => g.Key)
                .ToList();

            if (buckets.Count == 0)
            {
                return "";
            }

            var records = buckets.Select(g => new Dictionary<string, object?>
            {
                ["battery"] = g.Average(r => r.Battery),
                ["temperature"] = g.Average(r => r.Temperature),
                ["soilTemperature"] = g.Average(r => r.SoilTemperature),
                ["humidity"] = g.Average(r => r.Humidity),
                ["vpd"] = g.Average(r => VpdCalculator.Compute(r.Temperature, r.Humidity)),
                ["moisture"] = g.Average(r => r.Moisture),
                ["light"] = g.Average(r => r.Light),
                ["co2"] = g.Average(r => r.Co2),
                ["tvoc"] = g.Average(r => r.Tvoc),
                ["barometer"] = g.Average(r => r.Barometer),
                ["liquidPH"] = g.Average(r => r.LiquidPH),
                ["rainLevel"] = g.Average(r => r.RainLevel),
                ["waterLevel"] = g.Average(r => r.WaterLevel),
                ["wind"] = g.Average(r => r.Wind),
                ["dateCreated"] = g.Key.ToString("yyyy-MM-dd HH:mm:ss"),
            });

            return JsonSerializer.Serialize(new Dictionary<string, object?> { ["sensorData"] = records });
        }
    }
}
