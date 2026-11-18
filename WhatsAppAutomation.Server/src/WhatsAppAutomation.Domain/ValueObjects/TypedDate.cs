using System.Globalization;

namespace WhatsAppAutomation.Domain.ValueObjects;

/// <summary>
/// A date as people type it: day first, then month, the year optional. "06-05", "06-05-02" and "06-05-2002" are
/// all 6 May; "/", "." and spaces also separate. A two-digit year is never in the future: 02 is 2002, 27 (in 2026) is
/// 1927, so it fits birthdays and memorial dates.
/// </summary>
public static class TypedDate
{
    /// <summary>Day, month and the year if typed; null when it is not a real date.</summary>
    public static (int Day, int Month, int? Year)? Parse(string? text, int thisYear)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Trim().Split(['-', '/', '.', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 2 or > 3 ||
            parts[0].Length > 2 || parts[1].Length > 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var day) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            month is < 1 or > 12 || day < 1)
        {
            return null;
        }

        int? year = null;
        if (parts.Length == 3)
        {
            if (parts[2].Length is not (2 or 4) || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var y))
            {
                return null;
            }

            if (parts[2].Length == 2)
            {
                var century = thisYear / 100 * 100;
                y = century + y > thisYear ? century - 100 + y : century + y;
            }

            if (y is < 1800 or > 2200)
            {
                return null;
            }

            year = y;
        }

        // Without a year, 29 February is allowed (a leap year is used to check it).
        return day <= DateTime.DaysInMonth(year ?? 2000, month) ? (day, month, year) : null;
    }
}
