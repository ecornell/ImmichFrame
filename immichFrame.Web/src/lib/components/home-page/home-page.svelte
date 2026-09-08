<script lang="ts">
	import * as api from '$lib/index';
	import ProgressBar from '$lib/components/elements/progress-bar.svelte';
	import { slideshowStore } from '$lib/stores/slideshow.store';
	import { clientIdentifierStore, authSecretStore } from '$lib/stores/persist.store';
	import { onDestroy, onMount, setContext, tick } from 'svelte';
	import OverlayControls from '../elements/overlay-controls.svelte';
	import AssetComponent from '../elements/asset-component.svelte';
	import type AssetComponentInstance from '../elements/asset-component.svelte';
	import { configStore } from '$lib/stores/config.store';
	import ErrorElement from '../elements/error-element.svelte';
	import Clock from '../elements/clock.svelte';
	import Appointments from '../elements/appointments.svelte';
	import LoadingElement from '../elements/LoadingElement.svelte';
	import { page } from '$app/state';
	import { ProgressBarLocation, ProgressBarStatus } from '../elements/progress-bar.types';
	import { isImageAsset, isVideoAsset } from '$lib/constants/asset-type';
	import {
		assetPromiseKeysToRemove,
		ConsecutiveMediaFailures,
		fatalMediaErrorAfterCandidate,
		transitionIsCurrent,
		withObjectUrlCleanup
	} from '$lib/recovery';

	interface AssetsState {
		assets: [string, api.AssetResponseDto, api.AssetFaceResponseDto[], api.AlbumResponseDto[]][];
		error: boolean;
		loaded: boolean;
		split: boolean;
		hasBday: boolean;
	}

	api.init();

	const PRELOAD_ASSETS = 5;
	const TRANSITION_WATCHDOG_MS = 10000;
	const VIDEO_STALL_MS = 15000;
	const CURSOR_HIDE_MS = 2000;
	const RETRY_ON_ERROR_MS = 5000;
	const MAX_ASSET_HISTORY = 250;
	const MAX_RECENT_ASSETS = 1000;
	const ASSET_REQUEST_ATTEMPTS = 4;
	const RECENT_ASSETS_STORAGE_PREFIX = 'immichframe:recent-assets:';

	let assetHistory: api.AssetResponseDto[] = $state([]);
	let assetBacklog: api.AssetResponseDto[] = $state([]);
	let recentAssetKeys: string[] = loadRecentAssetKeys();

	let displayingAssets: api.AssetResponseDto[] = $state([]);

	const { restartProgress, stopProgress, instantTransition } = slideshowStore;

	let progressBarStatus: ProgressBarStatus = $state(ProgressBarStatus.Playing);
	let progressBar: ProgressBar = $state() as ProgressBar;
	let assetComponent: AssetComponentInstance = $state() as AssetComponentInstance;
	let currentDuration: number = $state($configStore.interval ?? 20);

	const mediaFailures = new ConsecutiveMediaFailures();
	let errorSkipScheduled = false;
	let watchdogTimer: number | undefined;
	let activeTransitionController: AbortController | undefined;
	let videoStallTimeout: number | undefined;
	let timeoutId: number | undefined;

	let userPaused: boolean = $state(false);

	let error: boolean = $state(false);
	let fatalMediaError: boolean = $state(false);
	let offline: boolean = $state(false);
	let infoVisible: boolean = $state(false);
	let authError: boolean = $state(false);
	let errorMessage: string = $state('');
	let assetsState: AssetsState = $state({
		assets: [],
		error: false,
		loaded: false,
		split: false,
		hasBday: false
	});
	let assetPromisesDict: Record<
		string,
		Promise<[string, api.AssetResponseDto, api.AssetFaceResponseDto[], api.AlbumResponseDto[]]>
	> = {};
	let assetPromiseControllers: Record<string, AbortController> = {};

	let unsubscribeRestart: () => void;
	let unsubscribeStop: () => void;
	let retryInterval: number;

	let cursorVisible = $state(true);

	const authsecret = page.url.searchParams.get('authsecret');

	if (authsecret && authsecret != $authSecretStore) {
		authSecretStore.set(authsecret);
		api.init();
	}

	const hideCursor = () => {
		cursorVisible = false;
	};

	setContext('close', provideClose);

	async function provideClose() {
		infoVisible = false;
		userPaused = false;
		await assetComponent?.play?.();
		await progressBar.play();
	}

	const showCursor = () => {
		cursorVisible = true;
		clearTimeout(timeoutId);
		timeoutId = window.setTimeout(hideCursor, CURSOR_HIDE_MS);
	};

	async function updateAssetPromises(
		visibleAssets = displayingAssets,
		backlog = assetBacklog,
		signal?: AbortSignal
	) {
		const ensurePromise = (asset: api.AssetResponseDto, cancelWithTransition: boolean) => {
			let controller = assetPromiseControllers[asset.id];
			if (!(asset.id in assetPromisesDict)) {
				controller = new AbortController();
				assetPromiseControllers[asset.id] = controller;
				const promise = loadAsset(asset, controller.signal);
				assetPromisesDict[asset.id] = promise;
				promise.catch(() => {
					if (assetPromisesDict[asset.id] === promise) {
						delete assetPromisesDict[asset.id];
						delete assetPromiseControllers[asset.id];
					}
				});
			}
			if (cancelWithTransition && signal) {
				signal.addEventListener('abort', () => controller?.abort(), { once: true });
			}
		};

		for (let asset of visibleAssets) ensurePromise(asset, true);
		for (let i = 0; i < Math.min(PRELOAD_ASSETS, backlog.length); i++) {
			ensurePromise(backlog[i], false);
		}
		// Collect keys to remove first to avoid modifying dict during async iteration
		const keysToRemove = assetPromiseKeysToRemove(
			Object.keys(assetPromisesDict),
			visibleAssets.map((asset) => asset.id),
			backlog.map((asset) => asset.id),
			displayingAssets.map((asset) => asset.id)
		);

		keysToRemove.forEach((key) => {
			const promise = assetPromisesDict[key];
			assetPromiseControllers[key]?.abort();
			delete assetPromiseControllers[key];
			delete assetPromisesDict[key];
			promise
				.then(([url]) => revokeObjectUrl(url))
				.catch((err) => console.warn('Failed to resolve asset during cleanup:', err));
		});
	}

	function recentAssetsStorageKey() {
		return `${RECENT_ASSETS_STORAGE_PREFIX}${$clientIdentifierStore ?? 'default'}`;
	}

	function assetHistoryKey(asset: api.AssetResponseDto) {
		return asset.checksum || asset.id;
	}

	function loadRecentAssetKeys(): string[] {
		if (typeof window === 'undefined') return [];

		try {
			const stored = JSON.parse(window.localStorage.getItem(recentAssetsStorageKey()) ?? '[]');
			return Array.isArray(stored)
				? stored.filter((id): id is string => typeof id === 'string').slice(-MAX_RECENT_ASSETS)
				: [];
		} catch {
			return [];
		}
	}

	function recordRecentAssets(assets: api.AssetResponseDto[]) {
		for (const asset of assets) {
			const key = assetHistoryKey(asset);
			recentAssetKeys = recentAssetKeys.filter((candidate) => candidate !== key);
			recentAssetKeys.push(key);
		}

		recentAssetKeys = recentAssetKeys.slice(-MAX_RECENT_ASSETS);
		try {
			window.localStorage.setItem(recentAssetsStorageKey(), JSON.stringify(recentAssetKeys));
		} catch (err) {
			console.warn('Failed to save recent asset history:', err);
		}
	}

	async function loadAssets(signal: AbortSignal, epoch: number) {
		try {
			// This request-local map is never rendered and does not need reactive instrumentation.
			// eslint-disable-next-line svelte/prefer-svelte-reactivity
			const candidates = new Map<string, api.AssetResponseDto>();
			const recentAssetKeysSet = new Set([
				...recentAssetKeys,
				...assetHistory.map(assetHistoryKey),
				...displayingAssets.map(assetHistoryKey)
			]);

			for (let attempt = 0; attempt < ASSET_REQUEST_ATTEMPTS; attempt++) {
				const assetRequest = await api.getAssets(
					{ clientIdentifier: $clientIdentifierStore },
					{ signal }
				);

				if (assetRequest.status != 200) {
					if (transitionIsCurrent(epoch, transitionEpoch, signal)) {
						authError = assetRequest.status == 401;
						markConnectionUnavailable();
					}
					return false;
				}

				for (const asset of assetRequest.data) {
					const key = assetHistoryKey(asset);
					if ((isImageAsset(asset) || isVideoAsset(asset)) && !candidates.has(key)) {
						candidates.set(key, asset);
					}
				}

				const unseenCount = [...candidates.keys()].filter(
					(key) => !recentAssetKeysSet.has(key)
				).length;
				if (unseenCount >= PRELOAD_ASSETS) break;
			}

			if (!transitionIsCurrent(epoch, transitionEpoch, signal)) return false;
			const uniqueAssets = [...candidates.values()];
			const unseenAssets = uniqueAssets.filter(
				(asset) => !recentAssetKeysSet.has(assetHistoryKey(asset))
			);

			if (unseenAssets.length) {
				assetBacklog = unseenAssets;
			} else {
				// Once every returned asset has been seen, restart with the least-recently shown
				// candidate rather than whichever asset happened to be first in the API response.
				const recency = new Map(recentAssetKeys.map((key, index) => [key, index]));
				assetBacklog = [...uniqueAssets].sort(
					(a, b) =>
						(recency.get(assetHistoryKey(a)) ?? -1) - (recency.get(assetHistoryKey(b)) ?? -1)
				);
			}
			return true;
		} catch (caught) {
			if (transitionIsCurrent(epoch, transitionEpoch, signal)) markConnectionUnavailable();
			if (caught instanceof DOMException && caught.name === 'AbortError') return false;
			return false;
		}
	}

	function markConnectionUnavailable() {
		errorMessage = 'Connection unavailable. Retrying while the last photo remains displayed.';
		if (assetsState.loaded) offline = true;
		else error = true;
	}

	let isHandlingAssetTransition = $state(false);
	let transitionEpoch = 0;
	let pendingTransition: { previous: boolean; instant: boolean } | null = $state(null);

	const handleDone = async (previous: boolean = false, instant: boolean = false) => {
		if (isHandlingAssetTransition) {
			pendingTransition = { previous, instant };
			return;
		}

		const currentEpoch = ++transitionEpoch;
		const controller = new AbortController();
		activeTransitionController = controller;
		isHandlingAssetTransition = true;

		clearTimeout(watchdogTimer);
		clearTimeout(videoStallTimeout);
		watchdogTimer = window.setTimeout(() => {
			if (transitionIsCurrent(currentEpoch, transitionEpoch, controller.signal)) {
				console.error('Transition watchdog triggered: aborting stalled requests');
				markConnectionUnavailable();
				pendingTransition ??= { previous: false, instant: true };
				controller.abort();
			}
		}, TRANSITION_WATCHDOG_MS);

		try {
			userPaused = false;
			progressBar.restart(false);
			$instantTransition = instant;
			if (previous) await getPreviousAssets(currentEpoch, controller.signal);
			else await getNextAssets(currentEpoch, controller.signal);
			if (!transitionIsCurrent(currentEpoch, transitionEpoch, controller.signal)) return;

			await tick();
			await assetComponent?.play?.();
			await progressBar.play();
		} catch (caught) {
			if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
				console.error('Asset transition failed:', caught);
				if (transitionIsCurrent(currentEpoch, transitionEpoch, controller.signal)) {
					markConnectionUnavailable();
				}
			}
		} finally {
			if (currentEpoch === transitionEpoch) {
				isHandlingAssetTransition = false;
				activeTransitionController = undefined;
				clearTimeout(watchdogTimer);

				if (pendingTransition) {
					const next = pendingTransition;
					pendingTransition = null;
					handleDone(next.previous, next.instant).catch((caught) =>
						console.error('handleDone failed:', caught)
					);
				}
			}
		}
	};

	async function getNextAssets(epoch: number, signal: AbortSignal) {
		if (!assetBacklog.length && !(await loadAssets(signal, epoch))) return;
		if (!transitionIsCurrent(epoch, transitionEpoch, signal)) return;

		if (!assetBacklog.length) {
			error = true;
			errorMessage = 'No assets were found! Check your configuration.';
			return;
		}

		const useSplit = shouldUseSplitView(assetBacklog);
		const count = useSplit ? 2 : 1;
		const next = assetBacklog.slice(0, count);
		const nextBacklog = assetBacklog.slice(count);
		let nextHistory = displayingAssets.length
			? [...assetHistory, ...displayingAssets]
			: [...assetHistory];
		nextHistory = nextHistory.slice(-MAX_ASSET_HISTORY);

		await updateAssetPromises(next, nextBacklog, signal);
		const nextState = await pickAssets(next);
		if (!transitionIsCurrent(epoch, transitionEpoch, signal)) return;
		if (!nextState.loaded) {
			markConnectionUnavailable();
			return;
		}

		assetBacklog = nextBacklog;
		assetHistory = nextHistory;
		displayingAssets = next;
		assetsState = nextState;
		fatalMediaError = fatalMediaErrorAfterCandidate(fatalMediaError, nextState.loaded);
		error = false;
		offline = false;
		authError = false;
		recordRecentAssets(next);
	}

	async function getPreviousAssets(epoch: number, signal: AbortSignal) {
		if (!assetHistory.length) return;

		const useSplit = shouldUseSplitView(assetHistory.slice(-2));
		const count = useSplit ? 2 : 1;
		const next = assetHistory.slice(-count);
		const nextHistory = assetHistory.slice(0, -count);
		const nextBacklog = displayingAssets.length
			? [...displayingAssets, ...assetBacklog]
			: [...assetBacklog];

		await updateAssetPromises(next, nextBacklog, signal);
		const nextState = await pickAssets(next);
		if (!transitionIsCurrent(epoch, transitionEpoch, signal) || !nextState.loaded) return;

		assetHistory = nextHistory;
		assetBacklog = nextBacklog;
		displayingAssets = next;
		assetsState = nextState;
		fatalMediaError = fatalMediaErrorAfterCandidate(fatalMediaError, nextState.loaded);
		recordRecentAssets(next);
	}

	function isPortrait(asset: api.AssetResponseDto) {
		if (isVideoAsset(asset)) {
			return false;
		}

		const isFlipped = (orientation: number) => [5, 6, 7, 8].includes(orientation);
		let assetHeight = asset.exifInfo?.exifImageHeight ?? 0;
		let assetWidth = asset.exifInfo?.exifImageWidth ?? 0;
		if (isFlipped(Number(asset.exifInfo?.orientation ?? 0))) {
			[assetHeight, assetWidth] = [assetWidth, assetHeight];
		}
		return assetHeight > assetWidth;
	}

	function shouldUseSplitView(assets: api.AssetResponseDto[]): boolean {
		return (
			$configStore.layout?.trim().toLowerCase() === 'splitview' &&
			assets.length > 1 &&
			isImageAsset(assets[0]) &&
			isImageAsset(assets[1]) &&
			isPortrait(assets[0]) &&
			isPortrait(assets[1])
		);
	}

	function hasBirthday(assets: api.AssetResponseDto[]) {
		let today = new Date();
		let hasBday: boolean = false;

		for (let asset of assets) {
			for (let person of asset.people ?? []) {
				let birthdate = new Date(person.birthDate ?? '');
				if (birthdate.getDate() === today.getDate() && birthdate.getMonth() === today.getMonth()) {
					hasBday = true;
					break;
				}
			}
			if (hasBday) break;
		}

		return hasBday;
	}

	function updateCurrentDuration(assets: api.AssetResponseDto[]) {
		const durations = assets
			.map((asset) => getAssetDurationSeconds(asset))
			.filter((value) => value > 0);
		const fallback = $configStore.interval ?? 20;
		currentDuration = durations.length ? Math.max(...durations) : fallback;
	}

	function getAssetDurationSeconds(asset: api.AssetResponseDto) {
		if (isVideoAsset(asset)) {
			const parsed = parseAssetDuration(asset.duration);
			const fallback = $configStore.interval ?? 20;
			return parsed > 0 ? parsed : fallback;
		}
		return $configStore.interval ?? 20;
	}

	function parseAssetDuration(duration?: number | null) {
		if (!duration || duration <= 0) {
			return 0;
		}
		return duration / 1000; // milliseconds → seconds
	}

	async function pickAssets(assets: api.AssetResponseDto[]) {
		let newAssets = [];
		try {
			updateCurrentDuration(assets);
			for (let asset of assets) {
				const promise = assetPromisesDict[asset.id];
				if (!promise) throw new Error(`Missing preload promise for asset ${asset.id}`);
				newAssets.push(await promise);
			}
			return {
				assets: newAssets,
				error: false,
				loaded: true,
				split: assets.length == 2 && assets.every(isImageAsset),
				hasBday: hasBirthday(assets)
			};
		} catch {
			updateCurrentDuration([]);
			return {
				assets: [],
				error: true,
				loaded: false,
				split: false,
				hasBday: false
			};
		}
	}

	async function loadAsset(assetResponse: api.AssetResponseDto, signal?: AbortSignal) {
		let assetUrl: string;

		if (isVideoAsset(assetResponse)) {
			// Stream videos directly instead of preloading
			assetUrl = api.getAssetStreamUrl(
				assetResponse.id,
				$clientIdentifierStore,
				assetResponse.type
			);
		} else {
			// Preload images as blobs
			const req = await api.getAsset(
				assetResponse.id,
				{
					clientIdentifier: $clientIdentifierStore,
					assetType: assetResponse.type
				},
				{ signal }
			);
			if (req.status != 200) {
				throw new Error(`Failed to load asset ${assetResponse.id}: status ${req.status}`);
			}
			assetUrl = getObjectUrl(req.data);
		}

		return withObjectUrlCleanup(
			assetUrl,
			async () => {
				let album: api.AlbumResponseDto[] | null = null;
				if ($configStore.showAlbumName) {
					const albumReq = await api.getAlbumInfo(
						assetResponse.id,
						{ clientIdentifier: $clientIdentifierStore },
						{ signal }
					);
					album = albumReq.data ?? [];
				}

				// if the people array is already populated, there is no need to call the API again
				if ($configStore.showPeopleDesc && (assetResponse.people ?? []).length == 0) {
					const assetInfoRequest = await api.getAssetInfo(
						assetResponse.id,
						{ clientIdentifier: $clientIdentifierStore },
						{ signal }
					);
					assetResponse.people = assetInfoRequest.data.people;
				}

				let faces: api.AssetFaceResponseDto[] = [];
				if (!isVideoAsset(assetResponse) && ($configStore.imageZoom || $configStore.imagePan)) {
					const facesRequest = await api.getAssetFaces(
						assetResponse.id,
						{ clientIdentifier: $clientIdentifierStore },
						{ signal }
					);
					faces = facesRequest.data;
				}

				return [assetUrl, assetResponse, faces, album] as [
					string,
					api.AssetResponseDto,
					api.AssetFaceResponseDto[],
					api.AlbumResponseDto[]
				];
			},
			revokeObjectUrl
		);
	}

	function getObjectUrl(image: Blob) {
		return URL.createObjectURL(image);
	}

	function revokeObjectUrl(url: string) {
		// Only revoke blob URLs, not streaming URLs
		if (!url.startsWith('blob:')) return;
		try {
			URL.revokeObjectURL(url);
		} catch {
			console.warn('Failed to revoke object URL:', url);
		}
	}

	// Re-applies the theme whenever the config changes, so colours saved from the settings page
	// restyle the running frame instead of waiting for a reload. onMount below still applies these
	// once on load; that duplicate is deliberate — leaving its block untouched keeps this file's
	// diff against upstream purely additive, and it is the most frequently changed file in the repo.
	$effect(() => {
		if ($configStore.primaryColor) {
			document.documentElement.style.setProperty('--primary-color', $configStore.primaryColor);
		}

		if ($configStore.secondaryColor) {
			document.documentElement.style.setProperty('--secondary-color', $configStore.secondaryColor);
		}

		if ($configStore.baseFontSize) {
			document.documentElement.style.fontSize = $configStore.baseFontSize;
		}
	});

	onMount(() => {
		window.addEventListener('mousemove', showCursor);
		window.addEventListener('click', showCursor);

		// Retry API work in place so an outage does not discard the last rendered photo.
		retryInterval = window.setInterval(() => {
			if ((error || offline) && !authError && !isHandlingAssetTransition) {
				handleDone(false, true).catch((caught) =>
					console.error('Recovery transition failed:', caught)
				);
			}
		}, RETRY_ON_ERROR_MS);

		if ($configStore.primaryColor) {
			document.documentElement.style.setProperty('--primary-color', $configStore.primaryColor);
		}

		if ($configStore.secondaryColor) {
			document.documentElement.style.setProperty('--secondary-color', $configStore.secondaryColor);
		}

		if ($configStore.baseFontSize) {
			document.documentElement.style.fontSize = $configStore.baseFontSize;
		}

		unsubscribeRestart = restartProgress.subscribe((value) => {
			if (value) {
				progressBar.restart(value);
				assetComponent?.play?.();
			}
		});

		unsubscribeStop = stopProgress.subscribe((value) => {
			if (value) {
				progressBar.restart(false);
				assetComponent?.pause?.();
			}
		});

		handleDone();

		return () => {
			window.removeEventListener('mousemove', showCursor);
			window.removeEventListener('click', showCursor);
			window.clearInterval(retryInterval);
			window.clearTimeout(timeoutId);
			window.clearTimeout(videoStallTimeout);
			window.clearTimeout(watchdogTimer);
			activeTransitionController?.abort();
		};
	});

	onDestroy(async () => {
		if (unsubscribeRestart) {
			unsubscribeRestart();
		}

		if (unsubscribeStop) {
			unsubscribeStop();
		}

		Object.values(assetPromiseControllers).forEach((controller) => controller.abort());
		const revokes = Object.values(assetPromisesDict).map(async (p) => {
			try {
				const [url] = await p;
				revokeObjectUrl(url);
			} catch (err) {
				console.warn('Failed to resolve asset during destroy cleanup:', err);
			}
		});
		await Promise.allSettled(revokes);
		assetPromisesDict = {};
		assetPromiseControllers = {};
	});
</script>

<section class="fixed grid h-dvh-safe w-screen bg-black" class:cursor-none={!cursorVisible}>
	{#if fatalMediaError || (error && !assetsState.loaded)}
		<ErrorElement {authError} message={errorMessage} />
	{:else if assetsState.loaded}
		<div class="absolute h-screen w-screen">
			<AssetComponent
				showLocation={$configStore.showImageLocation}
				interval={currentDuration}
				showPhotoDate={$configStore.showPhotoDate}
				showImageDesc={$configStore.showImageDesc}
				showPeopleDesc={$configStore.showPeopleDesc}
				showTagsDesc={$configStore.showTagsDesc}
				showAlbumName={$configStore.showAlbumName}
				{...assetsState}
				imageFill={$configStore.imageFill}
				imageZoom={$configStore.imageZoom}
				imagePan={$configStore.imagePan}
				bind:this={assetComponent}
				bind:showInfo={infoVisible}
				playAudio={$configStore.playAudio}
				onVideoWaiting={async () => {
					await progressBar.pause();
					clearTimeout(videoStallTimeout);
					if (userPaused) return;

					videoStallTimeout = window.setTimeout(
						() => {
							if (!userPaused) {
								console.warn('Video stalled, skipping...');
								handleDone(false, true);
							}
						},
						Math.max(5000, Math.min(VIDEO_STALL_MS, currentDuration * 1000))
					);
				}}
				onVideoPlaying={async () => {
					mediaFailures.succeeded();
					fatalMediaError = false;
					clearTimeout(videoStallTimeout);
					if (!userPaused) {
						await progressBar.play();
					}
				}}
				onAssetLoaded={() => {
					mediaFailures.succeeded();
					fatalMediaError = false;
				}}
				onAssetError={async () => {
					if (errorSkipScheduled) return;
					errorSkipScheduled = true;

					if (mediaFailures.failed() > 10) {
						error = true;
						fatalMediaError = true;
						errorMessage =
							'Too many consecutive asset load failures. Please check your network or server connection.';
						errorSkipScheduled = false;
						return;
					}

					await handleDone(false, true);
					errorSkipScheduled = false;
				}}
			/>
		</div>

		{#if offline}
			<div class="absolute right-4 top-4 z-[1001] rounded bg-black/70 px-3 py-2 text-white">
				Offline — retrying
			</div>
		{/if}

		{#if $configStore.showClock}
			<Clock />
		{/if}

		<Appointments />

		<OverlayControls
			next={async () => {
				await handleDone(false, true);
				infoVisible = false;
			}}
			back={async () => {
				await handleDone(true, true);
				infoVisible = false;
			}}
			pause={async () => {
				infoVisible = false;
				if (progressBarStatus == ProgressBarStatus.Paused) {
					userPaused = false;
					await assetComponent?.play?.();
					await progressBar.play();
				} else {
					userPaused = true;
					await assetComponent?.pause?.();
					await progressBar.pause();
				}
			}}
			showInfo={async () => {
				if (infoVisible) {
					infoVisible = false;
					userPaused = false;
					await assetComponent?.play?.();
					await progressBar.play();
				} else {
					infoVisible = true;
					userPaused = true;
					await assetComponent?.pause?.();
					await progressBar.pause();
				}
			}}
			bind:status={progressBarStatus}
			bind:infoVisible
			overlayVisible={cursorVisible}
		/>

		<ProgressBar
			autoplay
			duration={currentDuration}
			hidden={!$configStore.showProgressBar}
			location={ProgressBarLocation.Bottom}
			bind:this={progressBar}
			bind:status={progressBarStatus}
			onDone={handleDone}
		/>
	{:else}
		<LoadingElement />
	{/if}
</section>
