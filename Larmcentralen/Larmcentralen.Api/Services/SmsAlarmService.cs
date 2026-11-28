using System.Text.RegularExpressions;
using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Api.Services;

/// <summary>
/// Handles an alarm SMS from the plant's alarm system (OCSS), passed on by 46elks. Only SMS from a configured sender
/// to the configured 46elks number count (Inställningar, SMS-larm). The alarm is looked up in the knowledge base by
/// unit (in the avdelning for SMS-larm) and code. One that isn't documented yet is added, like the import adds
/// alarms: the unit too if needed (only when an avdelning is set), the shared code, and the SMS text as the unit's
/// own text, priority Låg. For a Hög or Kritisk alarm an alert SMS goes to the alert number set under SMS-larm.
/// What happened to the last SMS is kept for the admin page.
/// </summary>
public partial class SmsAlarmService(
    IAlarmRepository alarmRepo,
    IEquipmentRepository equipmentRepo,
    IEquipmentService equipmentService,
    IAlarmService alarmService,
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

        var areaId = (await settings.GetAsync()).AlarmAreaId;
        var units = await equipmentRepo.FindByTitleAsync(equipment, areaId);
        if (units.Count > 1)
            return $"Enheten {equipment} finns på flera avdelningar. Välj avdelning för SMS-larm under SMS-larm.";

        string? added = null;
        var unitId = units.SingleOrDefault()?.Id;
        if (unitId is null)
        {
            if (areaId is null)
                return $"Enheten {equipment} finns inte. Välj avdelning för SMS-larm under SMS-larm, så läggs nya enheter och larm till där.";

            // Not documented yet: add the unit the way the import does.
            var created = await equipmentService.CreateAsync(new CreateEquipmentDto(equipment, null, "OCSS", null, areaId.Value));
            if (created.Status != WriteStatus.Ok)
                return $"Enheten {equipment} finns inte och kunde inte läggas till: {created.Error ?? created.Status.ToString()}";
            unitId = created.Value!.Id;
            added = $"Enheten {equipment} och larmet";
            logger.LogInformation("Unit {Equipment} added from an alarm SMS", equipment);
        }

        var alarm = await alarmRepo.FindByCodeAsync(unitId.Value, alarmCode);
        if (alarm is null)
        {
            // Not documented yet: add it like the other alarms, with the unit's own text from the SMS and the shared
            // code (added to the code list if new). Priority Låg, as imported alarms start out.
            var title = description.Length > 0 ? description : alarmCode;
            if (title.Length > 300) title = title[..300];
            var result = await alarmService.CreateAsync(new CreateAlarmDto(title, alarmCode, "Låg",
                $"Lades till automatiskt från ett SMS-larm {DateTime.Now:yyyy-MM-dd HH:mm}. Saknar åtgärder.", unitId.Value));
            alarm = await alarmRepo.FindByCodeAsync(unitId.Value, alarmCode); // another SMS may have added it meanwhile
            if (alarm is null)
                return $"{alarmCode} på {equipment} finns inte och kunde inte läggas till: {result.Error ?? result.Status.ToString()}";
            if (result.Status == WriteStatus.Ok) added ??= "Larmet";
            logger.LogInformation("Alarm {Code} on {Equipment} added from an alarm SMS", alarmCode, equipment);
        }

        var code = alarm.AlarmCode!.Code;
        var unit = alarm.Equipment.Title;
        var addedText = added is null ? "" : $" {added} fanns inte och har lagts till. Lägg till åtgärder i larmcentralen.";
        if (AlarmText.SeverityRank(alarm.Severity) < AlarmText.SeverityRank("Hög"))
        {
            logger.LogInformation("Alarm {Code} on {Equipment} is {Severity}, no alert", code, unit, alarm.Severity);
            return $"{code} på {unit} ({alarm.Severity}).{addedText} Ingen avisering för den prioriteten.";
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
