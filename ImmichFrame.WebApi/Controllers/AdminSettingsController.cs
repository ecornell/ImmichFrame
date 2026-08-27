using ImmichFrame.WebApi.Models.Admin;
using ImmichFrame.WebApi.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ImmichFrame.WebApi.Controllers;

/// <summary>
/// Reads and writes the running configuration.
/// </summary>
/// <remarks>
/// Deliberately a separate controller from <c>ConfigController</c>, which is intentionally
/// unauthenticated so the SPA can bootstrap before it has a token. Sharing a class would leave the
/// write actions one forgotten attribute away from being anonymous.
/// <para>
/// It also never injects <c>IImmichFrameLogic</c>. That is what guarantees this page still loads and
/// still saves when the Immich configuration is broken — otherwise a bad save could lock you out of
/// the only screen that can fix it.
/// </para>
/// <para>
/// Note that <c>[Authorize]</c> is inert while <c>AuthenticationSecret</c> is unset, matching every
/// other controller. Setting that secret turns enforcement on here with no code change.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AdminSettingsController(
    SettingsStore store,
    SettingsReloadService reload,
    ConfigLocation location,
    IImmichCatalogBrowser catalog,
    ILogger<AdminSettingsController> logger) : ControllerBase
{
    [HttpGet(Name = "GetAdminSettings")]
    public ActionResult<AdminSettingsViewDto> Get()
    {
        return AdminSettingsMapper.ToView(store.Current, location, reload.CollectWarnings());
    }

    /// <summary>Dry run: reports connectivity without writing or publishing anything.</summary>
    [HttpPost("Validate", Name = "ValidateAdminSettings")]
    public async Task<ActionResult<SaveSettingsResultDto>> Validate(
        [FromBody] AdminSettingsUpdateDto dto, CancellationToken ct)
    {
        var current = store.Current;
        var candidate = AdminSettingsMapper.Apply(dto, current.Settings);

        var result = new SaveSettingsResultDto
        {
            Applied = false,
            Version = current.Version,
            Warnings = reload.CollectWarnings()
        };

        if (!SettingsValidator.TryValidate(candidate, out var problems))
        {
            result.Problems = problems;
            return Ok(result);
        }

        var probes = await reload.ValidateOnlyAsync(candidate, ct);
        result.AccountResults = ToProbeDtos(probes);
        return Ok(result);
    }

    /// <summary>Lists an account's albums, people, and tags so they can be picked by name.</summary>
    /// <remarks>
    /// Never fails the request on a connectivity problem — the picker degrades to raw ID entry, and a
    /// 500 here would look like the settings page itself was broken.
    /// </remarks>
    [HttpPost("Browse", Name = "BrowseImmichCatalog")]
    public async Task<ActionResult<AdminBrowseResultDto>> Browse(
        [FromBody] AdminBrowseRequestDto dto, CancellationToken ct)
    {
        var apiKey = dto.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey) && dto.Index is { } index)
        {
            // Same presence semantics as a save: no key on the wire means "use the configured one",
            // which is the only way to browse for an account whose key came from ApiKeyFile.
            apiKey = store.Current.Settings.Accounts.ElementAtOrDefault(index)?.ApiKey;
        }

        var result = await catalog.Browse(dto.ImmichServerUrl, apiKey ?? string.Empty, ct);

        return Ok(new AdminBrowseResultDto
        {
            Reachable = result.Reachable,
            Error = result.Error,
            Albums = ToEntryDtos(result.Albums),
            People = ToEntryDtos(result.People),
            Tags = ToEntryDtos(result.Tags)
        });
    }

    // Declared so the generated TypeScript client gets the full status union and the UI can branch on
    // it in a type-safe way rather than guessing.
    [HttpPost(Name = "SaveAdminSettings")]
    [ProducesResponseType<SaveSettingsResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<SaveSettingsResultDto>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<SaveSettingsResultDto>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<SaveSettingsResultDto>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<SaveSettingsResultDto>(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<SaveSettingsResultDto>> Save(
        [FromBody] AdminSettingsUpdateDto dto, CancellationToken ct)
    {
        var current = store.Current;
        var candidate = AdminSettingsMapper.Apply(dto, current.Settings);

        var outcome = await reload.SaveAsync(candidate, dto.Version, dto.Force, ct);

        var result = new SaveSettingsResultDto
        {
            Applied = outcome.Status == SaveStatus.Applied,
            Version = outcome.Version,
            Problems = [.. outcome.Problems],
            Warnings = [.. outcome.Warnings],
            AccountResults = ToProbeDtos(outcome.Probes)
        };

        switch (outcome.Status)
        {
            case SaveStatus.Applied:
                return Ok(result);

            case SaveStatus.Invalid:
                return BadRequest(result);

            case SaveStatus.Unreachable:
                // Structurally fine but at least one server failed the probe. The UI turns this into
                // a "save anyway?" prompt, which comes back with Force = true.
                return StatusCode(StatusCodes.Status422UnprocessableEntity, result);

            case SaveStatus.Conflict:
            case SaveStatus.Busy:
                result.Problems.Add(outcome.Status == SaveStatus.Busy
                    ? "Another save is currently in progress. Try again in a moment."
                    : "These settings are out of date because they were changed elsewhere. Reload and reapply your changes.");
                return Conflict(result);

            case SaveStatus.WriteFailed:
                logger.LogError("Settings save failed to persist to {Path}", location.SettingsJsonPath);
                return StatusCode(StatusCodes.Status500InternalServerError, result);

            default:
                return StatusCode(StatusCodes.Status500InternalServerError, result);
        }
    }

    private static List<CatalogEntryDto> ToEntryDtos(IReadOnlyList<CatalogEntry> entries) =>
        entries.Select(e => new CatalogEntryDto { Id = e.Id, Name = e.Name, Count = e.Count }).ToList();

    private static List<AccountProbeResultDto> ToProbeDtos(IReadOnlyList<AccountProbeResult> probes) =>
        probes.Select((p, i) => new AccountProbeResultDto
        {
            Index = i,
            ImmichServerUrl = p.ImmichServerUrl,
            Reachable = p.Reachable,
            ServerVersion = p.ServerVersion,
            VersionSupported = p.VersionSupported,
            Error = p.Error
        }).ToList();
}
