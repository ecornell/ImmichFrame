<script lang="ts">
	import type { Snippet } from 'svelte';

	interface Props {
		title: string;
		description?: string;
		children: Snippet;
	}

	let { title, description = '', children }: Props = $props();
</script>

<section class="settings-section mb-6 rounded-xl border-2 border-primary/20 p-4">
	<h2 class="text-lg font-semibold text-primary">{title}</h2>
	{#if description}
		<p class="mb-2 text-xs text-primary/60">{description}</p>
	{/if}
	<div class="divide-y divide-primary/10">
		{@render children()}
	</div>
</section>
