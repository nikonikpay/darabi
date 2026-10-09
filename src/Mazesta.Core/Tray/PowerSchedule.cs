using Mazesta.Core.Text;
namespace Mazesta.Core.Tray;

public enum PowerAction { Sleep, Shutdown }

/// <summary>A sleep or shutdown the technician asked the tray for, a while ahead: what, and when. The tray warns <see cref="Warning"/> before it happens.</summary>
public sealed record PowerSchedule(PowerAction Action, DateTimeOffset Due)
{
    public static readonly TimeSpan Warning = TimeSpan.FromSeconds(60), Shortest = TimeSpan.FromMinutes(1), Longest = TimeSpan.FromHours(48);

    public TimeSpan Left(DateTimeOffset now) => Due > now ? Due - now : TimeSpan.Zero;
    public bool IsDue(DateTimeOffset now) => now >= Due;
    public bool IsWarning(DateTimeOffset now) => Left(now) <= Warning;

    /// <summary>"1:05:09" - the time left as hours, minutes and seconds, Latin digits.</summary>
    public static string Format(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";

    /// <summary>What was typed as a wait: minutes ("90"), or hours and minutes ("1:30"); Persian digits too. Null when it is neither or outside one minute to 48 hours.</summary>
    public static TimeSpan? ParseWait(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string s = PersianDigits.Normalize(text).Trim(); TimeSpan wait;
        if (s.Contains(':'))
        {
            var p = s.Split(':');
            if (p.Length != 2 || !int.TryParse(p[0], out int h) || !int.TryParse(p[1], out int m) || h < 0 || m is < 0 or > 59) return null;
            wait = new TimeSpan(h, m, 0);
        }
        else if (int.TryParse(s, out int minutes) && minutes > 0) wait = TimeSpan.FromMinutes(minutes);
        else return null;
        return wait >= Shortest && wait <= Longest ? wait : null;
    }
}
