using System.Net.Http.Headers;
using System.Text;
using Ical.Net;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Helpers;
using Microsoft.Extensions.Logging;

public class IcalCalendarService : ICalendarService, ISettingsResettable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    private readonly IGeneralSettings _serverSettings;
    private readonly ILogger<IcalCalendarService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private ApiCache _appointmentCache = new(CacheDuration);

    /// <summary>
    /// Drops cached appointments so an edited Webcalendars list takes effect immediately rather than
    /// up to <see cref="CacheDuration"/> later.
    /// </summary>
    public void ResetCaches() => _appointmentCache = new ApiCache(CacheDuration);

    public IcalCalendarService(IGeneralSettings serverSettings, ILogger<IcalCalendarService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _serverSettings = serverSettings;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<List<IAppointment>> GetAppointments()
    {
        return await _appointmentCache.GetOrAddAsync("appointments", async () =>
        {
            var appointments = new List<IAppointment>();
            var client = _httpClientFactory.CreateClient();
            var calendars = _serverSettings.Webcalendars;

            // Each calendar on its own: one unreachable or malformed feed must not hide the others.
            for (var index = 0; index < calendars.Count; index++)
            {
                var label = $"calendar #{index + 1}";

                if (!TryParse(calendars[index], out var auth, out var url))
                {
                    // Calendar URLs routinely carry secret tokens, so they are never logged.
                    _logger.LogError("Invalid URL for {Calendar}", label);
                    continue;
                }

                label = $"{label} ({Redact(url)})";

                try
                {
                    var ical = await GetCalendar(client, auth, url);
                    if (ical == null) continue;

                    var calendar = Calendar.Load(ical);
                    appointments.AddRange(calendar.GetOccurrences(DateTime.Today, DateTime.Today.AddDays(1)).Select(x => x.ToAppointment()));
                }
                catch (Exception ex)
                {
                    _logger.LogError("Failed to load {Calendar}: {Error}", label, ex.GetType().Name);
                }
            }

            return appointments;
        });
    }

    private static bool TryParse(string value, out string? auth, out Uri url)
    {
        auth = null;
        url = null!;

        if (!Uri.TryCreate(value.Replace("webcal://", "https://"), UriKind.Absolute, out var uri))
            return false;

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            auth = Uri.UnescapeDataString(uri.UserInfo);
            uri = new Uri(uri.GetComponents(UriComponents.AbsoluteUri & ~UriComponents.UserInfo, UriFormat.UriEscaped));
        }

        url = uri;
        return true;
    }

    /// <summary>
    /// Scheme and host only. Hosted calendars (Google, iCloud, Outlook) put the secret token in the
    /// path, so stripping the query string would not be enough.
    /// </summary>
    internal static string Redact(Uri url) => $"{url.Scheme}://{url.Host}";

    private async Task<string?> GetCalendar(HttpClient client, string? auth, Uri url)
    {
        _logger.LogDebug("Loading calendar: {Auth} - {Host}", auth != null ? "[authenticated]" : "no auth", Redact(url));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (!string.IsNullOrEmpty(auth))
        {
            var byteArray = Encoding.UTF8.GetBytes(auth);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        }

        using var response = await client.SendAsync(request);
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadAsStringAsync();
        }

        _logger.LogError("Failed to load calendar data from {Host} (Status: {Status})", Redact(url), response.StatusCode);
        return null;
    }
}
