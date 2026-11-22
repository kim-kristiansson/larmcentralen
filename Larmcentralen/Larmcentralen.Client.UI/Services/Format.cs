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

    /// <summary>Quill leaves "&lt;p&gt;&lt;br&gt;&lt;/p&gt;" in an empty editor.</summary>
    public static bool IsEmptyHtml(string? html)
        => string.IsNullOrWhiteSpace(html) || html.Trim() is "<p><br></p>" or "<p></p>";
}
