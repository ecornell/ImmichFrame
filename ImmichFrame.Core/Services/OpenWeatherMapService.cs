using System.Globalization;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;

public class OpenWeatherMapService : IWeatherService, ISettingsResettable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IGeneralSettings _settings;
    private IApiCache _weatherCache = new ApiCache(CacheDuration);
    public OpenWeatherMapService(IGeneralSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Drops cached weather so a changed API key, location or unit system is reflected immediately
    /// rather than up to <see cref="CacheDuration"/> later. <see cref="IApiCache"/> exposes no
    /// removal API, so the cache is replaced wholesale.
    /// </summary>
    public void ResetCaches() => _weatherCache = new ApiCache(CacheDuration);

    public async Task<IWeather?> GetWeather()
    {
        return await _weatherCache.GetOrAddAsync("weather", async () =>
        {
            if (!TryParseLatLong(_settings.WeatherLatLong, out var latitude, out var longitude))
            {
                // A malformed location means no weather, not a failed request.
                return null;
            }

            return await GetWeather(latitude, longitude);
        });
    }

    /// <summary>
    /// Parses "lat,long" with the invariant culture, as the settings validator does. The current
    /// culture would read "40.7128" as 407128 under e.g. a German locale.
    /// </summary>
    public static bool TryParseLatLong(string? value, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        if (string.IsNullOrWhiteSpace(value)) return true; // unset: historical behaviour, (0, 0)

        var parts = value.Split(',');
        return parts.Length == 2
               && double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)
               && double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out longitude);
    }

    public async Task<IWeather?> GetWeather(double latitude, double longitude)
    {
        OpenWeatherMap.OpenWeatherMapOptions options = new OpenWeatherMap.OpenWeatherMapOptions
        {
            ApiKey = _settings.WeatherApiKey,
            UnitSystem = _settings.UnitSystem,
            Language = _settings.Language,
        };

        try
        {
            OpenWeatherMap.IOpenWeatherMapService openWeatherMapService = new OpenWeatherMap.OpenWeatherMapService(options);
            var weatherInfo = await openWeatherMapService.GetCurrentWeatherAsync(latitude, longitude);

            return weatherInfo.ToWeather();
        }
        catch
        {
            //do nothing and return null
        }

        return null;
    }
}