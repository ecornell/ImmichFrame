<script lang="ts">
	import { onMount } from 'svelte';
	import { resolve } from '$app/paths';
	import * as api from '$lib/index';
	import type {
		AdminAccountUpdateDto,
		AdminAccountViewDto,
		AdminGeneralSettingsDto,
		AdminSettingsViewDto,
		AccountProbeResultDto,
		CatalogEntryDto
	} from '$lib/immichFrameApi';
	import SettingsSection from '$lib/components/settings/settings-section.svelte';
	import SettingRow from '$lib/components/settings/setting-row.svelte';
	import TextField from '$lib/components/settings/text-field.svelte';
	import NumberField from '$lib/components/settings/number-field.svelte';
	import ToggleField from '$lib/components/settings/toggle-field.svelte';
	import SelectField from '$lib/components/settings/select-field.svelte';
	import ListField from '$lib/components/settings/list-field.svelte';
	import CatalogPicker from '$lib/components/settings/catalog-picker.svelte';

	/** The albums/people/tags on one account's server, so they can be picked by name. */
	type Catalog = {
		albums: CatalogEntryDto[];
		people: CatalogEntryDto[];
		tags: CatalogEntryDto[];
	};

	/** An account plus the local-only bits that never go over the wire as-is. */
	type EditableAccount = AdminAccountViewDto & {
		/** Set only when the user chooses to replace the key; otherwise the key is left untouched. */
		newApiKey?: string;
		editingApiKey?: boolean;
		isNew?: boolean;
		/** Null until fetched; the pickers fall back to raw ID entry while it is. */
		catalog?: Catalog | null;
		catalogLoading?: boolean;
		catalogError?: string | null;
	};

	let loading = $state(true);
	let saving = $state(false);
	let loadError = $state('');
	let version = $state(0);
	let configFilePath = $state('');
	let canPersist = $state(true);
	let warnings = $state<string[]>([]);

	let general = $state<AdminGeneralSettingsDto>({});
	let accounts = $state<EditableAccount[]>([]);

	let problems = $state<string[]>([]);
	let probeResults = $state<AccountProbeResultDto[]>([]);
	let statusMessage = $state('');
	let needsForce = $state(false);

	const styleOptions = [
		{ value: 'none', label: 'None' },
		{ value: 'solid', label: 'Solid' },
		{ value: 'transition', label: 'Gradient' },
		{ value: 'blur', label: 'Blur' }
	];

	const layoutOptions = [
		{ value: 'splitview', label: 'Split view' },
		{ value: 'single', label: 'Single' }
	];

	const unitOptions = [
		{ value: 'imperial', label: 'Imperial (°F)' },
		{ value: 'metric', label: 'Metric (°C)' }
	];

	onMount(async () => {
		// The bearer header is installed per-component in this app, not globally.
		api.init();
		await load();
	});

	async function load() {
		loading = true;
		loadError = '';
		try {
			const res = await api.getAdminSettings();
			if (res.status !== 200) {
				loadError = `Could not load settings (HTTP ${res.status}).`;
				return;
			}
			applyView(res.data);
		} catch (e) {
			loadError = e instanceof Error ? e.message : 'Could not load settings.';
		} finally {
			loading = false;
		}
	}

	function applyView(view: AdminSettingsViewDto) {
		version = view.version ?? 0;
		configFilePath = view.configFilePath ?? '';
		canPersist = view.canPersist ?? true;
		warnings = view.warnings ?? [];
		general = { ...(view.general ?? {}) };
		accounts = (view.accounts ?? []).map((a) => ({ ...a, catalog: null }));

		// Fetched in the background so a slow or unreachable Immich never holds up the form.
		for (const account of accounts) loadCatalog(account);
	}

	/**
	 * Pulls the account's albums, people, and tags so they can be shown by name. The API key is only
	 * sent when the user has just typed one; otherwise the server uses the key it already has, which
	 * is what makes this work for a key that came from ApiKeyFile.
	 */
	async function loadCatalog(account: EditableAccount) {
		// Same presence rule as toUpdateDto: a key only counts while it is actually being edited.
		const typedKey = account.editingApiKey ? account.newApiKey : undefined;

		if (!account.immichServerUrl) return;
		if (!account.apiKeyIsSet && !typedKey) return;

		account.catalogLoading = true;
		account.catalogError = null;
		try {
			const res = await api.browseImmichCatalog({
				index: account.isNew ? null : account.index,
				immichServerUrl: account.immichServerUrl,
				apiKey: typedKey || null
			});

			if (res.status !== 200) {
				account.catalogError = `HTTP ${res.status}`;
				return;
			}

			if (!res.data.reachable) {
				account.catalogError = res.data.error ?? 'server unreachable';
				return;
			}

			account.catalog = {
				albums: res.data.albums ?? [],
				people: res.data.people ?? [],
				tags: res.data.tags ?? []
			};
		} catch (e) {
			account.catalogError = e instanceof Error ? e.message : 'request failed';
		} finally {
			account.catalogLoading = false;
		}
	}

	function addAccount() {
		accounts = [
			...accounts,
			{
				index: null as unknown as number,
				immichServerUrl: '',
				apiKeyIsSet: false,
				apiKeySource: 'none',
				albums: [],
				excludedAlbums: [],
				people: [],
				tags: [],
				isNew: true,
				editingApiKey: true,
				newApiKey: '',
				catalog: null
			}
		];
	}

	function removeAccount(target: EditableAccount) {
		accounts = accounts.filter((a) => a !== target);
	}

	function toUpdateDto(account: EditableAccount): AdminAccountUpdateDto {
		const dto: AdminAccountUpdateDto = {
			index: account.isNew ? null : account.index,
			immichServerUrl: account.immichServerUrl,
			apiKeyFile: account.apiKeyFile,
			showMemories: account.showMemories,
			showFavorites: account.showFavorites,
			showArchived: account.showArchived,
			showVideos: account.showVideos,
			imagesFromDays: account.imagesFromDays,
			imagesFromDate: account.imagesFromDate,
			imagesUntilDate: account.imagesUntilDate,
			albums: account.albums ?? [],
			excludedAlbums: account.excludedAlbums ?? [],
			people: account.people ?? [],
			tags: account.tags ?? [],
			rating: account.rating
		};

		// Presence semantics: only send apiKey when the user actually entered one. Leaving it off
		// tells the server to keep whatever key the account already has.
		if (account.editingApiKey && account.newApiKey !== undefined && account.newApiKey !== '') {
			dto.apiKey = account.newApiKey;
		}

		return dto;
	}

	async function save(force = false) {
		saving = true;
		problems = [];
		probeResults = [];
		statusMessage = '';
		needsForce = false;

		try {
			const res = await api.saveAdminSettings({
				version,
				force,
				general,
				accounts: accounts.map(toUpdateDto)
			});

			problems = res.data.problems ?? [];
			probeResults = res.data.accountResults ?? [];
			warnings = res.data.warnings ?? warnings;

			if (res.status === 200) {
				version = res.data.version ?? version;
				statusMessage = 'Saved and applied — no restart needed.';
				// Re-read so masked key state and account indexes reflect what the server stored.
				await load();
				return;
			}

			if (res.status === 422) {
				needsForce = true;
				statusMessage = 'Some Immich servers could not be reached.';
				return;
			}

			if (res.status === 409) {
				statusMessage = 'Settings changed elsewhere. Reload to get the latest, then reapply.';
				return;
			}

			statusMessage =
				res.status === 500 ? 'Could not write the settings file.' : 'Some settings are not valid.';
		} catch (e) {
			statusMessage = e instanceof Error ? e.message : 'Save failed.';
		} finally {
			saving = false;
		}
	}
</script>

<svelte:head>
	<title>ImmichFrame · Settings</title>
</svelte:head>

<main id="settings-page" class="min-h-dvh-safe w-screen overflow-y-auto bg-black px-4 py-6">
	<div class="mx-auto max-w-4xl">
		<header class="mb-6 flex flex-wrap items-center justify-between gap-3">
			<div>
				<h1 class="text-2xl font-bold text-primary">Settings</h1>
				{#if configFilePath}
					<p class="text-xs text-primary/50">{configFilePath}</p>
				{/if}
			</div>
			<div class="flex items-center gap-2">
				<a
					href={resolve('/')}
					class="rounded-md border-2 border-primary/30 px-3 py-1 text-sm text-primary hover:border-primary"
					>Back to frame</a
				>
				<button
					class="rounded-md border-2 border-primary bg-primary/10 px-4 py-1 text-sm font-medium
						text-primary disabled:opacity-40"
					disabled={saving || loading}
					onclick={() => save(false)}>{saving ? 'Saving…' : 'Save'}</button
				>
			</div>
		</header>

		{#if loading}
			<p class="text-primary/60">Loading…</p>
		{:else if loadError}
			<p class="rounded-md border-2 border-red-500/40 p-3 text-red-400">{loadError}</p>
		{:else}
			{#if !canPersist}
				<p class="mb-4 rounded-md border-2 border-amber-500/40 p-3 text-sm text-amber-300">
					The configuration directory is not writable, so changes cannot be saved. Mount a writable
					volume at <code>{configFilePath}</code>.
				</p>
			{/if}

			{#each warnings as warning (warning)}
				<p class="mb-2 rounded-md border-2 border-amber-500/40 p-3 text-sm text-amber-300">
					{warning}
				</p>
			{/each}

			{#if statusMessage}
				<p
					id="settings-status"
					class="mb-4 rounded-md border-2 p-3 text-sm
						{needsForce || problems.length
						? 'border-red-500/40 text-red-300'
						: 'border-green-500/40 text-green-300'}"
				>
					{statusMessage}
				</p>
			{/if}

			{#each problems as problem (problem)}
				<p class="mb-2 text-sm text-red-400">• {problem}</p>
			{/each}

			{#if probeResults.some((p) => !p.reachable || !p.versionSupported)}
				<div class="mb-4 rounded-md border-2 border-red-500/40 p-3 text-sm">
					{#each probeResults.filter((p) => !p.reachable || !p.versionSupported) as probe (probe.index)}
						<p class="text-red-300">{probe.immichServerUrl}: {probe.error}</p>
					{/each}
					{#if needsForce}
						<button
							class="mt-2 rounded-md border-2 border-amber-400 px-3 py-1 text-xs text-amber-300"
							onclick={() => save(true)}>Save anyway</button
						>
					{/if}
				</div>
			{/if}

			<SettingsSection title="Slideshow">
				<SettingRow label="Interval" hint="Seconds each photo is shown.">
					<NumberField bind:value={general.interval} min={1} />
				</SettingRow>
				<SettingRow label="Transition duration" hint="Seconds spent cross-fading.">
					<NumberField bind:value={general.transitionDuration} min={0} step={0.1} />
				</SettingRow>
				<SettingRow label="Layout">
					<SelectField bind:value={general.layout} options={layoutOptions} />
				</SettingRow>
				<SettingRow label="Zoom effect">
					<ToggleField bind:value={general.imageZoom!} />
				</SettingRow>
				<SettingRow label="Pan effect">
					<ToggleField bind:value={general.imagePan!} />
				</SettingRow>
				<SettingRow label="Fill screen" hint="Crop photos to fill instead of letterboxing.">
					<ToggleField bind:value={general.imageFill!} />
				</SettingRow>
				<SettingRow label="Play audio" hint="Only affects videos.">
					<ToggleField bind:value={general.playAudio!} />
				</SettingRow>
				<SettingRow label="Show progress bar">
					<ToggleField bind:value={general.showProgressBar!} />
				</SettingRow>
			</SettingsSection>

			<SettingsSection title="Clock">
				<SettingRow label="Show clock">
					<ToggleField bind:value={general.showClock!} />
				</SettingRow>
				<SettingRow label="Time format" hint="date-fns pattern, e.g. hh:mm or HH:mm.">
					<TextField bind:value={general.clockFormat} placeholder="hh:mm" />
				</SettingRow>
				<SettingRow label="Date format" hint="date-fns pattern, e.g. eee, MMM d.">
					<TextField bind:value={general.clockDateFormat} placeholder="eee, MMM d" />
				</SettingRow>
			</SettingsSection>

			<SettingsSection title="Photo information">
				<SettingRow label="Show date">
					<ToggleField bind:value={general.showPhotoDate!} />
				</SettingRow>
				<SettingRow label="Date format">
					<TextField bind:value={general.photoDateFormat} placeholder="MM/dd/yyyy" />
				</SettingRow>
				<SettingRow label="Show description">
					<ToggleField bind:value={general.showImageDesc!} />
				</SettingRow>
				<SettingRow label="Show people">
					<ToggleField bind:value={general.showPeopleDesc!} />
				</SettingRow>
				<SettingRow label="Show tags">
					<ToggleField bind:value={general.showTagsDesc!} />
				</SettingRow>
				<SettingRow label="Show album name">
					<ToggleField bind:value={general.showAlbumName!} />
				</SettingRow>
				<SettingRow label="Show location">
					<ToggleField bind:value={general.showImageLocation!} />
				</SettingRow>
				<SettingRow label="Location format">
					<TextField bind:value={general.imageLocationFormat} placeholder="City,State,Country" />
				</SettingRow>
			</SettingsSection>

			<SettingsSection title="Appearance">
				<SettingRow label="Primary colour" hint="Any CSS colour, e.g. #f5deb3.">
					<TextField bind:value={general.primaryColor} placeholder="#f5deb3" />
				</SettingRow>
				<SettingRow label="Secondary colour">
					<TextField bind:value={general.secondaryColor} placeholder="#000000" />
				</SettingRow>
				<SettingRow label="Overlay style">
					<SelectField bind:value={general.style} options={styleOptions} />
				</SettingRow>
				<SettingRow label="Base font size" hint="e.g. 17px.">
					<TextField bind:value={general.baseFontSize} placeholder="17px" />
				</SettingRow>
				<SettingRow label="Language" hint="Used for date formatting.">
					<TextField bind:value={general.language} placeholder="en" />
				</SettingRow>
			</SettingsSection>

			<SettingsSection title="Weather">
				<SettingRow label="OpenWeatherMap API key" hint="Leave blank to disable weather.">
					<TextField bind:value={general.weatherApiKey} />
				</SettingRow>
				<SettingRow label="Location" hint="Latitude,longitude.">
					<TextField bind:value={general.weatherLatLong} placeholder="40.7128,-74.0060" />
				</SettingRow>
				<SettingRow label="Units">
					<SelectField bind:value={general.unitSystem} options={unitOptions} />
				</SettingRow>
				<SettingRow label="Show description">
					<ToggleField bind:value={general.showWeatherDescription!} />
				</SettingRow>
			</SettingsSection>

			<SettingsSection title="Advanced">
				<SettingRow label="Web calendars" hint="Comma-separated iCal URLs.">
					<ListField bind:value={general.webcalendars!} placeholder="https://…/basic.ics" />
				</SettingRow>
				<SettingRow
					label="Album/people refresh"
					hint="Hours between refreshing album and people lists from Immich."
				>
					<NumberField bind:value={general.refreshAlbumPeopleInterval} min={0} />
				</SettingRow>
				<SettingRow label="Cache images on disk">
					<ToggleField bind:value={general.downloadImages!} />
				</SettingRow>
				<SettingRow label="Cached image lifetime" hint="Days before a cached image is refetched.">
					<NumberField bind:value={general.renewImagesDuration} min={0} />
				</SettingRow>
				<SettingRow label="Webhook URL" hint="Notified on frame events.">
					<TextField bind:value={general.webhook} />
				</SettingRow>
			</SettingsSection>

			<SettingsSection
				title="Immich accounts"
				description="Photos are drawn from every account, weighted by library size."
			>
				{#each accounts as account, i (i)}
					<div class="settings-account py-4">
						<div class="mb-2 flex items-center justify-between">
							<h3 class="font-medium text-primary">
								Account {i + 1}{account.isNew ? ' (new)' : ''}
							</h3>
							<button
								class="rounded-md border-2 border-red-500/40 px-2 py-1 text-xs text-red-300"
								onclick={() => removeAccount(account)}>Remove</button
							>
						</div>

						<SettingRow label="Immich server URL">
							<TextField bind:value={account.immichServerUrl} placeholder="http://immich:2283" />
						</SettingRow>

						<SettingRow
							label="API key"
							hint={account.apiKeySource === 'file'
								? 'Provided by a key file on the server.'
								: 'Stored in the config file.'}
						>
							{#if account.editingApiKey}
								<TextField
									bind:value={account.newApiKey!}
									type="password"
									autocomplete="new-password"
									placeholder="paste new API key"
								/>
							{:else}
								<div class="flex items-center gap-2">
									<span class="text-sm text-primary/60">
										{account.apiKeyIsSet ? '•••••••••••••• (set)' : 'not set'}
									</span>
									<button
										class="rounded-md border-2 border-primary/30 px-2 py-1 text-xs text-primary"
										onclick={() => (account.editingApiKey = true)}>Change</button
									>
								</div>
							{/if}
						</SettingRow>

						<SettingRow label="Favourites only">
							<ToggleField bind:value={account.showFavorites!} />
						</SettingRow>
						<SettingRow label="Include memories">
							<ToggleField bind:value={account.showMemories!} />
						</SettingRow>
						<SettingRow label="Include archived">
							<ToggleField bind:value={account.showArchived!} />
						</SettingRow>
						<SettingRow label="Include videos">
							<ToggleField bind:value={account.showVideos!} />
						</SettingRow>
						<SettingRow
							label="Albums"
							hint="Only these albums are shown. Leave everything unticked to use the whole library."
						>
							<CatalogPicker
								bind:value={account.albums!}
								options={account.catalog?.albums ?? null}
								loading={account.catalogLoading}
								error={account.catalogError}
								placeholder="Album IDs, comma-separated"
								emptyLabel="No albums on this server."
							/>
						</SettingRow>
						<SettingRow label="Excluded albums" hint="Photos in these albums are never shown.">
							<CatalogPicker
								bind:value={account.excludedAlbums!}
								options={account.catalog?.albums ?? null}
								loading={account.catalogLoading}
								error={account.catalogError}
								placeholder="Album IDs, comma-separated"
								emptyLabel="No albums on this server."
							/>
						</SettingRow>
						<SettingRow label="People">
							<CatalogPicker
								bind:value={account.people!}
								options={account.catalog?.people ?? null}
								loading={account.catalogLoading}
								error={account.catalogError}
								placeholder="Person IDs, comma-separated"
								emptyLabel="Nobody has been named on this server yet."
							/>
						</SettingRow>
						<SettingRow label="Tags">
							<CatalogPicker
								bind:value={account.tags!}
								options={account.catalog?.tags ?? null}
								loading={account.catalogLoading}
								error={account.catalogError}
								placeholder="Tags, comma-separated"
								emptyLabel="No tags on this server."
							/>
						</SettingRow>
						<SettingRow
							label="Album &amp; people list"
							hint="Re-read the names from Immich after changing the server or key."
						>
							<button
								class="rounded-md border-2 border-primary/30 px-3 py-1 text-sm text-primary
									disabled:opacity-40"
								disabled={account.catalogLoading}
								onclick={() => loadCatalog(account)}
							>
								{account.catalogLoading ? 'Loading…' : 'Reload names'}
							</button>
						</SettingRow>
						<SettingRow label="Rating" hint="Only show photos with this exact star rating.">
							<NumberField bind:value={account.rating} min={-1} max={5} nullable />
						</SettingRow>
					</div>
				{/each}

				<div class="pt-3">
					<button
						class="rounded-md border-2 border-primary/30 px-3 py-1 text-sm text-primary"
						onclick={addAccount}>Add account</button
					>
				</div>
			</SettingsSection>
		{/if}
	</div>
</main>
