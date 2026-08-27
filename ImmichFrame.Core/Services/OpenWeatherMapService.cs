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
            var weatherLatLong = _settings.WeatherLatLong;

            var weatherLat = !string.IsNullOrWhiteSpace(weatherLatLong) ? float.Parse(weatherLatLong!.Split(',')[0]) : 0f;
            var weatherLong = !string.IsNullOrWhiteSpace(weatherLatLong) ? float.Parse(weatherLatLong!.Split(',')[1]) : 0f;

            var weather = await GetWeather(weatherLat, weatherLong);

            return weather;
        });
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