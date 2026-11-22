using System.Text.RegularExpressions;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Api.Services;

/// <summary>
/// Handles an alarm SMS from the plant's alarm system (OCSS), passed on by 46elks. Only SMS from a configured sender
/// to the configured 46elks number count (Inställningar, SMS-larm). The alarm is looked up in the knowledge base by
/// unit and code; for a Hög or Kritisk alarm an alert SMS goes to the alert number set under SMS-larm.
/// What happened to the last SMS is kept for the admin page.
/// </summary>
public partial class SmsAlarmService(
    IAlarmRepository alarmRepo,
    ISmsSettingsService settings,
    IAlertSmsSender alertSender,
    HandledSmsIds handled,
    ILogger<SmsAlarmService> logger)
{
    /// <summary>Handles one SMS. <paramref name="id"/> is 46elks' id: an SMS seen before is skipped.</summary>
    public async Task ProcessSmsAsync(string? from, string? to, string? message, string? id = null)
    {
        if (!handled.TryMark(id))
        {
            logger.LogInformation("SMS {Id} already handled", id);
            return;
        }

        var check = await settings.CheckIncomingAsync(from, to);
        if (!check.Accepted)
        {
            logger.LogWarning("SMS from {From} to {To} turned away: {Reason}", from, to, check.Reason);
            await settings.RecordAsync(from, false, check.Reason!);
            return;
        }

        logger.LogInformation("SMS received from {From}: {Message}", from, message);
        await settings.RecordAsync(from, true, await HandleAsync(message));
    }

    /// <summary>What happened, in Swedish, for the admin page.</summary>
    private async Task<string> HandleAsync(string? message)
    {
        if (ParseSms(message) is not var (equipment, alarmCode, description))
        {
            logger.LogWarning("Could not parse SMS: {Message}", message);
            return "Texten känns inte igen som ett larm (väntade \"… Line : T102 : M206.6 …\").";
        }

        var alarm = await alarmRepo.FindByCodeAsync(equipment, alarmCode);
        if (alarm is null)
        {
            logger.LogInformation("Alarm {Code} on {Equipment} not found in database", alarmCode, equipment);
            return $"{alarmCode} på {equipment} finns inte i larmcentralen.";
        }

        var code = alarm.AlarmCode!.Code;
        var unit = alarm.Equipment.Title;
        if (AlarmText.SeverityRank(alarm.Severity) < AlarmText.SeverityRank("Hög"))
        {
            logger.LogInformation("Alarm {Code} on {Equipment} is {Severity}, no alert", code, unit, alarm.Severity);
            return $"{code} på {unit} ({alarm.Severity}). Ingen avisering för den prioriteten.";
        }

        var text = $"🚨 {alarm.Severity}: {code} på {unit}\n{alarm.Title}\n{description}".TrimEnd();
        var sent = await alertSender.SendAsync(text);
        logger.LogInformation("Alert for {Severity} alarm {Code} on {Equipment} sent: {Sent}", alarm.Severity, code, unit, sent);
        return sent
            ? $"{code} på {unit} ({alarm.Severity}). Avisering skickad."
            : $"{code} på {unit} ({alarm.Severity}). Aviseringen kunde inte skickas, se serverns logg.";
    }

    /// <summary>
    /// The unit, the code and the rest of the text. OCSS writes them as
    /// "06:56 / T10-T7 Line : T102 : M206.6 Ingen svarssignal tandbrännare ytterlucka".
    /// </summary>
    public static (string Equipment, string AlarmCode, string Description)? ParseSms(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var match = AlarmPattern().Match(message);
        if (!match.Success) return null;

        return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim(), match.Groups[3].Value.Trim());
    }

    [GeneratedRegex(@"Line\s*:\s*(\w+)\s*:\s*(\S+)\s*(.*)", RegexOptions.Singleline)]
    private static partial Regex AlarmPattern();
}
