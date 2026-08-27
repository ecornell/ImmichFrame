<script lang="ts">
	interface Props {
		/** Bound list. Edited as comma-separated text, which matches the config file format. */
		value: string[];
		placeholder?: string;
	}

	let { value = $bindable(), placeholder = '' }: Props = $props();

	// Kept as free text while editing so a trailing comma doesn't produce a phantom empty entry.
	let text = $state(value.join(', '));

	function commit() {
		value = text
			.split(',')
			.map((entry) => entry.trim())
			.filter((entry) => entry.length > 0);
	}
</script>

<textarea
	rows="2"
	{placeholder}
	class="w-full rounded-md border-2 border-primary/30 bg-transparent px-2 py-1 text-sm text-primary
		placeholder:text-primary/30 focus:border-primary focus:outline-none"
	bind:value={text}
	oninput={commit}
	onblur={commit}
></textarea>
