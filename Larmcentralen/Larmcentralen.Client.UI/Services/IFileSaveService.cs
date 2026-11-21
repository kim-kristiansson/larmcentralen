namespace Larmcentralen.Client.UI.Services;

/// <summary>Lets the user pick where to save a file. Each host provides its own (MAUI: a native save dialog).</summary>
public interface IFileSaveService
{
    /// <summary>Returns the saved path, or null when the user cancelled.</summary>
    Task<string?> SaveAsync(string suggestedFileName, byte[] content, CancellationToken ct = default);
}
