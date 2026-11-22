using System.Text.Json.Serialization;
using Larmcentralen.Api.Security;
using Larmcentralen.Api.Services;
using Larmcentralen.Application.Interfaces;
using Larmcentralen.Application.Services;
using Larmcentralen.Domain.Interfaces;
using Larmcentralen.Infrastructure.Data;
using Larmcentralen.Infrastructure.Repositories;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Anything that isn't a read needs the Admin role, see RequireAdminForWritesConvention.
builder.Services.AddControllers(o => o.Conventions.Add(new RequireAdminForWritesConvention()))
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
        .AddInterceptors(new SqliteConnectionInterceptor()));

// Repositories
builder.Services.AddScoped<IAreaRepository, AreaRepository>();
builder.Services.AddScoped<IEquipmentRepository, EquipmentRepository>();
builder.Services.AddScoped<IAlarmRepository, AlarmRepository>();
builder.Services.AddScoped<ISolutionRepository, SolutionRepository>();
builder.Services.AddScoped<IAlarmCodeRepository, AlarmCodeRepository>();
builder.Services.AddScoped<ITrashRepository, TrashRepository>();
builder.Services.AddScoped<ISmsSettingsRepository, SmsSettingsRepository>();

// Services
builder.Services.AddScoped<IAreaService, AreaService>();
builder.Services.AddScoped<IEquipmentService, EquipmentService>();
builder.Services.AddScoped<IAlarmService, AlarmService>();
builder.Services.AddScoped<ISolutionService, SolutionService>();
builder.Services.AddScoped<IAlarmCodeService, AlarmCodeService>();
builder.Services.AddScoped<ISyncService, SyncService>();
builder.Services.AddScoped<ITrashService, TrashService>();
builder.Services.AddScoped<ISmsSettingsService, SmsSettingsService>();
// Erases what has been in the bin (Papperskorg) longer than 30 days.
builder.Services.AddHostedService<TrashEraseService>();
builder.Services.AddScoped<ISharePointSyncService, SharePointSyncService>();
builder.Services.AddScoped<SharePointUploadService>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<SmsAlarmService>();
builder.Services.AddHttpClient<IElksApi, ElksApi>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<ElksAccount>();
builder.Services.AddScoped<IAlertSmsSender, ElksAlertSmsSender>();
// The company's own 46elks password (SMS-larm) is kept encrypted in the database.
var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(
    builder.Configuration.GetConnectionString("DefaultConnection")).DataSource;
var keysFolder = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dataSource))!, "keys"));
// Kept beside the database (back them up together), readable by any account on this machine.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Larmcentralen").PersistKeysToFileSystem(keysFolder);
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
builder.Services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
builder.Services.AddSingleton<HandledSmsIds>();
// Fetches new alarm SMS from 46elks, so the API needs no public address.
builder.Services.AddSingleton<ElksSmsFetcher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ElksSmsFetcher>());

// Configurations
builder.Services.Configure<EmailOptions>(
    builder.Configuration.GetSection("Email"));
builder.Services.Configure<SharePointOptions>(
    builder.Configuration.GetSection("SharePoint"));

// API keys: the read key gives the User role, the admin key the Admin role.
builder.Services.AddApiKeySecurity(builder.Configuration);

var app = builder.Build();

// Create the database file if needed and bring it up to date.
using (var scope = app.Services.CreateScope())
{
    await SqliteSetup.InitialiseAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseApiKeySecurity();
app.MapControllers();

app.Run();

// Lets the API tests start the app with WebApplicationFactory.
public partial class Program { }
