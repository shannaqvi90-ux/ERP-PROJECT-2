using System.Globalization;

namespace Erp.Modules.Jobs.Engine;

/// <summary>
/// A cron timing of five fields: minute (0-59), hour (0-23), day of month (1-31), month (1-12 or
/// jan-dec) and day of week (0-7 or sun-sat; 0 and 7 are Sunday). Each field is <c>*</c>, a number, a
/// range <c>a-b</c>, a list <c>a,b-c</c>, with an optional step <c>/n</c> (<c>*/15</c>, <c>10-40/5</c>).
/// When both day fields are restricted a day matches if either does (classic cron). Evaluated in a
/// time zone: a time that does not exist (the hour skipped when clocks go forward) is skipped, a time
/// that exists twice runs once.
/// </summary>
public sealed class CronSchedule
{
    private static readonly System.Collections.Immutable.ImmutableArray<string> MonthNames = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
    private static readonly System.Collections.Immutable.ImmutableArray<string> DayNames = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    private readonly bool[] _minutes;
    private readonly bool[] _hours;
    private readonly bool[] _daysOfMonth;
    private readonly bool[] _months;
    private readonly bool[] _daysOfWeek;
    private readonly bool _anyDayOfMonth;
    private readonly bool _anyDayOfWeek;

    private CronSchedule(bool[] minutes, bool[] hours, bool[] daysOfMonth, bool[] months, bool[] daysOfWeek, bool anyDayOfMonth, bool anyDayOfWeek)
    {
        _minutes = minutes;
        _hours = hours;
        _daysOfMonth = daysOfMonth;
        _months = months;
        _daysOfWeek = daysOfWeek;
        _anyDayOfMonth = anyDayOfMonth;
        _anyDayOfWeek = anyDayOfWeek;
    }

    /// <summary>The timing does not have five fields.</summary>
    public const string ErrorFields = "cronFields";

    /// <summary>A field is not valid.</summary>
    public const string ErrorField = "cronField";

    /// <summary>The timing never happens.</summary>
    public const string ErrorNever = "cronNever";

    /// <summary>Longest timing text the module keeps.</summary>
    public const int MaxLength = 100;

    /// <summary>Parse a timing, or return the code of what is wrong with it (<c>cronFields</c>,
    /// <c>cronField</c>, <c>cronNever</c>).</summary>
    public static bool TryParse(string? text, out CronSchedule? schedule, out string? error)
    {
        schedule = null;
        error = null;
        var parts = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 5 || (text ?? "").Length > MaxLength)
        {
            error = ErrorFields;
            return false;
        }
        if (!Field(parts[0], 0, 59, default, out var minutes, out _)
            || !Field(parts[1], 0, 23, default, out var hours, out _)
            || !Field(parts[2], 1, 31, default, out var daysOfMonth, out var anyDom)
            || !Field(parts[3], 1, 12, MonthNames, out var months, out _, nameBase: 1)
            || !Field(parts[4], 0, 7, DayNames, out var daysOfWeek, out var anyDow))
        {
            error = ErrorField;
            return false;
        }
        // 7 is Sunday as well.
        daysOfWeek[0] |= daysOfWeek[7];
        var built = new CronSchedule(minutes, hours, daysOfMonth, months, daysOfWeek, anyDom, anyDow);
        if (!built.CanFire())
        {
            error = ErrorNever;
            return false;
        }
        schedule = built;
        return true;
    }

    /// <summary>Parse a timing that is known to be valid.</summary>
    public static CronSchedule Parse(string text) =>
        TryParse(text, out var schedule, out var error) ? schedule! : throw new FormatException($"'{text}' is not a cron timing ({error}).");

    /// <summary>The first moment strictly after <paramref name="after"/> the timing fires, in
    /// <paramref name="zone"/>, or null when it never does within five years.</summary>
    public DateTimeOffset? NextAfter(DateTimeOffset after, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(after, zone).DateTime;
        // Start at the next whole minute of local time.
        var candidate = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified).AddMinutes(1);
        var limit = candidate.AddYears(5);
        while (candidate < limit)
        {
            if (!_months[candidate.Month])
            {
                candidate = new DateTime(candidate.Year, candidate.Month, 1).AddMonths(1);
                continue;
            }
            if (!DayMatches(candidate))
            {
                candidate = candidate.Date.AddDays(1);
                continue;
            }
            if (!_hours[candidate.Hour])
            {
                candidate = new DateTime(candidate.Year, candidate.Month, candidate.Day, candidate.Hour, 0, 0).AddHours(1);
                continue;
            }
            if (!_minutes[candidate.Minute])
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }
            if (zone.IsInvalidTime(candidate))
            {
                candidate = candidate.AddMinutes(1);
                continue;
            }
            var offset = zone.IsAmbiguousTime(candidate) ? zone.GetAmbiguousTimeOffsets(candidate).Max() : zone.GetUtcOffset(candidate);
            var moment = new DateTimeOffset(candidate, offset).ToUniversalTime();
            if (moment > after)
            {
                return moment;
            }
            candidate = candidate.AddMinutes(1);
        }
        return null;
    }

    private bool DayMatches(DateTime day)
    {
        var dom = _daysOfMonth[day.Day];
        var dow = _daysOfWeek[(int)day.DayOfWeek];
        if (_anyDayOfMonth && _anyDayOfWeek)
        {
            return true;
        }
        if (_anyDayOfMonth)
        {
            return dow;
        }
        if (_anyDayOfWeek)
        {
            return dom;
        }
        return dom || dow;
    }

    private bool CanFire()
    {
        // Some month has a day the day fields allow (31 February never exists).
        for (var month = 1; month <= 12; month++)
        {
            if (!_months[month])
            {
                continue;
            }
            for (var day = 1; day <= DateTime.DaysInMonth(2024, month); day++)
            {
                if (DayMatches(new DateTime(2024, month, day)))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool Field(string text, int min, int max, System.Collections.Immutable.ImmutableArray<string> names, out bool[] values, out bool any, int nameBase = 0)
    {
        values = new bool[max + 1];
        any = text == "*";
        foreach (var item in text.Split(','))
        {
            var step = 1;
            var range = item;
            var slash = item.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0)
            {
                if (!int.TryParse(item[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out step) || step < 1)
                {
                    return false;
                }
                range = item[..slash];
            }
            int from, to;
            if (range == "*")
            {
                from = min;
                to = max == 7 ? 6 : max;
            }
            else
            {
                var dash = range.IndexOf('-', StringComparison.Ordinal);
                if (dash < 0)
                {
                    if (!Number(range, min, max, names, nameBase, out from))
                    {
                        return false;
                    }
                    to = slash >= 0 ? max : from;
                }
                else if (!Number(range[..dash], min, max, names, nameBase, out from) || !Number(range[(dash + 1)..], min, max, names, nameBase, out to) || from > to)
                {
                    return false;
                }
            }
            for (var v = from; v <= to; v += step)
            {
                values[v] = true;
            }
        }
        return true;
    }

    private static bool Number(string text, int min, int max, System.Collections.Immutable.ImmutableArray<string> names, int nameBase, out int value)
    {
        value = 0;
        if (!names.IsDefaultOrEmpty)
        {
            var index = names.IndexOf(text.ToLowerInvariant());
            if (index >= 0)
            {
                value = index + nameBase;
                return true;
            }
        }
        return text.Length is > 0 and <= 2 && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
    }
}
