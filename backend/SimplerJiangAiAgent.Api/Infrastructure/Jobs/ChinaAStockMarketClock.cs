namespace SimplerJiangAiAgent.Api.Infrastructure.Jobs;

public static class ChinaAStockMarketClock
{
    private static readonly TimeZoneInfo ChinaTimeZone = ResolveChinaTimeZone();
    private static readonly TimeSpan MorningStart = new(9, 30, 0);
    private static readonly TimeSpan MorningEnd = new(11, 30, 0);
    private static readonly TimeSpan AfternoonStart = new(13, 0, 0);
    private static readonly TimeSpan AfternoonEnd = new(15, 0, 0);
    private static readonly HashSet<DateOnly> KnownHolidayClosures = new()
    {
        new DateOnly(2026, 1, 1),
        new DateOnly(2026, 1, 2),
        new DateOnly(2026, 2, 16),
        new DateOnly(2026, 2, 17),
        new DateOnly(2026, 2, 18),
        new DateOnly(2026, 2, 19),
        new DateOnly(2026, 2, 20),
        new DateOnly(2026, 2, 23),
        new DateOnly(2026, 4, 6),
        new DateOnly(2026, 5, 1),
        new DateOnly(2026, 5, 4),
        new DateOnly(2026, 5, 5),
        new DateOnly(2026, 6, 19),
        new DateOnly(2026, 9, 25),
        new DateOnly(2026, 10, 1),
        new DateOnly(2026, 10, 2),
        new DateOnly(2026, 10, 5),
        new DateOnly(2026, 10, 6),
        new DateOnly(2026, 10, 7)
    };

    public static bool IsTradingSession(DateTimeOffset utcNow)
    {
        var local = TimeZoneInfo.ConvertTime(utcNow, ChinaTimeZone);
        if (!IsTradingDay(DateOnly.FromDateTime(local.DateTime)))
        {
            return false;
        }

        var time = local.TimeOfDay;
        return (time >= MorningStart && time < MorningEnd)
            || (time >= AfternoonStart && time < AfternoonEnd);
    }

    public static bool IsTradingDay(DateOnly localDate)
    {
        if (localDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        return !KnownHolidayClosures.Contains(localDate);
    }

    private static TimeZoneInfo ResolveChinaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("China Standard Time", TimeSpan.FromHours(8), "China Standard Time", "China Standard Time");
        }
    }
}