using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

/// <summary>
/// Which SMS the alarm webhook acts on: only from a configured sender (the plant's alarm system, OCSS)
/// to the configured 46elks number. Until both are set, every SMS is turned away.
/// </summary>
public class SmsSettingsService(ISmsSettingsRepository repo) : ISmsSettingsService
{
    /// <summary>Plenty for one alarm system with a spare modem or two.</summary>
    public const int MaxSenders = 20;

    public async Task<SmsSettingsDto> GetAsync() => ToDto(await repo.GetAsync());

    public async Task<WriteResult<SmsSettingsDto>> UpdateAsync(UpdateSmsSettingsDto dto, DateTime? now = null)
    {
        string? elksNumber = null;
        if (dto.ElksNumber is { } typed && !string.IsNullOrWhiteSpace(typed))
        {
            elksNumber = SwedishPhoneNumber.Normalise(typed);
            if (elksNumber is null)
                return WriteResult<SmsSettingsDto>.Invalid($"46elks-numret \"{typed.Trim()}\" är inte ett svenskt telefonnummer.");
        }

        List<string>? senders = null;
        if (dto.SenderNumbers is not null)
        {
            senders = [];
            foreach (var number in dto.SenderNumbers.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                var normalised = SwedishPhoneNumber.Normalise(number);
                if (normalised is null)
                    return WriteResult<SmsSettingsDto>.Invalid($"Avsändaren \"{number.Trim()}\" är inte ett svenskt telefonnummer.");
                if (!senders.Contains(normalised)) senders.Add(normalised);
            }
            if (senders.Count > MaxSenders)
                return WriteResult<SmsSettingsDto>.Invalid($"Högst {MaxSenders} avsändare.");
        }

        var settings = await repo.GetAsync();
        if (dto.ElksNumber is not null) settings.ElksNumber = elksNumber;
        if (senders is not null) settings.SenderNumbers = senders;

        if (settings.ElksNumber is not null && settings.SenderNumbers.Contains(settings.ElksNumber))
            return WriteResult<SmsSettingsDto>.Invalid("46elks-numret kan inte också vara en avsändare.");

        settings.UpdatedAt = now ?? DateTime.UtcNow;
        await repo.SaveChangesAsync();
        return WriteResult<SmsSettingsDto>.Ok(ToDto(settings));
    }

    public async Task<SmsCheck> CheckIncomingAsync(string? from, string? to)
    {
        var settings = await repo.GetAsync();
        if (settings.ElksNumber is null)
            return new SmsCheck(false, "Inget 46elks-nummer är inställt.");
        if (settings.SenderNumbers.Count == 0)
            return new SmsCheck(false, "Inga avsändare är inställda.");
        if (!SwedishPhoneNumber.SameNumber(to, settings.ElksNumber))
            return new SmsCheck(false, $"Skickat till {Show(to)}, inte till 46elks-numret.");
        if (SwedishPhoneNumber.Normalise(from) is not { } sender || !settings.SenderNumbers.Contains(sender))
            return new SmsCheck(false, $"{Show(from)} är inte en godkänd avsändare.");
        return new SmsCheck(true, null);
    }

    public async Task RecordAsync(string? from, bool accepted, string result, DateTime? now = null)
    {
        var settings = await repo.GetAsync();
        settings.LastSmsAt = now ?? DateTime.UtcNow;
        settings.LastSmsFrom = Truncate(from, 50);
        settings.LastSmsAccepted = accepted;
        settings.LastSmsResult = Truncate(result, 500);
        await repo.SaveChangesAsync();
    }

    public async Task<SmsFetchState> GetFetchStateAsync()
    {
        var settings = await repo.GetAsync();
        return new SmsFetchState(settings.ElksNumber, settings.FetchedUntil);
    }

    public async Task RecordFetchAsync(DateTime at, DateTime? fetchedUntil, string? error)
    {
        var settings = await repo.GetAsync();
        settings.LastFetchAt = at;
        settings.LastFetchError = Truncate(error, 500);
        if (fetchedUntil is { } until && (settings.FetchedUntil is null || until > settings.FetchedUntil))
            settings.FetchedUntil = until;
        await repo.SaveChangesAsync();
    }

    private static string Show(string? number) => string.IsNullOrWhiteSpace(number) ? "okänt nummer" : number.Trim();

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];

    private static SmsSettingsDto ToDto(SmsSettings s) => new(
        s.ElksNumber,
        [.. s.SenderNumbers],
        s.LastSmsAt is { } at ? new LastSmsDto(at, s.LastSmsFrom, s.LastSmsAccepted, s.LastSmsResult ?? "") : null,
        s.UpdatedAt,
        s.LastFetchAt is { } fetched ? new LastFetchDto(fetched, s.LastFetchError) : null);
}
