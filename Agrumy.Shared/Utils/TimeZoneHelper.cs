namespace api.Utils
{
    /// UTC-to-user-local display conversion: the database keeps every timestamp in UTC and only the presentation layer converts, using the IANA zone id from user.TimeZone so DST resolves correctly on both Windows and Linux.
    public static class TimeZoneHelper
    {
        /// Null/empty/unknown zone id falls back to returning the UTC input unchanged — a stale or corrupt stored id must degrade to UTC display, never throw at render time.
        public static DateTime ToUserLocalTime(DateTime utcDateTime, string? userTimeZoneId)
        {
            if (string.IsNullOrWhiteSpace(userTimeZoneId))
            {
                return utcDateTime;
            }

            try
            {
                TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(userTimeZoneId);
                // DB reads come back DateTimeKind.Unspecified; ConvertTimeFromUtc rejects Kind.Local.
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc), tz);
            }
            catch (TimeZoneNotFoundException)
            {
                return utcDateTime;
            }
            catch (InvalidTimeZoneException)
            {
                return utcDateTime;
            }
        }

        /// Plain integer offset for the device-side half of schedule-mode relay control - an ESP32 has no timezone database, unlike an IANA id. Null/unknown zone falls back to 0 (UTC).
        public static int GetUtcOffsetSeconds(DateTime utcNow, string? timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId))
            {
                return 0;
            }

            try
            {
                TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return (int)tz.GetUtcOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).TotalSeconds;
            }
            catch (TimeZoneNotFoundException)
            {
                return 0;
            }
            catch (InvalidTimeZoneException)
            {
                return 0;
            }
        }

        /// Validates a caller-supplied zone id and normalizes it to IANA form, so a Windows id still stores as "Europe/Zagreb" - the DB holds one canonical format regardless of which OS served the page.
        public static bool TryNormalizeToIana(string? timeZoneId, out string ianaId)
        {
            ianaId = "";
            if (string.IsNullOrWhiteSpace(timeZoneId) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out TimeZoneInfo? tz))
            {
                return false;
            }

            ianaId = tz.HasIanaId
                ? tz.Id
                : TimeZoneInfo.TryConvertWindowsIdToIanaId(tz.Id, out string? converted) ? converted : tz.Id;
            return true;
        }

        /// Dropdown source: (IANA id, display name) pairs, deduplicated by IANA id because several Windows zones map onto one IANA zone.
        public static IReadOnlyList<(string Id, string DisplayName)> GetTimeZoneOptions()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var options = new List<(string Id, string DisplayName, TimeSpan Offset)>();
            foreach (TimeZoneInfo tz in TimeZoneInfo.GetSystemTimeZones())
            {
                if (TryNormalizeToIana(tz.Id, out string iana) && seen.Add(iana))
                {
                    options.Add((iana, tz.DisplayName, tz.BaseUtcOffset));
                }
            }
            return options
                .OrderBy(o => o.Offset)
                .ThenBy(o => o.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(o => (o.Id, o.DisplayName))
                .ToList();
        }
    }
}
