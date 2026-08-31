using System.Globalization;
using System.Text.Json.Nodes;
using api.Dal.Entities;
using api.Models;
using Microsoft.EntityFrameworkCore;

namespace api.Dal
{
    /// <summary>ISensorDataRepository members (roadmap #74 split), plus the JSON value coercion
    /// helpers only the telemetry push uses (firmware sends measurements as strings or null).</summary>
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
                    // Identity is server-authoritative: it comes from the authenticated device, not
                    // the payload. The deviceID/tenantID/deviceUnitID/deviceUnitZoneID keys in each
                    // JSON row are deliberately ignored.
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
                    // Replaces the sensorData_SetDateTimeOnNull trigger: a missing/blank timestamp
                    // becomes "now". UTC, not local: device timestamps are UTC (roadmap #71).
                    DateCreated = dc ?? DateTime.UtcNow,
                });
            }

            if (rows.Count == 0)
            {
                return;
            }

            await using var db = Db();
            db.SensorData.AddRange(rows);
            await db.SaveChangesAsync();
        }

        public async Task<string> SensorDataGetAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY, int? buildReport)
        {
            if (timeMDMY is not (0 or 1 or 2 or 3) || timeRange == null)
            {
                return ""; // proc: ELSE branch / NULL interval -> SQL NULL -> read as ""
            }

            // UTC so the cutoff compares against UTC DateCreated without a DST-sized skew (roadmap #71).
            DateTime now = DateTime.UtcNow;
            DateTime cutoff = timeMDMY switch
            {
                0 => now.AddMinutes(-timeRange.Value),
                1 => now.AddDays(-timeRange.Value),
                2 => now.AddMonths(-timeRange.Value),
                _ => now.AddYears(-timeRange.Value),
            };

            await using var db = Db();
            var rows = await db.SensorData.AsNoTracking()
                .Where(r => r.DeviceID == deviceID
                            && r.TenantID == tenantID
                            && r.Co2 != null && r.Co2 < 8000   // matches SensorDataReportBuilder: NULL Co2 rows are excluded
                            && r.DateCreated > cutoff)
                .ToListAsync();

            string json = SensorReportShaper.Build(rows, timeMDMY.Value);

            if (json.Length > 0 && buildReport > 0)
            {
                // The proc hard-coded deviceID 1000038 here - a bug: every saved report was
                // attributed to one device. Save it against the device the report is actually for.
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
            await using var db = Db();

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

            return new List<SensorDataReport>(); // proc CASE has no matching WHEN and no ELSE
        }

        public async Task SensorDataDeleteAsync(int? tenantID, int? deviceID, int? timeRange, int? timeMDMY)
        {
            if (timeMDMY is not (0 or 1 or 2 or 3) || timeRange == null)
            {
                return; // proc CASE has no ELSE
            }

            // UTC so the delete cutoff compares against UTC DateCreated (roadmap #71).
            DateTime now = DateTime.UtcNow;
            DateTime cutoff = timeMDMY switch
            {
                0 => now.AddMinutes(-timeRange.Value),
                1 => now.AddDays(-timeRange.Value),
                2 => now.AddMonths(-timeRange.Value),
                _ => now.AddYears(-timeRange.Value),
            };

            await using var db = Db();
            await db.SensorData
                .Where(r => r.DeviceID == deviceID && r.TenantID == tenantID && r.DateCreated < cutoff)
                .ExecuteDeleteAsync();
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
