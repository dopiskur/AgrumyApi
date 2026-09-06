using System.Globalization;
using System.Text.Json.Nodes;
using api.Dal.Entities;
using api.Dal.Interface;
using api.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace api.Dal
{
    /// ISensorDataRepository, extracted out of the EfRepository god class (roadmap #246) - a pure leaf, no dependency on any other facet. Includes the JSON value coercion helpers the telemetry push uses (firmware sends measurements as strings or null).
    internal sealed class EfSensorDataRepository(AgrumyDbContext db) : ISensorDataRepository
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
                    // Identity is server-authoritative - the matching keys in the JSON payload are deliberately ignored.
                    DeviceID = deviceID,
                    TenantID = tenantID,
                    DeviceUnitID = deviceUnitID,
                    DeviceUnitZoneID = deviceUnitZoneID,
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

        // A caller-supplied (timeRange, timeMDMY) pair is otherwise unbounded - years/decades would load the device's entire history into memory for in-process aggregation. Chosen generously above any legitimate chart/report window.
        private const int MaxLookbackDays = 400;

        /// UTC so the cutoff compares against UTC DateCreated without a DST-sized skew; never further back than MaxLookbackDays regardless of what the caller asked for.
        private static DateTime ClampedCutoff(DateTime now, int timeRange, int timeMDMY)
        {
            DateTime requested = timeMDMY switch
            {
                0 => now.AddMinutes(-timeRange),
                1 => now.AddDays(-timeRange),
                2 => now.AddMonths(-timeRange),
                _ => now.AddYears(-timeRange),
            };
            DateTime hardFloor = now.AddDays(-MaxLookbackDays);
            return requested < hardFloor ? hardFloor : requested;
        }

        public async Task<string> SensorDataGetAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport)
        {
            if (timeMDMY is not (0 or 1 or 2 or 3) || timeRange == null)
            {
                return "";
            }

            DateTime now = DateTime.UtcNow;
            DateTime cutoff = ClampedCutoff(now, timeRange.Value, timeMDMY.Value);

            // A null tenantID means "no filter" here - a bare == would translate to SQL TenantID IS NULL and match nothing.
            var rows = await db.SensorData.AsNoTracking()
                .Where(r => r.DeviceID == deviceID
                            && (tenantID == null || r.TenantID == tenantID)
                            && r.DateCreated > cutoff)
                .ToListAsync();

            // CCS811 sentinel/outlier guard, CO2 column only - <=400 means "not warmed up yet" (not a real reading), >=8000 means a bad reading; nulling just this field leaves every other reading on the same row (temperature, humidity, ...) untouched, unlike excluding the whole row would.
            foreach (var row in rows)
            {
                if (row.Co2 is int co2 && (co2 <= 400 || co2 >= 8000))
                {
                    row.Co2 = null;
                }
            }

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

        public Task<string> SensorDataZoneAverageGetAsync(int? tenantID, int deviceUnitZoneID, int? timeRange, int? timeMDMY) =>
            AveragedSensorJsonAsync(tenantID, q => q.Where(r => r.DeviceUnitZoneID == deviceUnitZoneID), timeRange, timeMDMY);

        public Task<string> SensorDataUnitAverageGetAsync(int? tenantID, int deviceUnitID, int? timeRange, int? timeMDMY) =>
            AveragedSensorJsonAsync(tenantID, q => q.Where(r => r.DeviceUnitID == deviceUnitID), timeRange, timeMDMY);

        /// Shared by the zone/unit averaged-chart endpoints - same time-cutoff/bucket logic as SensorDataGetAsync, scoped by the caller's predicate instead of a single device, shaped by BuildAveraged instead of Build.
        private async Task<string> AveragedSensorJsonAsync(int? tenantID, Func<IQueryable<SensorDataRow>, IQueryable<SensorDataRow>> scope, int? timeRange, int? timeMDMY)
        {
            if (timeMDMY is not (0 or 1 or 2 or 3) || timeRange == null)
            {
                return "";
            }

            DateTime now = DateTime.UtcNow;
            DateTime cutoff = ClampedCutoff(now, timeRange.Value, timeMDMY.Value);

            IQueryable<SensorDataRow> baseQuery = db.SensorData.AsNoTracking()
                .Where(r => (tenantID == null || r.TenantID == tenantID) && r.DateCreated > cutoff);
            var rows = await scope(baseQuery).ToListAsync();

            return SensorReportShaper.BuildAveraged(rows, timeMDMY.Value);
        }

        public async Task<IList<SensorDataReport>> SensorDataReportGetAsync(int? tenantID, int? getData, int? deviceID, int? reportID)
        {

            if (getData == 0)
            {
                // deviceID null lists every report in scope (the Reporting page) instead of one device's own (the per-device Report tab) - tenantID keeps its usual null-means-no-filter meaning.
                return await (from r in db.SensorDataReports.AsNoTracking()
                              join d in db.Devices.AsNoTracking() on r.DeviceID equals d.IDDevice
                              where (deviceID == null || r.DeviceID == deviceID) && (tenantID == null || d.TenantID == tenantID)
                              orderby r.DateGenerated descending
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
                              where r.IDSensorDataReport == reportID && (tenantID == null || d.TenantID == tenantID)
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
            // Per-device, not one giant query - bounds each transaction's row count and lets a mid-run failure leave already-processed devices genuinely optimized instead of rolling everything back.
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

                // Delete-then-insert in one transaction - a crash between the two would otherwise duplicate or silently lose the bucket.
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
                    // TimescaleDB extension not installed - sensorData is a plain table here (like MariaDB, minus the OPTIMIZE-TABLE shrink step below).
                    isHypertable = false;
                }

                if (isHypertable)
                {
                    // drop_chunks deletes whole chunk files (space returned immediately, unlike DELETE) - the embedded double-quotes keep the regclass cast from lowercasing this mixed-case table name.
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"""SELECT drop_chunks('"sensorData"'::regclass, older_than => {cutoffUtc});""", ct);
                    return;
                }

                await db.SensorData.Where(r => r.DateCreated < cutoffUtc).ExecuteDeleteAsync(ct);
                return;
            }

            // A single unbatched DELETE across a multi-million-row table holds its lock for the whole run - MySQL supports DELETE...LIMIT natively (EF's ExecuteDeleteAsync can't express it), so loop in PurgeBatchSize chunks with a short pause between them instead.
            int deletedRows;
            do
            {
                deletedRows = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM `sensorData` WHERE `DateCreated` < {cutoffUtc} LIMIT {PurgeBatchSize}", ct);
                if (deletedRows == PurgeBatchSize)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
                }
            } while (deletedRows == PurgeBatchSize);

            if (shrinkAfterPurge)
            {
                // InnoDB never shrinks its .ibd file on a plain DELETE - OPTIMIZE TABLE is the locking rebuild that actually returns space, only run when the admin opts in since it can take a long time.
                await db.Database.ExecuteSqlRawAsync("OPTIMIZE TABLE `sensorData`;", ct);
            }
        }

        private const int PurgeBatchSize = 10_000;

        private static DateTime BucketStart(DateTime timestamp) =>
            new(timestamp.Ticks - (timestamp.Ticks % OptimizeBucketSize.Ticks), DateTimeKind.Utc);

        /// One replacement row for a 5-minute bucket: TenantID/DeviceUnitID/DeviceUnitZoneID come from the most recent raw row, every sensor column is the average-without-outliers of that bucket's values.
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

        /// IQR outlier rule (exclude anything outside 1.5x the interquartile range), falling back to a plain average under 4 points or when every value is flagged.
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

        public async Task<IList<SensorData>> SensorDataExportGetAsync(int tenantID, DateTime? sinceUtc)
        {
            // Dirty reads are fine for an export snapshot - avoids InnoDB gap-locking a live device's concurrent SensorDataPushAsync inserts on what can be a huge table (Postgres treats this as ReadCommitted regardless, MVCC readers never block writers there anyway).
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadUncommitted);

            IQueryable<SensorDataRow> q = db.SensorData.AsNoTracking().Where(s => s.TenantID == tenantID);
            if (sinceUtc is DateTime since)
            {
                q = q.Where(s => s.DateCreated >= since);
            }
            return await q.Select(s => new SensorData
            {
                TenantID = s.TenantID,
                DeviceID = s.DeviceID,
                DeviceUnitID = s.DeviceUnitID,
                DeviceUnitZoneID = s.DeviceUnitZoneID,
                Battery = s.Battery,
                Temperature = s.Temperature,
                SoilTemperature = s.SoilTemperature,
                Humidity = s.Humidity,
                Moisture = s.Moisture,
                Light = s.Light,
                Co2 = s.Co2,
                Tvoc = s.Tvoc,
                Barometer = s.Barometer,
                LiquidPH = s.LiquidPH,
                RainLevel = s.RainLevel,
                WaterLevel = s.WaterLevel,
                Wind = s.Wind,
                DateCreated = s.DateCreated ?? default,
            }).ToListAsync();
        }

        public async Task SensorDataImportAsync(IList<SensorData> rows)
        {
            db.SensorData.AddRange(rows.Select(r => new SensorDataRow
            {
                TenantID = r.TenantID ?? 0,
                DeviceID = r.DeviceID ?? 0,
                DeviceUnitID = r.DeviceUnitID,
                DeviceUnitZoneID = r.DeviceUnitZoneID,
                Battery = r.Battery,
                Temperature = r.Temperature,
                SoilTemperature = r.SoilTemperature,
                Humidity = r.Humidity,
                Moisture = r.Moisture,
                Light = r.Light,
                Co2 = r.Co2,
                Tvoc = r.Tvoc,
                Barometer = r.Barometer,
                LiquidPH = r.LiquidPH,
                RainLevel = r.RainLevel,
                WaterLevel = r.WaterLevel,
                Wind = (int?)r.Wind, // SensorDataRow.Wind is int, api.Models.SensorData.Wind is double
                DateCreated = r.DateCreated,
            }));
            await db.SaveChangesAsync();
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
