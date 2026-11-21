using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Larmcentralen.Api.Tests;

/// <summary>
/// Runs the real API in memory as Production would, with its own throwaway SQLite file.
/// The keys are test values, not real secrets.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ReadKey = "test-read-key";
    public const string AdminKey = "test-admin-key";
    public const string WebhookSecret = "test-webhook-secret";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"larm-api-test-{Guid.NewGuid():N}.db");

    public Dictionary<string, string?> Settings { get; } = new()
    {
        ["Security:ApiKey"] = ReadKey,
        ["Security:AdminApiKey"] = AdminKey,
        ["Security:SmsWebhookSecret"] = WebhookSecret,
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(
            new Dictionary<string, string?>(Settings) { ["ConnectionStrings:DefaultConnection"] = $"Data Source={_dbPath}" }));
    }

    public HttpClient ClientWithKey(string? key)
    {
        var client = CreateClient();
        if (key is not null) client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(file)) File.Delete(file);
    }
}
