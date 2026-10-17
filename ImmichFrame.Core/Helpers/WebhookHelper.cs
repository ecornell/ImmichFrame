using System.Text.Json;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Helpers;
using Microsoft.Extensions.Logging;

public static class WebhookHelper
{
    // One notification is sent per asset shown, so share one client (a client per call leaks
    // sockets) and keep the timeout short: an unreachable webhook must not hold connections open.
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new PolymorphicJsonConverter<IWebhookNotification>() },
        WriteIndented = true
    };

    // Callers fire and forget, so this never throws: failures are logged instead.
    public static async Task SendWebhookNotification(IWebhookNotification notification, string? webhookUrl, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl)) return;

        try
        {
            string json = JsonSerializer.Serialize(notification, SerializerOptions);
            using var data = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var response = await HttpClient.PostAsync(webhookUrl, data);

            if (response.IsSuccessStatusCode)
            {
                logger?.LogDebug("Webhook successfully sent.");
            }
            else
            {
                Log(logger, $"Failed to send notification to webhook: {Convert.ToInt32(response.StatusCode)} {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Log(logger, $"Failed to send notification to webhook: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Log(ILogger? logger, string message)
    {
        if (logger != null)
            logger.LogWarning("{Message}", message);
        else
            Console.WriteLine(message);
    }
}
