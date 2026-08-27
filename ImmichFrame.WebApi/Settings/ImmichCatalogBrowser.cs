using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;

namespace ImmichFrame.WebApi.Settings;

/// <summary>One selectable album, person, or tag, keyed by the value the config file stores.</summary>
public sealed record CatalogEntry(string Id, string Name, long? Count);

public sealed record CatalogResult(
    bool Reachable,
    string? Error,
    IReadOnlyList<CatalogEntry> Albums,
    IReadOnlyList<CatalogEntry> People,
    IReadOnlyList<CatalogEntry> Tags)
{
    public static CatalogResult Failed(string error) => new(false, error, [], [], []);
}

/// <summary>
/// Lists the albums, people, and tags on an Immich server so the settings page can offer names
/// instead of asking for hand-copied GUIDs.
/// </summary>
public interface IImmichCatalogBrowser
{
    Task<CatalogResult> Browse(string serverUrl, string apiKey, CancellationToken ct = default);
}

public sealed class ImmichCatalogBrowser(
    IHttpClientFactory httpClientFactory,
    ILogger<ImmichCatalogBrowser> logger) : IImmichCatalogBrowser
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>People are paged; this bounds a very large library to a few round trips.</summary>
    private const int PeoplePageSize = 1000;
    private const int MaxPeoplePages = 10;

    public async Task<CatalogResult> Browse(string serverUrl, string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
            return CatalogResult.Failed("No Immich server URL.");

        if (string.IsNullOrWhiteSpace(apiKey))
            return CatalogResult.Failed("No API key for this account yet. Enter one and save before browsing.");

        try
        {
            var httpClient = httpClientFactory.CreateClient("ImmichApiAccountClient");
            httpClient.UseApiKey(apiKey);
            var immichApi = new ImmichApi(serverUrl, httpClient);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);

            // Independent endpoints, so pay for the slowest rather than the sum.
            var albumsTask = GetAlbums(immichApi, cts.Token);
            var peopleTask = GetPeople(immichApi, cts.Token);
            var tagsTask = GetTags(immichApi, cts.Token);

            await Task.WhenAll(albumsTask, peopleTask, tagsTask);

            return new CatalogResult(true, null, await albumsTask, await peopleTask, await tagsTask);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not list albums/people/tags from {Server}", serverUrl);
            return CatalogResult.Failed(ex.Message);
        }
    }

    private static async Task<IReadOnlyList<CatalogEntry>> GetAlbums(ImmichApi api, CancellationToken ct)
    {
        var albums = await api.GetAllAlbumsAsync(null, null, null, null, null, ct);

        return [.. albums
            .Select(a => new CatalogEntry(a.Id.ToString(), a.AlbumName, a.AssetCount))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static async Task<IReadOnlyList<CatalogEntry>> GetPeople(ImmichApi api, CancellationToken ct)
    {
        var entries = new List<CatalogEntry>();

        for (var page = 1; page <= MaxPeoplePages; page++)
        {
            var response = await api.GetAllPeopleAsync(null, null, page, PeoplePageSize, false, ct);

            // Immich returns every detected face, most of them unnamed. Only named people are
            // selectable — an unnamed face is not something you can meaningfully pick from a list.
            entries.AddRange(response.People
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .Select(p => new CatalogEntry(p.Id.ToString(), p.Name, null)));

            if (response.HasNextPage != true) break;
        }

        return [.. entries.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static async Task<IReadOnlyList<CatalogEntry>> GetTags(ImmichApi api, CancellationToken ct)
    {
        var tags = await api.GetAllTagsAsync(ct);

        // Tags are configured by value, not id — see TagAssetsPool — so the value is the key here.
        return [.. tags
            .Select(t => new CatalogEntry(t.Value, t.Value, null))
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];
    }
}
