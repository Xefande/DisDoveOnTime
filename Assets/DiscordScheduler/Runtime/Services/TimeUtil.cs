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
            if (string.IsNullOrWhiteSpace(iso))
                return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

            if (DateTime.TryParseExact(iso.Trim(), IsoFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }

            // fallback parse
            if (DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out dt))
            {
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }

            return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
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
            error = "";
            if (!DateTime.TryParseExact(dateYmd?.Trim() ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
            {
                error = "Hibás dátum formátum. Várt: YYYY-MM-DD";
                return DateTime.UtcNow;
            }

            if (!DateTime.TryParseExact(timeHm?.Trim() ?? "", "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var time))
            {
                error = "Hibás idő formátum. Várt: HH:MM (24 órás)";
                return DateTime.UtcNow;
            }

            var local = new DateTime(date.Year, date.Month, date.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
            var tz = GetBudapestTimeZone();
            var utc = TimeZoneInfo.ConvertTimeToUtc(local, tz);
            return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
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
