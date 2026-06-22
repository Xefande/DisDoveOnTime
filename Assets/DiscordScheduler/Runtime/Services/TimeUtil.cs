using System;
using System.Globalization;

namespace DiscordScheduler
{
    public static class TimeUtil
    {
        // store ISO in UTC with 'Z'
        private const string IsoFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        public static string ToIsoUtc(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc)
                utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

            return utc.ToString(IsoFormat, CultureInfo.InvariantCulture);
        }

        public static DateTime ParseIsoUtc(string iso)
        {
            if (TryParseIsoUtc(iso, out var utc))
                return utc;

            return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        }

        public static bool TryParseIsoUtc(string iso, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrWhiteSpace(iso))
                return false;

            if (DateTime.TryParseExact(iso.Trim(), IsoFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                return true;
            }

            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
            {
                utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                return true;
            }

            return false;
        }

        public static TimeZoneInfo GetBudapestTimeZone()
        {
            // try IANA then Windows ID
            string[] ids = new[]
            {
                "Europe/Budapest",
                "Central Europe Standard Time",
                "W. Europe Standard Time"
            };

            foreach (var id in ids)
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(id);
                }
                catch { }
            }

            return TimeZoneInfo.Local;
        }

        public static DateTime LocalBudapestToUtc(string dateYmd, string timeHm, out string error)
        {
            if (TryLocalBudapestToUtc(dateYmd, timeHm, out var utc, out error))
                return utc;

            return DateTime.UtcNow;
        }

        public static bool TryLocalBudapestToUtc(string dateYmd, string timeHm, out DateTime utc, out string error)
        {
            utc = default;
            error = "";

            if (!DateTime.TryParseExact(dateYmd?.Trim() ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                error = "Invalid date format. Expected: YYYY-MM-DD";
                return false;
            }

            if (!DateTime.TryParseExact(timeHm?.Trim() ?? "", "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var time))
            {
                error = "Invalid time format. Expected: HH:MM";
                return false;
            }

            var local = new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
            var tz = GetBudapestTimeZone();

            if (tz.IsInvalidTime(local))
            {
                error = "Invalid local time in Budapest time zone.";
                return false;
            }

            if (tz.IsAmbiguousTime(local))
            {
                error = "Ambiguous local time in Budapest time zone.";
                return false;
            }

            utc = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(local, tz), DateTimeKind.Utc);
            return true;
        }

        public static (string dateYmd, string timeHm) UtcToBudapestFields(DateTime utc)
        {
            var tz = GetBudapestTimeZone();
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
            return (local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    local.ToString("HH:mm", CultureInfo.InvariantCulture));
        }
    }
}
