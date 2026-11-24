using System.Globalization;

namespace Larmcentralen.Client.UI.Services;

public static class Format
{
    public static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>"i dag 08:42", "i går 16:10" or "20 nov 14:02", in local time.</summary>
    public static string When(DateTimeOffset value, DateTimeOffset? now = null)
    {
        var local = value.ToLocalTime();
        var today = (now ?? DateTimeOffset.Now).ToLocalTime().Date;
        var time = local.ToString("HH:mm", Swedish);
        if (local.Date == today) return $"i dag {time}";
        if (local.Date == today.AddDays(-1)) return $"i går {time}";
        return local.ToString("d MMM HH:mm", Swedish);
    }

    /// <summary>Shorter, for the top bar: "08:42" today, "i går 16:10", or "20 nov 14:02", in local time.</summary>
    public static string Short(DateTimeOffset value, DateTimeOffset? now = null)
    {
        var text = When(value, now);
        return text.StartsWith("i dag ", StringComparison.Ordinal) ? text["i dag ".Length..] : text;
    }

    /// <summary>"1 enhet", "3 enheter": the count with the right form of the word.</summary>
    public static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    /// <summary>
    /// What a removal takes with it, as a list: "2 enheter, 14 larm och 3 åtgärder". Leaves out what is zero;
    /// null when nothing goes along.
    /// </summary>
    public static string? Contents(int units, int alarms, int solutions)
    {
        var parts = new List<string>();
        if (units > 0) parts.Add(Count(units, "enhet", "enheter"));
        if (alarms > 0) parts.Add(Count(alarms, "larm", "larm"));
        if (solutions > 0) parts.Add(Count(solutions, "åtgärd", "åtgärder"));
        return parts.Count switch
        {
            0 => null,
            1 => parts[0],
            _ => string.Join(", ", parts[..^1]) + " och " + parts[^1]
        };
    }
}
