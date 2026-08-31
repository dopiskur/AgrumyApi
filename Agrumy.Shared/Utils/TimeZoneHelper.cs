namespace api.Utils
{
    /// <summary>UTC-to-user-local display conversion for the stored-UTC-only policy (roadmap #71):
    /// the database keeps every timestamp in UTC and only the presentation layer converts, using the
    /// IANA zone id from user.TimeZone - IANA ids (not offsets) so DST resolves correctly via
    /// TimeZoneInfo on both Windows dev boxes and the Linux server.</summary>
    public static class TimeZoneHelper
    {
        /// <summary>Null/empty/unknown zone id falls back to returning the UTC input unchanged -
        /// a stale or corrupt stored id must degrade to UTC display, never throw at render time.</summary>
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

        /// <summary>Validates a caller-supplied zone id and normalizes it to IANA form, so a Windows
        /// id picked from a Windows-hosted dropdown still stores as "Europe/Zagreb" - the DB holds
        /// one canonical format regardless of which OS served the page.</summary>
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

        /// <summary>Dropdown source: (IANA id, display name) pairs from the host OS's zone catalog,
        /// deduplicated by IANA id because several Windows zones map onto one IANA zone.</summary>
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
