using CommunityToolkit.Maui.Storage;
using Larmcentralen.Client.UI.Services;

namespace Larmcentralen.App.Services;

/// <summary>Native "Save as" dialog, so the user picks where the Word file goes.</summary>
public sealed class MauiFileSaveService : IFileSaveService
{
    public async Task<string?> SaveAsync(string suggestedFileName, byte[] content, CancellationToken ct = default)
    {
        using var stream = new MemoryStream(content);
        var result = await MainThread.InvokeOnMainThreadAsync(() => FileSaver.Default.SaveAsync(suggestedFileName, stream, ct));
        if (result.IsSuccessful) return result.FilePath;
        if (result.Exception is OperationCanceledException or null) return null;
        throw result.Exception;
    }
}
