<script lang="ts">
	interface Props {
		value: number | null | undefined;
		min?: number;
		max?: number;
		step?: number;
		/** Allow clearing the field back to null (for optional settings like Rating). */
		nullable?: boolean;
	}

	let { value = $bindable(), min, max, step = 1, nullable = false }: Props = $props();

	// Held as the raw string so a half-typed or cleared field doesn't coerce to 0 mid-edit.
	//
	// Deliberately NOT `bind:value`: on type="number" Svelte coerces the bound variable to a
	// number, which made the trim() below throw on every keystroke and silently swallowed every
	// numeric edit. Reading the string off the event keeps this a string for real.
	let text = $state(value === null || value === undefined ? '' : String(value));

	/** What we last pushed up, so an external change can be told apart from our own. */
	let pushed: number | null | undefined = $state(value);

	// Reloading the form (a save returns the stored settings) has to refresh the box; without this
	// the field would keep showing whatever was typed before.
	$effect(() => {
		if (value !== pushed) {
			text = value === null || value === undefined ? '' : String(value);
			pushed = value;
		}
	});

	function commit(raw: string) {
		text = raw;

		if (raw.trim() === '') {
			value = nullable ? null : value;
			pushed = value;
			return;
		}

		const parsed = Number(raw);
		if (!Number.isNaN(parsed)) {
			value = parsed;
			pushed = parsed;
		}
	}
</script>

<input
	type="number"
	{min}
	{max}
	{step}
	value={text}
	class="w-full rounded-md border-2 border-primary/30 bg-transparent px-2 py-1 text-sm text-primary
		focus:border-primary focus:outline-none"
	oninput={(e) => commit(e.currentTarget.value)}
	onblur={(e) => commit(e.currentTarget.value)}
/>
