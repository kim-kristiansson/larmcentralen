using System.Text.RegularExpressions;
using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Api.Services;

/// <summary>
/// Handles an alarm SMS from the plant's alarm system (OCSS), passed on by 46elks. Only SMS from a configured sender
/// to the configured 46elks number count (Inställningar, SMS-larm). The alarm is looked up in the knowledge base by
/// unit and code, in the avdelning the SMS's line belongs to (<see cref="Lines"/>). One that isn't documented yet
/// is added, like the import adds alarms: the unit too if needed, the shared code, and the SMS text as the unit's
/// own text, priority Låg. Nothing new is added when the line is unknown. For a Hög or Kritisk alarm an alert SMS goes to the alert number set under SMS-larm.
/// What happened to the last SMS is kept for the admin page.
/// </summary>
public partial class SmsAlarmService(
    IAlarmRepository alarmRepo,
    IEquipmentRepository equipmentRepo,
    IAreaRepository areaRepo,
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


    /// <summary>
    /// The lines the alarm system names in its SMS ("06:56 / T10-T7 Line : …"), and the start of the name of the
    /// avdelning each belongs to. "T10-T7 Line" is a legacy name, but it tells which line an alarm comes from if the
    /// number one day carries alarms from other lines too. Add a line here when one turns up.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Lines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["T10-T7"] = "SQ", // SQ-linan (OCSS)
    };

    /// <summary>What happened, in Swedish, for the admin page.</summary>
    private async Task<string> HandleAsync(string? message)
    {
        if (ParseSms(message) is not var (line, equipment, alarmCode, description))
        {
            logger.LogWarning("Could not parse SMS: {Message}", message);
            return "Texten känns inte igen som ett larm (väntade \"… T10-T7 Line : T102 : M206.6 …\").";
        }

        // The line decides the avdelning. New units and alarms are only added when it is known.
        var (areaId, noArea) = await AreaForLineAsync(line);
        var units = await equipmentRepo.FindByTitleAsync(equipment, areaId);
        if (units.Count > 1)
            return $"Enheten {equipment} finns på flera avdelningar, och linjen i SMS:et pekar inte ut en av dem. {noArea}";

        string? added = null;
        var unitId = units.SingleOrDefault()?.Id;
        if (unitId is null)
        {
            if (areaId is null)
                return $"Enheten {equipment} finns inte i larmcentralen. {noArea}";

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
            if (areaId is null)
                return $"{alarmCode} på {equipment} finns inte i larmcentralen. {noArea}";

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
    /// The avdelning for the line in the SMS: the one whose name starts with the line's name (SQ for T10-T7), when
    /// exactly one does. Otherwise null, with the reason (Swedish) why nothing new can be added.
    /// </summary>
    private async Task<(int? AreaId, string Reason)> AreaForLineAsync(string? line)
    {
        if (line is null)
            return (null, "SMS:et anger ingen linje, så inget nytt läggs till.");
        if (!Lines.TryGetValue(line, out var prefix))
            return (null, $"Linjen \"{line}\" är okänd, så inget nytt läggs till.");

        var matches = (await areaRepo.GetAllAsync())
            .Where(a => Regex.IsMatch(a.Title, $@"^{Regex.Escape(prefix)}\b", RegexOptions.IgnoreCase))
            .ToList();
        return matches.Count switch
        {
            1 => (matches[0].Id, ""),
            0 => (null, $"Ingen avdelning heter {prefix}… för linjen {line}, så inget nytt läggs till."),
            _ => (null, $"Flera avdelningar heter {prefix}… för linjen {line}, så inget nytt läggs till."),
        };
    }

    /// <summary>
    /// The line, the unit, the code and the rest of the text. OCSS writes them as
    /// "06:56 / T10-T7 Line : T102 : M206.6 Ingen svarssignal tandbrännare ytterlucka". The line is null when the SMS
    /// names none.
    /// </summary>
    public static (string? Line, string Equipment, string AlarmCode, string Description)? ParseSms(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        var match = AlarmPattern().Match(message);
        if (!match.Success) return null;

        var line = match.Groups["line"].Value.Trim();
        return (line.Length > 0 ? line : null, match.Groups["unit"].Value.Trim(),
            match.Groups["code"].Value.Trim(), match.Groups["text"].Value.Trim());
    }

    // "<time> / <line> Line : <unit> : <code> <text>"; the line is what stands between the last "/" or ":" before
    // "Line" and "Line".
    [GeneratedRegex(@"(?:(?<line>[^/:\s][^/:]*?)\s+)?Line\s*:\s*(?<unit>\w+)\s*:\s*(?<code>\S+)\s*(?<text>.*)", RegexOptions.Singleline)]
    private static partial Regex AlarmPattern();
}
