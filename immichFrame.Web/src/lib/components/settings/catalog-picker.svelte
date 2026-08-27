<script lang="ts">
	import type { CatalogEntryDto } from '$lib/immichFrameApi';

	interface Props {
		/**
		 * Selected entries as the config file stores them: GUIDs for albums and people, values for
		 * tags. Names are only ever a display concern — nothing name-based is persisted.
		 */
		value: string[];
		/** Null until the account's catalog has been fetched; falls back to raw ID entry. */
		options: CatalogEntryDto[] | null;
		loading?: boolean;
		error?: string | null;
		emptyLabel?: string;
		placeholder?: string;
	}

	let {
		value = $bindable(),
		options,
		loading = false,
		error = null,
		emptyLabel = 'Nothing here yet on this server.',
		placeholder = ''
	}: Props = $props();

	let filter = $state('');

	// The generated DTO makes every field nullable; narrow once here rather than at each use.
	let entries = $derived(
		(options ?? [])
			.map((entry) => ({ id: entry.id ?? '', name: entry.name ?? '', count: entry.count }))
			.filter((entry) => entry.id !== '')
	);

	// Configured entries the server does not know about — a deleted album, or a hand-typed typo.
	// They get their own rows so that switching to the picker can never silently drop a selection.
	let unknown = $derived(
		options === null ? [] : value.filter((id) => !entries.some((entry) => entry.id === id))
	);

	let visible = $derived.by(() => {
		const needle = filter.trim().toLowerCase();
		return entries.filter((entry) => entry.name.toLowerCase().includes(needle));
	});

	function toggle(id: string, selected: boolean) {
		value = selected ? [...value, id] : value.filter((entry) => entry !== id);
	}

	// Raw fallback, identical to the old comma-separated field, for when the list can't be fetched.
	let text = $state(value.join(', '));

	function commitText() {
		value = text
			.split(',')
			.map((entry) => entry.trim())
			.filter((entry) => entry.length > 0);
	}
</script>

{#if options !== null}
	{#if entries.length === 0 && unknown.length === 0}
		<p class="text-sm text-primary/50">{emptyLabel}</p>
	{:else}
		<div class="rounded-md border-2 border-primary/30">
			{#if entries.length > 8}
				<input
					placeholder="Filter…"
					class="w-full border-b-2 border-primary/20 bg-transparent px-2 py-1 text-sm text-primary
						placeholder:text-primary/30 focus:outline-none"
					bind:value={filter}
				/>
			{/if}

			<div class="max-h-56 overflow-y-auto px-2 py-1">
				{#each unknown as id (id)}
					<label class="flex cursor-pointer items-center gap-2 py-0.5 text-sm text-primary/60">
						<input
							type="checkbox"
							class="h-4 w-4 shrink-0 accent-current"
							checked={true}
							onchange={() => toggle(id, false)}
						/>
						<span class="truncate" title={id}>Not on this server — {id}</span>
					</label>
				{/each}

				{#each visible as entry (entry.id)}
					<label class="flex cursor-pointer items-center gap-2 py-0.5 text-sm text-primary">
						<input
							type="checkbox"
							class="h-4 w-4 shrink-0 accent-current"
							checked={value.includes(entry.id)}
							onchange={(e) => toggle(entry.id, e.currentTarget.checked)}
						/>
						<span class="truncate" title={entry.id}>{entry.name}</span>
						{#if entry.count != null}
							<span class="ml-auto shrink-0 text-xs text-primary/50">{entry.count}</span>
						{/if}
					</label>
				{/each}

				{#if visible.length === 0 && filter.trim() !== ''}
					<p class="py-0.5 text-sm text-primary/50">No match.</p>
				{/if}
			</div>
		</div>

		<p class="pt-1 text-xs text-primary/50">{value.length} selected</p>
	{/if}
{:else}
	<textarea
		rows="2"
		{placeholder}
		class="w-full rounded-md border-2 border-primary/30 bg-transparent px-2 py-1 text-sm text-primary
			placeholder:text-primary/30 focus:border-primary focus:outline-none"
		bind:value={text}
		oninput={commitText}
		onblur={commitText}
	></textarea>

	{#if loading}
		<p class="pt-1 text-xs text-primary/50">Loading names from Immich…</p>
	{:else if error}
		<p class="pt-1 text-xs text-primary/60">Names unavailable ({error}) — enter IDs directly.</p>
	{/if}
{/if}
