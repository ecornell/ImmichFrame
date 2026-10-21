<script lang="ts">
	import { onDestroy, onMount, untrack } from 'svelte';
	import { SlideTimer } from '$lib/slide-timer';
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

	// The timer, not the bar's animation, decides when the slide is over, so the slideshow
	// advances on time with the bar hidden too. The bar is a CSS animation of transform, which
	// runs on the compositor instead of re-laying out a width every frame from JavaScript.
	const timer = new SlideTimer(
		untrack(() => duration),
		() => {
			// The bar is left running so its animation settles at full.
			const result = untrack(() => onDone());
			void Promise.resolve(result).catch((err) => {
				console.error('ProgressBar onDone failed:', err);
			});
		}
	);

	let running = $state(false);
	// Re-creating the bar element restarts its animation; the negative delay resumes it at the
	// time already spent on the slide (after a duration change).
	let cycle = $state(0);
	let offsetSeconds = $state(0);
	let animationSeconds = $state(untrack(() => duration));

	function redraw() {
		offsetSeconds = timer.elapsedMs / 1000;
		animationSeconds = timer.durationMs / 1000;
		cycle++;
	}

	$effect(() => {
		const seconds = duration;
		untrack(() => {
			if (seconds * 1000 === timer.durationMs) return;
			timer.setDuration(seconds);
			running = timer.playing;
			redraw();
		});
	});

	// A bar that appears mid-slide starts where the slide is, not at zero.
	$effect(() => {
		if (!hidden) untrack(redraw);
	});

	onMount(() => {
		if (autoplay) play();
	});

	onDestroy(() => timer.dispose());

	// Every method returns synchronously: awaiting a whole slide (or a paused one, forever) is
	// what used to wedge the slideshow. Completion is reported through onDone.
	export const play = () => {
		status = ProgressBarStatus.Playing;
		onPlaying();
		timer.play();
		running = timer.playing;
	};

	export const pause = () => {
		status = ProgressBarStatus.Paused;
		onPaused();
		timer.pause();
		running = false;
	};

	export const restart = (autoplay: boolean) => {
		timer.restart(false);
		running = false;
		redraw();
		if (autoplay) play();
	};

	export const reset = () => {
		status = ProgressBarStatus.Paused;
		timer.restart(false);
		running = false;
		redraw();
	};
</script>

{#if !hidden}
	{#key cycle}
		<span
			id="progressbar"
			class="progress-bar fixed left-0 h-[3px] w-full bg-primary z-[1000]
			{location == ProgressBarLocation.Top ? 'top-0' : 'bottom-0'}"
			style:animation-duration={`${animationSeconds}s`}
			style:animation-delay={`${-offsetSeconds}s`}
			style:animation-play-state={running ? 'running' : 'paused'}
		></span>
	{/key}
{/if}

<style>
	.progress-bar {
		transform: scaleX(0);
		transform-origin: left;
		animation-name: progress-fill;
		animation-timing-function: linear;
		animation-fill-mode: forwards;
		will-change: transform;
	}

	@keyframes progress-fill {
		from {
			transform: scaleX(0);
		}
		to {
			transform: scaleX(1);
		}
	}
</style>
