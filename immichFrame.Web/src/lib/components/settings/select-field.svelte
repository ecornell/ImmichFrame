<script lang="ts">
	interface Props {
		value: string | null | undefined;
		options: { value: string; label: string }[];
	}

	let { value = $bindable(), options }: Props = $props();
</script>

<select
	class="w-full rounded-md border-2 border-primary/30 bg-transparent px-2 py-1 text-sm text-primary
		focus:border-primary focus:outline-none"
	bind:value
>
	{#each options as option (option.value)}
		<!-- The page renders on a dark ground; without this the native menu is white-on-white. -->
		<option value={option.value} class="bg-neutral-900 text-white">{option.label}</option>
	{/each}
</select>
