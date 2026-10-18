<script lang="ts">
	import HomePage from '$lib/components/home-page/home-page.svelte';
	import ErrorElement from '$lib/components/elements/error-element.svelte';
	import { configChanged, fetchClientConfig, loadClientConfig } from '$lib/config-loader';
	import { configStore } from '$lib/stores/config.store';
	import { get } from 'svelte/store';
	import { retryDelay } from '$lib/recovery';
	import { onDestroy, onMount } from 'svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();
	let configLoaded = $state(false);
	let retryTimer: number | undefined;
	let retryController: AbortController | undefined;
	let refreshTimer: number | undefined;

	// A frame can run for days, so check now and then for settings saved in the editor. Several
	// settings are only read when the slideshow starts, so a change reloads the page; the shown
	// history is kept in localStorage, so the reload doesn't repeat photos.
	const CONFIG_REFRESH_MS = 5 * 60 * 1000;

	function watchForConfigChanges() {
		refreshTimer = window.setInterval(async () => {
			try {
				if (configChanged(get(configStore), await fetchClientConfig())) {
					window.location.reload();
				}
			} catch {
				// Offline or unauthorized: keep showing the current configuration.
			}
		}, CONFIG_REFRESH_MS);
	}

	onMount(() => {
		if (data.configLoaded) {
			configLoaded = true;
			watchForConfigChanges();
			return;
		}
		if (data.configAuthError) return;

		let attempt = 0;
		const retry = async () => {
			retryController = new AbortController();
			try {
				await loadClientConfig(retryController.signal);
				configLoaded = true;
				watchForConfigChanges();
			} catch {
				if (!retryController.signal.aborted) {
					retryTimer = window.setTimeout(retry, retryDelay(attempt++));
				}
			}
		};

		retryTimer = window.setTimeout(retry, retryDelay(attempt++));
	});

	onDestroy(() => {
		window.clearTimeout(retryTimer);
		window.clearInterval(refreshTimer);
		retryController?.abort();
	});
</script>

<svelte:head>
	<title>immichFrame</title>
</svelte:head>

{#if configLoaded}
	<HomePage />
{:else}
	<section class="fixed grid h-dvh-safe w-screen bg-black">
		<ErrorElement
			authError={data.configAuthError}
			message={data.configAuthError
				? 'Authentication failed while loading the frame configuration.'
				: 'Unable to load frame configuration. Retrying automatically while the page remains open.'}
		/>
	</section>
{/if}
