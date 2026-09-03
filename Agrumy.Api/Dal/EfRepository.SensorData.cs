using System.Globalization;
using System.Text.Json.Nodes;
using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace api.Dal
{
    /// <summary>ISensorDataRepository members, plus the JSON value coercion helpers only the
    /// telemetry push uses (firmware sends measurements as strings or null).</summary>
    internal partial class EfRepository
    {
        public async Task SensorDataPushAsync(JsonArray jsonArray, int deviceID, int tenantID, int? deviceUnitID, int? deviceUnitZoneID)
        {
            var rows = new List<SensorDataRow>();
            foreach (var node in jsonArray)
            {
                if (node is not JsonObject o)
                {
                    continue;
                }

                DateTime? dc = ReadDateTime(o, "dateCreated");
                rows.Add(new SensorDataRow
                {
                    // Identity is server-authoritative (from the authenticated device) - the
                    // deviceID/tenantID/deviceUnitID/deviceUnitZoneID keys in the JSON payload are
                    // deliberately ignored.
                    DeviceID = deviceID,
                    TenantID = tenantID,
                    DeviceUnitID = deviceUnitID ?? 0,
                    DeviceUnitZoneID = deviceUnitZoneID ?? 0,
                    Battery = ReadInt(o, "battery"),
                    Temperature = ReadDouble(o, "temperature"),
                    SoilTemperature = ReadDouble(o, "soilTemperature"),
                    Humidity = ReadDouble(o, "humidity"),
                    Moisture = ReadInt(o, "moisture"),
                    Light = ReadInt(o, "light"),
                    Co2 = ReadInt(o, "co2"),
                    Tvoc = ReadInt(o, "tvoc"),
                    Barometer = ReadDouble(o, "barometer"),
                    LiquidPH = ReadDouble(o, "liquidPH"),
                    RainLevel = ReadInt(o, "rainLevel"),
                    WaterLevel = ReadInt(o, "waterLevel"),
                    Wind = ReadInt(o, "wind"),
                    // A missing/blank timestamp becomes "now" (UTC - device timestamps are UTC).
                    DateCreated = dc ?? DateTime.UtcNow,
                });
            }

            if (rows.Count == 0)
            {
                return;
            }

            db.SensorData.AddRange(rows);
            await db.SaveChangesAsync();
        }

        public async Task<string> SensorDataGetAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport)
        {
            if (timeMDMY is not (0 or 1 or 2 or 3) || timeRange == null)
            {
                return "";
            }

            // UTC so the cutoff compares against UTC DateCreated without a DST-sized skew.
            DateTime now = DateTime.UtcNow;
            DateTime cutoff = timeMDMY switch
            {
                0 => now.AddMinutes(-timeRange.Value),
                1 => now.AddDays(-timeRange.Value),
                2 => now.AddMonths(-timeRange.Value),
                _ => now.AddYears(-timeRange.Value),
            };

            var rows = await db.SensorData.AsNoTracking()
                .Where(r => r.DeviceID == deviceID
                            && r.TenantID == tenantID
                            && r.Co2 != null && r.Co2 < 8000   // matches SensorDataReportBuilder: NULL Co2 rows are excluded
                            && r.DateCreated > cutoff)
                .ToListAsync();

            string json = SensorReportShaper.Build(rows, timeMDMY.Value);

            if (json.Length > 0 && buildReport > 0)
            {
                db.SensorDataReports.Add(new SensorDataReportRow
                {
                    DeviceID = deviceID,
                    ReportName = now.ToString("yyyy-MM-dd HH:mm:ss"),
                    SensorData = json,
                });
                await db.SaveChangesAsync();
            }

            return json;
        }

        public async Task<IList<SensorDataReport>> SensorDataReportGetAsync(int? tenantID, int? getData, int? deviceID, int? reportID)
        {

            if (getData == 0)
            {
                return await (from r in db.SensorDataReports.AsNoTracking()
                              join d in db.Devices.AsNoTracking() on r.DeviceID equals d.IDDevice
                              where r.DeviceID == deviceID && d.TenantID == tenantID
                              select new SensorDataReport
                              {
                                  IDSensorDataReport = r.IDSensorDataReport,
                                  DeviceID = r.DeviceID,
                                  ReportName = r.ReportName,
                                  DateGenerated = r.DateGenerated,
                              }).ToListAsync();
            }

            if (getData > 0)
            {
                return await (from r in db.SensorDataReports.AsNoTracking()
                              join d in db.Devices.AsNoTracking() on r.DeviceID equals d.IDDevice
                              where r.IDSensorDataReport == reportID && d.TenantID == tenantID
                              select new SensorDataReport
                              {
                                  IDSensorDataReport = r.IDSensorDataReport,
                                  DeviceID = r.DeviceID,
                                  ReportName = r.ReportName,
                                  DateGenerated = r.DateGenerated,
                                  SensorData = r.SensorData,
                              }).ToListAsync();
            }

            return new List<SensorDataReport>();
        }

        public async Task SensorDataDeleteAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY)
        {
            if (timeMDMY is not (0 or 1 or 2 or 3) || timeRange == null)
            {
                return;
            }

            // UTC so the delete cutoff compares against UTC DateCreated.
            DateTime now = DateTime.UtcNow;
            DateTime cutoff = timeMDMY switch
            {
                0 => now.AddMinutes(-timeRange.Value),
                1 => now.AddDays(-timeRange.Value),
                2 => now.AddMonths(-timeRange.Value),
                _ => now.AddYears(-timeRange.Value),
            };

            await db.SensorData
                .Where(r => r.DeviceID == deviceID && r.TenantID == tenantID && r.DateCreated < cutoff)
                .ExecuteDeleteAsync();
        }

        private static readonly TimeSpan OptimizeBucketSize = TimeSpan.FromMinutes(5);

        public async Task OptimizeOldSensorDataAsync(DateTime cutoffUtc, CancellationToken ct)
        {
            // Per-device, not one giant query - bounds each transaction's row count and lets a
            // failure partway through leave already-processed devices genuinely optimized instead
            // of rolling back the entire run.
            List<int> deviceIds = await db.SensorData.AsNoTracking()
                .Where(r => r.DateCreated < cutoffUtc)
                .Select(r => r.DeviceID)
                .Distinct()
                .ToListAsync(ct);

            foreach (int deviceId in deviceIds)
            {
                ct.ThrowIfCancellationRequested();

                List<SensorDataRow> rows = await db.SensorData.AsNoTracking()
                    .Where(r => r.DeviceID == deviceId && r.DateCreated < cutoffUtc && r.DateCreated != null)
                    .OrderBy(r => r.DateCreated)
                    .ToListAsync(ct);

                if (rows.Count == 0)
                {
                    continue;
                }

                List<SensorDataRow> replacements = rows
                    .GroupBy(r => BucketStart(r.DateCreated!.Value))
                    .Select(bucket => BuildOptimizedRow(deviceId, bucket.Key, bucket.ToList()))
                    .ToList();

                // Delete-then-insert in one transaction - a crash between the two would otherwise
                // duplicate or silently lose the bucket.
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await db.SensorData
                    .Where(r => r.DeviceID == deviceId && r.DateCreated < cutoffUtc)
                    .ExecuteDeleteAsync(ct);
                db.SensorData.AddRange(replacements);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
        }

        public async Task PurgeOldSensorDataAsync(DateTime cutoffUtc, bool shrinkAfterPurge, CancellationToken ct)
        {
            if (db.Database.IsNpgsql())
            {
                bool isHypertable;
                try
                {
                    isHypertable = await db.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*)::int FROM timescaledb_information.hypertables WHERE hypertable_name = 'sensorData'")
                        .FirstAsync(ct) > 0;
                }
                catch (PostgresException)
                {
                    // TimescaleDB extension not installed - sensorData is a plain table here (like
                    // MariaDB, minus the OPTIMIZE-TABLE shrink step below).
                    isHypertable = false;
                }

                if (isHypertable)
                {
                    // drop_chunks deletes whole chunk files (space returned immediately, unlike
                    // DELETE below) - the embedded double-quotes in the regclass literal keep the
                    // cast from lowercasing this mixed-case table name.
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"""SELECT drop_chunks('"sensorData"'::regclass, older_than => {cutoffUtc});""", ct);
                    return;
                }

                await db.SensorData.Where(r => r.DateCreated < cutoffUtc).ExecuteDeleteAsync(ct);
                return;
            }

            await db.SensorData.Where(r => r.DateCreated < cutoffUtc).ExecuteDeleteAsync(ct);
            if (shrinkAfterPurge)
            {
                // InnoDB never shrinks its .ibd file on a plain DELETE - OPTIMIZE TABLE is the
                // locking rebuild that actually returns space to the OS; only run when the admin
                // explicitly opts in since it can take a long time on a large table.
                await db.Database.ExecuteSqlRawAsync("OPTIMIZE TABLE `sensorData`;", ct);
            }
        }

        private static DateTime BucketStart(DateTime timestamp) =>
            new(timestamp.Ticks - (timestamp.Ticks % OptimizeBucketSize.Ticks), DateTimeKind.Utc);

        /// <summary>One replacement row for a 5-minute bucket: TenantID/DeviceUnitID/DeviceUnitZoneID
        /// come from the bucket's most recent raw row, every sensor column is the
        /// average-without-outliers of whatever raw values that bucket has (nulls excluded).</summary>
        private static SensorDataRow BuildOptimizedRow(int deviceId, DateTime bucketStart, List<SensorDataRow> rows)
        {
            SensorDataRow mostRecent = rows[^1]; // rows arrive pre-sorted by DateCreated ascending
            return new SensorDataRow
            {
                TenantID = mostRecent.TenantID,
                DeviceID = deviceId,
                DeviceUnitID = mostRecent.DeviceUnitID,
                DeviceUnitZoneID = mostRecent.DeviceUnitZoneID,
                Battery = TrimmedMeanInt(rows.Select(r => r.Battery)),
                Temperature = TrimmedMean(rows.Select(r => r.Temperature)),
                SoilTemperature = TrimmedMean(rows.Select(r => r.SoilTemperature)),
                Humidity = TrimmedMean(rows.Select(r => r.Humidity)),
                Moisture = TrimmedMeanInt(rows.Select(r => r.Moisture)),
                Light = TrimmedMeanInt(rows.Select(r => r.Light)),
                Co2 = TrimmedMeanInt(rows.Select(r => r.Co2)),
                Tvoc = TrimmedMeanInt(rows.Select(r => r.Tvoc)),
                Barometer = TrimmedMean(rows.Select(r => r.Barometer)),
                LiquidPH = TrimmedMean(rows.Select(r => r.LiquidPH)),
                RainLevel = TrimmedMeanInt(rows.Select(r => r.RainLevel)),
                WaterLevel = TrimmedMeanInt(rows.Select(r => r.WaterLevel)),
                Wind = TrimmedMeanInt(rows.Select(r => r.Wind)),
                DateCreated = bucketStart,
            };
        }

        /// <summary>IQR outlier rule (exclude anything outside 1.5x the interquartile range), falling
        /// back to a plain average under 4 points or when every value gets flagged as an outlier.</summary>
        private static double? TrimmedMean(IEnumerable<double?> source)
        {
            List<double> values = source.Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v).ToList();
            if (values.Count == 0)
            {
                return null;
            }
            if (values.Count < 4)
            {
                return values.Average();
            }

            double q1 = Percentile(values, 0.25);
            double q3 = Percentile(values, 0.75);
            double iqr = q3 - q1;
            double lower = q1 - 1.5 * iqr;
            double upper = q3 + 1.5 * iqr;
            List<double> kept = values.Where(v => v >= lower && v <= upper).ToList();
            return kept.Count > 0 ? kept.Average() : values.Average();
        }

        private static int? TrimmedMeanInt(IEnumerable<int?> source)
        {
            double? mean = TrimmedMean(source.Select(v => (double?)v));
            return mean.HasValue ? (int)Math.Round(mean.Value, MidpointRounding.AwayFromZero) : null;
        }

        private static double Percentile(List<double> sortedValues, double p)
        {
            double index = p * (sortedValues.Count - 1);
            int lower = (int)Math.Floor(index);
            int upper = (int)Math.Ceiling(index);
            if (lower == upper)
            {
                return sortedValues[lower];
            }
            double fraction = index - lower;
            return sortedValues[lower] + (sortedValues[upper] - sortedValues[lower]) * fraction;
        }

        // ---- JSON value coercion (firmware sends measurements as strings or null) --------

        private static int? ReadInt(JsonObject o, string key)
        {
            if (!o.TryGetPropertyValue(key, out var n) || n is not JsonValue v)
            {
                return null;
            }
            if (v.TryGetValue(out int i)) return i;
            if (v.TryGetValue(out long l)) return (int)l;
            if (v.TryGetValue(out double d)) return (int)d;
            if (v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s))
            {
                if (int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var si)) return si;
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var sd)) return (int)sd;
            }
            return null;
        }

        private static double? ReadDouble(JsonObject o, string key)
        {
            if (!o.TryGetPropertyValue(key, out var n) || n is not JsonValue v)
            {
                return null;
            }
            if (v.TryGetValue(out double d)) return d;
            if (v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s)
                && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var sd)) return sd;
            return null;
        }

        private static DateTime? ReadDateTime(JsonObject o, string key)
        {
            if (!o.TryGetPropertyValue(key, out var n) || n is not JsonValue v)
            {
                return null;
            }
            if (v.TryGetValue(out DateTime dt)) return dt;
            if (v.TryGetValue(out string? s) && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var sd))
            {
                return sd;
            }
            return null;
        }
    }
}
