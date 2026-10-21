<script lang="ts">
	import '../app.css';
	import { onMount } from 'svelte';
	import { useViewportHeight } from '$lib/hooks/useViewportHeight';
	import { claimAutoReload } from '$lib/heartbeat';

	interface Props {
		children?: import('svelte').Snippet;
	}

	let { children }: Props = $props();

	// An unattended frame has nobody to press reload, so a crashed render reloads itself.
	const CRASH_RELOAD_DELAY_MS = 30_000;
	let crashReloadTimer: number | undefined;

	function isAbort(reason: unknown) {
		return reason instanceof DOMException && reason.name === 'AbortError';
	}

	function onRenderError(error: unknown) {
		console.error('ImmichFrame crashed while rendering; reloading soon:', error);
		if (crashReloadTimer !== undefined) return;
		const scheduleReload = () => {
			const wait = claimAutoReload(sessionStorage, Date.now());
			crashReloadTimer = window.setTimeout(
				wait > 0 ? scheduleReload : () => window.location.reload(),
				Math.max(CRASH_RELOAD_DELAY_MS, wait)
			);
		};
		scheduleReload();
	}

	onMount(() => {
		useViewportHeight();

		// Stray errors are left to the browser's own logging and never trigger a reload: a real
		// wedge is caught by the slideshow heartbeat. Aborted requests are routine here (watchdog,
		// timeouts, superseded transitions), so they are kept out of the log.
		const onRejection = (event: PromiseRejectionEvent) => {
			if (isAbort(event.reason)) event.preventDefault();
		};
		window.addEventListener('unhandledrejection', onRejection);

		return () => {
			window.removeEventListener('unhandledrejection', onRejection);
			window.clearTimeout(crashReloadTimer);
		};
	});
</script>

<link rel="stylesheet" href="/static/custom.css" />

<svelte:boundary onerror={onRenderError}>
	{@render children?.()}

	{#snippet failed()}
		<section class="fixed grid h-dvh-safe w-screen place-items-center bg-black text-gray-400">
			<p class="text-xl">Something went wrong. ImmichFrame will reload shortly.</p>
		</section>
	{/snippet}
</svelte:boundary>
