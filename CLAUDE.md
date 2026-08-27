# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

ImmichFrame turns an [Immich](https://immich.app/) photo server into a digital photo frame. It is a single deployable: an ASP.NET Core 8 Web API (`ImmichFrame.WebApi`) that serves a pre-built SvelteKit SPA from `wwwroot` and proxies/curates assets from one or more Immich servers. Docs at `docs/` are a separate Docusaurus site published to GitHub Pages.

## Commands

Most day-to-day tasks are in the root `Makefile`:

```bash
make dev                # dotnet run --project ./ImmichFrame.WebApi  (http://localhost:5217, Swagger at /swagger)
make test-core          # dotnet test ImmichFrame.Core.Tests
make test-webapi        # dotnet test ImmichFrame.WebApi.Tests
make docs               # Docusaurus dev server for docs/
make api                # regenerate the frontend API client (see "Generated API clients")
make docker-build-prod  # multi-arch production image build
```

Run a single test (NUnit):

```bash
dotnet test ImmichFrame.Core.Tests/ImmichFrame.Core.Tests.csproj --filter "FullyQualifiedName~MultiAssetPoolTests"
dotnet test ImmichFrame.Core.Tests/ImmichFrame.Core.Tests.csproj --filter "Name=GetAssets_ReturnsExpected"
```

Frontend (`immichFrame.Web/`, npm; Node 24 recommended):

```bash
npm install
npm run dev      # Vite dev server on :5173, proxies /api and /static to :5217
npm run check    # svelte-check typecheck
npm run lint     # prettier --check + eslint
npm run format   # prettier --write
npm run build    # static build into immichFrame.Web/build (copied to wwwroot in Docker)
```

`make dev` launches the SPA dev server automatically via `Microsoft.AspNetCore.SpaProxy` (`SpaProxyLaunchCommand` in `ImmichFrame.WebApi.csproj`), so running `npm run dev` separately is only needed when working on the frontend alone.

CI (`.github/workflows/test.yml`) runs only the two `dotnet test` projects — there are no frontend tests. Every PR must carry at least one label (`.github/workflows/pr-require-label.yml`), and labels drive changelog categories in `.github/release.yml`.

## Architecture

### Settings pipeline

`Program.cs` resolves a config directory (`IMMICHFRAME_CONFIG_PATH`, else a `Config/` folder next to the binary) and hands it to `ConfigLoader`, which tries formats in order and falls back on failure:

1. `Settings.json` as current-schema `ServerSettings`
2. `Settings.json` as legacy `ServerSettingsV1` (wrapped in `ServerSettingsV1Adapter`)
3. `Settings.yml` / `Settings.yaml`, same two attempts
4. Environment variables mapped onto `ServerSettingsV1` by property name

The V1 path is the flat, single-account legacy layout; the adapters in `Helpers/Config/ServerSettingsV1.cs` project it onto the current `General` + `Accounts[]` shape. **Any new setting must be added to `GeneralSettings`/`ServerAccountSettings` and, if it existed in V1, wired through the adapters.** Client-visible settings also need to reach `ClientSettingsDto`, which is what the SPA consumes via `GET /api/Config`.

DI then registers the settings interfaces as singletons layered from the loaded `IServerSettings`: `IGeneralSettings` → `IClientSettings` / `IServerBehaviorSettings`. Startup aborts (`Environment.Exit(1)`) if any configured Immich server is unreachable or older than `ImmichServerVersionChecker.MinimumSupportedMajorVersion`.

### Asset selection: pools and accounts

The core abstraction is `IAssetPool` (`ImmichFrame.Core/Logic/Pool/`): `GetAssetCount()` + `GetAssets(n)`. Pools compose:

- `PooledImmichFrameLogic.BuildPool()` inspects one account's settings. With no filters at all it uses `AllAssetsPool` (server-side random search). Otherwise it builds a `MultiAssetPool` over the enabled sources: `FavoriteAssetsPool`, `MemoryAssetsPool`, `AlbumAssetsPool`, `PersonAssetsPool` (in `PeopleAssetsPool.cs`), `TagAssetsPool`.
- `MultiAssetPool` picks a sub-pool weighted by asset count, so larger albums/tags appear proportionally more often.
- `CachingApiAssetsPool` is the base for the filter pools: it fetches the full id list once per `ApiCache` window (`RefreshAlbumPeopleInterval` hours), applies account filters and excluded albums, then samples randomly.
- `AggregatingAssetPool` adapts a one-at-a-time `GetNextAsset` into batched `GetAssets`. `QueuingAssetPool` (background prefetch via a `Channel`) exists and is tested but is **not currently wired into any pool**.

Above the pools, multi-account support layers the same way: `MultiImmichFrameLogicDelegate` implements `IImmichFrameLogic` by delegating to one `PooledImmichFrameLogic` per configured account, choosing between them with `TotalAccountImagesSelectionStrategy` (weighted by each account's total asset count). Because asset ids are only meaningful within their own Immich server, `BloomFilterAssetAccountTracker` records which account served each asset so follow-up calls (`GetAssetInfoById`, `GetAsset`, …) route back to the right one, and `WithAccount()` stamps `ImmichServerUrl` onto every returned `AssetResponseDto`.

### Auth

`CustomAuthenticationMiddleware` authenticates *every* request through `ImmichFrameAuthenticationHandler` before the normal auth pipeline, returning 401 with the failure message. When `AuthenticationSecret` is unset, or the endpoint has no `[Authorize]`, everything is treated as an anonymous success — so the secret is opt-in and `ConfigController` (unauthenticated by design) stays reachable.

### Frontend

SvelteKit 5 + Tailwind, `adapter-static` with `ssr = false` and `prerender = true` — it is a pure SPA, no Node server in production. `+page.ts` loads `GET /api/Config` into `configStore`; that single DTO drives layout, clock, overlays, transitions, and intervals. `slideshow.store.ts` coordinates playback state; `persist.store.ts` holds the `?client=<id>` identifier that is sent as `clientIdentifier` on API calls for per-device logging.

## Generated API clients

Two generated layers, both committed to git — do not hand-edit either:

- **C# → Immich**: `ImmichFrame.Core.csproj` has an `OpenApiReference` on `ImmichFrame.Core/OpenAPIs/immich-openapi-specs.json` (`SourceUri` points at immich `main`), NSwag-generated into `ImmichFrame.Core.Api.ImmichApi` at build time. Update the Immich API surface by refreshing that spec file. `Api/ImmichApi.cs` and `Api/AssetResponseDto.cs` are hand-written `partial` extensions of the generated types (custom constructor, ranged video playback, thumbhash) — those *are* editable.
- **TS → ImmichFrame**: `immichFrame.Web/src/lib/immichFrameApi.ts` is oazapfts-generated from `openApi/swagger.json`. After changing any controller or DTO, run the API with `make dev` and then `make api`, which re-downloads `swagger.json` from the running Swagger endpoint and regenerates the client. Commit both files.

## Release Writeup Rule

When asked to create a release writeup or release notes, follow this process and format:

### Process

1. Run `git log --oneline <prev-tag>..HEAD` to get all commits since the last release.
2. Run `git diff <prev-tag>..HEAD --stat` to understand the scope of changes.
3. Fetch the GitHub release page if a URL is provided to cross-reference the auto-generated changelog.
4. Combine the raw git history with the GitHub changelog to produce a human-friendly writeup.

### Output Format

Use the template at `templates/release-template.md`. Key rules:

- **Title**: `# 📦 ImmichFrame Release vX.X.X.X – <Date>`
- **Intro**: One sentence summarising the release highlights (no heading).
- **Sections**: Follow the category order from `.github/release.yml` — Breaking Changes, New Features, Fixes, Documentation, Maintenance, Other Changes.
- **Each entry**:
  - H4 heading with emoji + feature name
  - Bold `**PR [#NNN](url) by @author**` attribution line
  - 2–4 sentences describing *what* changed and *why it matters* to the user
  - Include a code block if a config snippet helps illustrate usage
  - Separate entries with `---`
- **New Contributors**: Call out first-time contributors with 🎉
- **Footer**: Always end with the full changelog comparison URL.

### Tone

- Write for end users, not developers. Avoid internal refactor jargon unless it has a user-visible effect.
- Keep descriptions concise — 2–4 sentences per entry is enough.
- Use "you" / "your" to address users directly.

## Versioning

The app version is `<Version>` in `ImmichFrame.WebApi.csproj` and is also passed as the `VERSION` build arg / `AssemblyVersion` in Docker builds. Tagging `v*` triggers both the GHCR image build and the GitHub release.
