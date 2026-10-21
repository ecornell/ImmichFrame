<script lang="ts">
	import { onMount, untrack } from 'svelte';
	import { Tween } from 'svelte/motion';
	import { ProgressBarLocation, ProgressBarStatus } from './progress-bar.types';

	interface Props {
		autoplay?: boolean;
		status?: ProgressBarStatus;
		location?: ProgressBarLocation;
		hidden?: boolean;
		duration?: number;
		onDone: () => void | Promise<void>;
		onPlaying?: () => void;
		onPaused?: () => void;
	}

	let {
		autoplay = false,
		status = $bindable(ProgressBarStatus.Paused),
		location = ProgressBarLocation.Bottom,
		hidden = false,
		duration = 5,
		onDone,
		onPlaying = () => {},
		onPaused = () => {}
	}: Props = $props();

	const progress = new Tween<number>(0, {
		duration: (from: number, to: number) => {
			if (to === 0) return 0;
			return duration * 1000 * (to - from);
		}
	});

	let completed = false;
	$effect(() => {
		if (progress.current >= 1 && !completed) {
			completed = true;
			const result = untrack(() => onDone());
			void Promise.resolve(result).catch((err) => {
				console.error('ProgressBar onDone failed:', err);
			});
		} else if (progress.current < 1) {
			completed = false;
		}
	});

	onMount(() => {
		if (autoplay) play();
	});

	// Starts the bar and returns at once. The tween's promise settles only when the bar fills (a
	// whole slide) and never if pause() supersedes it, so awaiting it wedged callers; completion is
	// reported through onDone by the effect above instead.
	export const play = () => {
		status = ProgressBarStatus.Playing;
		onPlaying();
		void progress.set(1);
	};

	export const pause = async () => {
		status = ProgressBarStatus.Paused;
		onPaused();
		// Freeze in place: targeting the current value with duration(to-from≈0) ≈ 0 halts motion.
		await progress.set(progress.current);
	};

	export const restart = async (autoplay: boolean) => {
		await progress.set(0);

		if (autoplay) play();
	};

	export const reset = async () => {
		status = ProgressBarStatus.Paused;
		await progress.set(0);
	};
</script>

{#if !hidden}
	<span
		id="progressbar"
		class="fixed left-0 h-[3px] bg-primary z-[1000]
		{location == ProgressBarLocation.Top ? 'top-0' : 'bottom-0'}"
		style:width={`${progress.current * 100}%`}
	></span>
{/if}
