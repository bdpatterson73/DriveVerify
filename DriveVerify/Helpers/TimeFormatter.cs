namespace DriveVerify.Helpers;

public static class TimeFormatter
{
    public static string Format(TimeSpan ts)
    {
        if (ts.TotalSeconds < 1) return "0s";

        int hours = (int)ts.TotalHours;
        int minutes = ts.Minutes;
        int seconds = ts.Seconds;

        if (hours > 0)
            return $"{hours}h {minutes}m {seconds}s";

        if (minutes > 0)
            return $"{minutes}m {seconds}s";

        return $"{seconds}s";
    }
}
