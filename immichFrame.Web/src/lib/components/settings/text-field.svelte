<script lang="ts">
	interface Props {
		value: string | null | undefined;
		placeholder?: string;
		type?: 'text' | 'password' | 'url';
		autocomplete?: 'off' | 'new-password';
	}

	let {
		value = $bindable(),
		placeholder = '',
		type = 'text',
		autocomplete = 'off'
	}: Props = $props();
</script>

<input
	{placeholder}
	{autocomplete}
	type={type === 'password' ? 'password' : 'text'}
	inputmode={type === 'url' ? 'url' : undefined}
	class="w-full rounded-md border-2 border-primary/30 bg-transparent px-2 py-1 text-sm text-primary
		placeholder:text-primary/30 focus:border-primary focus:outline-none"
	bind:value
/>
