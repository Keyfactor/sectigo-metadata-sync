using System;
using System.Globalization;

public static class DateParser
{
    // Good coverage for ISO + your configured format + US date-only + RFC1123
    private static readonly string[] Formats =
    {
        "o", // ISO 8601 round-trip
        "yyyy-MM-dd'T'HH:mmK",
        "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd",
        "M/d/yyyy h:mm tt",
        "M/d/yyyy h:mm:ss tt",
        "M/d/yyyy",
        "r" // RFC1123
    };

    public static DateTimeOffset? ParseToUtcOrNull(
        string? raw,
        TimeZoneInfo assumeZone,
        string? preferredFormat = null)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // 1) If a preferred format is supplied (from config), try it first.
        if (!string.IsNullOrWhiteSpace(preferredFormat)
            && DateTime.TryParseExact(raw, preferredFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var dtPref))
            return AssumeZoneToUtc(dtPref, assumeZone);

        // 2) Try exact with known formats into DateTimeOffset (captures explicit offsets/UTC)
        if (DateTimeOffset.TryParseExact(raw, Formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var dtoExact))
            return dtoExact.ToUniversalTime();

        // 3) Try broad DateTimeOffset parse
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var dtoAny))
            return dtoAny.ToUniversalTime();

        // 4) If date-only, map to midnight in assumeZone
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dOnly))
        {
            var unspecified = new DateTime(dOnly.Year, dOnly.Month, dOnly.Day, 0, 0, 0, DateTimeKind.Unspecified);
            return AssumeZoneToUtc(unspecified, assumeZone);
        }

        return null;
    }

    private static DateTimeOffset AssumeZoneToUtc(DateTime dt, TimeZoneInfo zone)
    {
        // Treat Unspecified as ‘zone’; Local stays local; Utc stays Utc
        return dt.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(dt, TimeSpan.Zero),
            DateTimeKind.Local => new DateTimeOffset(dt).ToUniversalTime(),
            _ => new DateTimeOffset(dt, zone.GetUtcOffset(dt)).ToUniversalTime()
        };
    }
}