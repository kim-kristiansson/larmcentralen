using Larmcentralen.Application.DTOs;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Domain.Entities;
using Larmcentralen.Domain.Interfaces;

namespace Larmcentralen.Application.Services;

/// <summary>
/// Which SMS the alarm webhook acts on: only from a configured sender (the plant's alarm system, OCSS)
/// to the configured 46elks number. Until both are set, every SMS is turned away.
/// </summary>
public class SmsSettingsService(ISmsSettingsRepository repo, ISecretProtector protector) : ISmsSettingsService
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

        string? recipient = null;
        if (dto.AlertRecipient is { } typedRecipient && !string.IsNullOrWhiteSpace(typedRecipient))
        {
            recipient = SwedishPhoneNumber.Normalise(typedRecipient);
            if (recipient is null)
                return WriteResult<SmsSettingsDto>.Invalid($"Aviseringsnumret \"{typedRecipient.Trim()}\" är inte ett svenskt telefonnummer.");
        }

        var username = dto.ElksUsername?.Trim();
        if (username is { Length: > 100 } || (username?.Any(char.IsWhiteSpace) ?? false))
            return WriteResult<SmsSettingsDto>.Invalid("API-användarnamnet till 46elks ser inte rätt ut.");

        var settings = await repo.GetAsync();
        if (dto.ElksNumber is not null) settings.ElksNumber = elksNumber;
        if (senders is not null) settings.SenderNumbers = senders;
        if (dto.AlertRecipient is not null) settings.AlertRecipient = recipient;

        if (username is not null)
        {
            if (username.Length == 0)
            {
                settings.ElksUsername = null;
                settings.ElksPasswordProtected = null;
            }
            else
            {
                if (username != settings.ElksUsername && string.IsNullOrEmpty(dto.ElksPassword))
                    settings.ElksPasswordProtected = null;
                settings.ElksUsername = username;
            }
        }
        if (!string.IsNullOrEmpty(dto.ElksPassword))
        {
            if (settings.ElksUsername is null)
                return WriteResult<SmsSettingsDto>.Invalid("Ange API-användarnamnet till 46elks också.");
            settings.ElksPasswordProtected = protector.Protect(dto.ElksPassword);
        }
        if (settings.ElksUsername is not null && settings.ElksPasswordProtected is null)
            return WriteResult<SmsSettingsDto>.Invalid("Ange API-lösenordet till 46elks också.");

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

    public async Task<ElksLogin?> GetElksLoginAsync()
    {
        var settings = await repo.GetAsync();
        if (settings.ElksUsername is null || settings.ElksPasswordProtected is null) return null;
        var password = protector.Unprotect(settings.ElksPasswordProtected)
                       ?? throw new InvalidOperationException(UnreadablePassword);
        return new ElksLogin(settings.ElksUsername, password);
    }

    /// <summary>Shown when the saved password can't be decrypted any more.</summary>
    public const string UnreadablePassword = "Det sparade lösenordet till 46elks kan inte läsas längre (serverns nycklar har bytts). Ange det igen under SMS-larm.";

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
        s.LastFetchAt is { } fetched ? new LastFetchDto(fetched, s.LastFetchError) : null,
        s.ElksUsername,
        s.ElksPasswordProtected is not null,
        s.AlertRecipient);
}
