using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Models;
using Microsoft.AspNetCore.Authentication;
using System.Reflection;
using ImmichFrame.Core.Logic;
using ImmichFrame.Core.Logic.AccountSelection;
using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Helpers.Config;
using ImmichFrame.WebApi.Settings;

var builder = WebApplication.CreateBuilder(args);
//log the version number
var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
Console.WriteLine($@"
 _                     _      _    ______                        
(_)                   (_)    | |   |  ___|                       
 _ _ __ ___  _ __ ___  _  ___| |__ | |_ _ __ __ _ _ __ ___   ___ 
| | '_ ` _ \| '_ ` _ \| |/ __| '_ \|  _| '__/ _` | '_ ` _ \ / _ \
| | | | | | | | | | | | | (__| | | | | | | | (_| | | | | | |  __/
|_|_| |_| |_|_| |_| |_|_|\___|_| |_\_| |_|  \__,_|_| |_| |_|\___| Version {version}");
Console.WriteLine();

// Add services to the container.
builder.Services.AddLogging(builder =>
{
    LogLevel level = LogLevel.Information;
    var logLevel = Environment.GetEnvironmentVariable("LOG_LEVEL");
    if (!string.IsNullOrWhiteSpace(logLevel))
    {
        Enum.TryParse(logLevel, true, out level);
    }

    Console.WriteLine($"LogLevel: {level}");
    builder.SetMinimumLevel(level);
    builder.AddSimpleConsole(options =>
    {
        // Customizing the log output format
        options.TimestampFormat = "yy-MM-dd HH:mm:ss "; // Custom timestamp format
        options.SingleLine = true;
    });

    // Disable SpaProxy info logs
    builder.AddFilter("Microsoft.AspNetCore.SpaProxy", LogLevel.Warning);
    // Disable AspNetCore info logs
    builder.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
    // Only show HttpClient request info logs when LOG_LEVEL is Debug or lower
    if (level > LogLevel.Debug)
    {
        builder.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
    }
});


// Setup Config.
// Resolved lazily rather than here, because in Development DotEnv.Load() only populates the process
// environment after the host is built — evaluating this eagerly would ignore an
// IMMICHFRAME_CONFIG_PATH set in docker/.env and silently fall back to the build output directory.
static string ResolveConfigPath() =>
    // Normalized so a relative IMMICHFRAME_CONFIG_PATH (handy in development) still reports and
    // writes to an unambiguous location.
    Path.GetFullPath(
        Environment.GetEnvironmentVariable("IMMICHFRAME_CONFIG_PATH") ??
            Directory.EnumerateDirectories(AppDomain.CurrentDomain.BaseDirectory, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), "Config", StringComparison.OrdinalIgnoreCase))
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config"));

builder.Services.AddTransient<ConfigLoader>();
builder.Services.AddSingleton(_ => new ConfigLocation(ResolveConfigPath()));

// Bootstrap seed only — this is the configuration as it was on disk at startup and it does NOT
// track edits made through the admin API. Inject IGeneralSettings (or SettingsStore) for live values.
builder.Services.AddSingleton<IServerSettings>(srv =>
    srv.GetRequiredService<ConfigLoader>().LoadConfig(srv.GetRequiredService<ConfigLocation>().Directory));

// Live settings infrastructure
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<ImmichFrameLogicFactory>();
builder.Services.AddSingleton<SettingsBootstrapper>();
builder.Services.AddSingleton<SettingsFileWriter>();
builder.Services.AddSingleton<SettingsReloadService>();

// Register sub-settings.
// IGeneralSettings resolves to a proxy over the published generation, so the consumers that hold it
// (ConfigController, AssetController, weather/calendar, PooledImmichFrameLogic's per-call reads)
// observe saved changes without being rebuilt. The two lines below alias it, as they always have.
builder.Services.AddSingleton<IGeneralSettings, LiveGeneralSettings>();
builder.Services.AddSingleton<IClientSettings>(srv => srv.GetRequiredService<IGeneralSettings>());
builder.Services.AddSingleton<IServerBehaviorSettings>(srv => srv.GetRequiredService<IGeneralSettings>());

// Register services
builder.Services.AddSingleton<IWeatherService, OpenWeatherMapService>();
builder.Services.AddSingleton<ICalendarService, IcalCalendarService>();
builder.Services.AddSingleton<IImmichServerProbe, ImmichServerProbe>();
builder.Services.AddSingleton<IImmichCatalogBrowser, ImmichCatalogBrowser>();

// Services holding config-derived caches, flushed on save so changes are visible immediately.
builder.Services.AddSingleton<ISettingsResettable>(srv => (ISettingsResettable)srv.GetRequiredService<IWeatherService>());
builder.Services.AddSingleton<ISettingsResettable>(srv => (ISettingsResettable)srv.GetRequiredService<ICalendarService>());
builder.Services.AddSingleton<IAssetAccountTracker, BloomFilterAssetAccountTracker>();
builder.Services.AddSingleton<Func<IList<IAccountImmichFrameLogic>, IAccountSelectionStrategy>>(srv =>
    accounts => ActivatorUtilities.CreateInstance<TotalAccountImagesSelectionStrategy>(srv, accounts));
builder.Services.AddHttpClient(); // Ensures IHttpClientFactory is available

builder.Services.AddTransient<Func<IAccountSettings, IAccountImmichFrameLogic>>(srv =>
    account => ActivatorUtilities.CreateInstance<PooledImmichFrameLogic>(srv, account));

// A stable facade over the published generation's account graph. The graph itself is rebuilt on
// every save, since API keys, server URLs and pool topology are all frozen at construction.
builder.Services.AddSingleton<IImmichFrameLogic>(srv =>
{
    var store = srv.GetRequiredService<SettingsStore>();
    return new SwappableImmichFrameLogic(() => store.Current.Logic);
});

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SchemaFilter<ImmichFrame.WebApi.Helpers.NoReadOnlySchemaFilter>());

builder.Services.AddAuthorization(options => { options.AddPolicy("AllowAnonymous", policy => policy.RequireAssertion(context => true)); });

builder.Services.AddAuthentication("ImmichFrameScheme")
    .AddScheme<AuthenticationSchemeOptions, ImmichFrameAuthenticationHandler>("ImmichFrameScheme", options => { });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();
if (app.Environment.IsProduction())
{
    app.UseDefaultFiles();
}

if (app.Environment.IsDevelopment())
{
    var root = Directory.GetCurrentDirectory();
    var dotenv = Path.Combine(root, "..", "docker", ".env");

    dotenv = Path.GetFullPath(dotenv);
    DotEnv.Load(dotenv);
}

// app.UseHttpsRedirection();
app.UseMiddleware<CustomAuthenticationMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("/index.html");

// Publish generation 1. Must come after DotEnv.Load above, since env-var configuration is only
// visible to the loader once those values are in the process environment.
app.Services.GetRequiredService<SettingsBootstrapper>().Initialize();

var immichStartupAllowed = await ImmichServerVersionChecker.CheckServerVersions(app.Services, app.Logger);
if (!immichStartupAllowed)
{
    app.Logger.LogCritical("ImmichFrame cannot start: Immich server requirements are not satisfied (see log above). Shutting down.");
    Environment.Exit(1);
}

app.Run();

// Make Program public for WebApplicationFactory
public partial class Program { }
