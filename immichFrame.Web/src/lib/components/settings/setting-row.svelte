<script lang="ts">
	import type { Snippet } from 'svelte';

	interface Props {
		label: string;
		hint?: string;
		/** Rendered beneath the control, e.g. a validation message. */
		problem?: string;
		children: Snippet;
	}

	let { label, hint = '', problem = '', children }: Props = $props();
</script>

<div class="setting-row grid grid-cols-1 gap-1 py-3 sm:grid-cols-[1fr_minmax(0,20rem)] sm:gap-4">
	<div class="min-w-0">
		<div class="text-sm font-medium text-primary">{label}</div>
		{#if hint}
			<div class="setting-hint text-xs text-primary/60">{hint}</div>
		{/if}
	</div>
	<div class="min-w-0">
		{@render children()}
		{#if problem}
			<div class="setting-problem mt-1 text-xs text-red-400">{problem}</div>
		{/if}
	</div>
</div>
