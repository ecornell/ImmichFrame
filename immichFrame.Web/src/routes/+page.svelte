<script lang="ts">
	import HomePage from '$lib/components/home-page/home-page.svelte';
	import ErrorElement from '$lib/components/elements/error-element.svelte';
	import { loadClientConfig } from '$lib/config-loader';
	import { retryDelay } from '$lib/recovery';
	import { onDestroy, onMount } from 'svelte';
	import type { PageData } from './$types';

	let { data }: { data: PageData } = $props();
	let configLoaded = $state(false);
	let retryTimer: number | undefined;
	let retryController: AbortController | undefined;

	onMount(() => {
		if (data.configLoaded) {
			configLoaded = true;
			return;
		}
		if (data.configAuthError) return;

		let attempt = 0;
		const retry = async () => {
			retryController = new AbortController();
			try {
				await loadClientConfig(retryController.signal);
				configLoaded = true;
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
