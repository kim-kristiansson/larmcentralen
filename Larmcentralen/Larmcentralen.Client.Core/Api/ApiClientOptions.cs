namespace Larmcentralen.Client.Core.Api;

public class ApiClientOptions
{
    /// <summary>Base address of the Larmcentralen API, ending with a slash.</summary>
    public Uri BaseAddress { get; set; } = new("https://localhost:7094/");

    /// <summary>How long a request may take before the client treats the API as unreachable.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
}
