namespace TinyClips.Helpers;

/// <summary>
/// Pure calculation helpers extracted from VideoTrimmerWindow.
/// All methods are static and UI-free for testability.
/// </summary>
internal static class VideoTrimHelper
{
    /// <summary>
    /// Format a TimeSpan as MM:SS.d for the video trimmer UI.
    /// </summary>
    public static string FormatTime(TimeSpan t)
    {
        return t.TotalMinutes >= 1
            ? $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 100}"
            : $"00:{t.Seconds:D2}.{t.Milliseconds / 100}";
    }

    /// <summary>
    /// Returns the effective trim end point.
    /// A trimEnd of Zero means "use full duration".
    /// </summary>
    public static TimeSpan EffectiveTrimEnd(TimeSpan trimEnd, TimeSpan duration)
    {
        return trimEnd > TimeSpan.Zero ? trimEnd : duration;
    }

    /// <summary>
    /// Determines whether the user has set any trim points.
    /// </summary>
    public static bool HasTrim(TimeSpan trimStart, TimeSpan trimEnd, TimeSpan duration)
    {
        return trimStart > TimeSpan.Zero || (trimEnd > TimeSpan.Zero && trimEnd < duration);
    }

    /// <summary>
    /// Calculates the start and end fractions (0..1) for the trim range bar.
    /// </summary>
    public static (double StartFraction, double EndFraction) TrimRangeFractions(
        TimeSpan trimStart, TimeSpan effectiveTrimEnd, TimeSpan duration)
    {
        if (duration.TotalSeconds <= 0)
            return (0, 1);

        double startFrac = trimStart.TotalSeconds / duration.TotalSeconds;
        double endFrac = effectiveTrimEnd.TotalSeconds / duration.TotalSeconds;
        return (startFrac, endFrac);
    }
}
