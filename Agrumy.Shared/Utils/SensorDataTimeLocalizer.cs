using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace api.Utils
{
    /// <summary>Display-side companion to TimeZoneHelper for the SensorData JSON payload: dateCreated values are rewritten to the user's zone before the page renders, while the database and API payload stay UTC-only.</summary>
    public static class SensorDataTimeLocalizer
    {
        // Must match SensorReportShaper's dateCreated output format exactly.
        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

        /// <summary>No zone, empty payload, or malformed JSON all return the input untouched — a chart falling back to UTC labels beats a broken page.</summary>
        public static string? LocalizeDates(string? sensorDataJson, string? userTimeZoneId)
        {
            if (string.IsNullOrWhiteSpace(sensorDataJson) || string.IsNullOrWhiteSpace(userTimeZoneId))
            {
                return sensorDataJson;
            }

            try
            {
                JsonNode? root = JsonNode.Parse(sensorDataJson);
                if (root?["sensorData"] is not JsonArray records)
                {
                    return sensorDataJson;
                }

                foreach (JsonNode? record in records)
                {
                    if (record?["dateCreated"] is JsonValue v
                        && v.TryGetValue(out string? raw)
                        && DateTime.TryParseExact(raw, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime utc))
                    {
                        record["dateCreated"] = TimeZoneHelper.ToUserLocalTime(utc, userTimeZoneId)
                            .ToString(DateFormat, CultureInfo.InvariantCulture);
                    }
                }
                return root.ToJsonString();
            }
            catch (JsonException)
            {
                return sensorDataJson;
            }
        }
    }
}
