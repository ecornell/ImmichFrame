<script lang="ts">
	interface Props {
		value: boolean;
		label?: string;
	}

	let { value = $bindable(), label = '' }: Props = $props();
</script>

<label class="flex cursor-pointer items-center gap-2 text-sm text-primary">
	<input type="checkbox" class="h-4 w-4 shrink-0 accent-current" bind:checked={value} />
	<span class="text-primary/70">{label || (value ? 'On' : 'Off')}</span>
</label>
